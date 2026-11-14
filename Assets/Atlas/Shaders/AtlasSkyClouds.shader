// ATLAS 3D - sky with soft clouds (procedural, no textures): a gradient from
// the horizon to the zenith, a sun disc and fluffy clouds made of layered
// noise. Original shader for ATLAS.
Shader "Atlas/SkyClouds"
{
    Properties
    {
        _TopColor ("Zenith", Color) = (0.16, 0.42, 0.88, 1)
        _HorizonColor ("Horizon", Color) = (0.72, 0.86, 1, 1)
        _GroundColor ("Ground", Color) = (0.42, 0.48, 0.55, 1)
        _CloudColor ("Clouds", Color) = (1, 1, 1, 1)
        _CloudShade ("Cloud shadow", Color) = (0.72, 0.78, 0.88, 1)
        _Cover ("Cloud cover", Range(0, 1)) = 0.5
        _SunDir ("Sun direction", Vector) = (0.3, 0.6, -0.7, 0)
        _SunColor ("Sun", Color) = (1, 0.95, 0.85, 1)
        _Exposure ("Exposure", Range(0, 3)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor, _HorizonColor, _GroundColor, _CloudColor, _CloudShade, _SunColor;
                half _Cover, _Exposure;
                float4 _SunDir;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.dir = i.positionOS.xyz;
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float v = 0, a = 0.5;
                for (int k = 0; k < 5; k++) { v += a * Noise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return v;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 s = normalize(_SunDir.xyz);
                half3 col;
                if (d.y >= 0)
                {
                    half t = pow(saturate(d.y), 0.45);
                    col = lerp(_HorizonColor.rgb, _TopColor.rgb, t);
                    // Clouds on a flat layer above, fading to the horizon
                    float2 uv = d.xz / (d.y + 0.08) * 1.6;
                    float n = Fbm(uv);
                    float c = smoothstep(1 - _Cover, 1 - _Cover + 0.28, n);
                    float shade = Fbm(uv + s.xz * 0.35);
                    half3 cloud = lerp(_CloudShade.rgb, _CloudColor.rgb, saturate(1.15 - shade * 0.9));
                    col = lerp(col, cloud, c * saturate(d.y * 6));
                }
                else
                {
                    col = lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-d.y * 4));
                }
                // Sun disc and glow
                half sd = saturate(dot(d, s));
                col += _SunColor.rgb * (pow(sd, 900) * 6 + pow(sd, 40) * 0.25);
                return half4(col * _Exposure, 1);
            }
            ENDHLSL
        }
    }
}
