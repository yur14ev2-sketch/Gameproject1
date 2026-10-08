Shader "Hidden/ProjectionSilhouetteDisplay"
{
    Properties { _MainTex ("Silhouette Mask", 2D) = "black" {} }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4x4 _ProjectionViewProjection;
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float4 projectionClip = mul(
                    _ProjectionViewProjection,
                    float4(input.positionWS, 1.0));
                clip(projectionClip.w);
                float2 projectionUv = projectionClip.xy / projectionClip.w;
                projectionUv = projectionUv * 0.5 + 0.5;
                clip(projectionUv.x);
                clip(projectionUv.y);
                clip(1.0 - projectionUv.x);
                clip(1.0 - projectionUv.y);
                half4 mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, projectionUv);
                clip(mask.a - 0.5h);
                return half4(0.02h, 0.02h, 0.02h, mask.a);
            }
            ENDHLSL
        }
    }
}
