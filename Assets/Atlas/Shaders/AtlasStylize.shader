// ATLAS 3D - full-screen looks for the quick ambiences (runs after URP's
// post-processing, through the renderer's Full Screen Pass):
//   _Mode 0: nothing (the picture as it is)
//   _Mode 1: «Pesadilla retro» - printed halftone dots, few colours, blue
//            shadows and yellow light, colour fringes (horror posters)
//   _Mode 2: «Cómic tinta» - ink outlines, flat colours, dots in shadows
//   _Mode 3: «VHS maldito» - wobbling lines, colour bleed, scanlines, noise
//   _Mode 4: «Spider-Verse» - comic print: colours a little out of register,
//            halftone dots in the shadows, ink outlines
//   _Mode 5: «Anime» - light in flat bands, bright colours, thin ink
//   _Mode 6: «Póster» - few flat tones per colour
//   _Mode 7: «Pintura» - brush strokes (Kuwahara filter) on canvas
//   _Mode 8: «Boceto» - pencil hatching on paper
//   _Mode 9: «Contorno de tinta» - the picture as it is, with ink outlines
// On top of any of them (each with its own amount): pixelated, wide lens,
// dirty lens, floating dust and vintage film.
Shader "Atlas/Stylize"
{
    Properties
    {
        _Mode ("Mode", Float) = 0
        _Amount ("Amount", Range(0, 1)) = 1
        _Pixel ("Pixel size", Float) = 0
        _Wide ("Wide lens", Range(0, 1)) = 0
        _Lens ("Dirty lens", Range(0, 1)) = 0
        _Dust ("Dust", Range(0, 1)) = 0
        _Vintage ("Vintage", Range(0, 1)) = 0
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
            float _Pixel, _Wide, _Lens, _Dust, _Vintage;

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

            float Edge(float2 uv, float2 texel)
            {
                float tl = Luma(Src(uv + texel * float2(-1, 1))), t = Luma(Src(uv + texel * float2(0, 1))), tr = Luma(Src(uv + texel * float2(1, 1)));
                float l = Luma(Src(uv + texel * float2(-1, 0))), r = Luma(Src(uv + texel * float2(1, 0)));
                float bl = Luma(Src(uv + texel * float2(-1, -1))), b = Luma(Src(uv + texel * float2(0, -1))), br = Luma(Src(uv + texel * float2(1, -1)));
                float gx = -tl - 2.0 * l - bl + tr + 2.0 * r + br;
                float gy = tl + 2.0 * t + tr - bl - 2.0 * b - br;
                return sqrt(gx * gx + gy * gy);
            }

            half3 SpiderVerse(float2 uv, float2 px, float2 texel, float h)
            {
                // Print out of register: red and blue plates slightly shifted.
                float2 off = texel * max(1.5, h / 540.0) * 1.6;
                half3 c = half3(Src(uv + off).r, Src(uv).g, Src(uv - off).b);
                float L = Luma(c);
                half3 sat = lerp(L.xxx, c, 1.35);
                half3 flat = floor(saturate(sat) * 6.0 + 0.45) / 6.0;
                // Halftone dots of a darker shade of the same colour.
                float cell = max(3.0, h / 150.0);
                float dots = Dots(px, cell, 1.0 - smoothstep(0.08, 0.6, L)) * (1.0 - smoothstep(0.5, 0.7, L));
                half3 o = lerp(flat, flat * 0.3, dots);
                // Thin diagonal lines in the deep shadows.
                float lines = step(0.5, frac((px.x + px.y) / max(3.0, h / 200.0)));
                o = lerp(o, o * 0.55, lines * (1.0 - smoothstep(0.05, 0.22, L)));
                float e = smoothstep(0.22, 0.5, Edge(uv, texel * max(1.0, h / 900.0)));
                return lerp(o, half3(0.03, 0.02, 0.05), e);
            }

            half3 Anime(float2 uv, float2 texel, float h)
            {
                half3 c = Src(uv);
                float L = Luma(c);
                // Light in three flat bands, keeping each colour's hue.
                float q = L < 0.22 ? 0.16 : L < 0.55 ? 0.42 : L < 0.86 ? 0.74 : 0.98;
                half3 hue = c / max(L, 0.02);
                half3 o = saturate(hue * q);
                o = lerp(Luma(o).xxx, o, 1.25);
                float e = smoothstep(0.25, 0.55, Edge(uv, texel * max(1.0, h / 1000.0)));
                return lerp(o, half3(0.08, 0.06, 0.1), e * 0.9);
            }

            half3 Poster(float2 uv)
            {
                half3 c = Src(uv);
                float L = Luma(c);
                half3 sat = lerp(L.xxx, c, 1.3);
                return floor(saturate(sat) * 4.0 + 0.5) / 4.0;
            }

            // Kuwahara: each pixel takes the calmest of four corners around
            // it, which flattens areas into brush strokes but keeps edges.
            half3 Paint(float2 uv, float2 px, float2 texel, float h)
            {
                float2 st = texel * max(1.0, h / 540.0) * 1.5;
                half3 m[4]; float v[4];
                [unroll] for (int q = 0; q < 4; q++)
                {
                    float2 dir = float2(q == 1 || q == 3 ? 1.0 : -1.0, q >= 2 ? 1.0 : -1.0);
                    half3 sum = 0; half3 sq = 0;
                    [unroll] for (int j = 0; j < 3; j++)
                        [unroll] for (int i = 0; i < 3; i++)
                        {
                            half3 s = Src(uv + float2(i, j) * dir * st);
                            sum += s; sq += s * s;
                        }
                    m[q] = sum / 9.0;
                    half3 var3 = abs(sq / 9.0 - m[q] * m[q]);
                    v[q] = var3.r + var3.g + var3.b;
                }
                half3 o = m[0]; float best = v[0];
                [unroll] for (int k = 1; k < 4; k++) if (v[k] < best) { best = v[k]; o = m[k]; }
                // Canvas weave.
                float weave = 0.96 + 0.04 * sin(px.x * 1.7) * sin(px.y * 1.7);
                return o * weave;
            }

            half3 Sketch(float2 uv, float2 px, float2 texel, float h)
            {
                float L = Luma(Src(uv));
                float s = max(1.0, h / 700.0);
                // Hatching: more line layers the darker it is.
                float a = frac((px.x + px.y) / (5.0 * s));
                float b = frac((px.x - px.y) / (5.0 * s));
                float c = frac(px.x / (4.0 * s));
                float ink = 0.0;
                if (L < 0.75) ink = max(ink, 1.0 - smoothstep(0.0, 0.25, a));
                if (L < 0.5) ink = max(ink, 1.0 - smoothstep(0.0, 0.25, b));
                if (L < 0.28) ink = max(ink, 1.0 - smoothstep(0.0, 0.3, c));
                float e = smoothstep(0.15, 0.45, Edge(uv, texel * s));
                ink = max(ink * 0.55, e);
                half3 paper = half3(0.96, 0.94, 0.89) * (0.97 + 0.03 * Hash(floor(px / 2.0)));
                return lerp(paper, half3(0.18, 0.17, 0.2), ink);
            }

            half3 InkLines(float2 uv, float2 texel, float h)
            {
                float e = smoothstep(0.2, 0.5, Edge(uv, texel * max(1.0, h / 1000.0)));
                return lerp(Src(uv), half3(0.02, 0.02, 0.03), e);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            // Smudges on the lens that light up where the picture is bright.
            half3 DirtyLens(float2 uv, half3 c, float amt)
            {
                float smudge = Noise(uv * 4.0 + 3.1) * 0.6 + Noise(uv * 11.0 + 7.7) * 0.4;
                smudge = smoothstep(0.45, 0.9, smudge);
                half3 glow = 0;
                [unroll] for (int k = 0; k < 6; k++)
                {
                    float an = k * 1.0472;
                    glow += max(Src(uv + float2(cos(an), sin(an)) * 0.08) - 0.6, 0.0);
                }
                return c + glow * smudge * amt * half3(1.0, 0.92, 0.8) * 0.9;
            }

            // Specks of dust floating slowly through the light.
            half3 Dust(float2 uv, float2 px, half3 c, float amt, float h)
            {
                float t = _Time.y;
                float d = 0.0;
                [unroll] for (int layer = 0; layer < 2; layer++)
                {
                    float sc = h / (layer == 0 ? 40.0 : 22.0);
                    float2 p = px / sc + float2(t * (0.05 + layer * 0.04), -t * (0.08 + layer * 0.03));
                    float2 cell = floor(p), f = frac(p);
                    float r = Hash(cell + layer * 17.0);
                    if (r > 0.82)
                    {
                        float2 pos = float2(Hash(cell + 3.3), Hash(cell + 9.1)) * 0.6 + 0.2 + 0.08 * sin(t * (0.5 + r) + r * 20.0);
                        float rad = 0.04 + 0.05 * Hash(cell + 5.5);
                        d += (1.0 - smoothstep(rad * 0.3, rad, length(f - pos))) * (0.5 + 0.5 * sin(t * 2.0 + r * 40.0));
                    }
                }
                return c + d * amt * (0.35 + Luma(c)) * half3(1.0, 0.95, 0.85) * 0.7;
            }

            half3 Vintage(float2 uv, float2 px, half3 c, float amt)
            {
                float t = _Time.y;
                float L = Luma(c);
                half3 sepia = half3(L * 1.07 + 0.05, L * 0.9 + 0.03, L * 0.68);
                half3 o = lerp(c, sepia, 0.85);
                o *= 0.94 + 0.06 * Hash(float2(floor(t * 18.0), 1.0));           // flicker
                float col = floor(uv.x * 400.0);
                float fr = floor(t * 12.0);
                float scratch = step(0.996, Hash(float2(col, fr))) * step(0.4, Hash(float2(fr, 3.0)));
                o = lerp(o, half3(0.9, 0.86, 0.75), scratch * 0.6);
                o += (Hash(px * 0.5 + frac(t) * 50.0) - 0.5) * 0.08;               // film grain
                float2 v = uv - 0.5;
                o *= saturate(1.2 - dot(v, v) * 2.2);
                return lerp(c, o, amt);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.texcoord;
                float2 size = _ScreenParams.xy;
                // Wide lens: the middle bulges, the borders bend.
                if (_Wide > 0.001)
                {
                    float2 d = uv - 0.5;
                    float r2 = dot(d * float2(size.x / size.y, 1.0), d * float2(size.x / size.y, 1.0));
                    float k = _Wide * 0.35;
                    uv = 0.5 + d * (1.0 + k * r2) / (1.0 + k * 0.6);
                }
                // Pixelated: big square pixels (size at a 1080 px tall picture).
                if (_Pixel > 0.5)
                {
                    float p = max(1.0, _Pixel * size.y / 1080.0);
                    uv = (floor(uv * size / p) + 0.5) * p / size;
                }
                half3 src = Src(uv);
                bool plain = _Mode < 0.5 || _Amount <= 0.001;
                if (plain && _Lens <= 0.001 && _Dust <= 0.001 && _Vintage <= 0.001) return half4(src, 1.0);
                float2 px = uv * size;
                float2 texel = 1.0 / size;
                half3 o = src;
                if (!plain)
                {
                    if (_Mode < 1.5) o = Horror(uv, px, size.y);
                    else if (_Mode < 2.5) o = Comic(uv, px, texel, size.y);
                    else if (_Mode < 3.5) o = Vhs(uv, px, size.y);
                    else if (_Mode < 4.5) o = SpiderVerse(uv, px, texel, size.y);
                    else if (_Mode < 5.5) o = Anime(uv, texel, size.y);
                    else if (_Mode < 6.5) o = Poster(uv);
                    else if (_Mode < 7.5) o = Paint(uv, px, texel, size.y);
                    else if (_Mode < 8.5) o = Sketch(uv, px, texel, size.y);
                    else o = InkLines(uv, texel, size.y);
                    o = lerp(src, o, _Amount);
                }
                if (_Vintage > 0.001) o = Vintage(uv, px, o, _Vintage);
                if (_Lens > 0.001) o = DirtyLens(uv, o, _Lens);
                if (_Dust > 0.001) o = Dust(uv, px, o, _Dust, size.y);
                return half4(o, 1.0);
            }
            ENDHLSL
        }
    }
}
