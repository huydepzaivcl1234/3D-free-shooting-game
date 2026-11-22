Shader "UI/Rust Upgrade Backdrop"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Attributes
            {
                float4 position : POSITION;
                half4 color : COLOR;
            };
            struct Varyings
            {
                float4 position : SV_POSITION;
                half4 color : COLOR;
                float4 screenPosition : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = TransformObjectToHClip(input.position.xyz);
                output.screenPosition = ComputeScreenPos(output.position);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.screenPosition.xy / input.screenPosition.w;
                float2 grid = max(_ScreenParams.xy / 3, 1);
                uv = (floor(uv * grid) + .5) / grid;
                half3 scene = SampleSceneColor(uv);
                return half4(lerp(scene, half3(.055, .047, .043), .65), input.color.a);
            }
            ENDHLSL
        }
    }
}
