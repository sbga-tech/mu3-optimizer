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

[MonoModIfFlag("BetterNotes")]
public class patch_NotesManager : NotesManager
{
    // Index of the first NoteControl in the sorted list that has not yet ended.
    private int _noteControlSpawnCursor;

    private Dictionary<int, Dictionary<Material, Material>> _instancedMaterials;

    //use (noteType << 16 | index) as key to avoid collisions across NotesCache pools
    private HashSet<int> _processedItems;

    private HashSet<int> _mirrorApplied;

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
    [MonoModIgnore] private NotesCacheList _noteCacheList;

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

    
    // Returns true and sets rq for note types whose render queue is
    // reassigned by the optimization. Other types keep their original queue.
    private static bool getOptimalRenderQueue(NoteModel.Type noteType, out int rq)
    {
        //Render Queue Explanation:
        //Tap End should be below other notes in case that it mask them.
        //Tap(2DNotes) and Wall(TransparentCutout) should NOT be on the same rq because it destroys GPU instancing.
        //Flick and Tap shouldn't be on the same rq because flicks
        //have two materials with could interrupt GPU instancing sequence.
        switch (noteType)
        {
            // TapEnd → 2550
            case NoteModel.Type.TapEndR:
            case NoteModel.Type.TapEndB:
            case NoteModel.Type.TapEndG:
            case NoteModel.Type.TapEndRA:
            case NoteModel.Type.TapEndGA:
            case NoteModel.Type.TapEndW:
            case NoteModel.Type.TapEndK:
                rq = 2550;
                return true;
            // SideHoldEnd → 2551
            case NoteModel.Type.KnockLEndV:
            case NoteModel.Type.KnockREndV:
            case NoteModel.Type.KnockLEndP:
            case NoteModel.Type.KnockREndP:
            case NoteModel.Type.KnockLEndW:
            case NoteModel.Type.KnockREndW:
            case NoteModel.Type.KnockLEndK:
            case NoteModel.Type.KnockREndK:
                rq = 2551;
                return true;

            // Tap → 2600
            case NoteModel.Type.TapR:
            case NoteModel.Type.TapB:
            case NoteModel.Type.TapG:
            case NoteModel.Type.TapRA:
            case NoteModel.Type.TapGA:
            case NoteModel.Type.TapW:
            case NoteModel.Type.TapK:
            // ExTap → 2600
            case NoteModel.Type.ExR:
            case NoteModel.Type.ExB:
            case NoteModel.Type.ExG:
            case NoteModel.Type.ExRA:
            case NoteModel.Type.ExGA:
            case NoteModel.Type.ExW:
            case NoteModel.Type.ExK:
                rq = 2600;
                return true;
            // Side → 2601
            case NoteModel.Type.KnockLV:
            case NoteModel.Type.KnockRV:
            case NoteModel.Type.KnockLP:
            case NoteModel.Type.KnockRP:
            case NoteModel.Type.KnockLW:
            case NoteModel.Type.KnockRW:
            case NoteModel.Type.KnockLK:
            case NoteModel.Type.KnockRK:
            // ExSide → 2601
            case NoteModel.Type.ExKnockLV:
            case NoteModel.Type.ExKnockRV:
            case NoteModel.Type.ExKnockLP:
            case NoteModel.Type.ExKnockRP:
            // SideHold → 2601
            case NoteModel.Type.KnockHLV:
            case NoteModel.Type.KnockHRV:
            case NoteModel.Type.KnockHLP:
            case NoteModel.Type.KnockHRP:
            case NoteModel.Type.KnockHLW:
            case NoteModel.Type.KnockHRW:
            case NoteModel.Type.KnockHLK:
            case NoteModel.Type.KnockHRK:
            // ExSideHold → 2601
            case NoteModel.Type.ExKnockHLV:
            case NoteModel.Type.ExKnockHRV:
            case NoteModel.Type.ExKnockHLP:
            case NoteModel.Type.ExKnockHRP:
                rq = 2601;
                return true;
            // Flick / ExFlick → 2590
            case NoteModel.Type.FlickL:
            case NoteModel.Type.FlickR:
            case NoteModel.Type.ExFlickL:
            case NoteModel.Type.ExFlickR:
                rq = 2590;
                return true;

            default:
                rq = 0;
                return false;
        }
    }

    [MonoModReplace]
    public new NotesCacheItem createNoteModel(NoteModel noteModel)
    {
        if (_instancedMaterials == null) _instancedMaterials = new Dictionary<int, Dictionary<Material, Material>>();
        if (_processedItems == null) _processedItems = new HashSet<int>();
        if (_mirrorApplied == null) _mirrorApplied = new HashSet<int>();

        var notesCache = _noteCacheList[(int)noteModel.type];
        var notesCacheItem = notesCache.pop();

        var itemKey = ((int)noteModel.type << 16) | notesCacheItem.index;
        if (!_processedItems.Contains(itemKey))
        {
            _processedItems.Add(itemKey);

            if (getOptimalRenderQueue(noteModel.type, out var rq))
            {
                // instance materials keyed by target queue so
                // all note types in the same queue share the same instances,
                // while types in different queues get separate instances even
                // if they share the same base material.
                if (!_instancedMaterials.TryGetValue(rq, out var matCache))
                {
                    matCache = new Dictionary<Material, Material>();
                    _instancedMaterials[rq] = matCache;
                }

                var renderers = notesCacheItem.go.GetComponentsInChildren<Renderer>();
                foreach (var renderer in renderers)
                {
                    var shared = renderer.sharedMaterial;
                    if (shared == null) continue;
                    if (shared.renderQueue == rq) continue;

                    if (!matCache.TryGetValue(shared, out var instanced))
                    {
                        instanced = new Material(shared) { renderQueue = rq };
                        matCache[shared] = instanced;
                    }
                    renderer.sharedMaterial = instanced;
                }
            }
            else
            {
                // keyed by base render queue so that
                // different note types sharing the same base rq (e.g. Shell,
                // Needle, Rect all at 2651) reuse the same layered instances.
                var rqFallback = noteModel.renderQueue;

                if (!_instancedMaterials.TryGetValue(rqFallback, out var matCache))
                {
                    matCache = new Dictionary<Material, Material>();
                    _instancedMaterials[rqFallback] = matCache;
                }

                var renderers = notesCacheItem.go.GetComponentsInChildren<Renderer>();
                foreach (var renderer in renderers)
                {
                    var shared = renderer.sharedMaterial;
                    if (shared == null) continue;
                    if (!matCache.TryGetValue(shared, out var instanced))
                    {
                        instanced = new Material(shared)
                        {
                            renderQueue = rqFallback + matCache.Count
                        };
                        matCache[shared] = instanced;
                    }
                    renderer.sharedMaterial = instanced;
                }
            }
        }

        
        //Great Rotation Tech
        if (noteModel.mirror && !_mirrorApplied.Contains(itemKey))
        {
            _mirrorApplied.Add(itemKey);
            var mrs = notesCacheItem.go.GetComponentsInChildren<Renderer>(true);

            // This is clearly problematic but works for now
            foreach (var renderer in mrs)
            {
                renderer.transform.rotation = Quaternion.Euler(0f, 0, 180f) * renderer.transform.rotation;
            }
            
            // MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            // for (int m = 0; m < mrs.Length; m++)
            // {
            //     mrs[m].GetPropertyBlock(mpb);
            //     mpb.SetFloat(_mirrorID, 1f);
            //     mrs[m].SetPropertyBlock(mpb);
            // }
        }

        return notesCacheItem;
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
            node.Value.updateState();
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
            case UserOption.eAbort.SSS:
                technicalRankID = TechnicalRankID.SSS;
                break;
            case UserOption.eAbort.SS:
                technicalRankID = TechnicalRankID.SS;
                break;
            case UserOption.eAbort.S:
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