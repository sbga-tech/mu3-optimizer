using System;
using System.Runtime.InteropServices;
using MonoMod;
using MU3.App;
using MU3.Mod.Native;
using UnityEngine;

namespace MU3.Mod.UnityPlayerHooks;

/// <summary>One startup for the independently enabled player hooks; only GPU waits shut down.</summary>
internal static class PlayerHooks
{
    [Flags]
    private enum HookFlags : uint
    {
        None = 0,
        GpuWait = 1,
        JobSignal = 2,
        GraphicsJobs = 4,
        GpuSkinning = 8
    }

    // Match the encoded i32 statuses in Native/mu3player/src/status.zig:
    // upper 16 bits identify the hook; lower 16 bits identify its reason.
    private enum PlayerStatus
    {
        Success = 0,
        GpuUnsupported = 0x00010001,
        GpuAlreadyInitialized = 0x00010002,
        GpuInvalidPlayerImage = 0x00010003,
        GpuTargetResolutionFailed = 0x00010004,
        GpuMinHookFailed = 0x00010005,
        GpuHookFailed = 0x00010006,
        GpuTlsAllocationFailed = 0x00010007,
        GpuShutdownFailed = 0x00010008,
        GpuInactive = 0x00010009,
        JobUnsupported = 0x00020001,
        JobAlreadyAttempted = 0x00020002,
        JobInvalidPlayerImage = 0x00020003,
        JobTargetResolutionFailed = 0x00020004,
        JobMinHookFailed = 0x00020005,
        JobHookFailed = 0x00020006,
        GraphicsJobsUnsupported = 0x00030001,
        GraphicsJobsAlreadyAttempted = 0x00030002,
        GraphicsJobsInvalidPlayerImage = 0x00030003,
        GraphicsJobsTargetResolutionFailed = 0x00030004,
        GraphicsJobsMinHookFailed = 0x00030005,
        GraphicsJobsHookFailed = 0x00030006,
        GraphicsJobsDeviceUnavailable = 0x00030007,
        GpuSkinningUnsupported = 0x00040001,
        GpuSkinningAlreadyAttempted = 0x00040002,
        GpuSkinningInvalidPlayerImage = 0x00040003,
        GpuSkinningTargetResolutionFailed = 0x00040004,
        GpuSkinningSettingsUnavailable = 0x00040005,
        InvalidFeature = 0x00000001
    }

    private const string LogPrefix = "[Steroid][UnityPlayerHooks] ";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate PlayerStatus InitDelegate(HookFlags feature);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate PlayerStatus ShutdownDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate PlayerStatus WaitedDelegate(out ulong nanoseconds);

    private static readonly object Sync = new object();
    private static bool _started;
    private static bool _gpuInitialized;
    private static bool _finished;
    private static ShutdownDelegate _shutdown;
    private static WaitedDelegate _waited;
    private static Host _host;

    [OnStateEnter(nameof(ApplicationMU3.EState.WaitAMDaemonReady))]
    [MonoModIfFlag(nameof(PatchConfig.UnityPlayerHooks))]
    internal static void TryStart(ApplicationMU3 application)
    {
        var flags = (MonoMod.UnityPlayerHooksConfig.GpuFenceWait ? HookFlags.GpuWait : HookFlags.None)
            | (MonoMod.UnityPlayerHooksConfig.JobSignalBatching ? HookFlags.JobSignal : HookFlags.None)
            | (MonoMod.UnityPlayerHooksConfig.GraphicsJobs ? HookFlags.GraphicsJobs : HookFlags.None)
            | (MonoMod.UnityPlayerHooksConfig.GpuSkinning ? HookFlags.GpuSkinning : HookFlags.None);
        if (flags == HookFlags.None)
            return;
        lock (Sync)
        {
            if (_started)
                return;
            _started = true;
        }

        try
        {
            Start(flags, application.gameObject);
        }
        catch (Exception exception)
        {
            Debug.LogError(LogPrefix + "activation failed: " + exception);
            if (_gpuInitialized)
                Finish("activation-error");
        }
    }

    private static void Start(HookFlags flags, GameObject hostObject)
    {
        var module = ModulesRegistry.LoadLibrary("mu3player.dll");
        var init = module.GetFunction<InitDelegate>("mu3_player_init");
        if ((flags & HookFlags.GpuWait) != HookFlags.None)
        {
            _shutdown = module.GetFunction<ShutdownDelegate>("mu3_player_shutdown");
            if (MonoMod.UnityPlayerHooksConfig.AdaptiveFrameQueue)
                _waited = module.GetFunction<WaitedDelegate>("mu3_gpu_wait_ns");
        }

        if ((flags & HookFlags.JobSignal) != HookFlags.None)
            ReportJobSignal(init(HookFlags.JobSignal));
        // Must run on the main thread: native init records its device as the one whose deletes stay immediate.
        if ((flags & HookFlags.GraphicsJobs) != HookFlags.None)
            ReportGraphicsJobs(init(HookFlags.GraphicsJobs));
        if ((flags & HookFlags.GpuSkinning) != HookFlags.None)
            ReportGpuSkinning(init(HookFlags.GpuSkinning));
        if ((flags & HookFlags.GpuWait) != HookFlags.None)
            StartGpuWait(init(HookFlags.GpuWait), hostObject);
    }

    private static void ReportJobSignal(PlayerStatus status)
    {
        if (status != PlayerStatus.Success)
            Debug.LogWarning(LogPrefix + "JobSignalBatching native init returned " + status
                + " (0x" + ((int)status).ToString("X8") + "); original signal behavior preserved.");
    }

    private static void ReportGraphicsJobs(PlayerStatus status)
    {
        if (status != PlayerStatus.Success)
            Debug.LogWarning(LogPrefix + "GraphicsJobs native init returned " + status
                + " (0x" + ((int)status).ToString("X8") + "); graphics jobs not switched on.");
    }

    private static void ReportGpuSkinning(PlayerStatus status)
    {
        if (status != PlayerStatus.Success)
            Debug.LogWarning(LogPrefix + "GpuSkinning native init returned " + status
                + " (0x" + ((int)status).ToString("X8") + "); CPU skinning kept.");
    }

    private static void StartGpuWait(PlayerStatus status, GameObject hostObject)
    {
        if (status != PlayerStatus.Success)
        {
            Debug.LogWarning(LogPrefix + "GpuFenceWait native init returned " + status
                + " (0x" + ((int)status).ToString("X8") + "); original GPU wait preserved.");
            return;
        }

        _gpuInitialized = true;
        AttachHost(hostObject);
        if (_waited != null)
            _host.queue = new FrameQueue(QualitySettings.maxQueuedFrames);
    }

    private static void AttachHost(GameObject hostObject)
    {
        if (hostObject == null)
        {
            hostObject = new GameObject("GpuFenceWait");
            UnityEngine.Object.DontDestroyOnLoad(hostObject);
        }

        _host = hostObject.GetComponent<Host>();
        if (_host == null)
            _host = hostObject.AddComponent<Host>();
    }

    private static void Finish(string reason)
    {
        if (_finished)
            return;
        _finished = true;
        if (_host != null)
            _host.enabled = false;

        if (!_gpuInitialized)
            return;

        try
        {
            var rc = _shutdown();
            if (rc != PlayerStatus.Success)
                Debug.LogError(LogPrefix + "GpuFenceWait native shutdown returned " + rc
                    + " (0x" + ((int)rc).ToString("X8") + ") during " + reason);
        }
        catch (Exception exception)
        {
            Debug.LogError(LogPrefix + "GpuFenceWait native shutdown failed during "
                + reason + ": " + exception);
        }
        _gpuInitialized = false;
    }

    private sealed class Host : MonoBehaviour
    {
        internal FrameQueue queue;

        private void Update()
        {
            if (queue != null && !queue.Tick())
                queue = null;
        }

        private void OnApplicationQuit() { Finish("quit"); }
        private void OnDestroy() { Finish("destroy"); }
    }

    private sealed class FrameQueue
    {
        private const float WindowSeconds = 0.5f;
        private const double MaxWaitShare = 0.2;
        private const float FirstRetrySeconds = 10f;
        private const float LongestRetrySeconds = 80f;
        private const float SettledSeconds = 60f;

        private readonly int _original;
        private float _windowStart;
        private ulong _windowWaited;
        private float _queuedSince;
        private float _retryAt;
        private float _retryDelay = FirstRetrySeconds;

        internal FrameQueue(int original)
        {
            _original = original;
        }

        internal bool Tick()
        {
            var now = Time.realtimeSinceStartup;
            if (_waited(out var waited) != PlayerStatus.Success)
            {
                Apply(_original, "the GPU fence wait is no longer active");
                return false;
            }

            if (QualitySettings.vSyncCount > 0)
            {
                Apply(_original, "VSync is on");
                _retryAt = now;
                return true;
            }

            if (QualitySettings.maxQueuedFrames != 1)
            {
                if (now < _retryAt)
                    return true;
                Apply(1, "trying a queued frame");
                _queuedSince = now;
                Restart(now, waited);
                return true;
            }

            var elapsed = now - _windowStart;
            if (elapsed < WindowSeconds)
                return true;
            var share = (waited - _windowWaited) / (elapsed * 1e9);
            if (share > MaxWaitShare)
            {
                Apply(_original, "the render thread waited " + (share * 100).ToString("F0")
                    + "% of the time for the GPU");
                _retryAt = now + _retryDelay;
                _retryDelay = Math.Min(_retryDelay * 2f, LongestRetrySeconds);
            }
            else if (now - _queuedSince >= SettledSeconds)
            {
                _retryDelay = FirstRetrySeconds;
            }
            Restart(now, waited);
            return true;
        }

        private void Restart(float now, ulong waited)
        {
            _windowStart = now;
            _windowWaited = waited;
        }

        private static void Apply(int frames, string reason)
        {
            if (QualitySettings.maxQueuedFrames == frames)
                return;
            QualitySettings.maxQueuedFrames = frames;
            Debug.Log(LogPrefix + "AdaptiveFrameQueue: " + frames + " queued frame(s); " + reason + ".");
        }
    }
}
