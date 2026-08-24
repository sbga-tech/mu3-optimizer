using MonoMod;
using System.Collections.Generic;
using MU3.Battle;
using MU3.Data;
using MU3.DB;
using MU3.Game;
using MU3.Reader;
using MU3.Sound;
using MU3.Sys;
using MU3.User;
using MU3.Util;
using UnityEngine;

namespace MU3.Notes;

[MonoModIfFlag("ActiveNoteTraversal")]
public class patch_NotesManager : NotesManager
{
    // Index of the first NoteControl in the sorted list that has not yet ended.
    private int _noteControlSpawnCursor;


    [MonoModIgnore] private NotesPosCache _posCache;
    [MonoModIgnore] private GameEngine _gameEngine;
    [MonoModIgnore] private float _frameReal;
    [MonoModIgnore] private MSStopwatch _stopwatch;
    [MonoModIgnore] private float _msecStartGap;
    [MonoModIgnore] private float _frameSkip;
    [MonoModIgnore] private bool _pause;
    [MonoModIgnore] private float _frame;
    [MonoModIgnore] private float _frameNoSE;
    [MonoModIgnore] private float _curFramePre;
    [MonoModIgnore] private float _curFrame;
    [MonoModIgnore] private float _frameOffset;
    [MonoModIgnore] private bool _isFinishNote;
    [MonoModIgnore] private bool _isFinishPlay;
    [MonoModIgnore] private SoflanDataListList _soflanDataList;
    [MonoModIgnore] private Dictionary<int, float> _zCurrent;
    [MonoModIgnore] private Dictionary<int, float> _frameVisible;
    [MonoModIgnore] private Dictionary<int, float> _frameInvisible;
    [MonoModIgnore] private NoteControlList _noteControlList;
    [MonoModIgnore] private NotesNodeCache _notesCache;
    [MonoModIgnore] private NotesList _pNotesList;
    [MonoModIgnore] private NotesCount _curNotes;
    [MonoModIgnore] private ShellsCount _curShells;
    [MonoModIgnore] private NotesCount _maxNotes;
    [MonoModIgnore] private ShellsCount _maxShells;
    [MonoModIgnore] private RetireResult _retireResult;
    [MonoModIgnore] private bool _isForceFinish;
    [MonoModIgnore] private float _frameNoteEnd;
    [MonoModIgnore] private HoldMisc _holdMisc;
    [MonoModIgnore] private FieldObject _fieldObject;
    [MonoModIgnore] private float _curAnimFrame;

    // -------------------------------------------------------------------------
    // Private methods of NotesManager that we call from the replaced update().
    // -------------------------------------------------------------------------
    [MonoModIgnore]
    private extern void progressFrameAndFrameReal();

    [MonoModIgnore]
    private extern void updateDropFrameCount(float diffFrame, float diffFrameReal);

    [MonoModIgnore]
    private extern void updateBarAndBeat(ReaderMain r, float msec);

    [MonoModIgnore]
    private extern void updateFader();

    [MonoModIgnore]
    private extern void updateTimingEvent();

    [MonoModIgnore]
    private extern bool calcGuideSE();

    [MonoModIgnore]
    private extern bool isHoldAnyNotes();

    private extern void orig_clearNotes();

    public new void clearNotes()
    {
        orig_clearNotes();

        // Sort by frameCreate so the spawn loop can break early and the cursor
        // can advance monotonically.
        if (_noteControlList.Count > 0)
            _noteControlList.Sort((a, b) => a.frameCreate.CompareTo(b.frameCreate));

        _noteControlSpawnCursor = 0;
    }



    [MonoModReplace]
    public new void update()
    {
        UnityEngine.Profiling.Profiler.BeginSample("NotesManager.Update");
        var instance = Singleton<ReaderMain>.instance;

        #region FrameTiming

        UnityEngine.Profiling.Profiler.BeginSample("FrameTiming");
        SingletonMonoBehaviour<GameEngine>.instance.battleFactory.bullet.beginFrame();
        _posCache.Clear();
        var frameReal = _frameReal;
        if (isPlaying)
        {
            var num = 0f;
            if (_stopwatch != null)
            {
                num = _stopwatch.ElapsedMilliseconds;
            }

            _frameReal = 0.06f * (_msecStartGap + num) + _frameSkip;
            if (_pause)
            {
                _frameSkip -= _frameReal - frameReal;
                _frameReal = frameReal;
            }
            else
            {
                progressFrameAndFrameReal();
            }
        }

        _curFramePre = _curFrame;
        _curFrame = _frame + _frameOffset;
        var addFrame = getAddFrame();
        _frameNoSE = Mathf.Max(_frameNoSE - addFrame, 0f);
        if (_frame > 0.9f && !_isFinishNote)
        {
            var diffFrameReal = _frameReal - frameReal;
            updateDropFrameCount(addFrame, diffFrameReal);
        }

        UnityEngine.Profiling.Profiler.EndSample();

        #endregion

        #region Soflan

        UnityEngine.Profiling.Profiler.BeginSample("SoflanUpdate");
        for (var i = 0; i < _soflanDataList.soflanList.Count; i++)
        {
            _zCurrent[i] = _soflanDataList.getZ(_curFrame, i);
            _frameVisible[i] = _soflanDataList.calcFrameAppearLimit(_curFrame, i);
            _frameInvisible[i] = _soflanDataList.calcFrameDisappearLimit(_curFrame, i);
        }

        UnityEngine.Profiling.Profiler.EndSample();

        #endregion

        #region EnemyUpdate

        UnityEngine.Profiling.Profiler.BeginSample("EnemyUpdate");
        _gameEngine.enemyManager.update(getCurrentFrame(), _sessionInfo.musicData);
        UnityEngine.Profiling.Profiler.EndSample();

        #endregion

        #region FieldSetup

        UnityEngine.Profiling.Profiler.BeginSample("FieldSetup");
        _curNotes.reset();
        _curShells.reset();
        _fieldObject.fixedUpdate();
        var msec = _curFrame * 16.666666f;
        _curAnimFrame = 216000f + _curFrame;
        updateBarAndBeat(instance, msec);
        updateFader();
        _holdMisc.update();
        notesColor.update();
        UnityEngine.Profiling.Profiler.EndSample();

        #endregion

        #region NoteSpawn

        // Activates GameObjects for notes whose approach window has started.
        // O(active_window) with cursor; was O(total_chart_notes) originally.
        UnityEngine.Profiling.Profiler.BeginSample("NoteSpawn");
        if (isPlaying)
        {
            while (_noteControlSpawnCursor < _noteControlList.Count
                   && _noteControlList[_noteControlSpawnCursor].isEnd)
            {
                _noteControlSpawnCursor++;
            }

            for (var j = _noteControlSpawnCursor; j < _noteControlList.Count; j++)
            {
                var noteControl = _noteControlList[j];
                if (noteControl.isEnd) continue; // ended out of order; skip
                if (noteControl.isPlay) continue; // already spawned
                if (_curFrame < noteControl.frameCreate) break; // rest aren't ready
                var note = noteControl.createNotesBase(_notesCache);
                addNotesBase(note);
            }
        }

        UnityEngine.Profiling.Profiler.EndSample();

        #endregion

        #region NoteUpdateState

        UnityEngine.Profiling.Profiler.BeginSample("NoteUpdateState");
        for (var node = _pNotesList.First; node != null; node = node.Next)
        {
            node.Value.updateState(bCheckPlay: true, bDrawModel: true);
        }

        UnityEngine.Profiling.Profiler.EndSample();

        #endregion

        #region NoteCheckEnd

        // Detects notes that finished this frame and returns their GameObjects
        // to the pool. Notes before the cursor are already isEnd=true (skipped).
        // Notes with frameCreate > _curFrame have never been spawned (isPlay=false),
        // so checkEnd() is provably a no-op for them — break at that boundary.
        UnityEngine.Profiling.Profiler.BeginSample("NoteCheckEnd");
        for (var k = _noteControlSpawnCursor; k < _noteControlList.Count; k++)
        {
            var nc = _noteControlList[k];
            if (nc.frameCreate > _curFrame) break;
            nc.checkEnd();
        }

        UnityEngine.Profiling.Profiler.EndSample();

        #endregion

        #region NoteRemoveEnd

        UnityEngine.Profiling.Profiler.BeginSample("NoteRemoveEnd");
        _pNotesList.removeAllEnd(_notesCache);
        UnityEngine.Profiling.Profiler.EndSample();

        #endregion

        #region NoteCount

        UnityEngine.Profiling.Profiler.BeginSample("NoteCount");
        _curNotes.reset();
        _curShells.reset();
        for (var node2 = _pNotesList.First; node2 != null; node2 = node2.Next)
        {
            var value = node2.Value;
            var noteType = value.getNoteType();
            if (noteType != NoteType.MAX)
            {
                _curNotes[(int)noteType]++;
            }
            else
            {
                var shellType = value.getShellType();
                if (shellType != Shells.MAX)
                {
                    _curShells[(int)shellType]++;
                }
            }
        }

        for (var l = 0; l < 8; l++)
        {
            _maxNotes[l] = Mathf.Max(_maxNotes[l], _curNotes[l]);
        }

        for (var m = 0; m < 10; m++)
        {
            _maxShells[m] = Mathf.Max(_maxShells[m], _curShells[m]);
        }

        UnityEngine.Profiling.Profiler.EndSample();

        #endregion

        #region GameLogic

        UnityEngine.Profiling.Profiler.BeginSample("GameLogic");
        var technicalRankID = TechnicalRankID.Invalid;
        switch (GameOption.abort)
        {
            case UserOptionValue.eAbort.SSS:
                technicalRankID = TechnicalRankID.SSS;
                break;
            case UserOptionValue.eAbort.SS:
                technicalRankID = TechnicalRankID.SS;
                break;
            case UserOptionValue.eAbort.S:
                technicalRankID = TechnicalRankID.S;
                break;
        }

        if (_gameEngine.counters.isDead)
        {
            _retireResult = RetireResult.NoLife;
        }

        if (_retireResult == RetireResult.None && technicalRankID != TechnicalRankID.Invalid)
        {
            var techScoreEnable = _gameEngine.counters.getTechScoreEnable();
            var lower = technicalRankID.getLower();
            if (techScoreEnable < lower)
            {
                _retireResult = RetireResult.ScoreRetire;
            }
        }

        if (_retireResult != 0)
        {
            _gameEngine.killPlayer();
        }

        if (isTutorial())
        {
            _isForceFinish = tutorialManager.isEnd;
            _isFinishNote = tutorialManager.isEnd;
        }
        else
        {
            _isFinishNote = _noteControlList.isAllEnd;
        }

        _isFinishPlay = !Singleton<GameSound>.instance.gameBGM.isPlay && _curFrame > _frameNoteEnd && _isFinishNote;
        calcGuideSE();
        UnityEngine.Profiling.Profiler.EndSample();

        #endregion

        #region SceneObjects

        UnityEngine.Profiling.Profiler.BeginSample("SceneObjects");
        _fieldObject.update();
        attackRouteManager.update();
        updateTimingEvent();
        UnityEngine.Profiling.Profiler.EndSample();

        #endregion

        #region LED

        UnityEngine.Profiling.Profiler.BeginSample("LED");
        if (!isTutorial() && !isPlaying)
        {
            led.setGameColor(isForce: true);
        }

        if (isTutorial())
        {
            tutorialManager.update();
        }

        _gameEngine.isPlayerContinuouslyAttcking = isHoldAnyNotes();
        led.execute();
        UnityEngine.Profiling.Profiler.EndSample();

        #endregion

        UnityEngine.Profiling.Profiler.EndSample();
    }

}
