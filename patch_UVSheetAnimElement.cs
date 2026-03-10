using System.Collections.Generic;
using MonoMod;
using UnityEngine;


[MonoModIfFlag("BetterNotes")]
public class patch_UVSheetAnimElement : UVSheetAnimElement
{
    private MaterialPropertyBlock _mpb;

    
    private static int _MainTex_ST;
    private static bool propertyIdInitialized;

    [MonoModIgnore] public new Renderer 対象オブジェクトのRenderer;
    [MonoModIgnore] public new int 縦のコマ数;
    [MonoModIgnore] public new int 横のコマ数;
    [MonoModIgnore] public new int 開始コマ数;
    [MonoModIgnore] public new bool 逆再生;
    [MonoModIgnore] public new int 切替までのフレーム数;
    [MonoModIgnore] public new e原点位置 UV座標原点;
    [MonoModIgnore] private float w;
    [MonoModIgnore] private float h;
    [MonoModIgnore] private List<Vector2> offsetList;
    [MonoModIgnore] private int frameCount;
    [MonoModIgnore] private int currentId;

    [MonoModReplace]
    public new void init_UVAnim()
    {
        if (縦のコマ数 == 0 || 横のコマ数 == 0 || 対象オブジェクトのRenderer == null)
            return;

        w = 1f / (float)横のコマ数;
        h = 1f / (float)縦のコマ数;

        対象オブジェクトのRenderer.sharedMaterial.mainTextureScale = new Vector2(w, h);

        offsetList.Clear();
        if (UV座標原点 == e原点位置.左上_一般的)
        {
            var num = 縦のコマ数 - 1;
            while (0 <= num)
            {
                var item = new Vector2(0f, 0f);
                item.y = (float)num * h;
                for (var i = 0; i < 横のコマ数; i++)
                {
                    item.x = (float)i * w;
                    offsetList.Add(item);
                }
                num--;
            }
        }
        else
        {
            for (var j = 0; j < 縦のコマ数; j++)
            {
                var item2 = new Vector2(0f, 0f);
                item2.y = (float)j * h;
                for (var k = 0; k < 横のコマ数; k++)
                {
                    item2.x = (float)k * w;
                    offsetList.Add(item2);
                }
            }
        }

        currentId = 開始コマ数;

        _mpb = new MaterialPropertyBlock();
    }

    [MonoModReplace]
    public new void update_UVAnim()
    {
        if (縦のコマ数 == 0 || 横のコマ数 == 0 || 対象オブジェクトのRenderer == null)
            return;

        if (!propertyIdInitialized)
        {
            _MainTex_ST = Shader.PropertyToID("_MainTex_ST");
            propertyIdInitialized = true;
        }
        
        if (frameCount >= 切替までのフレーム数)
        {
            var offset = offsetList[currentId];

            // Set per-instance texture offset via MaterialPropertyBlock.
            // _MainTex_ST = (scaleX, scaleY, offsetX, offsetY)
            // Scale is shared but must be included because MPB overrides the full vector.
            対象オブジェクトのRenderer.GetPropertyBlock(_mpb);
            _mpb.SetVector(_MainTex_ST, new Vector4(w, h, offset.x, offset.y));
            対象オブジェクトのRenderer.SetPropertyBlock(_mpb);

            if (逆再生)
            {
                if (currentId == 0)
                    currentId = offsetList.Count - 1;
                else
                    currentId--;
            }
            else if (currentId == offsetList.Count - 1)
            {
                currentId = 0;
            }
            else
            {
                currentId++;
            }

            frameCount = 0;
        }
        frameCount++;
    }
}