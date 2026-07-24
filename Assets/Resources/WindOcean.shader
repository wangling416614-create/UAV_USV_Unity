Shader "UavUsv/WindOcean"
{
    Properties
    {
        _DeepColor ("Deep Water", Color) = (0.05, 0.12, 0.16, 1)
        _ShallowColor ("Shallow Water", Color) = (0.13, 0.21, 0.23, 1)
        _FoamColor ("Foam", Color) = (0.62, 0.66, 0.64, 1)
        _WindDirection ("Wind Direction XZ", Vector) = (0.88, 0, 0.48, 0)
        _WindSpeed ("Wind Speed", Range(0, 20)) = 7
        _WaveAmplitude ("Wave Amplitude", Range(0, 1.5)) = 0.28
        _Smoothness ("Smoothness", Range(0, 1)) = 0.90
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

        float Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float Noise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(
                lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x),
                lerp(Hash21(i + float2(0, 1)), Hash21(i + 1), f.x),
                f.y
            );
        }

        float Wave(float2 p, float2 direction, float length, float speed, float phase)
        {
            float k = 6.2831853 / length;
            return sin(dot(p, normalize(direction)) * k + _Time.y * speed + phase);
        }

        float Height(float2 p)
        {
            float2 wind = normalize(_WindDirection.xz + float2(0.0001, 0));
            float speedScale = lerp(0.40, 1.20, saturate(_WindSpeed / 15.0));
            float h = 0;
            // Longer wavelengths reduce visible tiling across the 1 km domain.
            h += Wave(p, wind,                  48.0, 0.42 * speedScale, 0.0) * 0.34;
            h += Wave(p, Rotate2(wind, 0.55),   27.0, 0.58 * speedScale, 1.4) * 0.24;
            h += Wave(p, Rotate2(wind, -0.92),  15.5, 0.78 * speedScale, 2.7) * 0.18;
            h += Wave(p, Rotate2(wind, 1.45),   8.4,  1.05 * speedScale, 0.6) * 0.12;
            h += Wave(p, Rotate2(wind, -1.88),  4.6,  1.40 * speedScale, 3.2) * 0.08;
            float breakup = (Noise(p * 0.035 + _Time.y * 0.02) - 0.5) * 0.22;
            return (h + breakup) * _WaveAmplitude;
        }

        void vert(inout appdata_full v)
        {
            float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
            float e = 0.12;
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
            float noiseBreak = Noise(p * 0.08 + _Time.y * 0.03);
            microNormal.x = cos(p.x * 1.55 + p.y * 0.72 + _Time.y * 1.1) * 0.42 +
                            cos(p.x * 3.4 - p.y * 1.6 - _Time.y * 1.6) * 0.28 +
                            cos(p.x * 6.8 + p.y * 2.4 + _Time.y * 2.2) * 0.18;
            microNormal.y = cos(p.y * 1.7 - p.x * 0.55 + _Time.y * 1.0) * 0.40 +
                            cos(p.y * 3.7 + p.x * 1.3 - _Time.y * 1.7) * 0.30 +
                            cos(p.y * 7.4 - p.x * 2.1 + _Time.y * 2.4) * 0.18;
            microNormal *= lerp(0.85, 1.15, noiseBreak);
            o.Normal = normalize(float3(microNormal * 0.035, 1.0));

            float3 worldSurfaceNormal = normalize(WorldNormalVector(IN, o.Normal));
            float facing = saturate(dot(worldSurfaceNormal, normalize(IN.viewDir)));
            float fresnel = pow(1.0 - facing, 4.2);
            float detail = sin(IN.worldPos.x * 0.55 + _Time.y * 0.85) *
                           sin(IN.worldPos.z * 0.62 - _Time.y * 0.70);
            float longRipple = 0.5 + 0.5 *
                sin(IN.worldPos.x * 0.055 + IN.worldPos.z * 0.09 + _Time.y * 0.38);
            float crossRipple = 0.5 + 0.5 *
                sin(IN.worldPos.x * -0.08 + IN.worldPos.z * 0.04 - _Time.y * 0.28);
            float rippleLight = saturate(longRipple * 0.55 + crossRipple * 0.45);
            float steepness = 1.0 - worldSurfaceNormal.y;
            float crest = smoothstep(0.018, 0.115, steepness) *
                          saturate(detail * 0.18 + 0.86);
            fixed3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(fresnel * 0.28 + 0.08));
            water *= lerp(0.975, 1.025, rippleLight);

            float3 reflectionDirection = WorldReflectionVector(IN, o.Normal);
            half4 environmentSample = UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, normalize(reflectionDirection));
            half3 environment = DecodeHDR(environmentSample, unity_SpecCube0_HDR);
            float2 reflectionUv = IN.screenPos.xy / max(IN.screenPos.w, 0.0001);
            #if UNITY_UV_STARTS_AT_TOP
                if (_PlanarReflectionTex_TexelSize.y < 0) reflectionUv.y = 1.0 - reflectionUv.y;
            #endif
            float viewDistance = distance(_WorldSpaceCameraPos, IN.worldPos);
            float distortion = lerp(0.0015, 0.007, saturate(viewDistance / 110.0));
            reflectionUv += microNormal * distortion;
            float2 blurOffset = _PlanarReflectionTex_TexelSize.xy * lerp(1.2, 3.5, saturate(viewDistance / 120.0));
            half3 planarReflection = tex2D(_PlanarReflectionTex, reflectionUv).rgb * 0.36;
            planarReflection += tex2D(_PlanarReflectionTex, reflectionUv + float2( blurOffset.x, 0)).rgb * 0.14;
            planarReflection += tex2D(_PlanarReflectionTex, reflectionUv + float2(-blurOffset.x, 0)).rgb * 0.14;
            planarReflection += tex2D(_PlanarReflectionTex, reflectionUv + float2(0,  blurOffset.y)).rgb * 0.14;
            planarReflection += tex2D(_PlanarReflectionTex, reflectionUv + float2(0, -blurOffset.y)).rgb * 0.14;
            planarReflection = planarReflection / (1.0 + planarReflection * 0.22);
            float reflectionWeight = saturate(_ReflectionAvailable) * saturate(0.16 + fresnel * 0.62);
            environment = lerp(environment, planarReflection, reflectionWeight);

            o.Albedo = lerp(water * 0.88, _FoamColor.rgb, crest * 0.24);
            o.Metallic = 0.0;
            o.Smoothness = lerp(_Smoothness, 0.68, crest);
            o.Emission =
                water * (0.028 + rippleLight * 0.012) +
                environment * (0.08 + fresnel * 0.28) +
                _FoamColor.rgb * crest * 0.075;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Standard"
}
