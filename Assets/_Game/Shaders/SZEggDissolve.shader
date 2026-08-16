// The travel egg's shell: seen from inside (Cull Front), near black, and it
// burns away cell by cell as _Cut rises, with a thin parchment ember edge.
Shader "SpellyZombie/EggDissolve"
{
    Properties
    {
        _Color ("Color", Color) = (0.03, 0.025, 0.03, 1)
        _EdgeColor ("Edge Color", Color) = (0.95, 0.9, 0.78, 1)
        _Cut ("Dissolve", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _EdgeColor;
                half _Cut;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 os          : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.os = IN.positionOS.xyz;
                return OUT;
            }

            float Hash(float3 p)
            {
                return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453);
            }

            float Noise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash(i);
                float n100 = Hash(i + float3(1, 0, 0));
                float n010 = Hash(i + float3(0, 1, 0));
                float n110 = Hash(i + float3(1, 1, 0));
                float n001 = Hash(i + float3(0, 0, 1));
                float n101 = Hash(i + float3(1, 0, 1));
                float n011 = Hash(i + float3(0, 1, 1));
                float n111 = Hash(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float n = Noise(IN.os * 9.0) * 0.65 + Noise(IN.os * 27.0) * 0.35;
                float cut = _Cut * 1.06;
                clip(n - cut);
                if (n - cut < 0.06)
                    return _EdgeColor;
                return _Color;
            }
            ENDHLSL
        }
    }
}
