// Superman's cape: soft fabric, lit on both sides (the inside faces get flipped normals), casts and receives shadows.
Shader "OverPeak/Cape"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (0.6, 0.04, 0.05, 1)
        _InsideColor ("Inside Colour", Color) = (0.48, 0.03, 0.04, 1)
        _Sheen ("Fabric Sheen", Range(0, 1)) = 0.35
        _Cull ("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _InsideColor;
                half _Sheen, _Cull;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; float fog : TEXCOORD3; };

            V vert(A v)
            {
                V o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 N = normalize(i.normalWS) * (front ? 1 : -1);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 albedo = front ? _BaseColor.rgb : _InsideColor.rgb;
                // folds: a faint weave pattern across the cloth
                albedo *= 0.94 + 0.06 * sin(i.uv.x * 900) * sin(i.uv.y * 1300);
                float ndl = dot(N, sun.direction);
                half wrap = saturate(ndl * 0.6 + 0.4);                         // fabric lets a little light round the folds
                half3 col = albedo * sun.color * wrap * sun.shadowAttenuation;
                col += albedo * SampleSH(N) * 0.9;
                // sheen: soft bright edge on the cloth, like satin
                half rim = pow(1 - saturate(dot(N, V)), 3);
                col += sun.color * _Sheen * rim * saturate(ndl + 0.3) * 0.5;
                // light shining through the thin cloth from behind
                col += albedo * sun.color * saturate(-ndl) * 0.25;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
    FallBack Off
}
