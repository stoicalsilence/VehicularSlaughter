Shader "Custom/MuckGrassBuiltIn"
{
    Properties
    {
        _BaseColor ("Grass Tint", Color) = (1,1,1,1)
        _WindStrength ("Wind Strength", Range(0,0.3)) = 0.05
        _WindSpeed ("Wind Speed", Range(0,8)) = 1.8
        _WindScale ("Wind Scale", Range(0,2)) = 0.23
        _NearFadeStart ("Near Fade Start", Float) = 0.35
        _NearFadeEnd ("Near Fade End", Float) = 1.55
        _FadeStart ("Fade Start", Float) = 30
        _FadeEnd ("Fade End", Float) = 38
        _PlayerPosition ("Player Position", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _BaseColor;
            float _WindStrength;
            float _WindSpeed;
            float _WindScale;
            float _NearFadeStart;
            float _NearFadeEnd;
            float _FadeStart;
            float _FadeEnd;
            float4 _PlayerPosition;

            struct Attributes
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                fixed4 color : COLOR;
                UNITY_FOG_COORDS(0)
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                float d = distance(world.xz, _PlayerPosition.xz);
                float farFade = saturate((_FadeEnd - d) / max(0.01, _FadeEnd - _FadeStart));
                float nearFade = saturate((d - _NearFadeStart) / max(0.01, _NearFadeEnd - _NearFadeStart));
                float fade = farFade * nearFade;

                float4 local = v.vertex;
                local.y -= v.uv.x * v.uv.y * (1.0 - fade);
                float wave = _Time.y * _WindSpeed +
                             world.x * _WindScale + world.z * _WindScale * 1.31;
                local.x += sin(wave) * _WindStrength * v.uv.x * fade;
                local.z += cos(wave * 0.8) * _WindStrength * 0.4 * v.uv.x * fade;

                o.positionCS = UnityObjectToClipPos(local);
                o.color = v.color * _BaseColor;
                UNITY_TRANSFER_FOG(o, o.positionCS);
                return o;
            }

            fixed4 Frag(Varyings v) : SV_Target
            {
                fixed4 result = fixed4(v.color.rgb, 1);
                UNITY_APPLY_FOG(v.fogCoord, result);
                return result;
            }
            ENDCG
        }
    }
}
