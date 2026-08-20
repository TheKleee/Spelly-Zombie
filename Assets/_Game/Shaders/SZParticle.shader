// Shader for spell particles and matter blobs: vertex wobble, a squash
// dial, and a fresnel rim. All softness is GPU-side — physics stays one
// rigidbody per blob and none of it touches the network.
Shader "SpellyZombie/Particle"
{
    Properties
    {
        [MainColor] _BaseColor ("Color", Color) = (1,1,1,1)
        _WobbleAmp ("Wobble Amplitude", Float) = 0.0
        _WobbleFreq ("Wobble Frequency", Float) = 7.0
        _Rim ("Rim Glow", Float) = 0.6
        _Squash ("Squash", Range(0,1)) = 0
        _SrcBlend ("Src Blend", Float) = 5    // SrcAlpha
        _DstBlend ("Dst Blend", Float) = 10   // OneMinusSrcAlpha
        _ZWrite ("ZWrite", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Forward"
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _WobbleAmp;
                half _WobbleFreq;
                half _Rim;
                half _Squash;
                half _SrcBlend;
                half _DstBlend;
                half _ZWrite;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half3 normalWS     : TEXCOORD0;
                half3 viewWS       : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 p = IN.positionOS.xyz;

                // squash: sink vertically, bulge outward; driven per-object via MPB
                p.y  *= 1.0 - _Squash * 0.45;
                p.xz *= 1.0 + _Squash * 0.28;

                // per-object phase from world origin so blobs never wobble in lockstep
                float3 origin = float3(unity_ObjectToWorld._m03,
                                       unity_ObjectToWorld._m13,
                                       unity_ObjectToWorld._m23);
                float phase = origin.x * 7.13 + origin.y * 5.71 + origin.z * 3.37;

                // jelly wobble along the normal, varied around the surface
                float w = sin(_Time.y * _WobbleFreq + phase + p.y * 6.0 + p.x * 4.0);
                p += IN.normalOS * w * _WobbleAmp;

                float3 posWS = TransformObjectToWorld(p);
                OUT.positionHCS = TransformWorldToHClip(posWS);
                OUT.normalWS = half3(TransformObjectToWorldNormal(IN.normalOS));
                OUT.viewWS = half3(GetWorldSpaceViewDir(posWS));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 n = normalize(IN.normalWS);
                half3 v = normalize(IN.viewWS);
                half fres = pow(1.0 - saturate(dot(n, v)), 2.0);
                half3 col = _BaseColor.rgb + _BaseColor.rgb * fres * _Rim;
                half alpha = saturate(_BaseColor.a + fres * _Rim * 0.25);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
