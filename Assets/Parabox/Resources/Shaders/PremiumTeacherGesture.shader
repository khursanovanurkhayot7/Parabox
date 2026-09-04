Shader "Parabox/UI/Premium Teacher Gesture"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _GestureAngle ("Right Arm Gesture From Relaxed Pose", Range(-0.4,0.4)) = 0
        _ArmPivot ("Legacy Right Arm Pivot", Vector) = (0.46,0.66,0,0)
        _ArmBounds ("Right Arm Bounds", Vector) = (0.455,0.56,1,0.75)
        _LeftStep ("Left Foot Lift", Range(0,1)) = 0
        _RightStep ("Right Foot Lift", Range(0,1)) = 0
        _LeftPlant ("Left Foot Contact Offset", Vector) = (0,0,0,0)
        _RightPlant ("Right Foot Contact Offset", Vector) = (0,0,0,0)
        _FreeArmAngle ("Free Arm Walking Swing", Range(0,0.2)) = 0
        _BodySway ("Gentle Upper Body Sway", Range(-0.01,0.01)) = 0
        _Breath ("Upper Body Breath", Range(0,0.02)) = 0
        _ElbowAngle ("Speaking Elbow Bend", Range(-0.3,0.4)) = 0
        _WristAngle ("Speaking Wrist Turn", Range(-0.2,0.2)) = 0
        _FreeElbowAngle ("Free Elbow Bend", Range(0,0.4)) = 0
        _FreeWristAngle ("Free Wrist Turn", Range(-0.2,0.2)) = 0
        _EntranceLight ("Soft Entrance Highlight", Range(0,0.14)) = 0
        _HandOpen ("Speaking Finger Extension", Range(0,1)) = 0.22
        _HeldPointer ("Keep Teaching Fingers Around Pointer", Range(0,1)) = 0
        _PointerGripTip ("Held Pointer Grip and Tip UV", Vector) = (0,0,0,0)
        _PointerCanvasSize ("Held Pointer Drawing Size", Vector) = (480,500,0,0)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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
            Name "PremiumTeacherGesture"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

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
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _GestureAngle;
            float4 _ArmPivot;
            float4 _ArmBounds;
            float _LeftStep;
            float _RightStep;
            float4 _LeftPlant;
            float4 _RightPlant;
            float _FreeArmAngle;
            float _BodySway;
            float _Breath;
            float _ElbowAngle;
            float _WristAngle;
            float _FreeElbowAngle;
            float _FreeWristAngle;
            float _EntranceLight;
            float _HandOpen;
            float _HeldPointer;
            float4 _PointerGripTip;
            float4 _PointerCanvasSize;

            v2f vert(appdata_t input)
            {
                v2f output;
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            float InsideBounds(float2 uv, float4 bounds)
            {
                return step(bounds.x, uv.x) * step(bounds.y, uv.y)
                    * step(uv.x, bounds.z) * step(uv.y, bounds.w);
            }

            float LeftLegMask(float2 uv)
            {
                // Follow the approved silhouette: keep the hanging hand out of the leg cutout,
                // but include the wider shoe below the ankle (UVs run bottom to top).
                float leftEdge = uv.y > 0.34 ? 0.140 : (uv.y > 0.19 ? 0.122 : 0.075);
                return InsideBounds(uv, float4(leftEdge, 0.045, 0.270, 0.450));
            }

            float RightLegMask(float2 uv)
            {
                return InsideBounds(uv, float4(0.298, 0.045, 0.496, 0.450));
            }

            float FreeArmMask(float2 uv)
            {
                // Trace the hand/forearm edge without stealing the torso or the left thigh.
                float upperEdge = 0.103 + 0.019 * saturate((uv.y - 0.600) / 0.040);
                float rightEdge = uv.y > 0.55 ? upperEdge : (uv.y > 0.45 ? 0.103 : 0.134);
                return InsideBounds(uv, float4(0.0, 0.335, rightEdge, 0.665));
            }

            float TeachingArmMask(float2 uv)
            {
                // The legacy rectangular arm cutout overlapped the pink torso edge.
                // Keep that edge attached to the body, even when the hand is raised.
                float shoulderEdge = uv.y > 0.606 && uv.y < 0.668 ? 0.468 : 0.480;
                return InsideBounds(uv, float4(max(_ArmBounds.x, shoulderEdge),
                    _ArmBounds.y, _ArmBounds.z, _ArmBounds.w));
            }

            float2 RotateAt(float2 uv, float2 pivot, float angle)
            {
                float sine = sin(-angle);
                float cosine = cos(-angle);
                float2 delta = uv - pivot;
                return float2(cosine * delta.x - sine * delta.y,
                    sine * delta.x + cosine * delta.y) + pivot;
            }

            float2 WalkingLegUv(float2 uv, float outward, float footStep, float2 plant)
            {
                const float hipY = 0.450;
                const float ankleY = 0.185;
                float lift = saturate(footStep);
                float footLift = lift * 0.065 + plant.y;
                float legScale = max(0.55, 1.0 - footLift / (hipY - ankleY));
                float destinationAnkle = ankleY + footLift;
                float ankleX = outward < 0.0 ? 0.174 : 0.376;
                float footSideways = plant.x + outward * lift * 0.005;

                // A lifted boot rolls gently around its ankle; a planted boot remains rigid.
                // Never stretch the footwear when bending/foreshortening the leg above it.
                if (uv.y < destinationAnkle)
                {
                    float2 bootUv = RotateAt(uv,
                        float2(ankleX + footSideways, destinationAnkle), outward * lift * 0.075);
                    return bootUv - float2(footSideways, footLift);
                }
                float sourceY = hipY + (uv.y - hipY) / legScale;
                float jointProgress = saturate((hipY - sourceY) / (hipY - ankleY));
                float kneeBend = sin(jointProgress * 3.14159265);
                float sideways = outward * lift * kneeBend * 0.014
                    + footSideways * jointProgress;
                return float2(uv.x - sideways, sourceY);
            }

            fixed4 Over(fixed4 front, fixed4 back)
            {
                fixed4 result;
                result.a = front.a + back.a * (1.0 - front.a);
                result.rgb = (front.rgb * front.a + back.rgb * back.a * (1.0 - front.a))
                    / max(result.a, 0.0001);
                return result;
            }

            float2 MatchingGloveUv(float2 uv, float opening, float holding)
            {
                // Both hands use the approved left glove's own pixels, proportions and
                // chunky finger segments. Keep the glove continuous while gently extending
                // its curled fingers and easing the thumb out during the spoken emphasis.
                // Curl the gripping fingertips up towards their knuckles instead of
                // leaving an open palm alongside the rod. The other hand is unchanged.
                if (uv.y < 0.404)
                    uv.y = 0.404 + (uv.y - 0.404) / lerp(1.0, 0.60, holding);
                opening = saturate((opening - 0.22) / 0.78);
                float thumb = smoothstep(0.076, 0.115, uv.x)
                    * (1.0 - smoothstep(0.406, 0.435, uv.y)) * smoothstep(0.367, 0.380, uv.y);
                uv.x -= thumb * opening * 0.007;
                if (uv.y < 0.404)
                {
                    float tip = 1.0 - smoothstep(0.345, 0.404, uv.y);
                    uv.x = 0.060 + (uv.x - 0.060) / (1.0 + opening * tip * 0.12);
                    uv.y = 0.404 + (uv.y - 0.404) / (1.0 + opening * 0.20);
                }
                return uv;
            }

            fixed4 PointerRibbon(float2 canvasUV, float2 offset, float width,
                float from, float to, float3 dark, float3 light)
            {
                float2 delta = (_PointerGripTip.zw - _PointerGripTip.xy) * _PointerCanvasSize.xy;
                float2 direction = delta / max(length(delta), 0.001);
                float2 normal = float2(-direction.y, direction.x);
                float2 local = (canvasUV - _PointerGripTip.xy) * _PointerCanvasSize.xy - offset;
                float across = dot(local, normal);
                float along = dot(local, direction);
                float aa = max(fwidth(across), 0.15);
                float coverage = (1.0 - smoothstep(width * 0.5 - aa * 0.5,
                    width * 0.5 + aa * 0.5, abs(across))) * step(from, along) * step(along, to);
                return fixed4(lerp(dark, light, saturate(across / width + 0.5)), coverage);
            }

            fixed4 HoldingPalm(fixed4 glove, float2 gloveUV, float2 canvasUV)
            {
                if (_HeldPointer < 0.5) return glove;
                // The existing curled fingers and thumb remain in front of the rod, while
                // the small exposed shaft crosses the palm. No duplicated glove layer:
                // the entire hand is composited and faded exactly once with the teacher.
                float aa = max(fwidth(gloveUV.y), 0.0002);
                float fingerEdge = 0.391 + (gloveUV.x - 0.060) * 0.08;
                float fingers = 1.0 - smoothstep(fingerEdge - aa, fingerEdge + aa, gloveUV.y);
                float thumb = smoothstep(0.078 - aa, 0.078 + aa, gloveUV.x)
                    * (1.0 - smoothstep(0.420 - aa, 0.420 + aa, gloveUV.y));
                float palm = (1.0 - max(fingers, thumb))
                    * (1.0 - smoothstep(0.437 - aa, 0.437 + aa, gloveUV.y));
                float2 delta = (_PointerGripTip.zw - _PointerGripTip.xy) * _PointerCanvasSize.xy;
                float rodLength = length(delta);
                float2 normal = float2(-delta.y, delta.x) / max(rodLength, 0.001);
                fixed4 shaft = PointerRibbon(canvasUV, float2(0.7, -1.0), 4.8, -24.0, rodLength,
                    float3(79,35,9)/255.0, float3(120,60,16)/255.0);
                shaft = Over(PointerRibbon(canvasUV, float2(0,0), 3.6, -24.0, rodLength,
                    float3(171,80,12)/255.0, float3(255,213,94)/255.0), shaft);
                shaft = Over(PointerRibbon(canvasUV, normal * 0.8, 0.9, -24.0, rodLength,
                    float3(255,216,107)/255.0, float3(255,252,208)/255.0), shaft);
                shaft = Over(PointerRibbon(canvasUV, float2(0,0), 6.0, -25.0, 4.0,
                    float3(25,13,42)/255.0, float3(72,39,91)/255.0), shaft);
                glove.rgb = lerp(glove.rgb, shaft.rgb, shaft.a * palm);
                return glove;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 uv = input.texcoord;
                // Inverse-map a very small upper-body weight shift. The lower body keeps
                // its exact original coordinates, so neither foot slides or floats.
                if (uv.y > 0.45)
                    uv.y = 0.45 + (uv.y - 0.45) / (1.0 + _Breath);
                uv.x -= _BodySway * smoothstep(0.43, 0.74, uv.y);
                fixed4 original = tex2D(_MainTex, uv) + _TextureSampleAdd;

                // Remove each limb once, then composite its independently posed sample. At zero
                // lift/gesture the right arm hangs naturally beside the torso. Zero is a
                // relaxed pose, not the old permanently outstretched pointing pose.
                float sourceRegion = saturate(TeachingArmMask(uv)
                    + FreeArmMask(uv) + LeftLegMask(uv) + RightLegMask(uv));
                fixed4 body = original;
                body.rgb *= 1.0 - sourceRegion;
                body.a *= 1.0 - sourceRegion;

                // Mirror the approved hanging left arm across the shoulder centres. This
                // gives the right arm the same length, glove and natural arms-down rest.
                float2 armUv = float2(0.590 - uv.x, uv.y);
                armUv = RotateAt(armUv, float2(0.115, 0.643), -_GestureAngle);
                armUv = RotateAt(armUv, float2(0.075, 0.550),
                    -_ElbowAngle * (1.0 - smoothstep(0.485, 0.565, armUv.y)));
                armUv = RotateAt(armUv, float2(0.063, 0.439),
                    -_WristAngle * (1.0 - smoothstep(0.405, 0.455, armUv.y)));
                // The free hand can open while talking; the teaching hand keeps its curled
                // fingers around the prop. Both still use the approved glove artwork.
                float2 gloveUv = MatchingGloveUv(armUv, lerp(_HandOpen, 0.22, _HeldPointer), _HeldPointer);
                fixed4 arm = (tex2D(_MainTex, gloveUv) + _TextureSampleAdd) * FreeArmMask(gloveUv);
                arm = HoldingPalm(arm, gloveUv, input.texcoord);

                float2 freeArmUv = RotateAt(uv, float2(0.115, 0.643), _FreeArmAngle);
                freeArmUv = RotateAt(freeArmUv, float2(0.075, 0.550),
                    _FreeElbowAngle * (1.0 - smoothstep(0.485, 0.565, freeArmUv.y)));
                freeArmUv = RotateAt(freeArmUv, float2(0.063, 0.439),
                    _FreeWristAngle * (1.0 - smoothstep(0.405, 0.455, freeArmUv.y)));
                freeArmUv = MatchingGloveUv(freeArmUv, _HandOpen, 0.0);
                fixed4 freeArm = (tex2D(_MainTex, freeArmUv) + _TextureSampleAdd) * FreeArmMask(freeArmUv);
                float2 leftUv = WalkingLegUv(uv, -1.0, _LeftStep, _LeftPlant.xy);
                float2 rightUv = WalkingLegUv(uv, 1.0, _RightStep, _RightPlant.xy);
                fixed4 leftLeg = (tex2D(_MainTex, leftUv) + _TextureSampleAdd)
                    * LeftLegMask(leftUv);
                fixed4 rightLeg = (tex2D(_MainTex, rightUv) + _TextureSampleAdd)
                    * RightLegMask(rightUv);
                fixed4 colour = Over(freeArm, Over(leftLeg, Over(rightLeg, Over(body, arm))));
                // A quiet cool highlight catches the existing glossy finish during the fade.
                // It is confined to the teacher's opaque artwork, never a halo or backdrop.
                float highlight = smoothstep(0.20, 0.75,
                    max(colour.r, max(colour.g, colour.b)));
                colour.rgb = saturate(colour.rgb + _EntranceLight * highlight
                    * float3(0.30, 0.48, 0.60));
                // Fade the finished character once, so overlapping joints do not get darker
                // or more opaque than the torso halfway through the entrance.
                colour *= input.color;

                #ifdef UNITY_UI_CLIP_RECT
                colour.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(colour.a - 0.001);
                #endif
                return colour;
            }
            ENDCG
        }
    }
}
