using System;
using System.Runtime.InteropServices;
using MonoMod;
using MU3.App;
using MU3.Mod.Native;
using UnityEngine;

namespace MU3.Mod.LowLatency;

[MonoModIfFlag(nameof(PatchConfig.LowLatency))]
internal static class LowLatency
{
    private enum Backend : uint
    {
        None = 0,
        Reflex = 1,
        AntiLag2 = 2
    }

    private enum LowLatencyStatus
    {
        Success = 0,
        Unsupported = 0x00050001,
        AlreadyAttempted = 0x00050002,
        DeviceUnavailable = 0x00050003,
        UnsupportedVendor = 0x00050004,
        ReflexUnavailable = 0x00050005,
        AntiLag2Unavailable = 0x00050006,
        Inactive = 0x00050007,
        FrameFailed = 0x00050008,
        ReflexInitializeFailed = 0x00050009,
        ReflexSleepModeFailed = 0x0005000A,
        ReflexStatusFailed = 0x0005000B,
        ReflexNotEnabled = 0x0005000C
    }

    private const string LogPrefix = "[Steroid][LowLatency] ";
    private const int QueuedFrames = 1;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate LowLatencyStatus InitDelegate(IntPtr texture, out Backend backend);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate LowLatencyStatus FrameDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DetailDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate LowLatencyStatus VerifyDelegate(out uint sleepIntervalUs);

    private const int VerifyAfterFrames = 300;

    private static FrameDelegate _frame;
    private static DetailDelegate _detail;
    private static VerifyDelegate _verify;

    [OnStateEnter(nameof(ApplicationMU3.EState.WaitAMDaemonReady))]
    [MonoModIfFlag(nameof(PatchConfig.LowLatency))]
    internal static void Start(ApplicationMU3 application)
    {
        try
        {
            var module = ModulesRegistry.LoadLibrary("mu3latency.dll");
            var init = module.GetFunction<InitDelegate>("mu3_low_latency_init");
            _detail = module.GetFunction<DetailDelegate>("mu3_low_latency_detail");
            var texture = new Texture2D(1, 1);
            texture.Apply();
            var status = init(texture.GetNativeTexturePtr(), out var backend);
            UnityEngine.Object.Destroy(texture);
            if (status != LowLatencyStatus.Success)
            {
                Debug.Log(LogPrefix + "not active: " + Describe(status) + ".");
                return;
            }

            _frame = module.GetFunction<FrameDelegate>("mu3_low_latency_frame");
            _verify = module.GetFunction<VerifyDelegate>("mu3_low_latency_verify");
            QualitySettings.maxQueuedFrames = QueuedFrames;
            application.gameObject.AddComponent<Pacer>();
            Debug.Log(LogPrefix + (backend == Backend.Reflex ? "NVIDIA Reflex" : "AMD Radeon Anti-Lag 2") + " active.");
        }
        catch (Exception exception)
        {
            Debug.LogError(LogPrefix + "activation failed: " + exception);
        }
    }

    [DefaultExecutionOrder(-32000)]
    private sealed class Pacer : MonoBehaviour
    {
        private int _frames;

        private void Update()
        {
            var status = _frame();
            if (status != LowLatencyStatus.Success)
            {
                Debug.LogWarning(LogPrefix + "stopped: " + Describe(status) + ".");
                enabled = false;
                return;
            }

            if (++_frames != VerifyAfterFrames)
                return;
            status = _verify(out var sleepIntervalUs);
            if (status == LowLatencyStatus.Success)
                Debug.Log(LogPrefix + "driver confirms low-latency mode; latest sleep " + sleepIntervalUs + " us.");
            else
                Debug.LogWarning(LogPrefix + "driver does not confirm low-latency mode: " + Describe(status) + ".");
        }
    }

    private static string Describe(LowLatencyStatus status)
    {
        return status + " (0x" + ((int)status).ToString("X8") + ", driver 0x" + _detail().ToString("X8") + ")";
    }
}
