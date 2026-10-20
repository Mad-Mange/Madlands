// Draws nothing: a model region (a body material) that is hidden, like the hair under a helmet.
// Lives in Resources so builds include it (DisplayModel loads it by name).
Shader "Madlands/Invisible"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ColorMask 0
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 Vert(float4 position : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(position.xyz);
            }

            half4 Frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
