// Defensora - additive HDR flash for hit effects. One shader, three looks:
//   0 = soft glow (also used for stretched sparks)
//   1 = shockwave ring
//   2 = starburst (rays + hot core)
// Colour comes from the particle colour; _Intensity pushes it into HDR for Bloom.
Shader "Defensora/Particles/Hit Flash"
{
    Properties
    {
        [Enum(Glow, 0, Ring, 1, Starburst, 2)] _Mode("Mode", Float) = 0
        _Intensity("HDR Intensity", Range(0, 30)) = 6
        _Falloff("Glow Falloff", Range(0.5, 8)) = 2.5
        _RingWidth("Ring Width", Range(0.02, 0.5)) = 0.12
        _Rays("Starburst Rays", Range(2, 16)) = 6
        _CoreWhite("White-Hot Core", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "HitFlash"
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
                half _Falloff;
                half _RingWidth;
                half _Rays;
                half _CoreWhite;
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
                float2 p = input.uv * 2.0 - 1.0;
                float r = length(p);
                float core = exp(-r * r * 30.0);
                float value;

                if (_Mode < 0.5)
                {
                    value = pow(saturate(1.0 - r), _Falloff);
                }
                else if (_Mode < 1.5)
                {
                    value = pow(saturate(1.0 - abs(r - 0.78) / _RingWidth), 2.0);
                    core = 0;
                }
                else
                {
                    float a = atan2(p.y, p.x);
                    float rays = pow(abs(cos(a * _Rays * 0.5)), 16.0) * saturate(1.0 - r);
                    value = rays + pow(saturate(1.0 - r), 3.0) * 0.6;
                }

                half3 color = input.color.rgb * value + core * _CoreWhite;
                return half4(color * input.color.a * _Intensity, 0.0);
            }
            ENDHLSL
        }
    }
}
