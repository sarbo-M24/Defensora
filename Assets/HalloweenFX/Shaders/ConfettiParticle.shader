// Defensora - procedural confetti / marigold petal particle.
// No textures: each particle picks a shape (strip, dot, star, marigold petal) from its
// stable random value. Cards tumble in 3D and catch a glint as they flip, colours are
// pushed into HDR so Bloom makes them pop, and fade-out dissolves with a dither
// (cutout + ZWrite, so no sorting problems with hundreds of particles).
//
// Required particle vertex streams (set by the builder):
//   Position, Normal, Color, UV, StableRandom.x   -> TEXCOORD0.xyz
Shader "Defensora/Particles/Confetti"
{
    Properties
    {
        _Glow("HDR Glow", Range(0, 8)) = 1.8
        _GlintStrength("Flip Glint", Range(0, 4)) = 1.5
        _FlutterSpeed("Flutter Speed", Range(0, 60)) = 22
        _ForceShape("Shape (-1 random, 0 strip, 1 dot, 2 star, 3 petal)", Range(-1, 3)) = -1
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off
        ZWrite On

        Pass
        {
            Name "Confetti"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Glow;
                half _GlintStrength;
                half _FlutterSpeed;
                half _ForceShape;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                float3 uvRandom   : TEXCOORD0; // xy = uv, z = stable random
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float3 uvRandom   : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.uvRandom = input.uvRandom;
                return output;
            }

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // Returns coverage (0/1-ish) of the chosen shape at p in [-1,1]^2.
            float ShapeMask(float2 p, float shape, float rnd)
            {
                if (shape < 0.5) // paper strip, random aspect
                {
                    float2 halfSize = float2(0.95, lerp(0.28, 0.55, frac(rnd * 7.13)));
                    float2 d = abs(p) / halfSize;
                    return step(max(d.x, d.y), 1.0);
                }
                if (shape < 1.5) // dot
                {
                    return step(length(p), 0.75);
                }
                if (shape < 2.5) // five point star
                {
                    float a = atan2(p.y, p.x) + rnd * 6.2831;
                    float edge = lerp(0.38, 0.95, pow(abs(cos(a * 2.5)), 4.0));
                    return step(length(p), edge);
                }
                // marigold petal: narrow at the base, rounded and wide at the tip
                float width = 0.55 * sqrt(saturate(1.0 - p.y * p.y)) * lerp(0.35, 1.0, saturate(p.y * 0.5 + 0.5));
                return step(abs(p.x), width);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float rnd = input.uvRandom.z;
                float2 p = input.uvRandom.xy * 2.0 - 1.0;

                float shape = _ForceShape;
                if (shape < -0.5)
                    shape = rnd < 0.5 ? 0 : (rnd < 0.75 ? 1 : (rnd < 0.9 ? 2 : 3));

                clip(ShapeMask(p, shape, rnd) - 0.5);

                // Dithered fade driven by vertex alpha (Color over Lifetime).
                clip(input.color.a - Hash12(input.positionCS.xy) - 0.001);

                // Paper flutter: brightness follows how much the card faces the camera,
                // plus a per-particle flicker so even camera-facing cards shimmer.
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 n = normalize(input.normalWS);
                float facing = abs(dot(n, viewDirWS));
                float flutter = 0.75 + 0.25 * sin(_Time.y * _FlutterSpeed + rnd * 60.0);
                half shade = (0.45 + 0.55 * facing) * flutter;

                // Glint when a card flips flat to the viewer.
                half glint = pow(facing, 24.0) * _GlintStrength;

                half3 color = input.color.rgb;
                if (shape > 2.5) // petal: darker base, brighter tip
                    color *= lerp(0.55, 1.15, saturate(p.y * 0.5 + 0.5));

                color = color * shade * _Glow + glint * (color * 0.5 + 0.5);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
