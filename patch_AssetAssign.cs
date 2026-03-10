using MonoMod;
using MU3.Mod.Assets;
using Sxc.Unity;
using UnityEngine;

[MonoModIfFlag("BetterNotes")]
public class patch_AssetAssign : AssetAssign
{
    [MonoModIgnore]
    private NoteAssign _noteAssign;
    
    private extern void orig_Awake();
    
    private void LoadNotes(AssetBundle bundle)
    {
        // nt_tap_red.prefab
        // nt_tap_grn.prefab
        // nt_tap_blu.prefab
        var tapRed = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_tap_red.prefab");
        var tapGreen = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_tap_grn.prefab");
        var tapBlue = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_tap_blu.prefab");
        var tapBlack = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_tap_blk.prefab");
        var tapWhite = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_tap_wht.prefab");
        
        var tapRedAlt = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_tap_win.prefab");
        var tapGreenAlt = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_tap_ygr.prefab");
        
        _noteAssign.tapRed = tapRed;
        _noteAssign.tapGreen = tapGreen;
        _noteAssign.tapBlue = tapBlue;
        _noteAssign.tapBlack = tapBlack;
        _noteAssign.tapWhite = tapWhite;
        _noteAssign.tapRedAlt = tapRedAlt;
        _noteAssign.tapGreenAlt = tapGreenAlt;
        
        // nt_ex_red.prefab
        // nt_ex_grn.prefab
        // nt_ex_blu.prefab
        var tapExRed = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_ex_red.prefab");
        var tapExGreen = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_ex_grn.prefab");
        var tapExBlue = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_ex_blu.prefab");
        var tapExRedAlt = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_ex_win.prefab");
        var tapExGreenAlt = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_ex_ygr.prefab");
        
        _noteAssign.crTapRed = tapExRed;
        _noteAssign.crTapGreen = tapExGreen;
        _noteAssign.crTapBlue = tapExBlue;
        _noteAssign.crTapRedAlt = tapExRedAlt;
        _noteAssign.crTapGreenAlt = tapExGreenAlt;
        
        // nt_end_red.prefab
        // nt_end_grn.prefab
        // nt_end_blu.prefab
        var holdEndRed = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_end_red.prefab");
        var holdEndGreen = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_end_grn.prefab");
        var holdEndBlue = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_end_blu.prefab");

        
        _noteAssign.holdEndRed = holdEndRed;
        _noteAssign.holdEndGreen = holdEndGreen;
        _noteAssign.holdEndBlue = holdEndBlue;
        _noteAssign.holdEndRedAlt = holdEndRed;
        _noteAssign.holdEndGreenAlt = holdEndGreen;
        
        // nt_tap_vio.prefab
        // nt_tap_pur.prefab
        var wallLeft = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_tap_vio.prefab");
        var wallRight = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_tap_pur.prefab");
        var wallBlack = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_tap_sblk.prefab");
        var wallWhite = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_tap_swht.prefab");
        
        _noteAssign.sideTapViolet = wallLeft;
        _noteAssign.sideTapPurple = wallRight;
        _noteAssign.sideTapBlack = wallBlack;
        _noteAssign.sideTapWhite = wallWhite;
        
        // nt_ex_vio.prefab
        // nt_ex_pur.prefab
        var wallExLeft = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_ex_vio.prefab");
        var wallExRight = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_ex_pur.prefab");
        
        _noteAssign.sideCrTapViolet = wallExLeft;
        _noteAssign.sideCrTapPurple = wallExRight;
        
        // nt_hold_vio.prefab
        // nt_hold_pur.prefab
        var wallHoldLeft = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_hold_vio.prefab");
        var wallHoldRight = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_hold_pur.prefab");
        var wallHoldBlack = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_hold_blk.prefab");
        var wallHoldWhite = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_hold_wht.prefab");
        
        _noteAssign.sideHoldViolet = wallHoldLeft;
        _noteAssign.sideHoldPurple = wallHoldRight;
        _noteAssign.sideHoldBlack = wallHoldBlack;
        _noteAssign.sideHoldWhite = wallHoldWhite;
        
        // nt_exhold_vio.prefab
        // nt_exhold_pur.prefab
        var wallExHoldLeft = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_exhold_vio.prefab");
        var wallExHoldRight = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_exhold_pur.prefab");
        
        _noteAssign.sideCrHoldViolet = wallExHoldLeft;
        _noteAssign.sideCrHoldPurple = wallExHoldRight;
        
        // nt_holdend_vio.prefab
        // nt_holdend_pur.prefab
        var wallHoldEndLeft = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_holdend_vio.prefab");
        var wallHoldEndRight = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_holdend_pur.prefab");
        var holdEndBlack = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_end_blk.prefab");
        var holdEndWhite = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_end_wht.prefab");
        
        _noteAssign.sideHoldEndViolet = wallHoldEndLeft;
        _noteAssign.sideHoldEndPurple = wallHoldEndRight;
        _noteAssign.sideHoldEndBlack = holdEndBlack;
        _noteAssign.sideHoldEndWhite = holdEndWhite;
        
        var flick = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_flick.prefab");
        var flickEx = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_ex_flick.prefab");
        
        _noteAssign.flick = flick;
        _noteAssign.crFlick = flickEx;
        
        var mineRed = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_mine_red.prefab");
        var minePurple = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_mine_pur.prefab");
        
        _noteAssign.shellNormal = mineRed;
        _noteAssign.shellHard = minePurple;
        
        var mineNeedleRed = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_mineneedle2_red.prefab");
        var mineNeedlePurple = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_mineneedle2_pur.prefab");
        var mineNeedleBlack = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_mineneedle2_blk.prefab");
        
        _noteAssign.needleNormal = mineNeedleRed;
        _noteAssign.needleHard = mineNeedlePurple;
        _noteAssign.needleDanger = mineNeedleBlack;
        
        var mineRectRed = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_minerect2_red.prefab");
        var mineRectPurple = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_minerect2_pur.prefab");
        var mineRectBlack = bundle.LoadAsset<GameObject>("Assets/GameObject/nt_minerect2_blk.prefab");
        
        _noteAssign.rectNormal = mineRectRed;
        _noteAssign.rectHard = mineRectPurple;
        _noteAssign.rectDanger = mineRectBlack;
        


        // var groundNotes = new GameObject[]
        // {
        //     tapRed, tapGreen, tapBlue, tapBlack, tapWhite,
        //     tapRedAlt, tapGreenAlt,
        //     tapExRed, tapExGreen, tapExBlue, tapExRedAlt, tapExGreenAlt,
        //     holdEndRed, holdEndGreen, holdEndBlue, wallHoldEndLeft, wallHoldEndRight
        // };
        //
        // var notesUnlit = bundle.LoadAsset<Material>("Assets/Material/NotesUnlit.mat");
        //
        // foreach (var groundNote in groundNotes)
        // {
        //     foreach (var renderer in groundNote.GetComponentsInChildren<Renderer>())
        //     {
        //         if (renderer.gameObject.name != "color") continue;
        //         renderer.sharedMaterial = notesUnlit;
        //         break;
        //     }
        // }
        //
        // var wallNotes = new GameObject[]
        // {
        //     wallLeft, wallRight, wallBlack, wallWhite,
        //     wallExLeft, wallExRight,
        //     wallHoldLeft, wallHoldRight, wallHoldBlack, wallHoldWhite,
        //     wallExHoldLeft, wallExHoldRight
        // };
    }
    
    private new void Awake()
    {
        var bundle = AssetBundle.LoadFromMemory(NoteOptimizationBundle.Data);
        if (bundle != null)
        {
            LoadNotes(bundle);
        }
        else
        {
            Debug.LogError("Failed to load note optimization asset bundle!");
        }

        bundle.Unload(false);

        orig_Awake();
        
#if ENABLE_PROFILER
        // Validate that all note assigns resolved to a prefab.
        ValidateNoteAssigns();
        // Attach batch debug tool — press F6 to dump report.
        gameObject.AddComponent<MU3.Notes.BatchDebug>();
#endif
    }

    private void ValidateNoteAssigns()
    {
        var assigns = note.assigns;
        var names = System.Enum.GetNames(typeof(Note.Type));
        for (var i = 0; i < assigns.Length; i++)
        {
            var name = i < names.Length ? names[i] : i.ToString();
            if (assigns[i] == null)
                Debug.LogError($"[NoteOpt] note.assigns[{i}] ({name}) is NULL!");
            else
                Debug.Log($"[NoteOpt] note.assigns[{i}] ({name}) = {assigns[i].name}");
        }
    }
}