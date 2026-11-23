Shader "Horde/DeathDissolveBuiltIn"
{
    Properties
    {
        _MainTex ("Base texture", 2D) = "white" {}
        _Color ("Base tint", Color) = (1,1,1,1)
        [HideInInspector] _UseVertexColor ("Use mesh vertex colors", Float) = 0
        _Dissolve ("Dissolve 0 to 1", Range(0,1)) = 0
        [HDR] _EdgeColor ("Glowing edge color", Color) = (1,0.3,0.05,1)
        _EdgeWidth ("Glowing edge width", Range(0.001,0.3)) = 0.09
        _EdgeBrightness ("Edge brightness", Range(0,8)) = 3
        _NoiseScale ("World-space noise scale", Float) = 7
        _Seed ("Per-death random seed", Float) = 1
        _RiseBias ("Dissolve bottom-to-top bias", Range(0,1)) = 0.28
        _HeightScale ("Height noise scale", Float) = 0.65
        _DeathOriginY ("World-space death Y", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        Cull Back
        ZWrite On

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _UseVertexColor;
            float _Dissolve;
            float4 _EdgeColor;
            float _EdgeWidth;
            float _EdgeBrightness;
            float _NoiseScale;
            float _Seed;
            float _RiseBias;
            float _HeightScale;
            float _DeathOriginY;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float4 color : COLOR;
                UNITY_FOG_COORDS(3)
            };

            float hash3(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float noise3(float3 p)
            {
                float3 cell = floor(p);
                float3 t = frac(p);
                t = t * t * (3.0 - 2.0 * t);
                float a = lerp(hash3(cell + float3(0,0,0)), hash3(cell + float3(1,0,0)), t.x);
                float b = lerp(hash3(cell + float3(0,1,0)), hash3(cell + float3(1,1,0)), t.x);
                float c = lerp(hash3(cell + float3(0,0,1)), hash3(cell + float3(1,0,1)), t.x);
                float d = lerp(hash3(cell + float3(0,1,1)), hash3(cell + float3(1,1,1)), t.x);
                return lerp(lerp(a,b,t.y), lerp(c,d,t.y), t.z);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 p = i.worldPos * max(0.5, _NoiseScale) + float3(_Seed, _Seed * 0.371, _Seed * 0.719);
                float n1 = noise3(p);
                float n2 = noise3(p * 2.07 + 17.6);
                float n = saturate(n1 * 0.78 + n2 * 0.22);

                // Larger Y means later dissolution: bottom to top.
                float rise = saturate(0.5 + (i.worldPos.y - _DeathOriginY) * _HeightScale);
                n = lerp(n, rise, saturate(_RiseBias));

                // Fully visible at 0, fully discarded at 1 regardless of noise.
                float threshold = lerp(-0.06, 1.06, saturate(_Dissolve));
                float surviving = n - threshold;
                clip(surviving);

                fixed4 tex = tex2D(_MainTex, i.uv) * _Color * lerp(fixed4(1,1,1,1), i.color, _UseVertexColor);
                clip(tex.a - 0.08);
                float3 normal = normalize(i.worldNormal);
                float3 ambient = max(ShadeSH9(float4(normal, 1.0)), float3(0.36,0.36,0.36));
                float3 sun = saturate(dot(normal, normalize(_WorldSpaceLightPos0.xyz))) * _LightColor0.rgb;
                float3 lightValue = saturate(ambient + sun * 0.33 + float3(0.28,0.28,0.28));
                float edge = 1.0 - smoothstep(0.0, max(0.001, _EdgeWidth), surviving);
                float3 outputColor = tex.rgb * lightValue + edge * _EdgeColor.rgb * _EdgeBrightness;
                float4 result = float4(outputColor, 1);
                UNITY_APPLY_FOG(i.fogCoord, result);
                return result;
            }
            ENDCG
        }
    }
    FallBack Off
}
