// Re-entry fire: a glowing shell of hot air around the hero, brightest at the leading edge (+Z), flickering.
Shader "OverPeak/Plasma"
{
    Properties
    {
        _HotColor ("Leading Edge", Color) = (4, 3.2, 2.2, 1)
        _FireColor ("Flames", Color) = (3.2, 1.1, 0.25, 1)
        _Intensity ("Intensity", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _HotColor, _FireColor;
                float _Intensity;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 posOS : TEXCOORD2; };

            V vert(A v)
            {
                V o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.posOS = v.positionOS.xyz;
                return o;
            }
            float H(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float N(float3 p)
            {
                float3 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(H(i), H(i + float3(1,0,0)), f.x), lerp(H(i + float3(0,1,0)), H(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(H(i + float3(0,0,1)), H(i + float3(1,0,1)), f.x), lerp(H(i + float3(0,1,1)), H(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            half4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float rim = pow(1 - abs(dot(n, v)), 1.6);
                float front = saturate(i.posOS.z * 2);                       // 0 at the middle, 1 at the nose of the shell
                float back = saturate(-i.posOS.z * 2);
                // flames streaming backwards
                float t = _Time.y;
                float f = N(float3(i.posOS.xy * 9, i.posOS.z * 4 + t * 14)) * 0.65 + N(float3(i.posOS.xy * 21, i.posOS.z * 9 + t * 25)) * 0.35;
                float flame = saturate(f * 1.6 - back * 0.9);
                // see-through in the middle (he stays visible), fire round the edges, white-hot at the nose
                half3 col = _HotColor.rgb * pow(front, 4) * (0.7 + 0.3 * f) * (0.3 + rim)
                          + _FireColor.rgb * flame * rim * (1 - back * 0.7);
                return half4(col * _Intensity, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
