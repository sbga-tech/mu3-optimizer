
using MonoMod;
using UnityEngine;

namespace MU3;

[MonoModIfFlag("NoUICameraDuringPlay")]
public class patch_BattleUI : BattleUI
{
    private Canvas[] _cachedCanvases;
    
    private RenderMode[] _origRenderModes;

    private extern void orig_Awake();

    private Camera _worldCamera;

    private void Awake()
    {
        orig_Awake();
        _cachedCanvases = GetComponentsInChildren<Canvas>();
        _origRenderModes = new RenderMode[_cachedCanvases.Length];
        _worldCamera = Camera.main;
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
            canvas.worldCamera = _worldCamera;
            canvas.enabled = true;
        }
    }
}