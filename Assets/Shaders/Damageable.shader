// Building surface that can be blown open.
//  - Same look as URP Lit (base map, color, normal map, smoothness, metallic, emission, shadows, fog)
//  - Holes: up to 256 capsules (global arrays filled by DamageSystem) are cut out of the surface,
//    so a laser tunnel through both walls lets you see the other side
//  - Hole edges glow molten (laser) then cool to black scorch; punched holes get a jagged, dusty rim
//  - Back faces (the inside of the building, seen through a hole) render as dark interior concrete
//  - Shadow / depth passes cut the same holes, so light shines through them
Shader "OverPeak/Damageable"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.3
        _Metallic ("Metallic", Range(0, 1)) = 0
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        _EmissionMap ("Emission Map", 2D) = "white" {}
        _InteriorColor ("Interior Color", Color) = (0.22, 0.21, 0.2, 1)
        [Toggle(_NORMALMAP)] _UseNormalMap ("Use Normal Map", Float) = 0
        [Toggle(_EMISSION)] _UseEmission ("Use Emission", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor;
        half _BumpScale, _Smoothness, _Metallic;
        half4 _EmissionColor;
        half4 _InteriorColor;
    CBUFFER_END

    // holes: A = (start.xyz, radius), B = (end.xyz, birth time; negative = punched hole)
    #define MAX_HOLES 256
    float4 _HoleA[MAX_HOLES];
    float4 _HoleB[MAX_HOLES];
    int _HoleCount;

    float Hash31(float3 p)
    {
        p = frac(p * 0.1031);
        p += dot(p, p.zyx + 31.32);
        return frac((p.x + p.y) * p.z);
    }

    float ValueNoise(float3 p)
    {
        float3 i = floor(p), f = frac(p);
        f = f * f * (3 - 2 * f);
        float n000 = Hash31(i), n100 = Hash31(i + float3(1, 0, 0)), n010 = Hash31(i + float3(0, 1, 0)), n110 = Hash31(i + float3(1, 1, 0));
        float n001 = Hash31(i + float3(0, 0, 1)), n101 = Hash31(i + float3(1, 0, 1)), n011 = Hash31(i + float3(0, 1, 1)), n111 = Hash31(i + float3(1, 1, 1));
        return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                    lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
    }

    // signed distance to the nearest hole rim (negative = inside a hole) and that hole's birth time
    float HoleDistance(float3 p, out float birth)
    {
        float best = 1e5;
        birth = -1e5;
        for (int i = 0; i < _HoleCount; i++)
        {
            float3 a = _HoleA[i].xyz, b = _HoleB[i].xyz;
            float3 ab = b - a;
            float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-5));
            float dist = distance(p, a + ab * t);
            float r = _HoleA[i].w;
            if (_HoleB[i].w < 0)
                r *= 0.82 + 0.36 * ValueNoise(p * 2.3);       // punched: jagged broken rim
            else
                r *= 0.96 + 0.08 * ValueNoise(p * 6.0);       // laser: clean melted circle
            float d = dist - r;
            if (d < best) { best = d; birth = _HoleB[i].w; }
        }
        return best;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _EMISSION
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = nrm.normalWS;
                o.tangentWS = float4(nrm.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float birth;
                float d = HoleDistance(i.positionWS, birth);
                clip(d);

                bool front = IS_FRONT_VFACE(face, true, false);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                float3 n = normalize(i.normalWS);
                half3 albedo = tex.rgb;
                half smooth = _Smoothness, metal = _Metallic;

                #if defined(_NORMALMAP)
                if (front)
                {
                    half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                    float3 bit = i.tangentWS.w * cross(n, i.tangentWS.xyz);
                    n = normalize(TransformTangentToWorld(nTS, half3x3(i.tangentWS.xyz, bit, n)));
                }
                #endif

                if (!front)
                {
                    // inside of the building
                    n = -n;
                    float grime = 0.75 + 0.25 * ValueNoise(i.positionWS * 0.6);
                    albedo = _InteriorColor.rgb * grime;
                    smooth = 0.05; metal = 0;
                }

                // scorch / crumble around holes
                float scorch = saturate(1 - d / 0.7);
                albedo *= 1 - scorch * scorch * 0.88;
                smooth *= 1 - scorch;

                // molten glow on fresh laser holes
                half3 glow = 0;
                if (birth >= 0)
                {
                    float age = _Time.y - birth;
                    float heat = saturate(1 - age / 7.0);
                    heat *= heat;
                    float rim = saturate(1 - d / 0.12);
                    float hot = saturate(1 - d / 0.32);
                    half3 white = half3(1.0, 0.85, 0.55) * 9;
                    half3 orange = half3(1.0, 0.32, 0.04) * 4;
                    half3 red = half3(0.6, 0.05, 0.0) * 1.5;
                    half3 c = lerp(red, lerp(orange, white, saturate(heat * 1.4 - 0.4)), heat);
                    glow = c * (rim * rim + hot * 0.35) * heat;
                }

                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.positionCS = i.positionCS;
                input.normalWS = n;
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                input.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                input.fogCoord = i.fogFactor;
                input.bakedGI = SampleSH(n) * (front ? 1 : 0.45);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.metallic = metal;
                s.smoothness = smooth;
                s.occlusion = 1;
                s.alpha = 1;
                s.normalTS = half3(0, 0, 1);
                s.emission = glow;
                #if defined(_EMISSION)
                if (front) s.emission += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb * (1 - scorch);
                #endif

                half4 col = UniversalFragmentPBR(input, s);
                col.rgb = MixFog(col.rgb, i.fogFactor);
                col.a = 1;
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, lightDir));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                o.positionWS = posWS;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float birth;
                clip(HoleDistance(i.positionWS, birth));
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half frag(Varyings i) : SV_Target
            {
                float birth;
                clip(HoleDistance(i.positionWS, birth));
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float birth;
                clip(HoleDistance(i.positionWS, birth));
                float3 n = normalize(i.normalWS) * (IS_FRONT_VFACE(face, 1, -1));
                return half4(n, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
