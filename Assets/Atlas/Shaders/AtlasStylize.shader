// ATLAS 3D - full-screen looks for the quick ambiences (runs after URP's
// post-processing, through the renderer's Full Screen Pass):
//   _Mode 0: nothing (the picture as it is)
//   _Mode 1: «Pesadilla retro» - printed halftone dots, few colours, blue
//            shadows and yellow light, colour fringes (horror posters)
//   _Mode 2: «Cómic tinta» - ink outlines, flat colours, dots in shadows
//   _Mode 3: «VHS maldito» - wobbling lines, colour bleed, scanlines, noise
Shader "Atlas/Stylize"
{
    Properties
    {
        _Mode ("Mode", Float) = 0
        _Amount ("Amount", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Pass
        {
            Name "AtlasStylize"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Mode;
            float _Amount;

            half3 Src(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv)).rgb;
            }

            float Luma(half3 c) { return dot(c, half3(0.299, 0.587, 0.114)); }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Round dots on a grid turned 45°; 1 inside a dot whose size
            // grows with `dark` (0..1).
            float Dots(float2 px, float cell, float dark)
            {
                float2 r = float2(px.x * 0.7071 - px.y * 0.7071, px.x * 0.7071 + px.y * 0.7071);
                float2 g = frac(r / cell) - 0.5;
                float d = length(g);
                float rad = sqrt(saturate(dark)) * 0.62;
                return 1.0 - smoothstep(rad - 0.07, rad + 0.07, d);
            }

            half3 Horror(float2 uv, float2 px, float h)
            {
                float2 off = (uv - 0.5) * 0.006;
                half3 c = half3(Src(uv + off).r, Src(uv).g, Src(uv - off).b);
                float L = Luma(c);
                // Few colours, pushed: blue-violet shadows, sick yellow light.
                half3 post = floor(saturate(c * 1.25) * 4.0 + 0.5) / 4.0;
                half3 shadowTint = half3(0.22, 0.24, 0.62);
                half3 lightTint = half3(1.08, 0.98, 0.5);
                half3 graded = post * lerp(shadowTint * 1.7, lightTint, smoothstep(0.12, 0.62, L));
                // Shadows stay a readable deep blue, not black.
                graded += shadowTint * 0.12 * (1.0 - smoothstep(0.0, 0.35, L));
                float cell = max(3.0, h / 210.0);
                float dots = Dots(px, cell, 1.0 - smoothstep(0.05, 0.85, L));
                half3 ink = half3(0.03, 0.03, 0.09);
                float midtones = 1.0 - smoothstep(0.72, 0.95, L);
                half3 o = lerp(graded, ink, dots * midtones * 0.9);
                o = lerp(o, ink, 1.0 - smoothstep(0.01, 0.04, L));
                // Rough print speckles.
                o *= 0.92 + 0.08 * Hash(floor(px / 2.0));
                float2 v = uv - 0.5;
                o *= saturate(1.15 - dot(v, v) * 1.6);
                return o;
            }

            half3 Comic(float2 uv, float2 px, float2 texel, float h)
            {
                half3 c = Src(uv);
                // Ink lines where the picture changes (Sobel on light).
                float tl = Luma(Src(uv + texel * float2(-1, 1))), t = Luma(Src(uv + texel * float2(0, 1))), tr = Luma(Src(uv + texel * float2(1, 1)));
                float l = Luma(Src(uv + texel * float2(-1, 0))), r = Luma(Src(uv + texel * float2(1, 0)));
                float bl = Luma(Src(uv + texel * float2(-1, -1))), b = Luma(Src(uv + texel * float2(0, -1))), br = Luma(Src(uv + texel * float2(1, -1)));
                float gx = -tl - 2.0 * l - bl + tr + 2.0 * r + br;
                float gy = tl + 2.0 * t + tr - bl - 2.0 * b - br;
                float edge = smoothstep(0.18, 0.42, sqrt(gx * gx + gy * gy));
                float L = Luma(c);
                // Flat, saturated colours.
                half3 sat = lerp(L.xxx, c, 1.45);
                half3 flat = floor(saturate(sat) * 5.0 + 0.4) / 5.0;
                float cell = max(3.0, h / 160.0);
                float dots = Dots(px, cell, 1.0 - smoothstep(0.1, 0.55, L)) * (1.0 - smoothstep(0.45, 0.6, L));
                half3 o = lerp(flat, flat * 0.35, dots);
                o = lerp(o, half3(0.02, 0.02, 0.03), edge);
                return o;
            }

            half3 Vhs(float2 uv, float2 px, float h)
            {
                float t = _Time.y;
                float row = floor(uv.y * 240.0);
                // Lines that wobble sideways and a tracking band that rolls.
                float jitter = (Hash(float2(row, floor(t * 24.0))) - 0.5) * 0.0025;
                float band = frac(t * 0.07);
                float inBand = 1.0 - smoothstep(0.0, 0.035, abs(uv.y - band));
                jitter += inBand * (Hash(float2(row, t)) - 0.5) * 0.03;
                float2 u = float2(uv.x + jitter, uv.y);
                half3 c;
                c.r = Src(u + float2(0.0035, 0.0)).r;
                c.g = Src(u).g;
                c.b = Src(u - float2(0.0035, 0.0)).b;
                // Soft tape colours: lifted blacks, purple-pink tint.
                c = lerp(c, c * half3(1.05, 0.9, 1.1) + half3(0.04, 0.02, 0.06), 0.8);
                float sl = 0.82 + 0.18 * sin(uv.y * h * 3.14159);
                c *= sl;
                c += (Hash(px + frac(t) * 100.0) - 0.5) * 0.09;
                c += inBand * 0.12 * Hash(float2(px.x, floor(t * 30.0)));
                float2 v = uv - 0.5;
                c *= saturate(1.1 - dot(v, v) * 1.3);
                return c;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.texcoord;
                half3 src = Src(uv);
                if (_Mode < 0.5 || _Amount <= 0.001) return half4(src, 1.0);
                float2 size = _ScreenParams.xy;
                float2 px = uv * size;
                half3 o;
                if (_Mode < 1.5) o = Horror(uv, px, size.y);
                else if (_Mode < 2.5) o = Comic(uv, px, 1.0 / size, size.y);
                else o = Vhs(uv, px, size.y);
                return half4(lerp(src, o, _Amount), 1.0);
            }
            ENDHLSL
        }
    }
}
