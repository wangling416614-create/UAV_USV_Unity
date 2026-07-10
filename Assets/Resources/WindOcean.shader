Shader "UavUsv/WindOcean"
{
    Properties
    {
        _DeepColor ("Deep Water", Color) = (0.045, 0.18, 0.24, 1)
        _ShallowColor ("Shallow Water", Color) = (0.11, 0.39, 0.48, 1)
        _FoamColor ("Foam", Color) = (0.92, 0.97, 0.98, 1)
        _WindDirection ("Wind Direction XZ", Vector) = (0.88, 0, 0.48, 0)
        _WindSpeed ("Wind Speed", Range(0, 20)) = 7
        _WaveAmplitude ("Wave Amplitude", Range(0, 1.5)) = 0.32
        _Smoothness ("Smoothness", Range(0, 1)) = 0.88
        [HideInInspector] _PlanarReflectionTex ("Planar Reflection", 2D) = "black" {}
        [HideInInspector] _ReflectionAvailable ("Reflection Available", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300
        Cull Off

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.0
        #include "UnityCG.cginc"

        fixed4 _DeepColor;
        fixed4 _ShallowColor;
        fixed4 _FoamColor;
        float4 _WindDirection;
        float _WindSpeed;
        float _WaveAmplitude;
        half _Smoothness;
        sampler2D _PlanarReflectionTex;
        float4 _PlanarReflectionTex_TexelSize;
        float _ReflectionAvailable;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            float3 viewDir;
            float3 worldRefl;
            float4 screenPos;
            INTERNAL_DATA
        };

        float2 Rotate2(float2 v, float angle)
        {
            float s = sin(angle), c = cos(angle);
            return float2(c * v.x - s * v.y, s * v.x + c * v.y);
        }

        float Wave(float2 p, float2 direction, float length, float speed, float phase)
        {
            float k = 6.2831853 / length;
            return sin(dot(p, normalize(direction)) * k + _Time.y * speed + phase);
        }

        float Height(float2 p)
        {
            float2 wind = normalize(_WindDirection.xz + float2(0.0001, 0));
            float speedScale = lerp(0.45, 1.35, saturate(_WindSpeed / 15.0));
            float h = 0;
            h += Wave(p, wind,                  15.0, 0.82 * speedScale, 0.0) * 0.30;
            h += Wave(p, Rotate2(wind, 0.61),   9.2,  1.08 * speedScale, 1.7) * 0.21;
            h += Wave(p, Rotate2(wind, -1.03),  6.1,  1.32 * speedScale, 3.1) * 0.17;
            h += Wave(p, Rotate2(wind, 1.67),   3.8,  1.68 * speedScale, 0.8) * 0.13;
            h += Wave(p, Rotate2(wind, -2.16),  2.35, 2.12 * speedScale, 2.4) * 0.11;
            h += Wave(p, Rotate2(wind, 2.72),   1.45, 2.76 * speedScale, 4.2) * 0.08;
            return h * _WaveAmplitude;
        }

        void vert(inout appdata_full v)
        {
            float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
            float e = 0.08;
            float h = Height(world.xz);
            float hx = Height(world.xz + float2(e, 0));
            float hz = Height(world.xz + float2(0, e));
            world.y += h;
            v.vertex = mul(unity_WorldToObject, float4(world, 1));
            float3 worldNormal = normalize(float3(-(hx - h) / e, 1, -(hz - h) / e));
            v.normal = normalize(mul((float3x3)unity_WorldToObject, worldNormal));
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 p = IN.worldPos.xz;
            float2 microNormal;
            microNormal.x = cos(p.x * 4.1 + p.y * 1.7 + _Time.y * 2.4) * 0.52 +
                            cos(p.x * 7.3 - p.y * 3.2 - _Time.y * 3.1) * 0.28 +
                            cos(p.x * 11.7 + p.y * 5.1 + _Time.y * 4.0) * 0.20;
            microNormal.y = cos(p.y * 4.7 - p.x * 1.3 + _Time.y * 2.1) * 0.48 +
                            cos(p.y * 8.1 + p.x * 2.8 - _Time.y * 3.4) * 0.31 +
                            cos(p.y * 13.2 - p.x * 4.5 + _Time.y * 4.3) * 0.21;
            o.Normal = normalize(float3(microNormal * 0.075, 1.0));

            float3 worldSurfaceNormal = normalize(WorldNormalVector(IN, o.Normal));
            float facing = saturate(dot(worldSurfaceNormal, normalize(IN.viewDir)));
            float fresnel = pow(1.0 - facing, 5.0);
            float detail = sin(IN.worldPos.x * 1.18 + _Time.y * 1.35) *
                           sin(IN.worldPos.z * 1.42 - _Time.y * 1.1);
            float crest = (1.0 - smoothstep(0.70, 0.88, worldSurfaceNormal.y)) * saturate(detail * 0.25 + 0.75);
            fixed3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(fresnel * 0.22 + 0.035));
            float3 reflectionDirection = WorldReflectionVector(IN, o.Normal);
            half4 environmentSample = UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, normalize(reflectionDirection));
            half3 environment = DecodeHDR(environmentSample, unity_SpecCube0_HDR);
            float2 reflectionUv = IN.screenPos.xy / max(IN.screenPos.w, 0.0001);
            #if UNITY_UV_STARTS_AT_TOP
                if (_PlanarReflectionTex_TexelSize.y < 0) reflectionUv.y = 1.0 - reflectionUv.y;
            #endif
            float viewDistance = distance(_WorldSpaceCameraPos, IN.worldPos);
            float distortion = lerp(0.002, 0.008, saturate(viewDistance / 90.0));
            reflectionUv += microNormal * distortion;
            float2 blurOffset = _PlanarReflectionTex_TexelSize.xy * lerp(1.5, 4.0, saturate(viewDistance / 100.0));
            half3 planarReflection = tex2D(_PlanarReflectionTex, reflectionUv).rgb * 0.34;
            planarReflection += tex2D(_PlanarReflectionTex, reflectionUv + float2( blurOffset.x, 0)).rgb * 0.14;
            planarReflection += tex2D(_PlanarReflectionTex, reflectionUv + float2(-blurOffset.x, 0)).rgb * 0.14;
            planarReflection += tex2D(_PlanarReflectionTex, reflectionUv + float2(0,  blurOffset.y)).rgb * 0.14;
            planarReflection += tex2D(_PlanarReflectionTex, reflectionUv + float2(0, -blurOffset.y)).rgb * 0.14;
            planarReflection = planarReflection / (1.0 + planarReflection * 0.28);
            float reflectionWeight = saturate(_ReflectionAvailable) * saturate(0.12 + fresnel * 0.58);
            environment = lerp(environment, planarReflection, reflectionWeight);
            o.Albedo = lerp(water * 0.86, _FoamColor.rgb, crest * 0.09);
            o.Metallic = 0.02;
            o.Smoothness = lerp(0.95, 0.76, crest);
            o.Emission = water * 0.028 + environment * (0.075 + fresnel * 0.34) + _FoamColor.rgb * crest * 0.032;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Standard"
}
