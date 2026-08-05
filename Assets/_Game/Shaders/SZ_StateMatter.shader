// =============================================================================
//  SPELLY ZOMBIE — STATE MATTER
//
//  ONE shader for the whole ladder (Marko's design): the same material reads
//  _StateT and becomes solid, liquid or gas.
//
//     _StateT = 1.0   SOLID   opaque, dead still
//     _StateT = 0.5   LIQUID  translucent, BUBBLES rising, a gentle puddle sway
//     _StateT = 0.1   GAS     faint, BREAKING UP into holes, swirling like a tornado
//
//  The game already drives _StateT on anything wearing a StateBlob — drop a
//  material using this shader on your blob and it just works, no wiring.
//
//  ALSO MEANT FOR THE SPELL FIELDS (tornado, whirlpool, black/white hole):
//  make a material per effect and park _StateT where you want it, then push
//  Swirl / Holes / Rim to taste. Suggested starting points at the bottom.
// =============================================================================
Shader "Spelly Zombie/State Matter"
{
    Properties
    {
        [MainColor] _BaseColor       ("Colour", Color) = (0.55, 0.75, 1.0, 1.0)
        _StateT                      ("State  (1 solid · 0.5 liquid · 0.1 gas)", Range(0,1)) = 1.0

        [Header(Movement)]
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _StateT;
                float  _Wobble, _WobbleSpeed, _Swirl, _SwirlSpeed, _Turbulence;
                float  _Bubbles, _BubbleScale, _BubbleRise, _Holes, _HoleScale, _Rim;
                float  _SolidAlpha, _LiquidAlpha, _GasAlpha;
                float  _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float2 state      : TEXCOORD3; // x = liquidness, y = gasness
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

            // SOLID sits still · LIQUID sways · GAS twists. Liquidness peaks at
            // the liquid mark and fades out into gas, so a cloud isn't ALSO
            // being sloshed like a puddle.
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
                    float n = noise3(pos * 8.0 + float3(0, t * _WobbleSpeed, 0)) - 0.5;
                    n += 0.5 * (noise3(pos * 16.0 - float3(t * _WobbleSpeed * 0.7, 0, 0)) - 0.5);
                    pos += nrm * n * (_Wobble * 0.8) * liq;
                }

                // GAS — twist around the up axis (the tornado) plus a puffing
                // turbulence, so it churns instead of merely floating
                if (gas > 0.001)
                {
                    float ang = _Swirl * gas * (pos.y * 1.5 + t * _SwirlSpeed);
                    float s = sin(ang), c = cos(ang);
                    pos.xz = float2(pos.x * c - pos.z * s, pos.x * s + pos.z * c);

                    float n2 = noise3(pos * 2.2 - float3(0, t * 1.3, 0)) - 0.5;
                    pos += normalize(pos + 1e-4) * n2 * _Turbulence * gas;
                }

                OUT.positionOS = pos;
                OUT.positionWS = TransformObjectToWorld(pos);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS   = TransformObjectToWorldNormal(nrm);
                OUT.state      = float2(liq, gas);
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

                // ---- lighting: simple, stylised, never fights the art ------
                Light main = GetMainLight();
                float  ndl = saturate(dot(N, main.direction)) * 0.55 + 0.45;
                half3  amb = SampleSH(N);
                half3  col = _BaseColor.rgb * (main.color * ndl + amb * 0.45);

                // wet sheen / energy edge — stronger the thinner it gets
                float fres = pow(1.0 - saturate(dot(N, V)), 3.0);
                col += fres * _Rim * (0.25 + 0.75 * gas) * _BaseColor.rgb;

                // bubbles read as brighter, thicker patches
                col += bubble * 0.7;

                // ---- alpha by state ----------------------------------------
                float a = lerp(lerp(_GasAlpha, _LiquidAlpha, saturate((_StateT - 0.1) / 0.4)),
                               _SolidAlpha, saturate((_StateT - 0.5) / 0.5));
                a *= _BaseColor.a;
                a = saturate(a + bubble * 0.35);      // bubbles are denser
                if (gas > 0.001) a *= lerp(0.55, 1.0, holeMask); // frayed edges fade

                return half4(col, a);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}

// -----------------------------------------------------------------------------
//  STARTING POINTS FOR THE SPELL FIELDS (make one material each):
//
//   TORNADO      StateT 0.10 · Swirl 4.5 · SwirlSpeed 3.0 · Turbulence 0.5
//                Holes 0.35 · Rim 1.6 · Cull Off
//   WHIRLPOOL    StateT 0.45 · Swirl 3.0 · SwirlSpeed -2.0 · Bubbles 0.9
//                Holes 0.0  · Rim 0.6
//   BLACK HOLE   StateT 0.10 · Colour near-black · Swirl 5 · Holes 0.8
//                Rim 2.5 (a dark body with a screaming edge) · Cull Off
//   WHITE HOLE   same as black but Colour white-hot · GasAlpha 0.45 · Rim 3
//   STEAM CLOUD  StateT 0.10 · Swirl 0.6 · Turbulence 0.5 · Holes 0.7
// -----------------------------------------------------------------------------
