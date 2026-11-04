// Ground cover the terrain scatters and draws GPU instanced (Art/Ground: grass tufts, flowers, ferns, pebbles).
// The colors come from the mesh's vertex colors (sRGB, baked from the Blender materials: a terrain detail may
// only have one material). Everything is lit as if it lay flat, by the sun with its shadows, the ambient light
// and the fog, so it takes the light like the ground it grows from and its roots melt into it; both sides of
// a blade look the same. It sways in the wind, more the higher it stands off the ground. Casts no shadows:
// thousands of blades would only cost.
Shader "Madlands/GroundCover"
{
    Properties
    {
        _Wind ("Wind (meters of sway at 1 m up)", Float) = 0.12
        _WindSpeed ("Wind speed", Float) = 1.4
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _Wind;
            float _WindSpeed;
        CBUFFER_END

        // Gusts rolling across the land: two waves at angles to each other, offset by where the piece stands.
        float3 Sway(float3 positionOS)
        {
            float3 positionWS = TransformObjectToWorld(positionOS);
            float3 origin = TransformObjectToWorld(float3(0, 0, 0));
            float t = _Time.y * _WindSpeed;
            float gust = sin(t + origin.x * 0.21 + origin.z * 0.13) * 0.6 + sin(t * 1.7 + origin.x * 0.05 - origin.z * 0.31) * 0.4;
            float up = max(0, positionWS.y - origin.y);
            positionWS.xz += float2(0.8, 0.45) * gust * _Wind * up * (0.5 + up);
            return positionWS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 color : TEXCOORD1;
                half fog : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.positionWS = Sway(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.color = SRGBToLinear(input.color.rgb);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normal = half3(0, 1, 0);
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 light = sun.color * (saturate(dot(normal, sun.direction)) * sun.shadowAttenuation) + SampleSH(normal);
                return half4(MixFog(input.color * light, input.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 Vert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformWorldToHClip(Sway(input.positionOS.xyz));
            }

            half4 Frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
