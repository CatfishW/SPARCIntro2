Shader "LifeIsStrangeInteraction/BlurredTexture"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _BlurRadius ("Blur Radius", Float) = 2.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "UIBlur"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float4 _Tint;
            float _BlurRadius;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.positionCS = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            fixed4 SampleBlur(float2 uv, float2 direction)
            {
                float2 offset = direction * _MainTex_TexelSize.xy * _BlurRadius;
                fixed4 color = tex2D(_MainTex, uv) * 0.227027f;
                color += tex2D(_MainTex, uv + offset * 1.384615f) * 0.316216f;
                color += tex2D(_MainTex, uv - offset * 1.384615f) * 0.316216f;
                color += tex2D(_MainTex, uv + offset * 3.230769f) * 0.070270f;
                color += tex2D(_MainTex, uv - offset * 3.230769f) * 0.070270f;
                return color;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 horizontal = SampleBlur(input.uv, float2(1.0, 0.0));
                fixed4 vertical = SampleBlur(input.uv, float2(0.0, 1.0));
                fixed4 color = lerp(horizontal, vertical, 0.5);
                return color * _Tint;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
