using MonoMod;

namespace MU3.Mod.PrimitiveMeshEmission;

[MonoModIfFlag(nameof(PatchConfig.PrimitiveMeshEmission))]
[MonoModPatch("global::NotesPrimitiveManager")]
public class NotesPrimitiveManagerPatch : NotesPrimitiveManager
{
    [MonoModIgnore] private PrimitiveMesh[] _mesh;

    /// <summary>
    /// The PrimitiveMesh for a MeshType when its fast emission buffers are
    /// bound, else null (caller falls back to the original List.Add path).
    /// </summary>
    internal PrimitiveMeshPatch fastMesh(int type)
    {
        var mesh = (PrimitiveMeshPatch)_mesh[type];
        return mesh.fastReady ? mesh : null;
    }
}
