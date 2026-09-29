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
        JobSignal = 2
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
        JobUnsupported = 0x00020001,
        JobAlreadyAttempted = 0x00020002,
        JobInvalidPlayerImage = 0x00020003,
        JobTargetResolutionFailed = 0x00020004,
        JobMinHookFailed = 0x00020005,
        JobHookFailed = 0x00020006,
        InvalidFeature = 0x00000001
    }

    private const string LogPrefix = "[Steroid][UnityPlayerHooks] ";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate PlayerStatus InitDelegate(HookFlags feature);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate PlayerStatus ShutdownDelegate();

    private static readonly object Sync = new object();
    private static bool _started;
    private static bool _gpuInitialized;
    private static bool _finished;
    private static ShutdownDelegate _shutdown;
    private static Host _host;

    [OnStateEnter(nameof(ApplicationMU3.EState.WaitAMDaemonReady))]
    [MonoModIfFlag(nameof(PatchConfig.UnityPlayerHooks))]
    internal static void TryStart(ApplicationMU3 application)
    {
        var flags = (MonoMod.UnityPlayerHooksConfig.GpuFenceWait ? HookFlags.GpuWait : HookFlags.None)
            | (MonoMod.UnityPlayerHooksConfig.JobSignalBatching ? HookFlags.JobSignal : HookFlags.None);
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
            _shutdown = module.GetFunction<ShutdownDelegate>("mu3_player_shutdown");

        if ((flags & HookFlags.JobSignal) != HookFlags.None)
            ReportJobSignal(init(HookFlags.JobSignal));
        if ((flags & HookFlags.GpuWait) != HookFlags.None)
            StartGpuWait(init(HookFlags.GpuWait), hostObject);
    }

    private static void ReportJobSignal(PlayerStatus status)
    {
        if (status != PlayerStatus.Success)
            Debug.LogWarning(LogPrefix + "JobSignalBatching native init returned " + status
                + " (0x" + ((int)status).ToString("X8") + "); original signal behavior preserved.");
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
        private void OnApplicationQuit() { Finish("quit"); }
        private void OnDestroy() { Finish("destroy"); }
    }
}
