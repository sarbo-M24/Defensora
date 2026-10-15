// Defensora - papel picado bunting.
// Double-sided, alpha-clipped tissue paper with GPU wind sway, so a whole string of flags is
// one mesh / one draw call and costs no CPU to animate. The mesh (built by PapelPicadoString)
// carries: uv0 = atlas UV, uv1.x = sway weight (0 at the string, 1 at the flag's free edge),
// uv1.y = per-flag phase, color.a = 1 for paper / 0 for the string itself.
// Works with Forward and Forward+ (cluster light loop), real-time shadows and fog.
Shader "Defensora/Papel Picado"
{
    Properties
    {
        [MainTexture] _BaseMap("Flags Atlas (RGBA)", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        _StringColor("String Color", Color) = (0.85, 0.8, 0.7, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.05

        [Header(Night Look)]
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.25
        _ShadowTint("Shadow Tint (coloured darkness)", Color) = (0.04, 0.02, 0.06, 1)
        _Translucency("Back-Lit Glow", Range(0, 1)) = 0.35

        [Header(Wind)]
        _WindStrength("Flutter Distance (m)", Float) = 0.04
        _WindSpeed("Flutter Speed", Float) = 2.2
        _WindGust("Gust Variation", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            half4 _StringColor;
            half _Smoothness;
            half _AmbientStrength;
            half4 _ShadowTint;
            half _Translucency;
            float _WindStrength;
            float _WindSpeed;
            half _WindGust;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        // Pushes a vertex along its normal: flags flap about the string, the string itself swings slightly.
        float3 ApplyWind(float3 positionWS, float3 normalWS, float2 sway)
        {
            float t = _Time.y * _WindSpeed + sway.y;
            float gust = 1.0 + _WindGust * sin(_Time.y * 0.37 + positionWS.x * 0.11 + positionWS.z * 0.07);
            float wave = sin(t + positionWS.x * 0.6) + 0.35 * sin(t * 2.7 + sway.y * 1.7);
            return positionWS + normalWS * (wave * gust * _WindStrength * sway.x);
        }

        half SampleAlpha(float2 uv, half isFlag)
        {
            return lerp(1.0h, SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a, isFlag);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                float2 sway       : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                half   isFlag     : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = ApplyWind(TransformObjectToWorld(input.positionOS.xyz), normalWS, input.sway);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.isFlag = input.color.a;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input, half facing : VFACE) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half alpha = lerp(1.0h, tex.a, input.isFlag);
                clip(alpha - _Cutoff);
                half3 albedo = lerp(_StringColor.rgb, tex.rgb * _BaseColor.rgb, input.isFlag);

                // Paper is double-sided: light the side we're looking at.
                float3 normalWS = normalize(input.normalWS) * (facing > 0 ? 1.0 : -1.0);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.bakedGI = SampleSH(normalWS) * _AmbientStrength;
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.alpha = 1.0;
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion = 1.0;
                surfaceData.normalTS = half3(0, 0, 1);

                half3 color = UniversalFragmentPBR(inputData, surfaceData).rgb;

                // Thin paper: a lamp behind it still lights the side facing us (cheap - same shading,
                // flipped normal, scaled). Lets strung flags glow when seen against the lanterns.
                if (_Translucency > 0)
                {
                    inputData.normalWS = -normalWS;
                    inputData.bakedGI = 0;
                    surfaceData.smoothness = 0;
                    color += UniversalFragmentPBR(inputData, surfaceData).rgb * (_Translucency * input.isFlag);
                }

                color += albedo * _ShadowTint.rgb;
                color = MixFog(color, inputData.fogCoord);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                float2 sway       : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half   isFlag     : TEXCOORD1;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = ApplyWind(TransformObjectToWorld(input.positionOS.xyz), normalWS, input.sway);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.isFlag = input.color.a;
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                clip(SampleAlpha(input.uv, input.isFlag) - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                float2 sway       : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half   isFlag     : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(ApplyWind(TransformObjectToWorld(input.positionOS.xyz), normalWS, input.sway));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.isFlag = input.color.a;
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                clip(SampleAlpha(input.uv, input.isFlag) - _Cutoff);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                float2 sway       : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half   isFlag     : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(ApplyWind(TransformObjectToWorld(input.positionOS.xyz), normalWS, input.sway));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = normalWS;
                output.isFlag = input.color.a;
                return output;
            }

            half4 DepthNormalsFrag(Varyings input, half facing : VFACE) : SV_Target
            {
                clip(SampleAlpha(input.uv, input.isFlag) - _Cutoff);
                return half4(NormalizeNormalPerPixel(input.normalWS * (facing > 0 ? 1.0 : -1.0)), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
