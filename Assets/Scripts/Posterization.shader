Shader "Hidden/Posterization"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Levels ("Color Levels", Float) = 6
        _Intensity ("Intensity", Range(0, 1)) = 1
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Levels;
            float _Intensity;

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                float levels = max(_Levels, 2.0);
                float3 posterized = round(saturate(col.rgb) * (levels - 1.0)) / (levels - 1.0);
                col.rgb = lerp(col.rgb, posterized, saturate(_Intensity));
                return col;
            }
            ENDCG
        }
    }

    Fallback Off
}
