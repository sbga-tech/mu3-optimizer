using System;
using System.Runtime.InteropServices;
using MonoMod;
using MU3.App;
using MU3.Mod.Native;
using UnityEngine;

namespace MU3.Mod.LowLatency;

[MonoModIfFlag(nameof(PatchConfig.LowLatency))]
internal static class FlipModel
{
    private enum FlipModelStatus
    {
        Success = 0,
        Unsupported = 0x00060001,
        AlreadyAttempted = 0x00060002,
        InvalidPlayerImage = 0x00060003,
        TargetResolutionFailed = 0x00060004,
        NoSwapChain = 0x00060005,
        VtableProtectFailed = 0x00060006,
        Pending = 0x00060007,
        SwapChainUnreadable = 0x00060008,
        FlipChainUnavailable = 0x00060009,
        SwapChainLost = 0x0006000A
    }

    private const string LogPrefix = "[Steroid][LowLatency] ";
    private const int ReportAfterFrames = 600;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate FlipModelStatus StatusDelegate();

    private static StatusDelegate _state;

    [OnStateEnter(nameof(ApplicationMU3.EState.WaitAMDaemonReady))]
    [MonoModIfFlag(nameof(PatchConfig.LowLatency))]
    internal static void Start(ApplicationMU3 application)
    {
        if (!MonoMod.LowLatencyConfig.FlipModel)
            return;
        try
        {
            var module = ModulesRegistry.LoadLibrary("mu3latency.dll");
            var status = module.GetFunction<StatusDelegate>("mu3_flip_model_init")();
            if (status != FlipModelStatus.Success)
            {
                Debug.LogWarning(LogPrefix + "flip model not active: " + Describe(status) + ".");
                return;
            }

            _state = module.GetFunction<StatusDelegate>("mu3_flip_model_state");
            Screen.SetResolution(Screen.width, Screen.height, Screen.fullScreen);
            application.gameObject.AddComponent<Reporter>();
        }
        catch (Exception exception)
        {
            Debug.LogError(LogPrefix + "flip model activation failed: " + exception);
        }
    }

    private static string Describe(FlipModelStatus status)
    {
        return status + " (0x" + ((int)status).ToString("X8") + ")";
    }

    private sealed class Reporter : MonoBehaviour
    {
        private int _frames;

        private void Update()
        {
            var status = _state();
            if (status == FlipModelStatus.Pending && ++_frames < ReportAfterFrames)
                return;
            if (status == FlipModelStatus.Success)
                Debug.Log(LogPrefix + "flip model active.");
            else
                Debug.LogWarning(LogPrefix + "flip model not active: " + Describe(status) + ".");
            Destroy(this);
        }
    }
}
