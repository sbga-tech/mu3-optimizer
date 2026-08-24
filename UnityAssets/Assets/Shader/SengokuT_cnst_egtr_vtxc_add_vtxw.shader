Shader "SengokuT/cnst_egtr_vtxc_add_vtxw"
{
	Properties
	{
		_WaveXAmp ("Wave X Amp",    Float) = 0.1
		_WaveXLen ("Wave X Length", Float) = 1
		_WaveXHgt ("Wave X Height", Float) = 1
		_WaveYAmp ("Wave Y Amp",    Float) = 0.1
		_WaveYLen ("Wave Y Length", Float) = 1
		_WaveYHgt ("Wave Y Height", Float) = 1
		_EAFunc   ("Edge Alpha Function", Float) = 1
		_EAPow    ("Edge Alpha Pow",      Float) = 3
		_EABias   ("Edge Alpha Bias",     Float) = 0
		_Color    ("Main Color", Color) = (1,1,1,1)
		_MainTex  ("Base (RGB)", 2D) = "white" {}
	}
	SubShader
	{
		Tags { "QUEUE" = "Transparent" "RenderType" = "Transparent" }
		Pass
		{
			Tags { "QUEUE" = "Transparent" "RenderType" = "Transparent" }
			Blend SrcAlpha One, SrcAlpha One
			ZWrite Off

			CGPROGRAM
			#pragma vertex   vert
			#pragma fragment frag
			#pragma target 4.0

			#include "UnityCG.cginc"

			sampler2D _MainTex;
			float4    _MainTex_ST;
			float4    _Color;
			float     _WaveXAmp, _WaveXLen, _WaveXHgt;
			float     _WaveYAmp, _WaveYLen, _WaveYHgt;
			float     _EAFunc, _EAPow, _EABias;

			struct appdata
			{
				float4 vertex   : POSITION;
				float3 normal   : NORMAL;
				float4 texcoord : TEXCOORD0;
				float4 color    : COLOR;
			};

			struct v2f
			{
				float4 pos   : SV_POSITION;
				float2 uv    : TEXCOORD0;
				// .xyz = vertex color RGB
				// .w   = edgeAlpha * original vertex alpha  (matches decompiled vertex output)
				float4 color : COLOR;
			};

			v2f vert(appdata v)
			{
				v2f o;

				// --- Wave displacement (object space) ---
				// wavedX = x + sin(time * _WaveXLen + y * _WaveXHgt) * _WaveXAmp
				// wavedY = y + sin(time * _WaveYLen + x * _WaveYHgt) * _WaveYAmp
				float wavedX = v.vertex.x + sin(_Time.y * _WaveXLen + v.vertex.y * _WaveXHgt) * _WaveXAmp;
				float wavedY = v.vertex.y + sin(_Time.y * _WaveYLen + v.vertex.x * _WaveYHgt) * _WaveYAmp;
				float4 wavedPos = float4(wavedX, wavedY, v.vertex.z, 1.0);

				o.pos = UnityObjectToClipPos(wavedPos);
				o.uv  = TRANSFORM_TEX(v.texcoord, _MainTex);

				// --- Edge alpha (rim effect, computed in object space to match original) ---
				// Camera position transformed to object space
				float3 camObjPos  = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz;
				float3 viewDirObj = normalize(camObjPos - float3(wavedX, wavedY, v.vertex.z));
				// ndotv: positive when surface faces the camera
				float  ndotv      = saturate(dot(viewDirObj, v.normal));
				// edge = 0 at front, 1 at grazing  (when _EAFunc = 1)
				float  edgeAlpha  = pow(abs(-ndotv + _EAFunc), _EAPow);

				o.color = float4(v.color.rgb, edgeAlpha * v.color.a);
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				float4 tex = tex2D(_MainTex, i.uv);

				float4 col;
				col.rgb = tex.rgb * i.color.rgb * _Color.rgb;
				// Original fragment: alpha = tex.a * color.a * _Color.a * color.a * _EABias
				// color.a already carries (edgeAlpha * origVertA), so squaring it intensifies the rim.
				col.a   = tex.a * i.color.a * _Color.a * i.color.a * _EABias;
				return col;
			}
			ENDCG
		}
	}
}
