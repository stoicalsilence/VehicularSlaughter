Shader "Custom/MuckGrassURP"
{
    Properties
    {
        _BaseColor ("Grass Tint", Color) = (1,1,1,1)
        _WindStrength ("Wind Strength", Range(0,0.3)) = 0.05
        _WindSpeed ("Wind Speed", Range(0,8)) = 1.8
        _WindScale ("Wind Scale", Range(0,2)) = 0.23
        _FadeStart ("Fade Start", Float) = 30
        _FadeEnd ("Fade End", Float) = 38
        _PlayerPosition ("Player Position", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        ZWrite On

        Pass
        {
            Name "MuckGrassForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _WindStrength;
                float _WindSpeed;
                float _WindScale;
                float _FadeStart;
                float _FadeEnd;
                float4 _PlayerPosition;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                half fogFactor : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(v.positionOS);
                float d = distance(world.xz, _PlayerPosition.xz);
                float fade = saturate((_FadeEnd - d) / max(0.01, _FadeEnd - _FadeStart));

                float3 local = v.positionOS;
                local.y -= v.uv.x * v.uv.y * (1.0 - fade);

                float wave = _Time.y * _WindSpeed +
                             world.x * _WindScale + world.z * _WindScale * 1.31;
                local.x += sin(wave) * _WindStrength * v.uv.x * fade;
                local.z += cos(wave * 0.8) * _WindStrength * 0.4 * v.uv.x * fade;

                o.positionCS = TransformObjectToHClip(local);
                o.color = v.color * _BaseColor;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings v) : SV_Target
            {
                return half4(MixFog(v.color.rgb, v.fogFactor), 1);
            }
            ENDHLSL
        }
    }
}
