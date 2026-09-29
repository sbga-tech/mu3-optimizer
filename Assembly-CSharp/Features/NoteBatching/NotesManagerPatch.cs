using System.Collections.Generic;
using MonoMod;
using MU3.Notes;
using UnityEngine;

namespace MU3.Mod.NoteBatching;

[MonoModIfFlag(nameof(PatchConfig.NoteBatching))]
[MonoModPatch("global::MU3.Notes.NotesManager")]
public class NotesManagerPatch : NotesManager
{
    // rqBase → (material + mesh key → instanced material).
    private Dictionary<int, Dictionary<int, Material>> _instancedMaterials;

    // Uses (noteType << 16 | index) to avoid collisions across NotesCache pools.
    private HashSet<int> _processedItems;

    private HashSet<int> _mirrorApplied;

    [MonoModIgnore] private NotesCacheList _noteCacheList;

    // Returns true and sets rq for note types whose render queue is
    // reassigned by the optimization. Other types keep their original queue.
    private static bool getOptimalRenderQueue(NoteModel.Type noteType, out int rq)
    {
        // Tap End should be below other notes in case it masks them.
        // Tap (2DNotes) and Wall (TransparentCutout) must use different queues
        // because mixing them destroys GPU instancing. Flick uses two materials,
        // so it also needs a separate queue from Tap.
        switch (noteType)
        {
            case NoteModel.Type.TapEndR:
            case NoteModel.Type.TapEndB:
            case NoteModel.Type.TapEndG:
            case NoteModel.Type.TapEndRA:
            case NoteModel.Type.TapEndGA:
            case NoteModel.Type.TapEndW:
            case NoteModel.Type.TapEndK:
                rq = 2550;
                return true;

            case NoteModel.Type.KnockLEndV:
            case NoteModel.Type.KnockREndV:
            case NoteModel.Type.KnockLEndP:
            case NoteModel.Type.KnockREndP:
            case NoteModel.Type.KnockLEndW:
            case NoteModel.Type.KnockREndW:
            case NoteModel.Type.KnockLEndK:
            case NoteModel.Type.KnockREndK:
                rq = 2560;
                return true;

            case NoteModel.Type.TapR:
            case NoteModel.Type.TapB:
            case NoteModel.Type.TapG:
            case NoteModel.Type.TapRA:
            case NoteModel.Type.TapGA:
            case NoteModel.Type.TapW:
            case NoteModel.Type.TapK:
            case NoteModel.Type.ExR:
            case NoteModel.Type.ExB:
            case NoteModel.Type.ExG:
            case NoteModel.Type.ExRA:
            case NoteModel.Type.ExGA:
            case NoteModel.Type.ExW:
            case NoteModel.Type.ExK:
                rq = 2600;
                return true;

            case NoteModel.Type.KnockLV:
            case NoteModel.Type.KnockRV:
            case NoteModel.Type.KnockLP:
            case NoteModel.Type.KnockRP:
            case NoteModel.Type.KnockLW:
            case NoteModel.Type.KnockRW:
            case NoteModel.Type.KnockLK:
            case NoteModel.Type.KnockRK:
            case NoteModel.Type.ExKnockLV:
            case NoteModel.Type.ExKnockRV:
            case NoteModel.Type.ExKnockLP:
            case NoteModel.Type.ExKnockRP:
            case NoteModel.Type.KnockHLV:
            case NoteModel.Type.KnockHRV:
            case NoteModel.Type.KnockHLP:
            case NoteModel.Type.KnockHRP:
            case NoteModel.Type.KnockHLW:
            case NoteModel.Type.KnockHRW:
            case NoteModel.Type.KnockHLK:
            case NoteModel.Type.KnockHRK:
            case NoteModel.Type.ExKnockHLV:
            case NoteModel.Type.ExKnockHRV:
            case NoteModel.Type.ExKnockHLP:
            case NoteModel.Type.ExKnockHRP:
                rq = 2610;
                return true;

            case NoteModel.Type.FlickL:
            case NoteModel.Type.FlickR:
            case NoteModel.Type.ExFlickL:
            case NoteModel.Type.ExFlickR:
                rq = 2590;
                return true;

            // Mine types use vertex coloring instead of UV mapping. Group each
            // type contiguously without batching different types together.
            case NoteModel.Type.ShellNormal:
                rq = 2655;
                return true;
            case NoteModel.Type.ShellHard:
                rq = 2660;
                return true;
            case NoteModel.Type.ShellDanger:
                rq = 2665;
                return true;
            case NoteModel.Type.NeedleNormal:
                rq = 2670;
                return true;
            case NoteModel.Type.NeedleHard:
                rq = 2675;
                return true;
            case NoteModel.Type.NeedleDanger:
                rq = 2680;
                return true;
            case NoteModel.Type.RectNormal:
                rq = 2685;
                return true;
            case NoteModel.Type.RectHard:
                rq = 2690;
                return true;
            case NoteModel.Type.RectDanger:
                rq = 2695;
                return true;

            default:
                rq = 0;
                return false;
        }
    }
    // Alpha-tested note bodies keep writing depth. A small per-queue bias
    // preserves the native hold-end -> flick -> tap order for coplanar quads
    // without changing material sharing inside any existing batch.
    private static float getCoplanarDepthOffset(int renderQueue)
    {
        switch (renderQueue)
        {
            case 2590:
                return -1f;
            case 2600:
                return -2f;
            default:
                return 0f;
        }
    }


    [MonoModReplace]
    public new NotesCacheItem createNoteModel(NoteModel noteModel)
    {
        if (_instancedMaterials == null)
            _instancedMaterials = new Dictionary<int, Dictionary<int, Material>>();
        if (_processedItems == null)
            _processedItems = new HashSet<int>();
        if (_mirrorApplied == null)
            _mirrorApplied = new HashSet<int>();

        var notesCache = _noteCacheList[(int)noteModel.type];
        var notesCacheItem = notesCache.pop();

        var itemKey = ((int)noteModel.type << 16) | notesCacheItem.index;
        if (!_processedItems.Contains(itemKey))
        {
            _processedItems.Add(itemKey);

            var rqBase = getOptimalRenderQueue(noteModel.type, out var rq)
                ? rq
                : noteModel.renderQueue;

            if (!_instancedMaterials.TryGetValue(rqBase, out var matCache))
            {
                matCache = new Dictionary<int, Material>();
                _instancedMaterials[rqBase] = matCache;
            }

            var renderers = notesCacheItem.go.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                var shared = renderer.sharedMaterial;
                if (shared == null)
                    continue;

                // Only the same material + mesh can share a queue safely.
                var meshFilter = renderer.GetComponent<MeshFilter>();
                var mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                const int prime = 397;
                var matMeshKey = (shared.GetInstanceID() * prime) ^
                                 (mesh != null ? mesh.GetInstanceID() : 0);

                if (!matCache.TryGetValue(matMeshKey, out var instanced))
                {
                    instanced = new Material(shared)
                    {
                        renderQueue = rqBase + matCache.Count
                    };
                    if (instanced.HasProperty("_DepthOffset"))
                        instanced.SetFloat("_DepthOffset", getCoplanarDepthOffset(rqBase));
                    matCache[matMeshKey] = instanced;
                }

                renderer.sharedMaterial = instanced;
            }
        }

        if (noteModel.mirror && !_mirrorApplied.Contains(itemKey))
        {
            _mirrorApplied.Add(itemKey);
            var renderers = notesCacheItem.go.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                renderer.transform.rotation = Quaternion.Euler(0f, 0f, 180f) *
                                              renderer.transform.rotation;
            }
        }

        return notesCacheItem;
    }
}
