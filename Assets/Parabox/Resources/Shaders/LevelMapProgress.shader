Shader "Parabox/UI/Level Map Progress"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _GeneratedReferenceTex ("Generated State Reference", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            sampler2D _GeneratedReferenceTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _NodeCenters[50];
            float _Completed[50];
            float _States[50];
            float _PathLit[50];
            float _CurrentIndex;
            float _TravelLink;
            float _TravelProgress;
            float4 _TravelEndpoints;

            v2f vert(appdata_t input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            float RoundedBoxDistance(float2 samplePosition, float2 halfSize, float radius)
            {
                float2 q = abs(samplePosition) - halfSize + radius;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            float ChamferedBoxDistance(float2 samplePosition, float2 halfSize, float chamfer)
            {
                float2 a = abs(samplePosition);
                float straightEdges = max(a.x - halfSize.x, a.y - halfSize.y);
                float cutCorners = (a.x + a.y - (halfSize.x + halfSize.y - chamfer))
                    * 0.70710678;
                return max(straightEdges, cutCorners);
            }

            float SegmentDistance(float2 samplePosition, float2 from, float2 to, out float along)
            {
                float2 segment = to - from;
                along = saturate(dot(samplePosition - from, segment) /
                    max(dot(segment, segment), 0.001));
                return length(samplePosition - (from + segment * along));
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 colour = (tex2D(_MainTex, input.texcoord) + _TextureSampleAdd) * input.color;
                float originalLuma = dot(colour.rgb, float3(0.2126, 0.7152, 0.0722));
                float2 pixel = input.texcoord * float2(1920.0, 1080.0);

                // Only the one connection currently changing is animated. Keeping this outside
                // the node loop avoids the expensive full-map multi-link pass that can flood the
                // UI material on Metal. Existing printed routes remain untouched.
                float travelActive = step(-0.5, _TravelLink);
                float travelLocalLink = fmod(max(_TravelLink, 0.0), 10.0);
                float travelInsideChapter = 1.0 - step(8.5, travelLocalLink);
                float2 travelFrom = _TravelEndpoints.xy * float2(1920.0, 1080.0);
                float2 travelTo = _TravelEndpoints.zw * float2(1920.0, 1080.0);
                float travelAlong;
                float travelDistance = SegmentDistance(pixel, travelFrom, travelTo, travelAlong);
                float travelled = step(travelAlong, saturate(_TravelProgress));
                float travelLine = (1.0 - smoothstep(0.55, 2.15, travelDistance))
                    * travelled * travelActive * travelInsideChapter;
                float travelChapterMix = saturate(floor(max(_TravelLink, 0.0) / 10.0) / 4.0);
                float3 travelColour = lerp(float3(0.12, 0.90, 0.98),
                    float3(0.94, 0.38, 0.82), travelChapterMix);
                colour.rgb = lerp(colour.rgb, travelColour, travelLine * 0.82);

                float2 travelHeadPosition = lerp(travelFrom, travelTo,
                    saturate(_TravelProgress));
                float travelHead = (1.0 - smoothstep(0.35, 3.8,
                    length(pixel - travelHeadPosition))) * travelActive * travelInsideChapter;
                colour.rgb = lerp(colour.rgb, float3(0.92, 1.0, 1.0), travelHead * 0.86);

                [unroll]
                for (int i = 0; i < 50; i++)
                {
                    float state = _States[i];
                    float currentByIndex = 1.0 - step(0.5,
                        abs(_CurrentIndex - (float)i));
                    float current = max(step(1.5, state), currentByIndex);
                    float completed = step(0.5, state) * (1.0 - step(1.5, state))
                        * (1.0 - current);
                    float locked = (1.0 - step(0.5, state)) * (1.0 - current);

                    float2 delta = (input.texcoord - _NodeCenters[i].xy) * float2(1920.0, 1080.0);
                    // The face is smaller than the printed bevel/frame. Animated light is always
                    // clipped to this inner shape, so nothing spills outside the node border.
                    float distanceToFace = ChamferedBoxDistance(delta,
                        float2(27.0, 20.5), 4.7);
                    float faceInside = 1.0 - smoothstep(-0.9, 0.9, distanceToFace);
                    float distanceToNode = RoundedBoxDistance(delta, float2(35.0, 28.0), 5.5);
                    float nodeInside = 1.0 - smoothstep(-0.9, 0.9, distanceToNode);
                    float numberRegion = (1.0 - step(17.0, abs(delta.x)))
                        * (1.0 - step(16.0, abs(delta.y)));
                    float numberMask = smoothstep(0.68, 0.90, originalLuma)
                        * faceInside * numberRegion;
                    float numberEraseMask = smoothstep(0.30, 0.68, originalLuma)
                        * faceInside * numberRegion;

                    float chapterMix = saturate(floor(i / 10.0) / 4.0);
                    float3 accent = lerp(float3(0.12, 0.88, 0.95),
                        float3(0.92, 0.38, 0.82), chapterMix);

                    // LOCKED: use the pixels from the approved generated image itself. Nodes 9-50
                    // are sampled at their exact map coordinates. Earlier nodes reuse the same
                    // generated lock treatment from node 11, positioned locally. This removes all
                    // procedural approximations and makes the runtime state match the reference.
                    if (locked > 0.5 && nodeInside > 0.001)
                    {
                        float useOwnReference = step(7.5, (float)i);
                        float2 sharedLockUv = _NodeCenters[10].xy
                            + delta / float2(1920.0, 1080.0);
                        float2 generatedUv = lerp(sharedLockUv, input.texcoord,
                            useOwnReference);
                        float3 generatedLocked = tex2D(_GeneratedReferenceTex,
                            generatedUv).rgb;
                        colour.rgb = lerp(colour.rgb, generatedLocked, nodeInside);
                    }

                    // COMPLETED: one solid fill covers the entire chamfered interior. The original
                    // border remains untouched and no additive light exists outside the face.
                    float3 completedFace = lerp(float3(0.050, 0.64, 0.70),
                        float3(0.96, 1.0, 1.0), numberMask);
                    colour.rgb = lerp(colour.rgb, completedFace, faceInside * completed);

                    // CURRENT: the same clean cyan tile, brighter than completed, with one tight
                    // animated frame line. The printed frame supplies the second reference edge.
                    float pulse = 0.5 + 0.5 * sin(_Time.y * 3.2);
                    float3 currentFace = lerp(float3(0.060, 0.78, 0.82),
                        float3(1.0, 1.0, 1.0), numberMask);
                    currentFace *= 0.97 + pulse * 0.03;
                    colour.rgb = lerp(colour.rgb, currentFace, faceInside * current);
                    float activeRing = 1.0 - smoothstep(0.20, 2.05,
                        abs(distanceToFace - 3.6));
                    colour.rgb = lerp(colour.rgb, float3(0.34, 1.0, 1.0),
                        activeRing * current * (0.88 + pulse * 0.10));
                }

                colour.rgb = saturate(colour.rgb);
                return colour;
            }
            ENDCG
        }
    }
}
