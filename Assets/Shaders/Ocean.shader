// Realistic-looking ocean for URP, cheap enough for mobile.
//  - 3 Gerstner waves move the vertices (real rolling swell)
//  - fine ripples in the pixel shader
//  - sky reflection (Fresnel), sun glitter, light through wave crests, foam on crests, fog
Shader "OverPeak/Ocean"
{
    Properties
    {
        _DeepColor     ("Deep Color", Color) = (0.015, 0.09, 0.16, 1)
        _ShallowColor  ("Wave Color", Color) = (0.05, 0.32, 0.38, 1)
        _FoamColor     ("Foam Color", Color) = (0.92, 0.95, 0.97, 1)
        _WaveA ("Wave A (dir xy, steepness, wavelength)", Vector) = (1, 0.3, 0.07, 64)
        _WaveB ("Wave B", Vector) = (0.6, 1, 0.06, 37)
        _WaveC ("Wave C", Vector) = (-0.4, 0.8, 0.05, 21)
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.35
        _SunSharpness ("Sun Glitter Sharpness", Float) = 900
        _FoamAmount ("Foam Amount", Range(0, 1)) = 0.3
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

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor, _ShallowColor, _FoamColor;
                float4 _WaveA, _WaveB, _WaveC;
                half _RippleStrength, _SunSharpness, _FoamAmount;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  crest      : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
            };

            float3 Gerstner(float4 wave, float3 p, inout float3 tangent, inout float3 binormal)
            {
                float steep = wave.z;
                float k = 2 * PI / wave.w;
                float c = sqrt(9.8 / k);
                float2 d = normalize(wave.xy);
                float f = k * (dot(d, p.xz) - c * _Time.y);
                float a = steep / k;
                float s = sin(f), co = cos(f);
                tangent  += float3(-d.x * d.x * steep * s, d.x * steep * co, -d.x * d.y * steep * s);
                binormal += float3(-d.x * d.y * steep * s, d.y * steep * co, -d.y * d.y * steep * s);
                return float3(d.x * a * co, a * s, d.y * a * co);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float3 grid = p;
                float3 tangent = float3(1, 0, 0), binormal = float3(0, 0, 1);
                float3 waves = Gerstner(_WaveA, grid, tangent, binormal)
                             + Gerstner(_WaveB, grid, tangent, binormal)
                             + Gerstner(_WaveC, grid, tangent, binormal);
                // far away the sea mesh is coarse (hundreds of metres between points): waves there would turn into
                // jagged spikes, so they calm down with distance and the far sea is smooth
                float calm = 1 - smoothstep(350, 1100, distance(grid, GetCameraPositionWS()));
                p += waves * calm;
                float amp = _WaveA.z / (2 * PI / _WaveA.w) + _WaveB.z / (2 * PI / _WaveB.w) + _WaveC.z / (2 * PI / _WaveC.w);
                o.crest = saturate((p.y - grid.y) / max(amp, 0.001)) * calm;  // 0 = trough, 1 = top of the waves
                o.normalWS = normalize(lerp(float3(0, 1, 0), normalize(cross(binormal, tangent)), calm));
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // small moving ripples: analytic slope of a few sine waves
            float2 RippleSlope(float2 xz, float t)
            {
                float2 s = 0;
                // 8 wavelets with unrelated directions / frequencies so no regular pattern appears
                const float2 dirs[8] = { float2(0.83, 0.55), float2(-0.47, 0.88), float2(0.17, -0.99), float2(-0.96, -0.27),
                                         float2(0.62, -0.78), float2(-0.71, 0.70), float2(0.99, 0.12), float2(-0.33, -0.94) };
                const float freq[8] = { 0.31, 0.53, 0.87, 1.37, 2.11, 3.29, 4.73, 7.19 };
                [unroll] for (int i = 0; i < 8; i++)
                {
                    float ph = dot(dirs[i], xz) * freq[i] + t * sqrt(9.8 * freq[i]) + i * 1.7;
                    s += dirs[i] * cos(ph) * (0.05 / (1 + i * 0.35));
                }
                return s;
            }

            half4 _CityCutTint;      // global: the Earth globe's average ocean colour
            float _SeaGlobeBlend;    // global: 0 near the water, ~1 high up (the city sea then matches the globe)

            half4 frag(Varyings i) : SV_Target
            {
                float dist = distance(i.positionWS, GetCameraPositionWS());
                float near = saturate(1 - dist / 600);                          // ripples fade out far away
                float2 slope = RippleSlope(i.positionWS.xz, _Time.y) * _RippleStrength * (0.35 + 0.65 * near) * 2.2;
                float3 N = normalize(i.normalWS + float3(-slope.x, 0, -slope.y));
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light sun = GetMainLight();

                // sky reflection, stronger at grazing angles
                float ndv = saturate(dot(N, V));
                float fresnel = 0.02 + 0.98 * pow(1 - ndv, 5);
                float3 R = reflect(-V, N);
                R.y = abs(R.y) + 0.03;                         // the sea only ever reflects sky, never the ground below the horizon
                R = normalize(R);
                half3 sky = GlossyEnvironmentReflection(R, 0.08, 1);

                // water body: deep blue, lighter on the wave crests and where the sun shines through them
                half3 ambient = SampleSH(N);
                half3 body = lerp(_DeepColor.rgb, _ShallowColor.rgb, i.crest * 0.6);
                float through = pow(saturate(dot(V, -sun.direction)), 4) * i.crest;
                body *= ambient * 0.9 + sun.color * saturate(dot(N, sun.direction)) * 0.25;
                body += _ShallowColor.rgb * through * sun.color * 0.6;

                // sun glitter: sharp highlight + soft glow
                float rs = saturate(dot(R, sun.direction));
                half3 spec = sun.color * (pow(rs, _SunSharpness) * 6 + pow(rs, 60) * 0.25);

                // foam on the top of the waves, broken up by ripple noise
                float2 fp = i.positionWS.xz;
                float noise = saturate(0.5 + 0.35 * sin(fp.x * 0.37 + fp.y * 0.21 + _Time.y * 0.8)
                                           + 0.25 * sin(fp.x * -0.53 + fp.y * 0.91 - _Time.y * 1.1)
                                           + 0.20 * sin(fp.x * 1.31 - fp.y * 0.77 + _Time.y * 1.7));
                float foam = saturate((i.crest - (1 - _FoamAmount * 0.6)) * 4) * noise;

                half3 col = lerp(body, sky, fresnel) + spec * (1 - foam);
                col = lerp(col, _FoamColor.rgb * (ambient + sun.color * 0.6), foam * 0.85);
                // seen from high up, the city's sea takes on the colour of the globe's oceans around it
                half3 globeSea = _CityCutTint.rgb * sun.color * saturate(sun.direction.y * 0.9 + 0.1) * 1.15;
                col = lerp(col, globeSea, _SeaGlobeBlend);
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
