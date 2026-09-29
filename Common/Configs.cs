namespace MonoMod
{
    [IniConfig]
    public static class PatchConfig
    {
        [IniField("Optimization", "NoImageBloom", 1)]
        public static bool NoImageBloom;

        [IniField("Optimization", "RenderLayers", 1)]
        public static bool RenderLayers;

        [IniField("Optimization", "SwingJointPhysics", 1)]
        public static bool SwingJointPhysics;

        [IniField("Optimization", "InactiveMirrorIndicator", 1)]
        public static bool InactiveMirrorIndicator;

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

        [IniField("Optimization", "ActiveNoteTraversal", 0)]
        public static bool ActiveNoteTraversal;

        [IniField("Optimization", "PrimitiveMeshEmission", 0)]
        public static bool PrimitiveMeshEmission;

        [IniField("Optimization", "GameplayPrewarm", 0)]
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
    }

    [IniConfig]
    public static class RenderLayersConfig
    {
        [IniField("Optimization.RenderLayers", "StageFPS")]
        public static float StageFPS;

        [IniField("Optimization.RenderLayers", "BGMergeFPS")]
        public static float BGMergeFPS;

        [IniField("Optimization.RenderLayers", "FXFPS", 30)]
        public static float FXFPS;

        [IniField("Optimization.RenderLayers", "DisableShadows", 1)]
        public static bool DisableShadows;
    }

    [IniConfig]
    public static class SwingJointPhysicsConfig
    {
        // 0 or negative runs costume physics every rendered frame.
        [IniField("Optimization.SwingJointPhysics", "SwingJointFPS", 60)]
        public static float SwingJointFPS;

        // Uses the embedded native solver only after a bit-exact live transform audit.
        [IniField("Optimization.SwingJointPhysics", "SwingJointNative", 0)]
        public static bool SwingJointNative;
    }

    [IniConfig]
    public static class UnityPlayerHooksConfig
    {
        [IniField("Optimization.UnityPlayerHooks", "GpuFenceWait", 1)]
        public static bool GpuFenceWait;

        [IniField("Optimization.UnityPlayerHooks", "JobSignalBatching", 1)]
        public static bool JobSignalBatching;
    }
}
