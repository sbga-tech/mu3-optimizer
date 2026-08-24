Shader "MU3Mod/UnlitTransparentCutout"
{
    Properties
    {
        _MainTex ("Texture (RGBA)", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff  ("Alpha Cutoff", Range(0,1)) = 0.5
        [Toggle]
        _Mirror ("Mirror X", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="AlphaTest"
            "RenderType"="TransparentCutout"
            "IgnoreProjector"="True"
        }

        // Usually you want ZWrite Off for transparent objects to avoid sorting artifacts
        ZWrite On
        Cull Off
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"
            #include "UnityInstancing.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float _Cutoff;

            UNITY_INSTANCING_CBUFFER_START (Props)
            UNITY_DEFINE_INSTANCED_PROP (float4, _MainTex_ST)
            UNITY_DEFINE_INSTANCED_PROP (float, _Mirror)
            UNITY_INSTANCING_CBUFFER_END

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
    
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);

                float mirror = UNITY_ACCESS_INSTANCED_PROP(_Mirror);
                v.vertex.x = lerp(v.vertex.x, -v.vertex.x, mirror);
                o.pos = UnityObjectToClipPos(v.vertex);

                float4 st = UNITY_ACCESS_INSTANCED_PROP(_MainTex_ST);
                o.uv = v.uv.xy * st.xy + st.zw;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                clip(col.a - _Cutoff);
                
                return col;
            }
            ENDCG
        }
    }

    Fallback Off
}