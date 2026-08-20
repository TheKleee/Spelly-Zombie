Shader "SpellyZombie/SZOutline"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.85, 0.35, 1)
        _Width ("Width (screen px at 1080p)", Float) = 4
    }

    SubShader
    {
        // draws after the object so the hull sits around a solid silhouette
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Outline"
            // inverted hull: keep the back faces, push them out along the normal
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Width;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = normalize(TransformObjectToWorldNormal(IN.normalOS));
                float4 posCS = TransformWorldToHClip(posWS);
                float3 nrmCS = TransformWorldToHClipDir(nrmWS);

                // expand in clip space so the rim reads the same width at any distance
                float2 offset = normalize(nrmCS.xy) * (_Width / _ScreenParams.y) * 2.0 * posCS.w;
                posCS.xy += offset;

                OUT.positionCS = posCS;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
