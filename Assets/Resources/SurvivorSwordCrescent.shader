Shader "Survivor/SwordCrescent"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half opacity : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.opacity = input.color.a;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float across = input.uv.x;
                float tail = input.uv.y;
                float curl = sin(tail * 18 + across * 8) * .075
                           + sin(tail * 43 - across * 13) * .018;
                float veins = smoothstep(.82, .99, sin((across + curl) * 38));
                float inner = smoothstep(.18, .48, across + curl);
                float rim = smoothstep(.94, .979, across);
                float3 cyan = float3(.005, .52, .72);
                float3 indigo = float3(.07, .012, .67);
                float3 violet = float3(.64, .035, 1.04);
                float3 color = lerp(cyan, lerp(indigo, violet, smoothstep(.38, .9, across)), inner);
                color += veins * inner * float3(.33, .17, .39);
                color = lerp(color, float3(1.55, 1.1, 1.72), rim);
                float edge = smoothstep(.005, .13, across + curl * .6)
                           * (1 - smoothstep(.988, 1, across));
                float taper = 1 - smoothstep(.69, 1, tail);
                return half4(color, input.opacity * edge * taper);
            }
            ENDHLSL
        }
    }
}
