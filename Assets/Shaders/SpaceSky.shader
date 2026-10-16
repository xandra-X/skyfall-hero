// Skybox that goes from a blue day sky to black space with stars as the hero climbs.
// _SpaceBlend (global, 0 = ground, 1 = space) and _StarBlend are set by SpaceSystem.
Shader "OverPeak/SpaceSky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.22, 0.42, 0.78, 1)
        _HorizonColor ("Horizon", Color) = (0.72, 0.82, 0.93, 1)
        _GroundColor ("Below Horizon", Color) = (0.45, 0.5, 0.55, 1)
        _SunSize ("Sun Size", Range(0.0005, 0.05)) = 0.004
        _StarDensity ("Star Density", Range(0.99, 0.9999)) = 0.9965
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half4 _ZenithColor, _HorizonColor, _GroundColor;
            float _SunSize, _StarDensity;
            float _SpaceBlend, _StarBlend, _Golden;
            float4 _SkySunDir;      // global: where the sun is drawn at golden hour (sitting on the cloud horizon)

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            V vert(A v)
            {
                V o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float Hash(float3 p)
            {
                p = frac(p * float3(443.897, 441.423, 437.195));
                p += dot(p, p.yzx + 19.19);
                return frac((p.x + p.y) * p.z);
            }

            float Noise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1,0,0)), f.x), lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x), lerp(Hash(i + float3(0,1,1)), Hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            // point-like stars on a 3D grid of cells around the view direction
            float Stars(float3 d, float scale, float density)
            {
                float3 p = d * scale;
                float3 cell = floor(p);
                float h = Hash(cell);
                if (h < density) return 0;
                float3 starPos = cell + 0.5 + (float3(Hash(cell + 11.3), Hash(cell + 27.1), Hash(cell + 41.7)) - 0.5) * 0.7;
                float dist = length(p - starPos);
                float size = 0.06 + 0.12 * Hash(cell + 7.7);
                float twinkle = 0.75 + 0.25 * sin(_Time.y * (1.5 + 3 * Hash(cell + 3.1)) + h * 50);
                return saturate(1 - dist / size) * twinkle * (0.4 + 0.6 * (h - density) / (1 - density));
            }

            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                Light sun = GetMainLight();
                float3 L = normalize(sun.direction);
                if (_Golden > 0.001) L = normalize(lerp(L, _SkySunDir.xyz, _Golden));
                float sunDot = dot(d, L);

                // ---- day sky
                float up = d.y;
                half3 day = up > 0 ? lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(saturate(up), 0.55))
                                   : lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-up * 4));
                day += half3(1.0, 0.85, 0.6) * pow(saturate(sunDot), 8) * 0.35;          // glow around the sun
                float disk = smoothstep(1 - _SunSize, 1 - _SunSize * 0.6, sunDot);
                day += half3(1.0, 0.95, 0.85) * disk * 6;

                // golden hour (above the clouds): warm horizon glowing toward the low sun, deep blue overhead
                if (_Golden > 0.001)
                {
                    float3 Lh = normalize(float3(L.x, 0, L.z) + 1e-5);
                    float toward = saturate(dot(normalize(float3(d.x, 0, d.z) + 1e-5), Lh) * 0.5 + 0.5);
                    half3 hor = lerp(half3(0.42, 0.32, 0.45), half3(0.92, 0.36, 0.08), pow(toward, 2.2));
                    half3 mid = lerp(half3(0.24, 0.26, 0.45), half3(0.7, 0.38, 0.22), pow(toward, 3));
                    half3 zen = half3(0.09, 0.16, 0.38);
                    float u = saturate(up);
                    half3 gold = up > 0 ? (u < 0.25 ? lerp(hor, mid, u / 0.25) : lerp(mid, zen, pow((u - 0.25) / 0.75, 0.6)))
                                        : lerp(hor, half3(0.6, 0.35, 0.22), saturate(-up * 3));
                    gold += half3(1.0, 0.5, 0.15) * pow(saturate(sunDot), 14) * 0.55;         // warm glow round the sun
                    gold += half3(1.0, 0.75, 0.4) * pow(saturate(sunDot), 400) * 1.6;
                    float smallDisk = smoothstep(1 - 0.0009, 1 - 0.0006, sunDot);              // small, sharp evening sun
                    gold += half3(1.0, 0.9, 0.7) * smallDisk * 12;
                    day = lerp(day - half3(1.0, 0.95, 0.85) * disk * 6 * _Golden, gold, _Golden);
                    day = lerp(day, gold, _Golden);
                }

                // ---- space: black, stars, faint milky way
                half3 space = 0;
                float s = Stars(d, 260, _StarDensity) + Stars(d, 120, 0.9985) * 1.6;
                half3 starCol = lerp(half3(0.75, 0.82, 1.0), half3(1.0, 0.88, 0.75), Hash(floor(d * 260)));
                space += starCol * s * 2.2;
                float3 band = normalize(float3(0.4, 0.75, 0.52));                 // galactic plane
                float milky = pow(saturate(1 - abs(dot(d, band)) * 5.5), 2.5) * (0.4 + Noise(d * 9) * 0.8) * (0.6 + Noise(d * 31) * 0.4);
                space += half3(0.55, 0.55, 0.7) * milky * 0.035;
                space += half3(1.0, 0.8, 0.5) * pow(saturate(sunDot), 60) * 0.25;            // the Sun itself is a real object up here

                // thin blue rim of atmosphere toward the horizon while still climbing
                half3 col = lerp(day, space, _SpaceBlend);
                col += _HorizonColor.rgb * pow(saturate(1 - abs(up)), 6) * (1 - _SpaceBlend) * _SpaceBlend * 1.5;
                col += starCol * s * 2.2 * _StarBlend * (1 - _SpaceBlend) * 0.6;   // first stars appear early
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
