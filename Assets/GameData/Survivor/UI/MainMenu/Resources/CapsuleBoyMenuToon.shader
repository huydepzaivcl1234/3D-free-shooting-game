Shader "Survivor/CapsuleBoy Menu Toon"
{
    Properties
    {
        _BaseMap("Face texture", 2D) = "white" {}
        _BaseColor("Skin color", Color) = (1,1,1,1)
        _FaceMotion("Animated face", Float) = 0
        _LookBlink("Eye offset / openness", Vector) = (0,0,1,0)
        _Outline("Outline in pixels", Float) = 0
        _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Name "Toon"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _LookBlink;
                half _FaceMotion;
                float _Outline;
                float _Cull;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                if (_Outline > 0.0)
                {
                    float3 n = TransformWorldToViewDir(output.normalWS);
                    float2 dir = n.xy * rsqrt(max(dot(n.xy, n.xy), 0.0001));
                    output.positionCS.xy += dir * (_Outline * 2.0 / _ScaledScreenParams.xy) * output.positionCS.w;
                }
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_Outline > 0.0) return half4(0.039, 0.025, 0.020, 1);
                float2 uv = frac(input.uv);
                half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                if (_FaceMotion > 0.5 && uv.x > .747 && uv.x < .997 && uv.y > .788 && uv.y < .884)
                {
                    float2 center = float2(uv.x < .874 ? .807 : .942, .828) + _LookBlink.xy;
                    float2 pupil = (uv - center) / float2(.024, .029 * max(.09, _LookBlink.z));
                    tex = lerp(half3(.025,.020,.018), half3(1,1,1),
                        smoothstep(.86, 1.08, dot(pupil,pupil)));
                }
                half3 normal = normalize(input.normalWS);
                Light sun = GetMainLight();
                half lit = dot(normal, sun.direction);
                half ramp = .58 + .18 * smoothstep(.08, .15, lit) + .21 * smoothstep(.52,.60,lit);
                half3 eye = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half rim = smoothstep(.55,.83,1-saturate(dot(normal,eye))) *
                    saturate(dot(normal, normalize(half3(.8,.35,-.45)))) * .24;
                half3 shade = lerp(half3(.79,.86,1), half3(1,.95,.84), saturate(lit + .24));
                return half4(MixFog(_BaseColor.rgb * tex * shade * ramp + half3(.36,.64,1) * rim, input.fog), 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
    Fallback Off
}
