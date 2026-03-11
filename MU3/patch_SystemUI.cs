using System;
using System.Collections.Generic;
using MonoMod;
using MU3.Sequence;
using UnityEngine;
using UnityEngine.UI;

namespace MU3;

[MonoModIfFlag("NoUICameraDuringPlay")]
public class patch_SystemUI : SystemUI
{
    [MonoModIgnore]
    private List<Canvas> _canvasList;

    [MonoModIgnore]
    private Camera _camera;

    private bool _optiEnabled;
    
    public static event Action<bool> OnUIOptimizeToggle;

    private void Optimize()
    {
        if (_optiEnabled)
            return;

        _optiEnabled = true;
        OnUIOptimizeToggle?.Invoke(_optiEnabled);

        foreach (var canvas in _canvasList)
        {
            // canvases assigned to Camera.main are layered WITH the scene —
            // pulling them to Overlay would break their depth relationship
            if (canvas.sortingOrder <= -1000)
                continue;

            canvas.enabled = false;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.enabled = true;
        }
        //Canvas.ForceUpdateCanvases();

        _camera.enabled = false;
    }

    private void Deoptimize()
    {
        if (!_optiEnabled)
            return;

        _optiEnabled = false;
        OnUIOptimizeToggle?.Invoke(_optiEnabled);
        
        _camera.enabled = true;

        foreach (var canvas in _canvasList)
        {
            if (canvas.sortingOrder <= -1000)
                continue;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            // Unity clears worldCamera on mode switch — reassign immediately
            // so we don't get a frame where it falls back to Camera.main
            canvas.worldCamera = _camera;
        }
    }

    private bool IsRequiredUIExist()
    {
        foreach (var canvas in _canvasList)
        {
            if (canvas.sortingOrder is (int) Graphics.Const.SortOrder.Dialog or (int) Graphics.Const.SortOrder.UI)
                return true;
        }

        return false;
    }
    
    public extern void orig_execute();

    public new void execute()
    {
        if (patch_PlayMusic.IsPlayingMusic && !IsRequiredUIExist())
            Optimize();
        else
            Deoptimize();

        if (!_optiEnabled)
        {
            orig_execute();
        }
    }
}