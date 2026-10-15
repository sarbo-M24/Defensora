// Defensora - glowing, see-through ghost body.
//   Fresnel rim + soft core glow, swirling ectoplasm noise that streams backwards along
//   local -Z (the direction of travel is +Z), a wobbling surface and an optional
//   teardrop "tail" stretched out behind. Premultiplied blending: the body is translucent,
//   the rim is additive HDR so it blooms.
// _BaseColor doubles as the hit-flash colour (EnemyHitReaction overrides it).
Shader "Defensora/Ghost"
{
    Properties
    {
        [HDR] _BaseColor("Core Color", Color) = (0.4, 0.9, 1, 1)
        [HDR] _RimColor("Rim Color", Color) = (0.2, 0.6, 1, 1)

        [Header(Glow)]
        _CoreIntensity("Core Intensity", Range(0, 10)) = 2
        _CoreSharpness("Core Sharpness", Range(0.5, 8)) = 2
        _RimIntensity("Rim Intensity", Range(0, 20)) = 4
        _RimPower("Rim Power", Range(0.5, 8)) = 2.5

        [Header(Transparency)]
        _Opacity("Body Opacity", Range(0, 1)) = 0.35
        _RimOpacity("Rim Opacity", Range(0, 1)) = 0.6

        [Header(Ectoplasm Swirl)]
        _NoiseScale("Noise Scale", Range(0.5, 12)) = 3.5
        _NoiseStretch("Stretch Along Travel", Range(0.1, 1)) = 0.35
        _FlowSpeed("Flow Speed", Range(0, 6)) = 1.5
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0.6

        [Header(Pulse)]
        _PulseSpeed("Pulse Speed", Range(0, 10)) = 2
        _PulseAmount("Pulse Amount", Range(0, 1)) = 0.2

        [Header(Shape)]
        _WobbleAmount("Wobble Amount", Range(0, 0.2)) = 0.04
        _WobbleFrequency("Wobble Frequency", Range(0.5, 12)) = 4
        _WobbleSpeed("Wobble Speed", Range(0, 10)) = 3
        _TailStretch("Tail Stretch", Range(0, 1.5)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "Ghost"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _RimColor;
                half _CoreIntensity;
                half _CoreSharpness;
                half _RimIntensity;
                half _RimPower;
                half _Opacity;
                half _RimOpacity;
                half _NoiseScale;
                half _NoiseStretch;
                half _FlowSpeed;
                half _NoiseStrength;
                half _PulseSpeed;
                half _PulseAmount;
                half _WobbleAmount;
                half _WobbleFrequency;
                half _WobbleSpeed;
                half _TailStretch;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float  phase      : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x),
                         lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                    lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                         lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y),
                    f.z);
            }

            float Fbm(float3 p)
            {
                float value = 0.5 * Noise(p);
                value += 0.25 * Noise(p * 2.03 + 17.1);
                value += 0.125 * Noise(p * 4.01 + 31.7);
                return value / 0.875;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Per-object phase so a wave of ghosts doesn't wobble in lockstep.
                float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
                float phase = frac(sin(dot(originWS.xz, float2(12.9898, 78.233))) * 43758.5453) * 6.2831;

                float3 p = input.positionOS.xyz;
                float3 n = normalize(input.normalOS);
                float t = _Time.y * _WobbleSpeed + phase;

                float wobble = sin(dot(p, float3(3.1, 2.3, 1.7)) * _WobbleFrequency + t)
                             * sin(p.y * _WobbleFrequency * 1.3 - t * 0.7);
                p += n * wobble * _WobbleAmount;

                // Teardrop: drag the back hemisphere out behind the direction of travel.
                float back = saturate(-n.z);
                p.z -= back * back * _TailStretch;

                output.positionOS = p;
                output.phase = phase;
                output.positionWS = TransformObjectToWorld(p);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 normalWS = normalize(input.normalWS);
                float3 viewWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float ndotv = saturate(dot(normalWS, viewWS));

                float rim = pow(1.0 - ndotv, _RimPower);
                float core = pow(ndotv, _CoreSharpness);

                // Swirl streams backwards like smoke past a body in a wind tunnel.
                float3 np = input.positionOS * _NoiseScale;
                np.z *= _NoiseStretch;
                np.z += _Time.y * _FlowSpeed;
                np.xy += input.phase;
                float swirl = Fbm(np);
                float noiseMul = lerp(1.0, swirl * 2.0, _NoiseStrength);

                float pulse = 1.0 + sin(_Time.y * _PulseSpeed + input.phase) * _PulseAmount;

                float alpha = saturate(_Opacity * (0.35 + 0.65 * core) * noiseMul + rim * _RimOpacity);
                float3 body = _BaseColor.rgb * (0.2 + core * _CoreIntensity) * noiseMul * pulse;
                float3 rimGlow = _RimColor.rgb * rim * _RimIntensity * lerp(1.0, swirl * 1.6, 0.5) * pulse;

                // Premultiplied: translucent body, additive rim on top.
                return half4(body * alpha + rimGlow, alpha);
            }
            ENDHLSL
        }
    }
}
