Shader "MU3Mod/UI/GpuScroll"
{
    Properties
    {
        [PerRendererData] _MainTex ("Font Atlas", 2D) = "white" {}
        _Color ("Text Color", Color) = (1,1,1,1)
        _Emission ("Emission", Color) = (0,0,0,0)
        _SaturationStrength ("Strength", Float) = 1.0
        _DiffusePower ("Diffuse Power", Range(1,4)) = 1.0

        _VertexOffsetX ("Vertex OffsetX", Float) = 0.0
        _VertexOffsetY ("Vertex OffsetY", Float) = 0.0
        _GpuClipHalfWidth ("Clip Half Width", Float) = 0.0

        _SrcBlend ("_SrcBlend", Float) = 5.0
        _DstBlend ("_DstBlend", Float) = 10.0
        _BlendOp ("_BlendOp", Float) = 0.0

        _StencilComp ("Stencil Comparison", Float) = 8.0
        _Stencil ("Stencil ID", Float) = 0.0
        _StencilOp ("Stencil Operation", Float) = 0.0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255.0
        _StencilReadMask ("Stencil Read Mask", Float) = 255.0
        _ColorMask ("Color Mask", Float) = 15.0
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Off
        BlendOp [_BlendOp]
        Blend [_SrcBlend] [_DstBlend]
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            // NOTE: the committed Build/GpuTextScrollShaderBundle is the
            // pixel-validated artifact (bit-exact CPU/GPU comparison in game).
            // This source intentionally hardcodes the MU3/UI/Default
            // TEXTURE_COLOR_AS_ALPHA font path with no keyword variants, like
            // the validated bundle. A bundle rebuilt from this source MUST
            // pass the in-game CPU-vs-GPU pixel audit before it is adopted.
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0

            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord0 : TEXCOORD0;
                float2 texcoord1 : TEXCOORD1;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _Emission;
            fixed4 _TextureSampleAdd;
            float _DiffusePower;
            float _VertexOffsetX;
            float _VertexOffsetY;
            float _GpuClipHalfWidth;

            v2f vert(appdata_t input)
            {
                v2f output;

                // texcoord1.x is the pre-Outline/Shadow horizontal source
                // coordinate in canvas space. texcoord1.y is dU/dX for the
                // original glyph quad. Built-in mesh effects preserve both.
                float sourceX = input.texcoord1.x + _VertexOffsetX;
                float clippedX = clamp(sourceX, -_GpuClipHalfWidth, _GpuClipHalfWidth);
                float clipDelta = clippedX - sourceX;

                // Translate every copy by the scrolling offset, then move only
                // boundary vertices back to the source text rectangle. Effect
                // offsets already present in input.vertex remain untouched.
                input.vertex.x += _VertexOffsetX + clipDelta;
                input.vertex.y += _VertexOffsetY;
                input.texcoord0.x += clipDelta * input.texcoord1.y;

                output.vertex = UnityObjectToClipPos(input.vertex);
                output.color = input.color * _Color;
                output.color.rgb *= _DiffusePower;
                output.texcoord = input.texcoord0;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 sample = tex2D(_MainTex, input.texcoord);

                // MU3/UI/Default TEXTURE_COLOR_AS_ALPHA semantics: the font
                // atlas stores glyph coverage in the red channel.
                return fixed4(input.color.rgb + _Emission.rgb,
                              input.color.a * sample.r);
            }
            ENDCG
        }
    }
}
