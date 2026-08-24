Shader "MU3Mod/2DNotes"
{
    Properties
    {
        _MainTex ("Texture (RGBA)", 2D) = "white" {}
        _Color   ("Tint", Color) = (1,1,1,1)
        _Cutoff  ("Alpha Cutoff", Range(0,1)) = 0.5
        [Toggle]
        _Mirror ("Mirror X", Float) = 0
    }

    SubShader
    {
        LOD 1000
        
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }

        Cull Off
        ZWrite On
        Lighting Off

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

                // Object center in world (per-instance)
                float3 centerW = mul(unity_ObjectToWorld, float4(0,0,0,1)).xyz;

                // Scaled object axes in world (preserve non-uniform scale)
                float3 xAxisScaledW = mul((float3x3)unity_ObjectToWorld, float3(1,0,0));
                float3 yAxisScaledW = mul((float3x3)unity_ObjectToWorld, float3(0,1,0));
                float3 zAxisScaledW = mul((float3x3)unity_ObjectToWorld, float3(0,0,1));

                float xLen = length(xAxisScaledW);
                float yLen = length(yAxisScaledW);
                float zLen = length(zAxisScaledW);

                // Directions without scale (avoid divide-by-zero)
                float3 xDirW = (xLen > 1e-8) ? xAxisScaledW / xLen : float3(1,0,0);
                float3 zDirW = (zLen > 1e-8) ? zAxisScaledW / zLen : float3(0,0,1);

                // Direction from object center to camera
                float3 camDirW = _WorldSpaceCameraPos.xyz - centerW;

                // Project camera direction onto plane perpendicular to X (rotate only around X)
                float3 camDirProjW = camDirW - xDirW * dot(camDirW, xDirW);
                float len2 = dot(camDirProjW, camDirProjW);

                // New forward (Z) direction in the YZ plane; fallback to original Z if degenerate
                float3 zNewDirW = (len2 > 1e-8) ? camDirProjW * rsqrt(len2) : zDirW;

                // New up (Y) direction, keeping right-handed basis with fixed X
                float3 yNewDirW = normalize(cross(zNewDirW, xDirW));

                // Mirror X if needed
                float mirror = UNITY_ACCESS_INSTANCED_PROP(_Mirror);
                float3 local = v.vertex.xyz;
                local.x = lerp(local.x, -local.x, mirror);
                float3 posW =
                    centerW
                    + (xDirW * xLen)   * local.x
                    + (yNewDirW * yLen)* local.y
                    + (zNewDirW * zLen)* local.z;

                o.pos = mul(UNITY_MATRIX_VP, float4(posW, 1.0));

                float4 st = UNITY_ACCESS_INSTANCED_PROP(_MainTex_ST);
                o.uv = v.uv.xy * st.xy + st.zw;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                clip(col.a - _Cutoff); // cutout
                return col;
            }
            ENDCG
        }
    }
}