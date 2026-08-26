// One material reads _StateT and becomes solid (1.0), liquid (0.5) or gas
// (0.1). StateBlob drives _StateT at runtime; spell-field materials park
// _StateT and tune Swirl / Holes / Rim.
Shader "Spelly Zombie/State Matter"
{
    Properties
    {
        [MainTexture] _BaseMap       ("Texture  (white = colour only)", 2D) = "white" {}
        [MainColor] _BaseColor       ("Colour", Color) = (0.55, 0.75, 1.0, 1.0)
        _StateT                      ("State  (1 solid · 0.5 liquid · 0.1 gas)", Range(0,1)) = 1.0

        [Header(Movement)]
        [Toggle(_SZ_SKINNED)] _Skinned ("Skinned body  (wobble rides the rig)", Float) = 0
        _Wobble                      ("Liquid wobble", Range(0,0.5)) = 0.015
        _WobbleSpeed                 ("Liquid speed", Range(0,8)) = 0.45
        _Swirl                       ("Gas swirl (tornado twist)", Range(0,6)) = 0.5
        _SwirlSpeed                  ("Gas swirl speed", Range(0,6)) = 0.4
        _Turbulence                  ("Gas turbulence", Range(0,1)) = 0.10

        [Header(Surface)]
        _Bubbles                     ("Liquid bubbles", Range(0,1)) = 0.55
        _BubbleScale                 ("Bubble size", Range(1,40)) = 14
        _BubbleRise                  ("Bubble rise", Range(0,3)) = 0.8
        _Holes                       ("Gas break-up (holes)", Range(0,1)) = 0.55
        _HoleScale                   ("Hole size", Range(1,40)) = 7
        _Rim                         ("Rim glow", Range(0,3)) = 0.9

        [Header(Transparency)]
        _SolidAlpha                  ("Solid alpha", Range(0,1)) = 1.0
        _LiquidAlpha                 ("Liquid alpha", Range(0,1)) = 0.80
        _GasAlpha                    ("Gas alpha", Range(0,1)) = 0.22

        [Header(Rendering)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull  (Off = see inside)", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
        }

        // DEPTH FIRST WHEN IT IS EFFECTIVELY OPAQUE. The forward pass below runs
        // ZWrite Off so liquid and gas blend correctly - but a SOLID body then
        // writes no depth and sorts against itself: the far arm paints over the
        // near torso and a held book shows through a hand, which reads as the
        // mesh clipping through itself. This lays down depth only when the
        // state is fully opaque, so solid behaves like solid geometry and
        // nothing about liquid or gas changes.
        Pass
        {
            Name "SolidDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZWrite On
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // MUST match the forward pass exactly or the SRP Batcher rejects
            // the shader and the two passes can disagree on their values.
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float  _StateT;
                float  _Wobble, _WobbleSpeed, _Swirl, _SwirlSpeed, _Turbulence;
                float  _Bubbles, _BubbleScale, _BubbleRise, _Holes, _HoleScale, _Rim;
                float  _SolidAlpha, _LiquidAlpha, _GasAlpha;
                float  _Cull;
            CBUFFER_END

            TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);

            struct AttributesD { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct VaryingsD   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            VaryingsD vertDepth(AttributesD IN)
            {
                VaryingsD OUT;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                // no wobble or swirl here on purpose: both are gated by liq/gas
                // in the forward pass, and both are zero whenever this pass
                // draws anything, so the depth matches the colour exactly.
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 fragDepth(VaryingsD IN) : SV_Target
            {
                // the same alpha the forward pass computes - uniform across the
                // mesh once liquid and gas are out of it
                float a = lerp(lerp(_GasAlpha, _LiquidAlpha, saturate((_StateT - 0.1) / 0.4)),
                               _SolidAlpha, saturate((_StateT - 0.5) / 0.5)) * _BaseColor.a;
                // a cut-out texture must not write depth where it is see-through
                a *= SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a;
                clip(a - 0.99);   // anything see-through keeps the old behaviour
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma shader_feature_local _SZ_SKINNED

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float  _StateT;
                float  _Wobble, _WobbleSpeed, _Swirl, _SwirlSpeed, _Turbulence;
                float  _Bubbles, _BubbleScale, _BubbleRise, _Holes, _HoleScale, _Rim;
                float  _SolidAlpha, _LiquidAlpha, _GasAlpha;
                float  _Cull;
            CBUFFER_END

            TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float2 state      : TEXCOORD3; // x = liquidness, y = gasness
                float2 uv         : TEXCOORD4;
            };

            // ---- cheap value noise (no textures, works everywhere) ----------
            float hash31(float3 p)
            {
                p = frac(p * 0.3183099 + float3(0.71, 0.113, 0.419));
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float noise3(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(lerp(hash31(i + float3(0,0,0)), hash31(i + float3(1,0,0)), f.x),
                         lerp(hash31(i + float3(0,1,0)), hash31(i + float3(1,1,0)), f.x), f.y),
                    lerp(lerp(hash31(i + float3(0,0,1)), hash31(i + float3(1,0,1)), f.x),
                         lerp(hash31(i + float3(0,1,1)), hash31(i + float3(1,1,1)), f.x), f.y),
                    f.z);
            }

            // liquidness peaks at the liquid mark and fades into gas so the two never stack
            void StateWeights(float t, out float liq, out float gas)
            {
                gas = 1.0 - saturate((t - 0.1) / 0.4);
                liq = (1.0 - saturate((t - 0.5) / 0.5)) * (1.0 - gas);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float liq, gas;
                StateWeights(_StateT, liq, gas);

                float3 pos = IN.positionOS.xyz;
                float3 nrm = IN.normalOS;
                float  t   = _Time.y;

                // LIQUID — a slow surface ripple
                if (liq > 0.001)
                {
                #if defined(_SZ_SKINNED)
                    // A SKINNED VERTEX ARRIVES ALREADY POSED, so noise sampled
                    // by position leaves the body swimming through a standing
                    // field - the surface crawls, and at a UV seam the two
                    // coincident vertices get different noise and the mesh
                    // splits. A travelling wave along the body axis has neither
                    // problem: it rides the rig in any pose, and coincident
                    // vertices always agree because they share a height.
                    float wave = sin(pos.y * 6.0 - t * _WobbleSpeed * 4.0)
                               + 0.5 * sin(pos.y * 11.0 + t * _WobbleSpeed * 2.6);
                    pos += nrm * wave * (_Wobble * 0.5) * liq;
                #else
                    // RADIAL, NOT ALONG THE NORMAL. The blob is flat-shaded, so
                    // coincident vertices on neighbouring triangles carry
                    // different normals - displacing along them tears the mesh
                    // into petals. The radial direction is shared by every copy
                    // of a vertex, so the surface can never split (the gas
                    // turbulence below always did it this way, and never tore).
                    //
                    // And SCALE-FREE: noise is sampled on the unit sphere and
                    // the amplitude rides the local radius, so a mesh authored
                    // at 0.005 units and one authored at 1 wobble by the same
                    // FRACTION of themselves instead of one exploding while the
                    // other barely moves.
                    float3 rdir = normalize(pos + 1e-5);
                    float rad  = length(pos);
                    float n = noise3(rdir * 3.0 + float3(0, t * _WobbleSpeed, 0)) - 0.5;
                    n += 0.5 * (noise3(rdir * 6.0 - float3(t * _WobbleSpeed * 0.7, 0, 0)) - 0.5);
                    pos += rdir * n * (_Wobble * 1.6) * rad * liq;
                #endif
                }

                // GAS — twist around the up axis plus turbulence.
                // The twist is a whole-body rotation about the object origin,
                // which on a rig turns the head against the feet and destroys
                // the pose - a skinned body only gets the turbulence.
                if (gas > 0.001)
                {
                #if !defined(_SZ_SKINNED)
                    // height into the twist as a PROPORTION of the body, so a
                    // tiny mesh twists like a tornado instead of rotating whole
                    float3 sdir = normalize(pos + 1e-5);
                    float ang = _Swirl * gas * (sdir.y * 1.5 + t * _SwirlSpeed);
                    float s = sin(ang), c = cos(ang);
                    pos.xz = float2(pos.x * c - pos.z * s, pos.x * s + pos.z * c);
                #endif

                    float n2 = noise3(normalize(pos + 1e-4) * 2.2 - float3(0, t * 1.3, 0)) - 0.5;
                    pos += normalize(pos + 1e-4) * n2 * _Turbulence * length(pos) * 1.2 * gas;
                }

                OUT.positionOS = pos;
                OUT.positionWS = TransformObjectToWorld(pos);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS   = TransformObjectToWorldNormal(nrm);
                OUT.state      = float2(liq, gas);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float liq = IN.state.x;
                float gas = IN.state.y;
                float t   = _Time.y;

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // ---- GAS BREAKS UP: holes eat through it as it thins -------
                float holeMask = 0.0;
                if (gas > 0.001)
                {
                    float h = noise3(IN.positionOS * _HoleScale + float3(0, -t * 0.5, t * 0.2));
                    float cut = _Holes * gas;
                    // hard holes in the middle, soft frayed edges around them
                    clip(h - cut * 0.72);
                    holeMask = smoothstep(cut * 0.72, cut * 0.72 + 0.18, h);
                }

                // ---- LIQUID BUBBLES: bright spots drifting upward ----------
                float bubble = 0.0;
                if (liq > 0.001)
                {
                    float b = noise3(IN.positionOS * _BubbleScale
                                   + float3(0, -t * _BubbleRise, 0));
                    bubble = smoothstep(0.70, 0.88, b) * _Bubbles * liq;
                }

                // ---- lighting: simple, stylised ------
                Light main = GetMainLight();
                float  ndl = saturate(dot(N, main.direction)) * 0.55 + 0.45;
                half3  amb = SampleSH(N);
                half3  col = _BaseColor.rgb * (main.color * ndl + amb * 0.45);

                // wet sheen / energy edge — stronger the thinner it gets
                float fres = pow(1.0 - saturate(dot(N, V)), 3.0);
                col += fres * _Rim * (0.25 + 0.75 * gas) * _BaseColor.rgb;

                col += bubble * 0.7;

                // ---- alpha by state ----------------------------------------
                float a = lerp(lerp(_GasAlpha, _LiquidAlpha, saturate((_StateT - 0.1) / 0.4)),
                               _SolidAlpha, saturate((_StateT - 0.5) / 0.5));
                // THE AUTHOR'S TEXTURE, OUR MATERIAL. White by default, so every
                // existing blob renders exactly as it did.
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                col *= tex.rgb;
                a *= _BaseColor.a * tex.a;
                a = saturate(a + bubble * 0.35);      // bubbles are denser
                if (gas > 0.001) a *= lerp(0.55, 1.0, holeMask); // frayed edges fade

                return half4(col, a);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
