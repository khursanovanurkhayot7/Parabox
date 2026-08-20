Shader "Luxodd/URP/DemoBackground"
{
    Properties
    {
        _TopColor("Top Color", Color) = (0.10, 0.10, 0.12, 1.0)
        _BottomColor("Bottom Color", Color) = (0.05, 0.05, 0.07, 1.0)
        _VignetteStrength("Vignette Strength", Range(0.0, 2.0)) = 0.65
        _VignetteSoftness("Vignette Softness", Range(0.1, 8.0)) = 1.35
        _NoiseStrength("Noise Strength", Range(0.0, 0.2)) = 0.015
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            half4 _TopColor;
            half4 _BottomColor;
            float _VignetteStrength;
            float _VignetteSoftness;
            float _NoiseStrength;

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;

                half3 color = lerp(_BottomColor.rgb, _TopColor.rgb, saturate(uv.y));

                float2 centeredUv = uv * 2.0 - 1.0;
                float vignette = 1.0 - saturate(pow(length(centeredUv), _VignetteSoftness) * _VignetteStrength);
                color *= vignette;

                float noise = (Hash21(floor(uv * 512.0)) - 0.5) * _NoiseStrength;
                color += noise;

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
