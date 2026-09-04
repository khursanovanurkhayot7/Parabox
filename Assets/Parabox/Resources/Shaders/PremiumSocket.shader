Shader "Parabox/Premium Cargo Socket"
{
    Properties
    {
        [PerRendererData] _MainTex ("Socket Artwork", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Lamp ("Pressure / Completion Light", Range(0,1)) = 0
        _FrameHueShift ("Cargo Family Hue", Float) = 0
        _Filled ("Goal Completion", Range(0,1)) = 0
        _ArtBounds ("Tile Atlas Bounds", Vector) = (0,0,1,1)
        _RecessRect ("Recess Centre and Half Size", Vector) = (0.5,0.52,0.304,0.314)
        _LampOnly ("Foreground Status Light and Goal Inlay", Float) = 0
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"
            "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment SocketFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            float _Lamp, _FrameHueShift, _Filled, _LampOnly;
            float4 _ArtBounds, _RecessRect;

            float3 ToHsv(float3 c)
            {
                float4 k = float4(0.0, -1.0/3.0, 2.0/3.0, -1.0);
                float4 p = lerp(float4(c.bg, k.wz), float4(c.gb, k.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                return float3(abs(q.z + (q.w - q.y) / (6.0*d + 0.00001)),
                    d / (q.x + 0.00001), q.x);
            }

            float3 FromHsv(float3 c)
            {
                float3 p = abs(frac(c.xxx + float3(0, 2.0/3.0, 1.0/3.0)) * 6.0 - 3.0);
                return c.z * lerp(float3(1,1,1), saturate(p - 1.0), c.y);
            }

            float RecessEdge(float2 tileUV)
            {
                // Measured against the tile silhouette, independent of transparent padding.
                float2 q = abs(tileUV - _RecessRect.xy) - _RecessRect.zw + 0.080;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - 0.080;
            }

            float RoundedBar(float2 sampleUv, float2 centre, float2 halfSize, float radius)
            {
                float2 q = abs(sampleUv - centre) - halfSize + radius;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            fixed4 SocketFrag(v2f input) : SV_Target
            {
                clip(input.texcoord - _ArtBounds.xy);
                clip(_ArtBounds.zw - input.texcoord);
                fixed4 pixel = SampleSpriteTexture(input.texcoord);
                float3 c = pixel.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                    c = LinearToGammaSpace(c);
                #endif
                float3 hsv = ToHsv(c);
                // Recolour the warm frame only. The green light and dark recessed cavity keep
                // their own material identities, and the approved coral artwork uses zero shift.
                float frame = (1.0 - smoothstep(0.12, 0.18, hsv.x))
                    * smoothstep(0.24, 0.45, hsv.y) * smoothstep(0.40, 0.62, hsv.z);
                float3 shifted = FromHsv(float3(frac(hsv.x + _FrameHueShift + 1.0), hsv.yz));
                c = lerp(c, shifted, frame);
                float lamp = smoothstep(0.055, 0.22, pixel.g - max(pixel.r, pixel.b) * 0.9);
                c *= lerp(1.0 + _Filled * 0.045 * frame, 0.16 + 0.84 * _Lamp, lamp);
                float2 tile = (input.texcoord - _ArtBounds.xy) / (_ArtBounds.zw - _ArtBounds.xy);
                float edge = RecessEdge(tile);
                float aa = max(fwidth(edge), 0.001);
                float core = 1.0 - smoothstep(0.004, 0.004 + aa, abs(edge));
                float halo = 1.0 - smoothstep(0.008, 0.025 + aa, abs(edge));
                if (_LampOnly > 0.5)
                {
                    float lampRect = step(0.409, input.texcoord.x) * step(input.texcoord.x, 0.594)
                        * step(0.13, input.texcoord.y) * step(input.texcoord.y, 0.185);
                    // Echo the lower status lamp with long, slim rails at both sides.
                    float leftRail = RoundedBar(tile, float2(0.032, 0.51),
                        float2(0.014, 0.255), 0.014);
                    float rightRail = RoundedBar(tile, float2(0.968, 0.51),
                        float2(0.014, 0.255), 0.014);
                    float railDistance = min(leftRail, rightRail);
                    float railAA = max(fwidth(railDistance), 0.001);
                    float railCore = 1.0 - smoothstep(-railAA, railAA, railDistance);
                    float railHalo = 1.0 - smoothstep(0.0, 0.026 + railAA, railDistance);
                    float rail = saturate(railCore * 0.92 + railHalo * 0.18) * _Lamp;

                    // Everything else remains transparent, including the entire cargo face.
                    float inlay = saturate(core * 0.95 + halo * 0.16) * _Filled;
                    float signal = max(inlay, rail);
                    float alpha = max(lampRect * pixel.a, signal);
                    clip(alpha - 0.001);
                    float3 mint = lerp(float3(0.06, 0.58, 0.36),
                        float3(0.30, 1.0, 0.72), max(core, railCore));
                    c = lerp(c, mint, signal / max(alpha, 0.001));
                    pixel.a = alpha;
                }
                else
                {
                    // A restrained inner step adds contact depth without a new outer ring.
                    float stepShadow = 1.0 - smoothstep(0.010, 0.024 + aa, abs(edge + 0.024));
                    c *= 1.0 - stepShadow * 0.18;
                }
                #ifndef UNITY_COLORSPACE_GAMMA
                    c = GammaToLinearSpace(c);
                #endif
                fixed4 result = fixed4(c, pixel.a) * input.color;
                result.rgb *= result.a;
                return result;
            }
            ENDCG
        }
    }
}
