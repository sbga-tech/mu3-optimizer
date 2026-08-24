using MonoMod;

[MonoModIfFlag("PrimitiveMeshEmission")]
public class patch_NotesPrimitiveManager : NotesPrimitiveManager
{
    [MonoModIgnore] private PrimitiveMesh[] _mesh;

    /// <summary>
    /// The PrimitiveMesh for a MeshType when its fast emission buffers are
    /// bound, else null (caller falls back to the original List.Add path).
    /// </summary>
    internal patch_PrimitiveMesh fastMesh(int type)
    {
        var mesh = (patch_PrimitiveMesh)_mesh[type];
        return mesh.fastReady ? mesh : null;
    }
}
