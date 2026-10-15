// Defensora - additive HDR wisps for ghost trails.
//   0 = ribbon: soft across the width (uv.y), broken up by noise scrolling along the
//       length (uv.x) so streamlines look like smoke in a wind tunnel. For TrailRenderers
//       and particle trails.
//   1 = soft dot: radial glow for particles (halos, embers).
// Colour comes from the vertex colour; _Intensity pushes it into HDR for Bloom.
Shader "Defensora/Particles/Ghost Wisp"
{
    Properties
    {
        [Enum(Ribbon, 0, Soft Dot, 1)] _Mode("Mode", Float) = 0
        _Intensity("HDR Intensity", Range(0, 30)) = 3
        _Softness("Edge Softness", Range(0.5, 8)) = 2

        [Header(Ribbon Noise)]
        _NoiseScale("Noise Scale (along)", Range(0.5, 30)) = 6
        _NoiseScroll("Noise Scroll", Range(-10, 10)) = 2.5
        _NoiseAmount("Noise Amount", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "GhostWisp"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Mode;
                half _Intensity;
                half _Softness;
                half _NoiseScale;
                half _NoiseScroll;
                half _NoiseAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float value;

                if (_Mode < 0.5)
                {
                    float across = 1.0 - abs(input.uv.y * 2.0 - 1.0);
                    value = pow(saturate(across), _Softness);

                    float2 np = float2(input.uv.x * _NoiseScale - _Time.y * _NoiseScroll, input.uv.y * 2.0);
                    float n = Noise(np) * 0.65 + Noise(np * 2.3 + 7.0) * 0.35;
                    value *= lerp(1.0, n * 1.8, _NoiseAmount);
                }
                else
                {
                    float r = length(input.uv * 2.0 - 1.0);
                    value = pow(saturate(1.0 - r), _Softness);
                }

                return half4(input.color.rgb * input.color.a * value * _Intensity, 0.0);
            }
            ENDHLSL
        }
    }
}
