Shader "UI/Upgrade Card Linear Wipe"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _LinearWipeLUT ("Linear Wipe LUT", 2D) = "black" {}
        _Color ("Highlight", Color) = (0.73, 0.89, 1, 0.27)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha One
        ColorMask RGB

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _LinearWipeLUT;
            half4 _Color;
            float _UpgradeCardWipePhase;

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float diagonal = dot(uv, float2(.72, .28));
                float sweep = _UpgradeCardWipePhase * 1.6 - .3;
                float distanceToWipe = diagonal - sweep;
                half3 lut = tex2D(_LinearWipeLUT, float2(frac(distanceToWipe + .5), .5)).rgb;
                half highlight = max(lut.r, max(lut.g, lut.b));
                half band = 1 - smoothstep(.015, .15, abs(distanceToWipe));
                half glint = 1 - smoothstep(.003, .024, abs(distanceToWipe));
                float2 corner = max(abs(uv - .5) - float2(.447, .466), 0);
                half edge = 1 - smoothstep(.84, 1, length(corner / float2(.053, .034)));
                half strength = band * (.25 + .75 * highlight) + glint * .25;
                return half4(_Color.rgb, saturate(strength) * edge * _Color.a);
            }
            ENDHLSL
        }
    }
}
