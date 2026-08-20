// Environment surface: no texture maps — _BaseColor (from the
// SurfaceMaterialDB palette) plus quiet world-space noise, so UV-less
// geometry works too. Detail stays small so drawn ink keeps the contrast.
Shader "SpellyZombie/Surface"
{
    Properties
    {
        [MainColor] _BaseColor ("Color", Color) = (0.6, 0.6, 0.6, 1)
        _GrainScale ("Grain Scale", Float) = 3.5
        _GrainStrength ("Grain Strength", Range(0, 0.2)) = 0.05
        _MacroScale ("Macro Scale", Float) = 0.35
        _MacroStrength ("Macro Strength", Range(0, 0.25)) = 0.07
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _GrainScale;
                half _GrainStrength;
                half _MacroScale;
                half _MacroStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                half3 normalWS     : TEXCOORD1;
                half fogFactor     : TEXCOORD2;
            };

            // cheap 3D value noise (world-space → triplanar for free)
            float hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float vnoise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash3(i);
                float n100 = hash3(i + float3(1, 0, 0));
                float n010 = hash3(i + float3(0, 1, 0));
                float n110 = hash3(i + float3(1, 1, 0));
                float n001 = hash3(i + float3(0, 0, 1));
                float n101 = hash3(i + float3(1, 0, 1));
                float n011 = hash3(i + float3(0, 1, 1));
                float n111 = hash3(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = half3(TransformObjectToWorldNormal(IN.normalOS));
                OUT.fogFactor = half(ComputeFogFactor(OUT.positionHCS.z));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 n = normalize(IN.normalWS);

                // quiet detail: fine grain + broad tonal drift, both capped
                float grain = vnoise(IN.positionWS * _GrainScale) - 0.5;
                float macro = vnoise(IN.positionWS * _MacroScale) - 0.5;
                half3 albedo = _BaseColor.rgb
                    * (1.0 + grain * 2.0 * _GrainStrength + macro * 2.0 * _MacroStrength);

                // lighting: main light + shadows + ambient + additional lights
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half3 lighting = SampleSH(n);
                lighting += mainLight.color * mainLight.shadowAttenuation
                    * saturate(dot(n, mainLight.direction));

                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                for (uint li = 0u; li < count; li++)
                {
                    Light l = GetAdditionalLight(li, IN.positionWS);
                    lighting += l.color * l.distanceAttenuation
                        * saturate(dot(n, l.direction));
                }
                #endif

                half3 col = albedo * lighting;
                col = MixFog(col, IN.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            float4 ShadowVert(ShadowAttributes IN) : SV_POSITION
            {
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return posCS;
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
