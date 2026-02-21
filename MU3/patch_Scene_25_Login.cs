using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using MonoMod;
using MonoMod.Utils;
using MU3.Client;
using MU3.Data;
using MU3.Operation;
using MU3.User;
using MU3.Util;
using UnityEngine;

namespace MU3;

[MonoModIfFlag("BoostLoginRequests")]
public class patch_Scene_25_Login : Scene_25_Login
{

    [MonoModIgnore]
    private enum State
    {
        Aime,
        PurchaseGP,
        Login,
        Logout,
        GetGameMusicReleaseState,
        UserData_Start,
        GetUserData,
        GetUserMusicItem,
        GetUserBoss,
        GetUserMusic,
        GetUserRival,
        GetUserRivalData,
        GetUserRivalMusic,
        GetUserTechCount,
        GetUserCard,
        GetUserStory,
        GetUserCharacter,
        GetUserChapter,
        GetUserMemoryChapter,
        GetUserDeck,
        GetUserDeckSkin,
        GetUserTrainingRoom,
        GetUserOption,
        GetUserActivityPlay,
        GetUserActivityMusic,
        GetUserRatinglog,
        GetUserRecentRating,
        GetUserPlate,
        GetUserTrophy,
        GetUserPresent,
        GetUserLimitBreakItem,
        GetUserProfileVoice,
        GetUserGachaTicket,
        GetUserKaikaItem,
        GetUserExpUpItem,
        GetUserIntimateUpItem,
        GetUserBookItem,
        GetUserSystemVoice,
        GetUserCostume,
        GetUserAttachment,
        GetUserUnlockItem,
        GetUserEventPoint,
        GetUserEventRanking,
        GetUserMissionPoint,
        GetUserLoginBonus,
        GetUserRegion,
        GetUserScenario,
        GetUserTradeItem,
        GetUserEventMusic,
        GetUserTechEvent,
        GetUserTechEventRanking,
        GetUserMapEvent,
        GetUserKop,
        UserData_End,
        ExchangeGP,
        Error
    }

    private bool _isLoggingIn;
    private bool _loginError;

    private Dictionary<State, StateExecution> _activeStates;

    private Mode<Scene_25_Login, State> mode_;

    private Packet packet_;

    private List<Packet> packets_ = new List<Packet>();
    
    private float accumulatedTime_;

    private class StateExecution
    {
        public State state;
        public bool initCalled;
        public bool completed;
        public bool error;
    }

    private Dictionary<State, FastReflectionDelegate> _initMethods;
    private Dictionary<State, FastReflectionDelegate> _procMethods;

    [MonoModIgnore]
    private extern void toNetworkError();

    [MonoModReplace]
    [MonoModConstructor]
    public void ctor()
    {
        _activeStates = new Dictionary<State, StateExecution>();
        _initMethods = new Dictionary<State, FastReflectionDelegate>();
        _procMethods = new Dictionary<State, FastReflectionDelegate>();
    }

    private void CacheMethodsForState(State state)
    {
        if (!_initMethods.ContainsKey(state))
        {
            var initMethod = GetType().GetMethod($"{state}_Init",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _initMethods[state] = initMethod.CreateFastDelegate();
        }

        if (!_procMethods.ContainsKey(state))
        {
            var procMethod = GetType().GetMethod($"{state}_Proc",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _procMethods[state] = procMethod.CreateFastDelegate();
        }
    }

    [MonoModReplace]
    private void Update()
    {
        if (_isLoggingIn)
        {
            if (mode_.get() == (int)State.Error)
            {
                StopAllParallelOperations();
            }
        }
        else
        {
            mode_.update();
        }
        updateProgressIndicatorState();
    }

    [MonoModReplace]
    private void updateProgressIndicatorState()
    {
        SystemUI instance = SingletonMonoBehaviour<SystemUI>.instance;
        if (instance == null)
            return;
        bool flag1 = _isLoggingIn;
        bool flag2 = instance.isProgressIndicator();
        if (flag1 && !flag2)
        {
            instance.showProgressIndicator();
        }
        else
        {
            if (flag1 || !flag2)
                return;
            instance.hideProgressIndicator();
        }
    }

    private void StopAllParallelOperations()
    {
        Debug.LogError("[QuickLogin] Error detected - stopping all parallel operations");
        _loginError = true;
        _isLoggingIn = false;

        foreach (var kvp in _activeStates)
        {
            kvp.Value.error = true;
        }

        StopAllCoroutines();

        _activeStates.Clear();
    }


    private extern void orig_GetUserData_Proc();

    private void GetUserData_Proc()
    {
        // Call original GetUserData_Proc logic
        orig_GetUserData_Proc();

        // After GetUserData completes, start parallel loading
        if ((int) mode_.get() > (int) State.GetUserData && !_isLoggingIn)
        {
            _isLoggingIn = true;
            _loginError = false;
            _activeStates.Clear();
            StartCoroutine(ParallelLoadAllUserData());
        }
    }

    // private extern void orig_invokeOnFinish(int status);
    //
    // private void invokeOnFinish(int status)
    // {
    //     Debug.Log("[QuickLogin] invokeOnFinish called with status: " + status);
    //     orig_invokeOnFinish(status);
    // }
    
    List<bool> rivalMusicFetched_;
    Dictionary<long, Dictionary<int, MU3.User.UserRivalMusic>> rivalMusicCache_;

    [MonoModReplace]
    private void GetUserRivalMusic_Init()
    {
        var rivals = Singleton<UserManager>.instance.UserRivalData.ToArray();
        if (rivals.Length < 1)
        {
            mode_.set(State.GetUserTechCount);
            return;
        }

        packets_ = new List<Packet>();
        rivalMusicFetched_ = new List<bool>();

        foreach (var rival in rivals)
        {
            var packet = new PacketGetUserRivalMusic([rival]);
            packets_.Add(packet);
            rivalMusicFetched_.Add(false); // Not yet done
        }

        rivalMusicCache_ = new Dictionary<long, Dictionary<int, MU3.User.UserRivalMusic>>();
    }

    [MonoModReplace]
    private void GetUserRivalMusic_Proc()
    {
        bool allDone = true;
    
        for (int i = 0; i < packets_.Count; i++)
        {
            if (rivalMusicFetched_[i]) continue; // Skip completed packets
        
            var result = packets_[i].proc();
        
            if (result == Packet.State.Done)
            {
                rivalMusicFetched_[i] = true;
            
                // Cache this packet's results before next packet overwrites singleton
                var singletonData = Singleton<UserManager>.instance.UserRivalMusic;
                foreach (var rivalKvp in singletonData)
                {
                    if (!rivalMusicCache_.ContainsKey(rivalKvp.Key))
                    {
                        rivalMusicCache_[rivalKvp.Key] = new Dictionary<int, User.UserRivalMusic>();
                    }
                    foreach (var musicKvp in rivalKvp.Value)
                    {
                        rivalMusicCache_[rivalKvp.Key][musicKvp.Key] = musicKvp.Value;
                    }
                }
            }
            else if (result == Packet.State.Error)
            {
                toNetworkError();
                return;
            }
            else
            {
                allDone = false; // At least one packet still processing
            }
        }
    
        if (allDone)
        {
            // Restore merged results to singleton
            Singleton<UserManager>.instance.UserRivalMusic = rivalMusicCache_;
            mode_.set(State.GetUserTechCount);
        }

    }

    private PacketGetUserTradeItem _makeTradeItemPacket(int num, int num2)
    {
        var packet = new PacketGetUserTradeItem();
        packet.addChapterRange(num, num2);
        packet.start();
        return packet;
    }
    
    List<bool> tradeItemsFetched_;
    Dictionary<int, Dictionary<int, User.UserTradeItem>> savedTradeItemDict_;
    

    [MonoModReplace]
    private void GetUserTradeItem_Init()
    {
        savedTradeItemDict_ = Singleton<UserManager>.instance.userTradeItem;
        Singleton<UserManager>.instance.userTradeItem = 
            new Dictionary<int, Dictionary<int, MU3.User.UserTradeItem>>();
        
        packets_ = new List<Packet>();
        tradeItemsFetched_ = new List<bool>();
        
        OperationManager instance = Singleton<OperationManager>.instance;
        List<int> list = new List<int>();
        ICollection<ChapterData> allChapterData = SingletonStateMachine<DataManager, DataManager.EState>.instance.allChapterData;
        foreach (ChapterData item in allChapterData)
        {
            if ((!item.isEventChapter || instance.isActiveEventChapterId(item.id)) && item.isSelectable)
            {
                list.Add(item.id);
            }
        }
        list.Sort();
        int num = list[0];
        int num2 = num;
        foreach (int item2 in list)
        {
            if (item2 > num2 + 1)
            {
                packets_.Add(_makeTradeItemPacket(num, num2));
                tradeItemsFetched_.Add(false);
                num = item2;
            }
            num2 = item2;
        }
        packets_.Add(_makeTradeItemPacket(num, num2));
        tradeItemsFetched_.Add(false);
    }

    [MonoModReplace]
    private void GetUserTradeItem_Proc()
    {
        bool allDone = true;
    
        for (int i = 0; i < packets_.Count; i++)
        {
            if (tradeItemsFetched_[i]) continue;
        
            var result = packets_[i].proc();
        
            if (result == Packet.State.Done)
            {
                tradeItemsFetched_[i] = true;
            }
            else if (result == Packet.State.Error)
            {
                // Restore original singleton on error
                Singleton<UserManager>.instance.userTradeItem = savedTradeItemDict_;
                toNetworkError();
                return;
            }
            else
            {
                allDone = false;
            }
        }
    
        if (allDone)
        {
            savedTradeItemDict_ = Singleton<UserManager>.instance.userTradeItem;
            mode_.set(State.GetUserTechCount);
        }

    }

    private IEnumerator ParallelLoadAllUserData()
    {
        var stopwatch = Stopwatch.startNew();
        
        Debug.Log("[QuickLogin] Starting quick login");

        var tasks = new List<Coroutine>();

        tasks.Add(StartCoroutine(ExecuteSequential(new[] {
            State.GetUserMusicItem,
            State.GetUserBoss,
            State.GetUserMusic
        })));

        tasks.Add(StartCoroutine(ExecuteSequential(new[] {
            State.GetUserRival,
            State.GetUserRivalData,
            State.GetUserRivalMusic
        })));
        tasks.Add(StartCoroutine(ExecuteSequential(new[] {
            State.GetUserActivityPlay,
            State.GetUserActivityMusic
        })));
        tasks.Add(StartCoroutine(ExecuteSequential(new[] {
            State.GetUserRatinglog,
            State.GetUserRecentRating
        })));
        tasks.Add(StartCoroutine(ExecuteSequential(new[] {
            State.GetUserEventPoint,
            State.GetUserEventRanking
        })));

        tasks.Add(StartCoroutine(ExecuteParallel(new[] {
            State.GetUserTechCount,
            State.GetUserCard,
            State.GetUserCharacter,
            State.GetUserStory,
            State.GetUserChapter,
            State.GetUserMemoryChapter,
            State.GetUserDeck,
            State.GetUserDeckSkin,
            State.GetUserTrainingRoom,
            State.GetUserOption
        })));


        tasks.Add(StartCoroutine(ExecuteParallel(new[] {
            State.GetUserPlate,
            State.GetUserTrophy,
            State.GetUserPresent,
            State.GetUserLimitBreakItem,
            State.GetUserProfileVoice,
            State.GetUserGachaTicket,
            State.GetUserKaikaItem,
            State.GetUserExpUpItem,
            State.GetUserIntimateUpItem,
            State.GetUserBookItem,
            State.GetUserSystemVoice,
            State.GetUserCostume,
            State.GetUserAttachment,
            State.GetUserUnlockItem
        })));

        tasks.Add(StartCoroutine(ExecuteParallel(new[] {
            State.GetUserMissionPoint,
            State.GetUserLoginBonus,
            State.GetUserRegion,
            State.GetUserScenario,
            State.GetUserEventMusic,
            State.GetUserTechEvent,
            State.GetUserTechEventRanking
        })));

        tasks.Add(StartCoroutine(ExecuteParallel(new[] {
            State.GetUserTradeItem,
            State.GetUserMapEvent,
            State.GetUserKop
        })));

        // var progressPrinter = StartCoroutine(printProgress());

        foreach (var task in tasks)
        {
            yield return task;
            if (_loginError) { HandleError(); yield break; }
        }

        // StopCoroutine(progressPrinter);
        
        stopwatch.Stop();
        
        var endTimeMillis = stopwatch.ElapsedMilliseconds;
        var origTimeMillis = accumulatedTime_;
        
        var endTime = endTimeMillis / 1000 + "s";
        var origTime = origTimeMillis / 1000 + "s";
        
        var percentageSaved = ((origTimeMillis - endTimeMillis) / origTimeMillis) * 100f;

        Debug.Log("[QuickLogin] Quick login completed successfully. Time taken: " + origTime + "->" + endTime + " (" + percentageSaved.ToString("F2") + "% faster)");
        _isLoggingIn = false;

        mode_.set(State.ExchangeGP);
    }

    private IEnumerator printProgress()
    {
        while (_isLoggingIn && !_loginError)
        {
            var stringBuilder = new StringBuilder();
            foreach (var state in _activeStates.Keys)
            {
                stringBuilder.Append(state.ToString() + ", ");
            }
            Debug.Log("[QuickLogin] Active states: " + stringBuilder.ToString().TrimEnd(',', ' ') + " (" + _activeStates.Count + " states)");
            yield return new WaitForSeconds(1f);
        }
    }
    private IEnumerator ExecuteSequential(State[] states)
    {
        foreach (var state in states)
        {
            yield return StartCoroutine(ExecuteState(state));
            if (_loginError) yield break;
        }
    }

    private IEnumerator ExecuteParallel(State[] states)
    {
        var tasksLeft = new List<State>(states);

        foreach (var state in states)
        {
            StartCoroutine(ExecuteStateCoroutine(state, tasksLeft));
        }

        while (tasksLeft.Count > 0 && !_loginError)
        {
            yield return null; 
        }
    }

    private IEnumerator ExecuteStateCoroutine(State state, List<State> tasksLeft)
    {
        yield return StartCoroutine(ExecuteState(state));
        tasksLeft.Remove(state);
    }

    private class Stopwatch
    {
        private long _accumulatedTicks = 0L;
        private long _startTicks;
        private bool _isRunning = false;

        public static Stopwatch startNew() {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            return stopwatch;
        }

        public void Start()
        {
            if (!_isRunning)
            {
                _startTicks = DateTime.Now.Ticks;
                _isRunning = true;
            }
        }

        public void Stop()
        {
            if (_isRunning)
            {
                long elapsed = DateTime.Now.Ticks - _startTicks;
                _accumulatedTicks += elapsed;
                _isRunning = false;
            }
        }

        public float ElapsedMilliseconds
        {
            get
            {
                long currentElapsed = _accumulatedTicks;
                if (_isRunning)
                {
                    currentElapsed += DateTime.Now.Ticks - _startTicks;
                }
                return (float)(currentElapsed / TimeSpan.TicksPerMillisecond);
            }
        }
    }

    private IEnumerator ExecuteState(State state)
    {
        var stopwatch = Stopwatch.startNew();

        var execution = new StateExecution { state = state };
        _activeStates[state] = execution;

        //Debug.Log($"[QuickLogin] ExecuteState({state}) started");

        // Save current packet to prevent corruption
        var originalPacket = packet_;
        var originalPacketsList = packets_;

        // Cache methods if not already cached
        CacheMethodsForState(state);

        var initMethod = _initMethods[state];

        int startingMode = mode_.get();

        if (initMethod != null)
        {
            try
            {
                initMethod(this);
                execution.initCalled = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[QuickLogin] Error in {state}_Init: {e.Message}");
                execution.error = true;
                _loginError = true;
                // Restore packet
                packet_ = originalPacket;
                packets_ = originalPacketsList;

                stopwatch.Stop();
                accumulatedTime_ += stopwatch.ElapsedMilliseconds;
                yield break;
            }
        }

        int modeAfterInit = mode_.get();
        if (modeAfterInit != startingMode)
        {
            mode_.set((State)startingMode);
            execution.completed = true;

            if (modeAfterInit == (int)State.Error)
            {
                Debug.LogError($"[QuickLogin] Error in {state}_Init");
                execution.error = true;
                _loginError = true;
            }

            packet_ = originalPacket;
            packets_ = originalPacketsList;
            _activeStates.Remove(state);

            stopwatch.Stop();
            accumulatedTime_ += stopwatch.ElapsedMilliseconds;
            yield break;
        }

        var myPacket = packet_;
        var myPacketsList = packets_;

        if (myPacket == null && (myPacketsList == null || myPacketsList.Count == 0))
        {
            Debug.LogError($"[QuickLogin] No packet created for {state}");
            execution.error = true;
            _loginError = true;
            packet_ = originalPacket;

            stopwatch.Stop();
            accumulatedTime_ += stopwatch.ElapsedMilliseconds;
            yield break;
        }

        packet_ = originalPacket;
        packets_ = originalPacketsList;

        var procMethod = _procMethods[state];

        if (procMethod == null)
        {
            Debug.LogError($"[QuickLogin] No Proc method for {state}");
            execution.error = true;
            _loginError = true;

            stopwatch.Stop();
            accumulatedTime_ += stopwatch.ElapsedMilliseconds;
            yield break;
        }

        while (!execution.completed && !execution.error && !_loginError)
        {
            packet_ = myPacket;
            packets_ = myPacketsList;

            try
            {
                procMethod(this);

                int currentMode = mode_.get();
                if (currentMode != startingMode)
                {
                    mode_.set((State)startingMode);
                    execution.completed = true;

                    if (currentMode == (int)State.Error)
                    {
                        Debug.LogError($"[QuickLogin] Error in {state}_Proc");
                        execution.error = true;
                        _loginError = true;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[QuickLogin] Error in {state}_Proc: {e.Message}");
                execution.error = true;
                _loginError = true;
            }

            packet_ = originalPacket;
            packets_ = originalPacketsList;

            if (_loginError)
            {
                break;
            }

            yield return null;
        }

        packet_ = originalPacket;
        packets_ = originalPacketsList;
        _activeStates.Remove(state);

        stopwatch.Stop();
        var elapsed = stopwatch.ElapsedMilliseconds;
        accumulatedTime_ += elapsed;
        //Debug.Log($"[QuickLogin] ExecuteState({state}) completed in {elapsed}ms, total accumulated: {accumulatedTime_}ms");
    }

    private void HandleError()
    {
        Debug.LogError("[QuickLogin] Quick login failed");
        _isLoggingIn = false;
        mode_.set(State.Error);
    }
}
