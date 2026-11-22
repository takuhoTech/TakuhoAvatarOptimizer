// Bake for TAO Optimize VRC Shader Fallback.
// Writes the transparency of a lilToon material into the alpha of the main texture, because the VRChat fallback
// shaders only use the main texture. The color is passed through unchanged.
// Mirrors lilToon: alpha = main texture alpha x color alpha, then the alpha mask.
Shader "Hidden/TAO/ShaderFallbackAlphaBake"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _TAOAlphaMask ("Alpha Mask", 2D) = "white" {}
        _TAOColorAlpha ("Color Alpha", Float) = 1
        _TAOMaskMode ("Alpha Mask Mode", Float) = 0
        _TAOMaskScale ("Alpha Mask Scale", Float) = 1
        _TAOMaskValue ("Alpha Mask Offset", Float) = 0
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"

            Texture2D _MainTex;
            Texture2D _TAOAlphaMask;

            // lilToon samples the alpha mask with the sampler of the main texture.
            SamplerState sampler_MainTex;

            float4 _TAOAlphaMask_ST;
            float _TAOColorAlpha;
            float _TAOMaskMode;
            float _TAOMaskScale;
            float _TAOMaskValue;

            float4 frag(v2f_img i) : SV_Target
            {
                float4 col = _MainTex.Sample(sampler_MainTex, i.uv);
                float alpha = col.a * _TAOColorAlpha;

                // 1: replace, 2: multiply, 3: add, 4: subtract
                if (_TAOMaskMode > 0.5)
                {
                    float2 maskUV = i.uv * _TAOAlphaMask_ST.xy + _TAOAlphaMask_ST.zw;
                    float mask = saturate(_TAOAlphaMask.Sample(sampler_MainTex, maskUV).r * _TAOMaskScale + _TAOMaskValue);

                    if (_TAOMaskMode < 1.5) alpha = mask;
                    else if (_TAOMaskMode < 2.5) alpha = alpha * mask;
                    else if (_TAOMaskMode < 3.5) alpha = saturate(alpha + mask);
                    else alpha = saturate(alpha - mask);
                }

                col.a = saturate(alpha);
                return col;
            }
            ENDCG
        }
    }
}
