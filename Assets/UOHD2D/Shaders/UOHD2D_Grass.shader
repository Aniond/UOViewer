Shader "UO HD2D/Grass"
{
	// Instanced grass tufts: cutout blades with vertex wind sway, simple main
	// light + ambient shading, and camera-distance shrink fade. Works with or
	// without a blade texture (a procedural blade mask covers the untextured
	// case so grass renders before any texture is generated).
	Properties
	{
		_BaseMap("Blade Texture (optional)", 2D) = "white" {}
		_ColorBottom("Color Bottom", Color) = (0.18, 0.34, 0.11, 1)
		_ColorTop("Color Top", Color) = (0.45, 0.65, 0.22, 1)
		_Cutoff("Alpha Cutoff", Range(0,1)) = 0.4
		_WindStrength("Wind Strength", Range(0,1)) = 0.25
		_WindSpeed("Wind Speed", Range(0,8)) = 1.6
		_FadeStart("Fade Start", Float) = 38
		_FadeEnd("Fade End", Float) = 55
	}

	SubShader
	{
		Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
		Cull Off

		Pass
		{
			Name "ForwardLit"
			Tags { "LightMode" = "UniversalForward" }

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
			#pragma multi_compile_fog

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			TEXTURE2D(_BaseMap);
			SAMPLER(sampler_BaseMap);

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseMap_ST;
				half4 _ColorBottom;
				half4 _ColorTop;
				half _Cutoff;
				half _WindStrength;
				half _WindSpeed;
				float _FadeStart;
				float _FadeEnd;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 positionWS : TEXCOORD1;
				half fogFactor : TEXCOORD2;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			Varyings vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);

				float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

				// Distance shrink fade: tufts sink into the ground past FadeStart.
				float dist = distance(positionWS, _WorldSpaceCameraPos);
				float fade = 1.0 - saturate((dist - _FadeStart) / max(_FadeEnd - _FadeStart, 0.01));
				float3 rootWS = TransformObjectToWorld(float3(input.positionOS.x, 0, input.positionOS.z));
				positionWS = lerp(rootWS, positionWS, fade);

				// Wind sway on the blade tops.
				float sway = sin(_Time.y * _WindSpeed + positionWS.x * 0.9 + positionWS.z * 0.7);
				positionWS.xz += sway * _WindStrength * input.uv.y * fade * 0.2;

				output.positionWS = positionWS;
				output.positionCS = TransformWorldToHClip(positionWS);
				output.uv = input.uv;
				output.fogFactor = ComputeFogFactor(output.positionCS.z);
				return output;
			}

			// Procedural blade mask: a few tapered vertical strips.
			half BladeMask(float2 uv)
			{
				float x = frac(uv.x * 3.0);
				float center = abs(x - 0.5) * 2.0;
				float taper = lerp(0.85, 0.12, uv.y);
				return center < taper ? 1.0h : 0.0h;
			}

			half4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);

				half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
				half alpha = tex.a * BladeMask(input.uv);
				clip(alpha - _Cutoff);

				half4 tint = lerp(_ColorBottom, _ColorTop, input.uv.y);
				half3 albedo = tex.rgb * tint.rgb;

				float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
				Light mainLight = GetMainLight(shadowCoord);
				half3 lighting = mainLight.color * mainLight.shadowAttenuation;
				half3 ambient = SampleSH(half3(0, 1, 0));
				half3 color = albedo * (lighting + ambient);

				color = MixFog(color, input.fogFactor);
				return half4(color, 1);
			}
			ENDHLSL
		}
	}
}
