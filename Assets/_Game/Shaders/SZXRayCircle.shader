// Composites the reveal camera's texture inside a soft screen-space ellipse
// around the body, at _Opacity, with the body's own picture laid over it whole.
// The other players the cut would clip are laid over the cut view first.
// Outside the ellipse only the body is drawn.
Shader "SpellyZombie/XRayCircle"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
        _BodyTex ("Body", 2D) = "black" {}
        _OthersTex ("Others", 2D) = "black" {}
        _OthersOn ("Others On", Float) = 0
        _Opacity ("Opacity", Float) = 1
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

            sampler2D _MainTex, _BodyTex, _OthersTex;
            float4 _Center;
            float _RadX, _RadY, _Soft, _Opacity, _OthersOn;

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
                fixed4 body = tex2D(_BodyTex, i.uv);
                // the near parts of players in plain sight, which the cut's near plane took away
                fixed4 oth = tex2D(_OthersTex, i.uv) * _OthersOn;
                float ca = oth.a + col.a * (1.0 - oth.a);
                col.rgb = (oth.rgb * oth.a + col.rgb * col.a * (1.0 - oth.a)) / max(ca, 0.0001);
                col.a = ca;
                // the cut view shows at _Opacity inside the ellipse (what it left empty shows the
                // scene); the body is laid over it whole, wherever it is
                float r = a * col.a * _Opacity;
                float outA = 1.0 - (1.0 - body.a) * (1.0 - r);
                float3 rgb = (body.rgb * body.a + col.rgb * r * (1.0 - body.a)) / max(outA, 0.0001);
                return fixed4(rgb, outA);
            }
            ENDCG
        }
    }
}
