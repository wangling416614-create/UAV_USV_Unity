Shader "UavUsv/CatalinaTerrain"
{
    Properties
    {
        _MainTex ("Satellite Texture", 2D) = "white" {}
        _Color ("Natural Tint", Color) = (0.92, 0.90, 0.84, 1)
        _Cutoff ("Coast Cutout", Range(0, 1)) = 0.42
        _Glossiness ("Smoothness", Range(0, 1)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "Queue" = "AlphaTest"
            "RenderType" = "TransparentCutout"
        }
        Cull Off
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows alphatest:_Cutoff addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _Glossiness;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 viewDir;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 satellite = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            float slopeShade = saturate(0.82 + 0.18 * saturate(IN.worldPos.y / 28.0));
            float fresnel = pow(1.0 - saturate(dot(normalize(IN.viewDir), float3(0, 1, 0))), 3.0);
            float wetEdge = saturate(1.0 - IN.worldPos.y / 2.4) * 0.18;
            o.Albedo = satellite.rgb * slopeShade;
            o.Albedo = lerp(o.Albedo, o.Albedo * float3(0.72, 0.78, 0.74), wetEdge);
            o.Metallic = 0.0;
            o.Smoothness = lerp(_Glossiness, 0.45, wetEdge) + fresnel * 0.04;
            o.Alpha = satellite.a;
            o.Emission = satellite.rgb * (0.015 + wetEdge * 0.02);
        }
        ENDCG
    }

    FallBack "Transparent/Cutout/Diffuse"
}
