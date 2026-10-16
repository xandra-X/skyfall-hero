// 3D cumulus clouds made of many soft alpha cards (the imported cloud model).
// Lit like a puffy volume: the mesh normals point out from the cloud's middle, the sun side glows warm,
// the shadow side is cool, and edges facing the sun get a bright silver lining. Fades out when flown into.
Shader "OverPeak/CloudCard"
{
    Properties
    {
        _BaseMap ("Card Alpha (A)", 2D) = "white" {}
        _BumpMap ("Card Normal", 2D) = "bump" {}
        _Opacity ("Opacity", Range(0, 1)) = 1
        _ShadowColor ("Shadow Side", Color) = (0.48, 0.5, 0.62, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-15" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            float _CloudSeaOpacity;   // global: fades all the 3D clouds out high up
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Opacity;
                half4 _ShadowColor;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2; float fog : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };

            V vert(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                float3 camPos = GetCameraPositionWS();
                float dist = distance(i.positionWS, camPos);
                // flying into a cloud: cards right in front of the camera fade so it doesn't go white
                float a = t.a * _Opacity * _CloudSeaOpacity * smoothstep(8, 70, dist);
                clip(a - 0.003);

                half3 bump = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv));
                float3 N = normalize(normalize(i.normalWS) + float3(bump.x, 0, bump.y) * 0.35);
                float3 V = normalize(camPos - i.positionWS);
                Light sun = GetMainLight();
                float3 L = sun.direction;
                half sunMax = max(max(sun.color.r, sun.color.g), max(sun.color.b, 0.001));
                half3 tint = sun.color / sunMax;                            // colour of the light, not its strength
                half level = min(sunMax, 1.3);

                float wrap = saturate(dot(N, L) * 0.55 + 0.45);
                half3 col = lerp(_ShadowColor.rgb * lerp(1, tint, 0.35), tint * 1.08, pow(wrap, 1.4));
                col *= 0.82 + 0.18 * t.r;                                   // texture detail inside the puffs
                // silver lining: thin edges glow when looking toward the sun
                float back = pow(saturate(dot(-V, L)), 6);
                col += tint * back * (1 - t.a) * 1.6;
                col *= level;
                col = MixFog(col, i.fog);
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
