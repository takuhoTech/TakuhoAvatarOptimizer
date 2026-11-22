// Scene preview for TAO Optimize VRC Shader Fallback.
// Emulates the simple VRChat fallback shaders (Toon, Unlit, Particle and Hidden), which only use the main texture.
Shader "Hidden/TAO/ShaderFallbackPreview"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Cutoff ("Cutoff", Range(0, 1)) = 0.5
        _TAOLit ("Lit", Float) = 0
        _TAOCutout ("Cutout", Float) = 0
        _TAOMultiply ("Multiply", Float) = 0
        _TAOHidden ("Hidden", Float) = 0
        _TAOCull ("Cull", Float) = 2
        _TAOZWrite ("ZWrite", Float) = 1
        _TAOSrcBlend ("Src Blend", Float) = 1
        _TAODstBlend ("Dst Blend", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "IgnoreProjector" = "True" }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull [_TAOCull]
            ZWrite [_TAOZWrite]
            Blend [_TAOSrcBlend] [_TAODstBlend]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Cutoff;
            float _TAOLit;
            float _TAOCutout;
            float _TAOMultiply;
            float _TAOHidden;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                half3 light : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // Hidden draws nothing.
                o.pos = _TAOHidden > 0.5 ? float4(0, 0, 0, 0) : UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;

                // Toon is lit by the main light and the average of the light probes.
                o.light = _LightColor0.rgb + ShadeSH9(half4(0, 0, 0, 1));
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                if (_TAOCutout > 0.5) clip(col.a - _Cutoff);

                if (_TAOMultiply > 0.5)
                {
                    // Particles/Multiply darkens what is behind it by the texture color, weighted by the alpha.
                    col *= i.color;
                    col = lerp(fixed4(1, 1, 1, 1), col, col.a);
                    UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fixed4(1, 1, 1, 1));
                    return col;
                }

                if (_TAOLit > 0.5) col.rgb *= i.light;
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
