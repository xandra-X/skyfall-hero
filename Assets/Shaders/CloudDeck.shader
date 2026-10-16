// The low cloud layer over the city: scattered clouds with clear gaps, so the city stays visible from high up.
// It follows the camera and bends down with the Earth's curve away from the city.
Shader "OverPeak/CloudDeck"
{
    Properties
    {
        _Scale ("Noise Scale", Float) = 0.0019
        _Cover ("Coverage", Range(0, 1)) = 0.34
        _Fade ("Edge Fade Radius", Float) = 15000
        _Opacity ("Opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-20" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float4 _CityCut;      // global: xz = city centre
            float4 _EarthBend;    // global: x = Earth radius, y = flat radius around the city, z = drop at that radius
            CBUFFER_START(UnityPerMaterial)
                float _Scale, _Cover, _Fade, _Opacity;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            V vert(A v)
            {
                V o;
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                if (_EarthBend.x > 0)
                {
                    float r = min(distance(p.xz, _CityCut.xz), _EarthBend.x * 0.99);
                    if (r > _EarthBend.y) p.y -= _EarthBend.x - sqrt(_EarthBend.x * _EarthBend.x - r * r) - _EarthBend.z;
                }
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                return o;
            }
            float H(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float N(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(H(i), H(i + float2(1, 0)), f.x), lerp(H(i + float2(0, 1)), H(i + 1), f.x), f.y);
            }
            float Fbm(float2 p)
            {
                float s = 0, a = 0.5;
                [unroll] for (int k = 0; k < 5; k++) { s += N(p) * a; p = p * 2.03 + 17.1; a *= 0.5; }
                return s;
            }
            half4 frag(V i) : SV_Target
            {
                float2 p = i.positionWS.xz * _Scale + _Time.y * 0.004;
                float n = Fbm(p) * 0.8 + Fbm(p * 3.7 + 4.3) * 0.2;
                // a clearing over the city, turning into a thick sea of clouds further out
                float cityD = distance(i.positionWS.xz, _CityCut.xz);
                float cover = lerp(_Cover, 0.8, smoothstep(2200, 7000, cityD));
                float density = smoothstep(1 - cover, 1 - cover + 0.22, n);
                float3 cam = GetCameraPositionWS();
                float edge = saturate(1 - distance(i.positionWS.xz, cam.xz) / _Fade);
                // thin out right around the camera so he can always see down through the gap he is in
                float close = distance(i.positionWS, cam);
                float gap = smoothstep(80, 600, close);
                // seen edge-on far away (out past the Earth's rim) the layer fades instead of floating in space
                float3 earthC = float3(_CityCut.x, _EarthBend.z - _EarthBend.x, _CityCut.z);
                float fadeFrom = lerp(0.04, 0.42, saturate((cam.y - 3000) / 6000));
                float facing = _EarthBend.x > 0 ? dot(normalize(i.positionWS - earthC), normalize(cam - i.positionWS)) : 1;
                float a = density * 0.88 * edge * gap * _Opacity * smoothstep(fadeFrom, fadeFrom + 0.2, facing);
                Light sun = GetMainLight();
                bool above = cam.y > i.positionWS.y;
                // puffy shading: thicker parts brighter on top, greyer underneath
                float thick = saturate((n - (1 - cover)) * 3);
                half sunMax = max(max(sun.color.r, sun.color.g), max(sun.color.b, 0.001));
                half3 tint = pow(sun.color / sunMax, 1.4);              // colour of the sunlight (gold at sunset), a bit richer
                half sunLevel = min(sunMax, 1.25);
                // fake bumps: slope of the noise toward the sun lights one side of each billow
                float n2 = Fbm(p + normalize(sun.direction.xz + 1e-4) * 0.06);
                float lit = saturate(0.55 + (n - n2) * 6 + thick * 0.3);
                half3 shade = lerp(half3(0.5, 0.52, 0.64), half3(0.36, 0.33, 0.48), saturate(1 - tint.b));   // purple shadows at sunset
                half3 col = above ? lerp(shade * lerp(1, tint, 0.3), tint * 1.05, lit)
                                  : lerp(half3(0.78, 0.8, 0.84), half3(0.55, 0.58, 0.64), thick) * lerp(1, tint, 0.4);
                float3 V = normalize(cam - i.positionWS);
                col += tint * pow(saturate(dot(-V, sun.direction)), 8) * (1 - thick) * 0.8;   // glow toward the sun
                return half4(col * sunLevel, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
