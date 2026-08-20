using MonoMod;
using UnityEngine;

namespace MU3.Notes;

[MonoModIfFlag("BetterNotes")]
public class patch_NoteModel
{

    [MonoModIgnore] public new float scale;

    [MonoModReplace]
    public new Vector3 getScale()
    {
        // Mirror is now handled in createNoteModel().
        return Vector3.one * scale;
    }
}