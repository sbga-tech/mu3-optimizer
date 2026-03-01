using System.Collections.Generic;
using MonoMod;
using MU3.Sequence;
using UnityEngine;

namespace MU3;

[MonoModIfFlag("NoUICameraDuringPlay")]
public class patch_SystemUI : SystemUI
{
    
    // public static extern Canvas orig_CreateCanvas(MU3.Graphics.Const.SortOrder sortOrder);
    //
    // public new static Canvas CreateCanvas(MU3.Graphics.Const.SortOrder sortOrder)
    // {
    //     Canvas canvas = orig_CreateCanvas(sortOrder);
    //     canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    //     return canvas;
    // }

    [MonoModIgnore]
    private List<Canvas> _canvasList;
    
    [MonoModIgnore]
    private Camera _camera;
    
    private bool _optiEnabled;
    
    public extern void orig_execute();
    
    private void optimize()
    {
        if (_optiEnabled)
            return;

        _optiEnabled = true;

        foreach (var canvas in _canvasList)
        {
            // canvases assigned to Camera.main are layered WITH the scene —
            // pulling them to Overlay would break their depth relationship
            if (canvas.sortingOrder <= -1000)
                continue;

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        _camera.enabled = false;
    }

    private void deoptimize()
    {
        if (!_optiEnabled)
            return;

        _optiEnabled = false;
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
    
    private bool isRequiredUIExist()
    {
        foreach (var canvas in _canvasList)
        {
            if (canvas.sortingOrder is (int) Graphics.Const.SortOrder.Dialog or (int) Graphics.Const.SortOrder.UI)
                return true;
        }

        return false;
    }
    
    public new void execute()
    {
        var rootScript = RootScript.instance as patch_RootScript;
        
        if (rootScript != null)
        {
            if (rootScript.isPlayingMusic() && !(isRequiredUIExist() || rootScript.getPlayMusicState() < PlayMusic.EState.Countdown))
            {
                optimize();
            }
            else
            {
                deoptimize();
            }
        }
        
        if (!_optiEnabled)
        {
            orig_execute();
        }
    }
}