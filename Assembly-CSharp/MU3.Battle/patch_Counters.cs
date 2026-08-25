using MonoMod;
using MU3.Notes;
using MU3.Sound;
using MU3.User;
using MU3.Util;
using UnityEngine;

namespace MU3.Battle;

[MonoModIfFlag("ScorePresentation")]
public class patch_Counters : Counters
{
    private const int ScoreTypeCount = (int)ScoreType.Max;
    private const int AllScoreTypesDirty = (1 << ScoreTypeCount) - 1;

    [MonoModIgnore] private int[] _scores;
    [MonoModIgnore] private long _tsjNoteTotal;
    [MonoModIgnore] private long _tsjBellLost;
    [MonoModIgnore] private long _tsjPlatinumScoreCurrent;
    [MonoModIgnore] private long _tsjPlatinumScoreLost;
    [MonoModIgnore] private UserOptionValue.eAbort _eAbort;
    [MonoModIgnore] private long _retireScoreCurrent;

    private int _scorePresentationDirty;
    private bool _platinumPresentationDirty;
    private bool _retirePresentationDirty;
    private object _presentedBattleUI;
    private int[] _presentedScores;
    private int _presentedScoreMask;
    private bool _presentedPlatinum;
    private int _presentedPlatinumCurrent;
    private int _presentedPlatinumTotal;
    private int _presentedPlatinumMaximum;
    private bool _presentedRetire;
    private int _presentedRetireScore;

    private void bindPresentationCache(object battleUI)
    {
        if (ReferenceEquals(_presentedBattleUI, battleUI))
            return;

        _presentedBattleUI = battleUI;
        if (_presentedScores == null)
            _presentedScores = new int[ScoreTypeCount];
        _presentedScoreMask = 0;
        _presentedPlatinum = false;
        _presentedRetire = false;
    }

    [MonoModReplace]
    private void updateUIScore(ScoreType type)
    {
        if (type == ScoreType.Max)
        {
            _scorePresentationDirty = AllScoreTypesDirty;
            return;
        }

        _scorePresentationDirty |= 1 << (int)type;
    }

    [MonoModReplace]
    private void updatePlatinumScore()
    {
        _tsjPlatinumScoreLost = (_tsjBellLost + _scores[(int)ScoreType.BulletHitCount]) * 2L;
        var current = _tsjPlatinumScoreCurrent - _tsjPlatinumScoreLost;
        if (current < 0L)
            current = 0L;

        setScore(ScoreType.PlatinumScoreCurrent, (int)current);
        _platinumPresentationDirty = true;
    }

    [MonoModReplace]
    private void updateUIRetire(long score)
    {
        if (_eAbort == UserOptionValue.eAbort.OFF)
            return;

        var current = (long)Mathf.Max((int)score, 0);
        if (current < _retireScoreCurrent)
        {
            var volume = GameOption.volSkill;
            if (volume >= 0.01f)
                Singleton<SoundManager>.instance.playVolume(321, false, volume);
        }

        _retireScoreCurrent = current;
        _retirePresentationDirty = true;
    }

    public void flushScorePresentation()
    {
        var scoreDirty = _scorePresentationDirty;
        var platinumDirty = _platinumPresentationDirty;
        var retireDirty = _retirePresentationDirty;
        if (scoreDirty == 0 && !platinumDirty && !retireDirty)
            return;

        var gameEngine = SingletonMonoBehaviour<GameEngine>.instance;
        if (gameEngine == null || gameEngine.battleUI == null)
            return;
        var battleUI = gameEngine.battleUI;
        bindPresentationCache(battleUI);

        _scorePresentationDirty = 0;
        _platinumPresentationDirty = false;
        _retirePresentationDirty = false;
        for (var i = 0; i < ScoreTypeCount; i++)
        {
            var bit = 1 << i;
            if ((scoreDirty & bit) == 0)
                continue;

            var value = _scores[i];
            if ((_presentedScoreMask & bit) != 0 && _presentedScores[i] == value)
                continue;

            _presentedScores[i] = value;
            _presentedScoreMask |= bit;
            battleUI.setScore((ScoreType)i, value);
        }

        if (platinumDirty)
        {
            var current = _tsjPlatinumScoreCurrent - _tsjPlatinumScoreLost;
            if (current < 0L)
                current = 0L;
            var currentValue = (int)current;
            var totalValue = _scores[(int)ScoreType.PlatinumScoreTotal];
            var maximumValue = (int)(_tsjNoteTotal / MU3.Notes.Const.volumeJudgeTechMax * 2L);
            if (!_presentedPlatinum
                || _presentedPlatinumCurrent != currentValue
                || _presentedPlatinumTotal != totalValue
                || _presentedPlatinumMaximum != maximumValue)
            {
                _presentedPlatinum = true;
                _presentedPlatinumCurrent = currentValue;
                _presentedPlatinumTotal = totalValue;
                _presentedPlatinumMaximum = maximumValue;
                battleUI.updatePlatinumScoreUI(currentValue, totalValue, maximumValue);
            }
        }

        if (retireDirty)
        {
            var retireValue = (int)_retireScoreCurrent;
            if (!_presentedRetire || _presentedRetireScore != retireValue)
            {
                _presentedRetire = true;
                _presentedRetireScore = retireValue;
                battleUI.setRetireScore(retireValue);
            }
        }
    }
}
