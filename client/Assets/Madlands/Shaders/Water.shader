// The water of a map: one mesh at the water level whose vertex colors hold how deep the water is there
// (red: (depth + 0.5) / 3 meters, HollowmereBuilder.CreateWater), so it needs no depth texture. Clear and
// light over the shallows, darker and less see-through where it is deep, the sky mirrored in it the more the
// flatter you look across it, foam that breathes along the shore, ripples that catch the sun.
Shader "Madlands/Water"
{
    Properties
    {
        _Shallow ("Shallow", Color) = (0.3, 0.62, 0.64, 1)
        _Deep ("Deep", Color) = (0.07, 0.27, 0.4, 1)
        _Sky ("Sky", Color) = (0.62, 0.78, 0.93, 1)
        _Foam ("Foam", Color) = (0.86, 0.93, 0.9, 1)
        _DeepAt ("Fully deep at (meters)", Float) = 0.9
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Shallow;
                half4 _Deep;
                half4 _Sky;
                half4 _Foam;
                float _DeepAt;
            CBUFFER_END

            // Set by the world lighting, so the reflected sky darkens with the real sky.
            half _MadlandsDaylight;

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float depth : TEXCOORD1;
                half fog : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.depth = input.color.r * 3.0 - 0.5;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // Break up the wave crests over several meters, so they do not form ruler-straight
            // stripes across the whole lake. Filter the finest waves when they cover a pixel.
            float2 Ripples(float2 p, float t)
            {
                float2 warp = float2(sin(p.y * 0.18 + p.x * 0.07 + t * 0.12),
                                     cos(p.x * 0.16 - p.y * 0.11 - t * 0.10));
                float2 q = p + warp * 2.0;
                float2 a = float2(0.9, 0.44), b = float2(-0.3, 0.95), c = float2(0.7, -0.71);
                float phaseA = dot(q, a) * 1.3 + t * 1.1;
                float phaseB = dot(q, b) * 2.3 + t * 1.7;
                float phaseC = dot(q, c) * 4.1 + t * 2.5;
                float envelope = 0.65 + 0.35 * sin(p.x * 0.12 - p.y * 0.15 + t * 0.18);
                float filterB = 1.0 - smoothstep(0.8, 2.4, fwidth(phaseB));
                float filterC = 1.0 - smoothstep(0.8, 2.4, fwidth(phaseC));
                return a * cos(phaseA) * (0.05 * envelope)
                     + b * cos(phaseB) * (0.035 * filterB)
                     + c * cos(phaseC) * (0.02 * filterC);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _Time.y;
                float2 slope = Ripples(input.positionWS.xz, t);
                half3 normal = normalize(half3(-slope.x, 1, -slope.y));
                half3 view = normalize(GetWorldSpaceViewDir(input.positionWS));
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shade = 0.6 + 0.4 * sun.shadowAttenuation;

                half deep = saturate(input.depth / _DeepAt);
                half3 color = lerp(_Shallow.rgb, _Deep.rgb, deep);
                half3 lit = color * (sun.color * 0.4 * shade + SampleSH(half3(0, 1, 0)));
                // The sky in the water, more of it at grazing angles, and a glint of the sun.
                half fresnel = 0.15 + 0.85 * pow(1.0 - saturate(dot(normal, view)), 3.0);
                lit = lerp(lit, _Sky.rgb * lerp(0.18, 1.0, _MadlandsDaylight), fresnel * 0.7);
                half3 halfway = normalize(sun.direction + view);
                lit += sun.color * pow(saturate(dot(normal, halfway)), 160.0) * 1.4 * sun.shadowAttenuation;

                // Foam along the shore, its edge creeping in and out.
                half reach = 0.1 + 0.05 * sin(t * 1.2 + input.positionWS.x * 0.35 + input.positionWS.z * 0.27);
                half foam = saturate(1.0 - input.depth / reach);
                foam *= foam;
                lit = lerp(lit, _Foam.rgb * (sun.color * 0.55 * shade + SampleSH(half3(0, 1, 0))), foam * 0.45);

                half alpha = max(saturate(lerp(0.35, 0.85, deep) + fresnel * 0.25), foam * 0.55);
                // Fade into the ground in the last few centimeters instead of drawing an opaque
                // edge where the flat water mesh intersects the shore and shallow marsh pools.
                alpha *= smoothstep(-0.02, 0.12, input.depth);
                return half4(MixFog(lit, input.fog), alpha);
            }
            ENDHLSL
        }
    }
}
