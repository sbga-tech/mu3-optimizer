namespace MonoMod
{
    [IniConfig]
    public static class PatchConfig
    {
        [IniField("Optimization", "ImageBloom", 1)]
        public static bool ImageBloom;

        [IniField("Optimization", "RenderLayers", 1)]
        public static bool RenderLayers;

        [IniField("Optimization", "SwingJointPhysics", 1)]
        public static bool SwingJointPhysics;

        [IniField("Optimization", "NoUICameraDuringPlay", 0)]
        public static bool NoUICameraDuringPlay;

        [IniField("Optimization", "GpuTextScroll", 1)]
        public static bool GpuTextScroll;

        [IniField("Optimization", "LoginRequestsBatching", 1)]
        public static bool LoginRequestsBatching;

        [IniField("Optimization", "AsyncLoginRequests", 1)]
        public static bool AsyncLoginRequests;

        [IniField("Optimization", "NoteBatching", 1)]
        public static bool NoteBatching;

        [IniField("Optimization", "PrimitiveMeshEmission", 1)]
        public static bool PrimitiveMeshEmission;

        [IniField("Optimization", "GameplayPrewarm", 1)]
        public static bool GameplayPrewarm;

        [IniField("Optimization", "UVAnimation", 1)]
        public static bool UVAnimation;

        [IniField("Optimization", "CollabSocketCaching", 1)]
        public static bool CollabSocketCaching;

        [IniField("Optimization", "CollabHeartbeatCaching", 1)]
        public static bool CollabHeartbeatCaching;

        [IniField("Optimization", "CollabMemberListCompaction", 1)]
        public static bool CollabMemberListCompaction;

        [IniField("Optimization", "InlinedAMDaemonCalls", 1)]
        public static bool InlinedAMDaemonCalls;

        [IniField("Optimization", "UnityPlayerHooks", 1)]
        public static bool UnityPlayerHooks;

        [IniField("Optimization", "LowLatency", 1)]
        public static bool LowLatency;
    }

    [IniConfig]
    public static class ImageBloomConfig
    {
        [IniField("Optimization.ImageBloom", "BloomFPS", 60)]
        public static float BloomFPS;

        [IniField("Optimization.ImageBloom", "NoGlow", 0)]
        public static bool NoGlow;
    }

    [IniConfig]
    public static class RenderLayersConfig
    {
        [IniField("Optimization.RenderLayers", "StageFPS", 60)]
        public static float StageFPS;

        [IniField("Optimization.RenderLayers", "BGMergeFPS", 60)]
        public static float BGMergeFPS;

        [IniField("Optimization.RenderLayers", "FXFPS", 60)]
        public static float FXFPS;

        [IniField("Optimization.RenderLayers", "DisableShadows", 1)]
        public static bool DisableShadows;

        // The FX gate camera renders only on frames where the FX layer redraws.
        [IniField("Optimization.RenderLayers", "ScheduleGateCamera", 0)]
        public static bool ScheduleGateCamera;

        // During transition effects, only the FX layer redraws every frame while the camera is still.
        [IniField("Optimization.RenderLayers", "EventFXOnly", 0)]
        public static bool EventFXOnly;
    }

    [IniConfig]
    public static class SwingJointPhysicsConfig
    {
        // 0 or negative runs costume physics every rendered frame.
        [IniField("Optimization.SwingJointPhysics", "SwingJointFPS", 60)]
        public static float SwingJointFPS;
    }

    [IniConfig]
    public static class UnityPlayerHooksConfig
    {
        [IniField("Optimization.UnityPlayerHooks", "GpuFenceWait", 1)]
        public static bool GpuFenceWait;

        [IniField("Optimization.UnityPlayerHooks", "JobSignalBatching", 1)]
        public static bool JobSignalBatching;

        [IniField("Optimization.UnityPlayerHooks", "GraphicsJobs", 1)]
        public static bool GraphicsJobs;

        [IniField("Optimization.UnityPlayerHooks", "GpuSkinning", 0)]
        public static bool GpuSkinning;
    }

    [IniConfig]
    public static class LowLatencyConfig
    {
        [IniField("Optimization.LowLatency", "ReflexAntiLag", 1)]
        public static bool ReflexAntiLag;

        [IniField("Optimization.LowLatency", "FlipModel", 1)]
        public static bool FlipModel;
    }
}
