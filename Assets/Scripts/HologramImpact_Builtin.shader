Shader "Custom/HologramImpact_Builtin"
{
    Properties
    {
        _MainTex ("Source Texture (alpha cutout)", 2D) = "white" {}
        [HDR] _HoloColor ("Hologram Color", Color) = (0.05, 1.5, 2.5, 0.9)
        _Cutoff ("Leaf Alpha Cutoff", Range(0, 1)) = 0.3
        _ScanDensity ("Scanline Density", Range(1, 100)) = 35
        _ScanSpeed ("Scanline Speed", Range(0, 30)) = 8
        _RimPower ("Rim Sharpness", Range(0.5, 8)) = 2.5
        _PulseSpeed ("Impact Wave Speed", Range(0, 30)) = 10
        _PulseWidth ("Impact Wave Width", Range(0.05, 3)) = 0.5
        [HideInInspector] _HoloOpacity ("Runtime Opacity", Float) = 0
        [HideInInspector] _ImpactPoint ("Runtime Hit Point", Vector) = (0, 0, 0, 1)
        [HideInInspector] _ImpactStartTime ("Runtime Hit Time", Float) = 0
        [HideInInspector] _ImpactStrength ("Runtime Hit Strength", Float) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _HoloColor;
            float _Cutoff;
            float _ScanDensity;
            float _ScanSpeed;
            float _RimPower;
            float _PulseSpeed;
            float _PulseWidth;
            float _HoloOpacity;
            float4 _ImpactPoint;
            float _ImpactStartTime;
            float _ImpactStrength;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                output.positionWS = mul(unity_ObjectToWorld, input.positionOS).xyz;
                output.normalWS = UnityObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                fixed mask = tex2D(_MainTex, input.uv).a;
                clip(mask - _Cutoff);

                float3 n = normalize(input.normalWS);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                float rim = pow(1.0 - abs(dot(n, viewDir)), _RimPower);
                float scan = pow(0.5 + 0.5 * sin(input.positionWS.y * _ScanDensity - _Time.y * _ScanSpeed), 6.0);

                float age = max(0.0, _Time.y - _ImpactStartTime);
                float dist = distance(input.positionWS, _ImpactPoint.xyz);
                float ring = saturate(1.0 - abs(dist - age * _PulseSpeed) / max(_PulseWidth, 0.05));
                ring *= _ImpactStrength;

                float flicker = 0.94 + 0.06 * sin(_Time.y * 43.0) * sin(_Time.y * 17.0);
                float brightness = (0.3 + 0.32 * scan + 1.2 * rim + 1.8 * ring) * flicker;
                float opacity = saturate((0.18 + 0.20 * scan + 0.6 * rim + 0.8 * ring)
                                         * _HoloOpacity * _HoloColor.a);

                return fixed4(_HoloColor.rgb * brightness, opacity);
            }
            ENDCG
        }
    }
    FallBack Off
}
