Shader "Luxodd/URP/JoystickOverlay"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1, 1, 1, 0.02)
        _GridColor("Grid Color", Color) = (1, 1, 1, 0.06)
        _AxisColor("Axis Color", Color) = (1, 1, 1, 0.14)
        _RingColor("Ring Color", Color) = (1, 1, 1, 0.12)
        _CenterColor("Center Color", Color) = (1, 1, 1, 0.85)
        _VectorColor("Vector Color", Color) = (1, 1, 1, 0.95)

        _GridDensity("Grid Density", Range(4, 80)) = 24
        _GridThickness("Grid Thickness", Range(0.0005, 0.02)) = 0.0025
        _AxisThickness("Axis Thickness", Range(0.0005, 0.05)) = 0.004
        _RingThickness("Ring Thickness", Range(0.0005, 0.03)) = 0.004
        _CenterRadius("Center Radius", Range(0.001, 0.05)) = 0.008
        _MainRadius("Main Radius", Range(0.05, 0.48)) = 0.22

        _Vector("Vector", Vector) = (0, 0, 0, 0)
        _VectorThickness("Vector Thickness", Range(0.001, 0.03)) = 0.006
        _VectorHeadRadius("Vector Head Radius", Range(0.002, 0.04)) = 0.012
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
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
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            half4 _BaseColor;
            half4 _GridColor;
            half4 _AxisColor;
            half4 _RingColor;
            half4 _CenterColor;
            half4 _VectorColor;

            float _GridDensity;
            float _GridThickness;
            float _AxisThickness;
            float _RingThickness;
            float _CenterRadius;
            float _MainRadius;

            float4 _Vector;
            float _VectorThickness;
            float _VectorHeadRadius;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            float GetLineMask(float distanceToLine, float thickness)
            {
                return 1.0 - smoothstep(thickness, thickness * 1.5, distanceToLine);
            }

            float GetRingMask(float distanceToCenter, float radius, float thickness)
            {
                float delta = abs(distanceToCenter - radius);
                return 1.0 - smoothstep(thickness, thickness * 1.5, delta);
            }

            float GetGridMask(float2 uv, float density, float thickness)
            {
                float2 gridUv = frac(uv * density);

                float lineDistanceX = min(gridUv.x, 1.0 - gridUv.x);
                float lineDistanceY = min(gridUv.y, 1.0 - gridUv.y);

                float maskX = 1.0 - smoothstep(thickness, thickness * 1.5, lineDistanceX);
                float maskY = 1.0 - smoothstep(thickness, thickness * 1.5, lineDistanceY);

                return max(maskX, maskY);
            }

            float GetSegmentDistance(float2 inputPoint, float2 startPoint, float2 endPoint)
            {
                float2 pointOffset = inputPoint - startPoint;
                float2 segment = endPoint - startPoint;
                float segmentLength = max(dot(segment, segment), 0.00001);
                float projection = saturate(dot(pointOffset, segment) / segmentLength);
                return length(pointOffset - segment * projection);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float2 centeredUv = uv - 0.5;
                float distanceToCenter = length(centeredUv);

                half4 color = _BaseColor;

                float gridMask = GetGridMask(uv, _GridDensity, _GridThickness);
                color.rgb = lerp(color.rgb, _GridColor.rgb, gridMask * _GridColor.a);
                color.a = max(color.a, gridMask * _GridColor.a);

                float axisXMask = GetLineMask(abs(centeredUv.x), _AxisThickness);
                float axisYMask = GetLineMask(abs(centeredUv.y), _AxisThickness);
                float axisMask = max(axisXMask, axisYMask);

                color.rgb = lerp(color.rgb, _AxisColor.rgb, axisMask * _AxisColor.a);
                color.a = max(color.a, axisMask * _AxisColor.a);

                float outerRingMask = GetRingMask(distanceToCenter, _MainRadius, _RingThickness);
                float middleRingMask = GetRingMask(distanceToCenter, _MainRadius * 0.66, _RingThickness * 0.85);
                float innerRingMask = GetRingMask(distanceToCenter, _MainRadius * 0.33, _RingThickness * 0.7);
                float ringsMask = max(outerRingMask, max(middleRingMask, innerRingMask));

                color.rgb = lerp(color.rgb, _RingColor.rgb, ringsMask * _RingColor.a);
                color.a = max(color.a, ringsMask * _RingColor.a);

                float centerMask = 1.0 - smoothstep(_CenterRadius, _CenterRadius * 1.5, distanceToCenter);
                color.rgb = lerp(color.rgb, _CenterColor.rgb, centerMask * _CenterColor.a);
                color.a = max(color.a, centerMask * _CenterColor.a);

                float2 vectorEndUv = _Vector.xy * _MainRadius + 0.5;

                float vectorLineDistance = GetSegmentDistance(uv, float2(0.5, 0.5), vectorEndUv);
                float vectorLineMask = GetLineMask(vectorLineDistance, _VectorThickness);
                float vectorHeadMask = 1.0 - smoothstep(_VectorHeadRadius, _VectorHeadRadius * 1.5, distance(uv, vectorEndUv));
                float vectorMask = max(vectorLineMask, vectorHeadMask);

                color.rgb = lerp(color.rgb, _VectorColor.rgb, vectorMask * _VectorColor.a);
                color.a = max(color.a, vectorMask * _VectorColor.a);

                return color;
            }
            ENDHLSL
        }
    }
}
