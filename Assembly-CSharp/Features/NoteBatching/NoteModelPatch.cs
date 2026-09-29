using MonoMod;
using MU3.Notes;
using UnityEngine;

namespace MU3.Mod.NoteBatching;

[MonoModIfFlag(nameof(PatchConfig.NoteBatching))]
[MonoModPatch("global::MU3.Notes.NoteModel")]
public class NoteModelPatch
{

    [MonoModIgnore] public new float scale;

    [MonoModReplace]
    public new Vector3 getScale()
    {
        // Mirror is now handled in createNoteModel().
        return Vector3.one * scale;
    }
}