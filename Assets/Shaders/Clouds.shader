// Two cloud shaders:
//  OverPeak/CloudShell - the planet's cloud layer (grayscale map = cloud amount), slowly drifting, lit by the sun
//  OverPeak/CloudDeck  - the cloud layer the hero flies through above the city (procedural), fades at the edges
Shader "OverPeak/CloudShell"
{
    Properties
    {
        _BaseMap ("Clouds (gray)", 2D) = "black" {}
        _Speed ("Drift", Float) = 0.002
        _Opacity ("Opacity", Float) = 0.95
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            float4 _CityCut;
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST; float _Speed, _Opacity;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2; };
            V vert(A v)
            {
                V o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv + float2(_Time.y * _Speed, 0);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).r;
                // over the real city the low cloud layer takes over: thin these out to nothing there
                if (_CityCut.w > 0 && i.positionWS.y > _CityCut.y - 8000)
                    c *= smoothstep(_CityCut.w * 1.1, _CityCut.w * 1.8, distance(i.positionWS.xz, _CityCut.xz));
                Light sun = GetMainLight();
                float ndl = dot(normalize(i.normalWS), sun.direction);
                half light = smoothstep(-0.2, 0.3, ndl) * 0.95 + 0.05;
                return half4(sun.color * light, saturate(c * 1.3) * _Opacity);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
