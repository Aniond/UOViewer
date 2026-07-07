Shader "UO HD2D/PixelComposite"
{
    // Composites the low-resolution, point-filtered character render target over the full-res
    // scene. The character RT is nearest-sampled so it stays chunky when scaled up. Drawn as a
    // full-screen quad after the scene. v1 composites by alpha only (character in the open reads
    // correctly); depth occlusion behind world geometry is a follow-up.
    Properties
    {
        _CharColor("Character Color RT", 2D) = "black" {}
        // Quantize each colour channel into this many bands. Fewer = flatter, more "pixel art".
        // 0 disables posterization (raw low-res 3D). ~16 gives a crisp banded sprite look.
        _ColorSteps("Color Steps (0 = off)", Float) = 16
        // Push saturation/contrast so the sprite pops against the painterly world.
        _Saturation("Saturation", Range(0.5, 2.0)) = 1.25
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "PixelComposite"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D(_CharColor); SAMPLER(sampler_CharColor);
            float4 _CharColor_TexelSize;
            float _ColorSteps;
            float _Saturation;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                // Positions arrive already in clip space (-1..1 quad).
                OUT.positionHCS = float4(IN.positionOS.xy, 0.0, 1.0);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Snap UV to the low-res texel grid so the character stays hard-edged pixel art.
                float2 texel = _CharColor_TexelSize.zw;
                float2 snapped = (floor(IN.uv * texel) + 0.5) / texel;
                half4 c = SAMPLE_TEXTURE2D(_CharColor, sampler_CharColor, snapped);

                // Saturation boost so the sprite reads crisp against the painterly world.
                half lum = dot(c.rgb, half3(0.299, 0.587, 0.114));
                c.rgb = lerp(half3(lum, lum, lum), c.rgb, _Saturation);

                // Posterize into hard colour bands - the key step that turns smooth low-res 3D
                // into deliberate pixel art (flat cells instead of gradients).
                if (_ColorSteps > 0.5)
                    c.rgb = floor(saturate(c.rgb) * _ColorSteps + 0.5) / _ColorSteps;

                return c; // blend uses c.a
            }
            ENDHLSL
        }
    }
}
