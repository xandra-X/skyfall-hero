// Planet atmosphere shell (additive) and cloud shell (alpha from a grayscale map), both lit by the sun.
Shader "OverPeak/Atmosphere"
{
    Properties
    {
        _Color ("Glow Color", Color) = (0.3, 0.55, 1.0, 1)
        _Power ("Edge Power", Float) = 3.5
        _Strength ("Strength", Float) = 1.6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float _AtmoVisible;     // global: 0 low over the city, 1 high up
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; half _Power, _Strength;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };
            V vert(A v)
            {
                V o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                // seen from outside through the back faces: brightest where the line of sight grazes the planet
                float3 N = -normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float edge = pow(saturate(1 - abs(dot(N, V))), _Power);
                float lit = saturate(dot(-N, GetMainLight().direction) + 0.3);
                return half4(_Color.rgb * edge * lit * _Strength * _AtmoVisible, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
