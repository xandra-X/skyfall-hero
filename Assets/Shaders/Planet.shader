// Earth / Moon surface lit by the sun: day side, soft terminator, dark night side, ocean glints,
// blue atmosphere rim. Optional polar cap cut-out (the Moon's landing crater sits there).
Shader "OverPeak/Planet"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _OceanMask ("Ocean Mask (R)", 2D) = "black" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _UseNormal ("Use Normal Map", Float) = 0
        _NightColor ("Night Side", Color) = (0.01, 0.015, 0.03, 1)
        _RimColor ("Atmosphere Rim", Color) = (0.35, 0.6, 1.0, 1)
        _RimStrength ("Rim Strength", Float) = 1.2
        _Specular ("Ocean Glint", Float) = 1.5
        _CapDir ("Cut-out Cap Direction (object)", Vector) = (0, 1, 0, 0)
        _CapCos ("Cut-out Cap Cos (1 = none)", Float) = 2
        _DetailMap ("Close-up Detail", 2D) = "gray" {}
        _DetailBump ("Close-up Detail Normal", 2D) = "bump" {}
        _DetailTiling ("Detail Tiling", Vector) = (64, 32, 0, 0)
        _DetailDistance ("Detail Fade Distance", Float) = 0
        _UseCityCut ("Hole Under The City", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_OceanMask); SAMPLER(sampler_OceanMask);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_DetailMap); SAMPLER(sampler_DetailMap);
            TEXTURE2D(_DetailBump); SAMPLER(sampler_DetailBump);
            float4 _CityCut;
            float4 _EarthshineDir;    // global: direction to the Earth seen from the Moon (w = strength)
            half4 _EarthshineColor;        // global: xz = city centre, y = its height, w = radius of the hole (0 = none)
            half4 _CityCutTint;     // global: colour the globe blends to around the hole (matches the sea)
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _NightColor, _RimColor;
                half _RimStrength, _Specular, _UseNormal;
                float4 _CapDir;
                float _CapCos;
                float4 _DetailTiling;
                float _DetailDistance, _UseCityCut;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2; float4 tangentWS : TEXCOORD3; float3 normalOS : TEXCOORD4; float fog : TEXCOORD5; };

            V vert(A v)
            {
                V o;
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = n.normalWS;
                o.tangentWS = float4(n.tangentWS, v.tangentOS.w);
                o.normalOS = normalize(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                clip(_CapCos - dot(i.normalOS, normalize(_CapDir.xyz)));
                // the real city and its sea sit in a hole cut in the globe's top
                float cityD = distance(i.positionWS.xz, _CityCut.xz);
                bool nearSide = _UseCityCut > 0.5 && _CityCut.w > 0 && i.positionWS.y > _CityCut.y - 8000;
                if (nearSide) clip(cityD - _CityCut.w);
                float3 N = normalize(i.normalWS);
                if (_UseNormal > 0.5)
                {
                    half3 nts = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv));
                    float3 b = i.tangentWS.w * cross(N, i.tangentWS.xyz);
                    N = normalize(TransformTangentToWorld(nts, half3x3(i.tangentWS.xyz, b, N)));
                }
                // close up (walking on the Moon): fine surface detail tiled over the blurry global map
                float detailK = _DetailDistance > 0 ? saturate(1 - distance(i.positionWS, GetCameraPositionWS()) / _DetailDistance) : 0;
                half3 detailCol = 1;
                if (detailK > 0)
                {
                    float2 duv = i.uv * _DetailTiling.xy;
                    half3 d = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, duv).rgb;
                    half3 avg = SAMPLE_TEXTURE2D_LOD(_DetailMap, sampler_DetailMap, duv, 12).rgb;
                    detailCol = lerp(1, d / max(avg, 0.05), detailK * 0.6);
                    half3 dn = UnpackNormal(SAMPLE_TEXTURE2D(_DetailBump, sampler_DetailBump, duv));
                    float3 b = i.tangentWS.w * cross(N, i.tangentWS.xyz);
                    N = normalize(lerp(N, TransformTangentToWorld(dn, half3x3(i.tangentWS.xyz, b, N)), detailK));
                }
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light sun = GetMainLight();
                float3 L = sun.direction;
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * detailCol;
                half ocean = SAMPLE_TEXTURE2D(_OceanMask, sampler_OceanMask, i.uv).r;

                float ndl = dot(N, L);
                float day = smoothstep(-0.15, 0.25, ndl);                     // soft terminator
                half3 col = albedo * sun.color * saturate(ndl * 0.9 + 0.1) * 1.15;
                col = lerp(albedo * _NightColor.rgb * 3 + _NightColor.rgb, col, day);

                float3 H = normalize(L + V);
                col += sun.color * pow(saturate(dot(N, H)), 120) * _Specular * ocean * day;

                // earthshine: the bright Earth faintly lights the Moon's surface, even on its night side
                col += albedo * _EarthshineColor.rgb * saturate(dot(N, _EarthshineDir.xyz) * 0.6 + 0.4) * _EarthshineDir.w * (1 - _UseCityCut);

                float rim = pow(1 - saturate(dot(N, V)), 3);
                col += _RimColor.rgb * rim * _RimStrength * saturate(ndl + 0.35);
                if (nearSide) col = lerp(_CityCutTint.rgb * saturate(ndl * 0.9 + 0.1) * 1.15, col, smoothstep(_CityCut.w, _CityCut.w * 2.4, cityD));
                return half4(MixFog(col, i.fog), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
