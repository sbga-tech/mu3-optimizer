using System;
using MonoMod;
using UnityEngine;

[MonoModIfFlag("BetterNotes")]
public class patch_UVScroll : UVScroll
{
    private static int _MainTex_ST; 

    [MonoModIgnore] private float _scrollSpeedU;
    [MonoModIgnore] private float _scrollSpeedV;
    [MonoModIgnore] private float _u;
    [MonoModIgnore] private float _v;


    private MaterialPropertyBlock _mpb;
    private Renderer _renderer;
    private Vector2 _texScale;

    private void Awake()
    {
        _MainTex_ST = Shader.PropertyToID("_MainTex_ST");
    }

    [MonoModReplace]
    private void Start()
    {
        _renderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();
        var s = _renderer.sharedMaterial.mainTextureScale;
        _texScale = s;
    }

    [MonoModReplace]
    private void Update()
    {
        _u = Mathf.Repeat(_u + Time.deltaTime * _scrollSpeedU, 1f);
        _v = Mathf.Repeat(_v + Time.deltaTime * _scrollSpeedV, 1f);
        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetVector(_MainTex_ST, new Vector4(_texScale.x, _texScale.y, _u, _v));
        _renderer.SetPropertyBlock(_mpb);
    }
}