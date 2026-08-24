using System;
using System.Collections.Generic;
using MonoMod;
using MU3.Mod.Assets;
using UnityEngine;
using UnityEngine.UI;

namespace MU3.CustomUI;

[MonoModIfFlag("GpuTextScroll")]
public class patch_MU3Text : MU3Text
{
    private const string ShaderAssetPath = "assets/shader/gputextscroll.shader";

    private static readonly int VertexOffsetXId = Shader.PropertyToID("_VertexOffsetX");
    private static readonly int VertexOffsetYId = Shader.PropertyToID("_VertexOffsetY");
    private static readonly int ClipHalfWidthId = Shader.PropertyToID("_GpuClipHalfWidth");

    private static bool _shadersInitialized;
    private static bool _gpuShaderSupported;
    private static Shader _gpuShader;
    private static Shader _mu3UiShader;

    [MonoModIgnore] private HorizontalOverflowEx _horizontalOverflowEx;
    [MonoModIgnore] private int _scrollCharaSpace;
    [MonoModIgnore] private float _scrollWaitTime;
    [MonoModIgnore] private float _scrollSpeed;
    [MonoModIgnore] private Color[] _colors;
    [MonoModIgnore] private int _colorIndex;
    [MonoModIgnore] private float _scrollWait;
    [MonoModIgnore] private float _scrollHorizontal;
    [MonoModIgnore] private Color _emission;

    private UIVertex[] _gpuTempVerts;
    private Material _gpuMaterial;
    private int _gpuSourceMaterialId;
    private float _gpuCanvasScaleX = 1f;
    private bool _gpuMeshModifierSupportChecked;
    private bool _gpuMeshModifiersSupported;
    private bool _rectMaskChecked;
    private bool _hasRectMask;


    private extern void orig_OnEnable();
    private extern void orig_Update();
    private extern void orig_OnDisable();
    private extern void orig_OnPopulateMesh(VertexHelper toFill);
    public extern Material orig_GetModifiedMaterial(Material baseMaterial);

    private bool CanUseGpuScroll
    {
        get
        {
            if (_horizontalOverflowEx != HorizontalOverflowEx.Scroll ||
                renderingPreferredWidth <= rectTransform.rect.width ||
                _emission != Color.black ||
                font == null ||
                font.dynamic ||
                SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 ||
                !HasOnlySupportedMeshModifiers() ||
                HasRectMask)
            {
                return false;
            }
            float canvasScale;
            if (!TryGetCanvasSpaceScrollScale(out canvasScale))
                return false;


            InitializeShaders();
            if (!_gpuShaderSupported)
                return false;

            Material source = material;
            return source != null &&
                   source.shader != null &&
                   source.shader == _mu3UiShader;
        }
    }

    // MaskUtilities.GetRectMaskForClippable walks all parents and allocates;
    // calling it every Update was the last per-frame CPU cost of the GPU
    // path. The result only changes on reparent/canvas/enable transitions,
    // all of which invalidate the cache below. Runtime AddComponent of a
    // RectMask2D onto an existing parent is not handled; the game never does
    // that to scrolling texts.
    private bool HasRectMask
    {
        get
        {
            if (!_rectMaskChecked)
            {
                _rectMaskChecked = true;
                _hasRectMask = MaskUtilities.GetRectMaskForClippable(this) != null;
            }
            return _hasRectMask;
        }
    }
    private bool HasOnlySupportedMeshModifiers()
    {
        if (_gpuMeshModifierSupportChecked)
            return _gpuMeshModifiersSupported;

        _gpuMeshModifierSupportChecked = true;
        _gpuMeshModifiersSupported = true;

        var modifiers = new List<Component>();
        GetComponents(typeof(IMeshModifier), modifiers);
        for (int i = 0; i < modifiers.Count; i++)
        {
            Type type = modifiers[i].GetType();
            if (type != typeof(Outline) && type != typeof(Shadow))
            {
                _gpuMeshModifiersSupported = false;
                break;
            }
        }

        return _gpuMeshModifiersSupported;
    }


    private float RenderingFontSize
    {
        get
        {
            if (font == null)
                return 1f;
            return fontSize <= 0 ? font.lineHeight : fontSize;
        }
    }

    private float ScrollSpace => _scrollCharaSpace >= 0
        ? _scrollCharaSpace * RenderingFontSize
        : rectTransform.rect.width;

    private float ScrollRoundLength => renderingPreferredWidth + ScrollSpace;

    private TextAnchor AlignmentForDraw
    {
        get
        {
            if (!CanUseGpuScroll)
                return alignment;

            switch (alignment)
            {
                case TextAnchor.UpperCenter:
                case TextAnchor.UpperRight:
                    return TextAnchor.UpperLeft;
                case TextAnchor.MiddleCenter:
                case TextAnchor.MiddleRight:
                    return TextAnchor.MiddleLeft;
                case TextAnchor.LowerCenter:
                case TextAnchor.LowerRight:
                    return TextAnchor.LowerLeft;
                default:
                    return alignment;
            }
        }
    }

    private static void InitializeShaders()
    {
        if (_shadersInitialized)
            return;

        _shadersInitialized = true;
        _mu3UiShader = Shader.Find("MU3/UI/Default");

        try
        {
            _gpuShader = AssetBundlesRegistry.LoadAsset<Shader>(
                "GpuTextScrollShaderBundle",
                ShaderAssetPath);
            _gpuShaderSupported = _gpuShader.isSupported;
            if (!_gpuShaderSupported)
                Debug.LogError("[GpuTextScroll] The scrolling shader is unavailable; using CPU scrolling.");
        }
        catch (Exception exception)
        {
            Debug.LogError("[GpuTextScroll] Failed to initialize the scrolling shader; using CPU scrolling. " + exception);
        }
    }

    private Vector2 GetRenderingScale(bool enableFit)
    {
        Vector2 result = Vector2.one;
        if (font != null && !font.dynamic && font.lineHeight > 0 && fontSize > 0)
            result *= (float)fontSize / font.lineHeight;

        if (enableFit &&
            _horizontalOverflowEx == HorizontalOverflowEx.Fit &&
            renderingPreferredWidth > rectTransform.rect.width)
        {
            result.x = rectTransform.rect.width / preferredWidth;
        }

        return result;
    }


    public new void Update()
    {
        bool eligible = CanUseGpuScroll;

        if (!eligible)
        {
            if (_gpuMaterial != null)
            {
                DestroyGpuMaterial();
                SetMaterialDirty();
            }

            orig_Update();
            return;
        }

        if (_gpuMaterial == null)
        {
            orig_Update();
            return;
        }


        if (_scrollWait > 0f)
        {
            _scrollWait -= Time.deltaTime;
            return;
        }

        _scrollHorizontal += _scrollSpeed * Time.deltaTime;
        while (_scrollHorizontal > ScrollRoundLength)
            _scrollHorizontal -= ScrollRoundLength;

        float canvasScale;
        if (!TryGetCanvasSpaceScrollScale(out canvasScale))
        {
            DestroyGpuMaterial();
            SetMaterialDirty();
            orig_Update();
            return;
        }

        if (!Mathf.Approximately(_gpuCanvasScaleX, canvasScale))
        {
            _gpuCanvasScaleX = canvasScale;
            SetVerticesDirty();
        }
        SetGpuMaterialOffset();
    }

    [MonoModReplace]
    public new void resetScroll()
    {
        _scrollWait = _scrollWaitTime;
        _scrollHorizontal = 0f;
        if (_gpuMaterial != null)
            SetGpuMaterialOffset();

        SetMaterialDirty();
    }

    protected override void OnDisable()
    {
        DestroyGpuMaterial();
        orig_OnDisable();
    }

    protected override void OnEnable()
    {
        _rectMaskChecked = false;
        orig_OnEnable();
    }

    protected override void OnTransformParentChanged()
    {
        _rectMaskChecked = false;
        base.OnTransformParentChanged();
    }

    protected override void OnCanvasHierarchyChanged()
    {
        _rectMaskChecked = false;
        base.OnCanvasHierarchyChanged();
    }

    public override Material GetModifiedMaterial(Material baseMaterial)
    {
        Material source = orig_GetModifiedMaterial(baseMaterial);
        if (!CanUseGpuScroll || source == null)
        {
            DestroyGpuMaterial();
            return source;
        }

        int sourceMaterialId = source.GetInstanceID();
        if (_gpuMaterial == null || _gpuSourceMaterialId != sourceMaterialId)
        {
            DestroyGpuMaterial();
            _gpuMaterial = new Material(_gpuShader)
            {
                name = source.name + " [GPU Scroll]",
                hideFlags = HideFlags.HideAndDontSave,
            };
            _gpuMaterial.CopyPropertiesFromMaterial(source);
            _gpuMaterial.SetFloat("_DiffusePower", 1f);
            _gpuSourceMaterialId = sourceMaterialId;
        }

        SetGpuMaterialOffset();
        return _gpuMaterial;
    }

    protected override void OnPopulateMesh(VertexHelper toFill)
    {
        bool eligible = CanUseGpuScroll;

        if (!eligible)
        {
            orig_OnPopulateMesh(toFill);
            return;
        }

        if (font == null)
            return;
        Canvas activeCanvas = canvas;
        if (activeCanvas != null)
            activeCanvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;

        m_DisableFontTextureRebuiltCallback = true;
        try
        {
            Vector2 size = rectTransform.rect.size;
            Vector2 renderingScale = GetRenderingScale(true);
            size.x /= renderingScale.x;
            size.y /= renderingScale.y;

            TextGenerationSettings settings = GetGenerationSettings(size);
            settings.textAnchor = AlignmentForDraw;
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            settings.resizeTextForBestFit = false;
            if (_colors != null &&
                _colors.Length > 0 &&
                _colorIndex >= 0 &&
                _colorIndex < _colors.Length)
            {
                settings.color = _colors[_colorIndex];
            }

            cachedTextGenerator.Populate(text, settings);

            Rect rect = rectTransform.rect;
            Vector2 clipCenterAndHalfWidth = new Vector2(rect.center.x, rect.width * 0.5f);
            Vector2 anchor = Text.GetTextAnchorPivot(AlignmentForDraw);
            Vector2 origin = new Vector2(
                Mathf.Lerp(rect.xMin, rect.xMax, anchor.x),
                Mathf.Lerp(rect.yMin, rect.yMax, anchor.y));
            Vector2 offset = PixelAdjustPoint(origin) - origin;
            IList<UIVertex> vertices = cachedTextGenerator.verts;

            float canvasScale;
            if (!TryGetCanvasSpaceScrollScale(out canvasScale))
            {
                orig_OnPopulateMesh(toFill);
                return;
            }
            _gpuCanvasScaleX = canvasScale;

            toFill.Clear();
            AddVertices(toFill, vertices, renderingScale, offset, clipCenterAndHalfWidth.x, canvasScale);
            offset.x += ScrollRoundLength;
            AddVertices(toFill, vertices, renderingScale, offset, clipCenterAndHalfWidth.x, canvasScale);
        }
        finally
        {
            m_DisableFontTextureRebuiltCallback = false;
        }
    }

    private void AddVertices(VertexHelper target, IList<UIVertex> vertices, Vector2 scale, Vector2 offset, float clipCenter, float canvasScale)
    {
        if (_gpuTempVerts == null)
            _gpuTempVerts = new UIVertex[4];

        scale *= 1f / pixelsPerUnit;
        int count = vertices.Count - 4;
        for (int i = 0; i < count; i += 4)
        {
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float uvAtMinX = 0f;
            float uvAtMaxX = 0f;

            for (int quadIndex = 0; quadIndex < 4; quadIndex++)
            {
                UIVertex vertex = vertices[i + quadIndex];
                vertex.position.x = vertex.position.x * scale.x + offset.x;
                vertex.position.y = vertex.position.y * scale.y + offset.y;
                _gpuTempVerts[quadIndex] = vertex;

                if (vertex.position.x < minX)
                {
                    minX = vertex.position.x;
                    uvAtMinX = vertex.uv0.x;
                }
                if (vertex.position.x > maxX)
                {
                    maxX = vertex.position.x;
                    uvAtMaxX = vertex.uv0.x;
                }
            }

            float canvasWidth = (maxX - minX) * canvasScale;
            float uvPerCanvasUnit = canvasWidth == 0f ? 0f : (uvAtMaxX - uvAtMinX) / canvasWidth;
            for (int quadIndex = 0; quadIndex < 4; quadIndex++)
            {
                UIVertex vertex = _gpuTempVerts[quadIndex];
                vertex.uv1 = new Vector2((vertex.position.x - clipCenter) * canvasScale, uvPerCanvasUnit);
                _gpuTempVerts[quadIndex] = vertex;
            }

            target.AddUIVertexQuad(_gpuTempVerts);
        }
    }


    private bool TryGetCanvasSpaceScrollScale(out float scale)
    {
        scale = 1f;
        Canvas activeCanvas = canvas;
        if (activeCanvas == null)
            return true;

        Vector3 worldAxis = rectTransform.TransformVector(Vector3.right);
        Vector3 canvasAxis = activeCanvas.transform.InverseTransformVector(worldAxis);
        if (canvasAxis.x <= 0f ||
            Mathf.Abs(canvasAxis.y) > 0.0001f ||
            float.IsNaN(canvasAxis.x) ||
            float.IsInfinity(canvasAxis.x))
        {
            return false;
        }

        scale = canvasAxis.x;
        return true;
    }

    private void SetGpuMaterialOffset()
    {
        _gpuMaterial.SetFloat(VertexOffsetXId, -_scrollHorizontal * _gpuCanvasScaleX);
        _gpuMaterial.SetFloat(VertexOffsetYId, 0f);
        _gpuMaterial.SetFloat(ClipHalfWidthId, rectTransform.rect.width * 0.5f * Mathf.Abs(_gpuCanvasScaleX));
    }

    private void DestroyGpuMaterial()
    {
        if (_gpuMaterial == null)
            return;

        UnityEngine.Object.Destroy(_gpuMaterial);
        _gpuMaterial = null;
        _gpuSourceMaterialId = 0;
        _gpuCanvasScaleX = 1f;
    }
}
