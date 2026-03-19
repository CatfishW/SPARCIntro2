Shader "ItemInteraction/InteractionOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1,1,1,1)
        _OutlineWidth ("Outline Width", Float) = 0.02
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
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            float4 _OutlineColor;
            float _OutlineWidth;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz) + normalWS * _OutlineWidth;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
