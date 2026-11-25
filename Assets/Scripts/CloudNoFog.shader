
Shader "Custom/CloudNoFog"
{
    Properties
    {
        _Color ("Cloud Color", Color) = (1,1,1,1)
        _Brightness ("Brightness", Range(0,2)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        LOD 200

        CGPROGRAM

        #pragma surface surf Lambert nofog noshadow exclude_path:deferred exclude_path:prepass
        #pragma target 3.0

        fixed4 _Color;
        half _Brightness;

        struct Input
        {
            float3 worldPos;
        };

        void surf(Input IN, inout SurfaceOutput o)
        {
            o.Albedo = _Color.rgb;
            o.Emission = _Color.rgb * _Brightness;
            o.Alpha = 1;
        }

        ENDCG
    }

    FallBack Off
}
