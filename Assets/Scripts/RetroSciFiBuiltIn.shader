Shader "RetroSciFi/BuiltIn Camera Look"
{
    Properties
    {
        [Header(Main)]
        _MainTex ("Source (automatically set)", 2D) = "white" {}
        _EffectStrength ("Overall Effect Strength (0 = Original)", Range(0,1)) = 0.65

        [Header(Color)]
        _Saturation ("Saturation", Range(0.5,1.5)) = 0.97
        _Contrast ("Contrast", Range(0.7,1.5)) = 1.035
        _GradeStrength ("Subtle Cool Shadow Warm Light", Range(0,1)) = 0.25

        [Header(Retro)]
        _ColorSteps ("Color Steps Per Channel", Range(2,128)) = 64
        _QuantizeStrength ("Color Quantization Amount", Range(0,1)) = 0.35
        _DitherStrength ("Ordered Dither Amount", Range(0,1)) = 0.20
        _PixelSize ("Pixel Block Size (1 = Off)", Range(1,6)) = 1
        _Vignette ("Vignette Strength", Range(0,0.4)) = 0.075
    }

    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _EffectStrength, _Saturation, _Contrast, _GradeStrength;
            float _ColorSteps, _QuantizeStrength, _DitherStrength, _PixelSize, _Vignette;

            float Bayer2(float2 p)
            {
                return 2.0 * p.x + 3.0 * p.y - 4.0 * p.x * p.y;
            }

            float Bayer4(float2 pixelPos)
            {
                float2 cell = fmod(floor(pixelPos), 4.0);
                return (4.0 * Bayer2(fmod(cell, 2.0)) + Bayer2(floor(cell * 0.5)) + 0.5) / 16.0 - 0.5;
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float4 original = tex2D(_MainTex, uv);
                float strength = saturate(_EffectStrength);
                if (strength < 0.0001)
                    return original; // Exact camera output; useful to diagnose other effects.

                float2 renderSize = max(_MainTex_TexelSize.zw, float2(1.0, 1.0));
                float pixelSize = max(1.0, floor(_PixelSize + 0.5));
                float3 color = original.rgb;
                if (pixelSize > 1.5)
                {
                    float2 snapUV = (floor(uv * renderSize / pixelSize) + 0.5) * pixelSize / renderSize;
                    color = tex2D(_MainTex, snapUV).rgb;
                }

                // Mild grade; no depth texture and NO fog.
                float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
                color = lerp(luminance.xxx, color, _Saturation);
                color = (color - 0.5) * _Contrast + 0.5;

                float3 coolShadows = float3(0.985, 0.995, 1.025);
                float3 warmHighlights = float3(1.025, 1.005, 0.985);
                float3 tint = lerp(coolShadows, warmHighlights, smoothstep(0.12, 0.85, luminance));
                color *= lerp(float3(1.0,1.0,1.0), tint, _GradeStrength);

                // Partial color quantization to retain distant-target visibility.
                float steps = max(2.0, floor(_ColorSteps + 0.5)) - 1.0;
                float threshold = Bayer4(uv * renderSize) * _DitherStrength;
                float3 quantized = floor(saturate(color) * steps + 0.5 + threshold) / steps;
                color = lerp(color, quantized, _QuantizeStrength);

                float2 fromCenter = (uv - 0.5) * 2.0;
                float edge = saturate(dot(fromCenter, fromCenter) * 0.5);
                color *= 1.0 - _Vignette * edge * edge;

                return float4(lerp(original.rgb, color, strength), original.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
