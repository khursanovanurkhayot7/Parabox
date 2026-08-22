Shader "Parabox/MenuArtworkBlend"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        _DarkAlpha ("Dark Background Alpha", Range(0,1)) = 0.035
        _TransparentBelow ("Transparent Below", Range(0,1)) = 0.035
        _OpaqueAbove ("Opaque Above", Range(0,1)) = 0.42
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
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
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            float _DarkAlpha;
            float _TransparentBelow;
            float _OpaqueAbove;

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 color = SampleSpriteTexture(IN.texcoord) * IN.color;
                float luminance = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
                float preserve = smoothstep(_TransparentBelow, _OpaqueAbove, luminance);
                color.a *= lerp(_DarkAlpha, 1.0, preserve);
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
