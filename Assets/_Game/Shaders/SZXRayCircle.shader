// Composites the reveal camera's texture inside a soft screen-space ellipse
// around the body. Outside the ellipse nothing is drawn.
Shader "SpellyZombie/XRayCircle"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
        _Center ("Center", Vector) = (0.5, 0.5, 0, 0)
        _RadX ("Radius X", Float) = 0.15
        _RadY ("Radius Y", Float) = 0.3
        _Soft ("Soft", Float) = 0.4
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            ZTest Always

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Center;
            float _RadX, _RadY, _Soft;

            struct a2v { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(a2v v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.pos);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 d = i.uv - _Center.xy;
                d.x /= max(_RadX, 0.0001);
                d.y /= max(_RadY, 0.0001);
                float a = 1.0 - smoothstep(1.0 - _Soft, 1.0, length(d));
                fixed4 col = tex2D(_MainTex, i.uv);
                return fixed4(col.rgb, a);
            }
            ENDCG
        }
    }
}
