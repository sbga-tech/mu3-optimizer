using MonoMod.InlineRT;

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

        [IniField("Optimization", "ActiveNoteTraversal", 1)]
        public static bool ActiveNoteTraversal;

        [IniField("Optimization", "LaneGeometryCulling", 1)]
        public static bool LaneGeometryCulling;

        [IniField("Optimization", "CachedNoteVisibility", 1)]
        public static bool CachedNoteVisibility;

        [IniField("Optimization", "PrimitiveMeshEmission", 1)]
        public static bool PrimitiveMeshEmission;

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

        static PatchConfig()
        {
            MonoModRule.Flag.Set(nameof(NoImageBloom), NoImageBloom);
            MonoModRule.Flag.Set(nameof(RenderLayers), RenderLayers);
            MonoModRule.Flag.Set(nameof(SwingJointPhysics), SwingJointPhysics);
            MonoModRule.Flag.Set(nameof(InactiveMirrorIndicator), InactiveMirrorIndicator);
            MonoModRule.Flag.Set(nameof(NoUICameraDuringPlay), NoUICameraDuringPlay);
            MonoModRule.Flag.Set(nameof(GpuTextScroll), GpuTextScroll);
            MonoModRule.Flag.Set(nameof(LoginRequestsBatching), LoginRequestsBatching);
            MonoModRule.Flag.Set(nameof(AsyncLoginRequests), AsyncLoginRequests);
            MonoModRule.Flag.Set(nameof(NoteBatching), NoteBatching);
            MonoModRule.Flag.Set(nameof(ActiveNoteTraversal), ActiveNoteTraversal);
            MonoModRule.Flag.Set(nameof(LaneGeometryCulling), LaneGeometryCulling);
            MonoModRule.Flag.Set(nameof(CachedNoteVisibility), CachedNoteVisibility);
            MonoModRule.Flag.Set(nameof(PrimitiveMeshEmission), PrimitiveMeshEmission);
            MonoModRule.Flag.Set(nameof(UVAnimation), UVAnimation);
            MonoModRule.Flag.Set(nameof(CollabSocketCaching), CollabSocketCaching);
            MonoModRule.Flag.Set(nameof(CollabHeartbeatCaching), CollabHeartbeatCaching);
            MonoModRule.Flag.Set(nameof(CollabMemberListCompaction), CollabMemberListCompaction);
            MonoModRule.Flag.Set(nameof(InlinedAMDaemonCalls), InlinedAMDaemonCalls);
        }
    }
}

#if MU3_ASSEMBLY_CSHARP
namespace MU3.Battle
{
    [MonoMod.IniConfig]
    public static class RenderLayersConfig
    {
        [MonoMod.IniField("Optimization.RenderLayers", "StageFPS")]
        public static float StageFPS;

        [MonoMod.IniField("Optimization.RenderLayers", "BGMergeFPS")]
        public static float BGMergeFPS;

        [MonoMod.IniField("Optimization.RenderLayers", "FXFPS", 30)]
        public static float FXFPS;

        [MonoMod.IniField("Optimization.RenderLayers", "DisableShadows", 1)]
        public static bool DisableShadows;

        static RenderLayersConfig()
        {
        }
    }
}

namespace MU3
{
    [MonoMod.IniConfig]
    public static class SwingJointPhysicsConfig
    {
        // 0 or negative runs costume physics every rendered frame.
        [MonoMod.IniField("Optimization.SwingJointPhysics", "SwingJointFPS", 60)]
        public static float SwingJointFPS;

        // Uses the embedded native solver only after a bit-exact live transform audit.
        [MonoMod.IniField("Optimization.SwingJointPhysics", "SwingJointNative", 0)]
        public static bool SwingJointNative;

        static SwingJointPhysicsConfig()
        {
        }
    }
}
#endif
