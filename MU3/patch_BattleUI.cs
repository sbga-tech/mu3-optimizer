
using MonoMod;
using UnityEngine;

namespace MU3;

[MonoModIfFlag("NoUICameraDuringPlay")]
public class patch_BattleUI : BattleUI
{
    private Canvas[] _cachedCanvases;
    
    private RenderMode[] _origRenderModes;
    private Camera[] _origCameras;

    private extern void orig_Awake();


    private void Awake()
    {
        orig_Awake();
        _cachedCanvases = GetComponentsInChildren<Canvas>();
        _origRenderModes = new RenderMode[_cachedCanvases.Length];
        _origCameras = new Camera[_cachedCanvases.Length];
        patch_SystemUI.OnUIOptimizeToggle += enable =>
        {
            if (enable)
                Optimize();
            else
                Deoptimize();
        };
    }

    private extern void orig_Update();

    private void Update()
    {
        orig_Update();
    }

    private void Optimize()
    {
        if (_cachedCanvases == null)
            return;

        for (var i = 0; i < _cachedCanvases.Length; i++)
        {
            var canvas = _cachedCanvases[i];
            if (canvas == null)
                continue;
            
            _origRenderModes[i] = canvas.renderMode;
            _origCameras[i] = canvas.worldCamera;
            canvas.enabled = false;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.enabled = true;
        }
    }

    //TODO: currently having sorting issue when failing (chara models get on top of UI)
    private void Deoptimize()
    {
        if (_cachedCanvases == null)
            return;

        for (var i = 0; i < _cachedCanvases.Length; i++)
        {
            var canvas = _cachedCanvases[i];
            if (canvas == null)
                continue;
            
            canvas.enabled = false;
            canvas.renderMode = _origRenderModes[i];
            canvas.worldCamera = _origCameras[i];
            canvas.enabled = true;
        }
    }
}