using System;
using System.Collections.Generic;
using System.Reflection;
using FX;
using MU3.Battle;
using UnityEngine;

namespace MU3.Mod.GameplayPrewarm;

internal static class GameplayPrewarmCompilation
{
    private static bool _complete;

    internal static void Prepare()
    {
        if (_complete)
            return;

        // Measured first-use paths at wave/boss transitions. GetFunctionPointer
        // compiles the named entrypoint on Unity Mono without invoking gameplay;
        // it may run its declaring type's initializer, but does not warm callees.
        Compile(typeof(CharacterSound), "play", "stop", "get_curFrame");
        Compile(typeof(ANM_PLY_EnemyLife_00), "setImageBoss", "changeWave");
        Compile(typeof(EnemyZako), "Enter_Run", "run", "Execute_Run");
        Compile(typeof(Skill.SkillManager), "isMatchEnemyAttribute", "isMatchJudgeCount",
            "isTimingEvent", "isMatchCombo", "isTimingNote", "calcResultOutput",
            "isMatchNotesJudge", "isMatchLane", "isMatchTechScore", "isMatchNotesField",
            "isMatchNotestype");
        Compile(typeof(GameEngine), "startWaveShiftEffect", "startOverkillEffect", "playBossSound",
            "instantiateGameplayPrewarmEffect");
        Compile(typeof(EnemyBoss), "hit", "Execute_Entry", "getTargetPos", "forward",
            "playSound", "Enter_Entry");
        Compile(typeof(CharacterObject), "playSound", "leaveField");
        Compile(typeof(Enemy), "damageSmall");
        Compile(typeof(ResourceManager), "getBossBattleUIResource");
        Compile(typeof(EnemyManager.EnemyLife), "get_isKillAny");
        Compile(typeof(UIEnemyInfo), "setImageBoss");
        Compile(typeof(FX_CopyMat_SetParam), "Start", "Update");
        Compile(typeof(Billboard), "Update");
        Compile(typeof(Time), "get_timeSinceLevelLoad");
        Compile(typeof(Transform), "LookAt");
        Compile(typeof(Evt_WaveShift), "setParam", "Awake", "Start", "Update");
        Compile(typeof(FX_Destroy), "Start");
        Compile(typeof(FX_Dbg_AutoMotion_StateLoop), "Start", "Update");
        Compile(typeof(FX_GenerateSingle), "Start");
        Compile(typeof(GameplayPrewarmSetup), "AcquireTransitionEffect");
        Compile(typeof(GameplayPrewarmEffects), "Prepare", "TryTake", "Clear");

        // Copies exercise judgement/score presentation, not the live-pool wrappers.
        Compile(typeof(UIJudge), "play");
        Compile(typeof(UIJudge.JudgeAnmList), "getAnm");
        Compile(typeof(Notes.NotesManager), "createNoteEffect");
        Compile(typeof(EffectManager), "createEffect");
        Compile(typeof(EffectManager.EffectCache), "create");
        Compile(typeof(ObjectCache), "tryPop");
        Compile(typeof(Notes.NotesManager).Assembly.GetType("MU3.Notes.Util"), "setRenderQueue");

        // MonoMod creates these only when the RenderLayers wrappers are applied.
        Compile(typeof(GameEngine), required: false,
            "orig_startWaveShiftEffect", "orig_startOverkillEffect");

        var soundParameter = typeof(CharacterSound).GetNestedType("SoundParam",
            BindingFlags.Public | BindingFlags.NonPublic);
        Compile(typeof(Dictionary<,>).MakeGenericType(typeof(int), soundParameter), "TryGetValue");
        // This obtains the delegate trampoline, not Mono's first-invoke marshal wrapper.
        Compile(typeof(Action<EnemyBoss>), "Invoke");

        _complete = true;
    }

    private static void Compile(Type type, params string[] names)
    {
        Compile(type, true, names);
    }

    private static void Compile(Type type, bool required, params string[] names)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        foreach (var name in names)
        {
            var found = false;
            foreach (var method in methods)
            {
                if (method.Name != name)
                    continue;
                _ = method.MethodHandle.GetFunctionPointer();
                found = true;
            }
            if (!found && required)
                throw new MissingMethodException(type.FullName, name);
        }
    }
}
