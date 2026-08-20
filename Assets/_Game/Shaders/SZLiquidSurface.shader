// One shader for every liquid surface - material colors decide which liquid.
// Foam and depth shading need the URP asset's Depth Texture ON; without it
// the surface still renders, just flat-colored and foamless.
Shader "SpellyZombie/LiquidSurface"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.38, 0.82, 0.84, 0.55)
        _DeepColor ("Deep Color", Color) = (0.08, 0.34, 0.58, 0.92)
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 0.95)
        _DepthFade ("Shallow To Deep (m)", Float) = 3.5
        _FoamWidth ("Foam Width (m)", Float) = 1.1
        _LapSpeed ("Foam Lap Speed", Float) = 0.35
        _WaveHeight ("Wave Height (m)", Float) = 0.07
        _WaveLength ("Wave Length (m)", Float) = 9
        _WaveSpeed ("Wave Speed", Float) = 0.6
        _RippleScale ("Ripple Scale", Float) = 0.14
        _RippleStrength ("Ripple Strength", Range(0,1)) = 0.22
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off   // visible from below too

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _FoamColor;
                float _DepthFade, _FoamWidth, _LapSpeed;
                float _WaveHeight, _WaveLength, _WaveSpeed;
                float _RippleScale, _RippleStrength;
            CBUFFER_END

            struct A { float4 pos : POSITION; };
            struct V
            {
                float4 pos : SV_POSITION;
                float4 screen : TEXCOORD0;
                float3 world : TEXCOORD1;
                float fog : TEXCOORD2;
            };

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            V vert(A v)
            {
                V o;
                float3 w = TransformObjectToWorld(v.pos.xyz);
                // two soft crossed swells - calm, never choppy
                float t = _Time.y * _WaveSpeed;
                float k = TWO_PI / max(0.5, _WaveLength);
                w.y += (sin(w.x * k + t) + sin((w.x * 0.6 + w.z * 0.8) * k * 1.3 + t * 1.17))
                       * 0.5 * _WaveHeight;
                o.world = w;
                o.pos = TransformWorldToHClip(w);
                o.screen = ComputeScreenPos(o.pos);
                o.fog = ComputeFogFactor(o.pos.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float diff = max(0.0, sceneEye - i.screen.w); // metres of water above the bed

                // depth shading: bright shallows into dark deeps
                half4 col = lerp(_ShallowColor, _DeepColor, saturate(diff / _DepthFade));
                // alpha ramps opaque by ~2x the color fade so the underwater rim never shows
                col.a = lerp(col.a, 0.97, smoothstep(_DepthFade, _DepthFade * 2.2, diff));

                // gentle surface ripples, purely tonal
                float t = _Time.y;
                float rip = Noise(i.world.xz * _RippleScale + t * 0.18)
                          * Noise(i.world.xz * _RippleScale * 2.7 - t * 0.13);
                col.rgb += (rip - 0.28) * _RippleStrength;

                // foam: a lapping band where the water thins; noise tears its edge
                float lap = 0.72 + 0.28 * sin(t * _LapSpeed * TWO_PI + Noise(i.world.xz * 0.05) * 6.0);
                float band = smoothstep(_FoamWidth * lap, _FoamWidth * lap * 0.3, diff);
                float tear = smoothstep(0.32, 0.62, Noise(i.world.xz * 0.9 + t * 0.22));
                float rim = smoothstep(0.14, 0.0, diff); // the waterline itself, always white
                float foam = saturate(band * (0.45 + 0.55 * tear) + rim);

                col.rgb = lerp(col.rgb, _FoamColor.rgb, foam * _FoamColor.a);
                col.a = saturate(col.a + foam * 0.5);
                col.a *= smoothstep(0.0, 0.08, diff); // no hard seam at the waterline

                // fog also drives alpha opaque so nothing shows past the fog wall
                col.rgb = MixFog(col.rgb, i.fog);
                col.a = lerp(1.0, col.a, saturate(i.fog));
                return col;
            }
            ENDHLSL
        }
    }
}
