// ATLAS 3D - water (sea, lake, pool): waves moved by the timeline's time
// (_WaveTime, so renders are exact frame by frame), the sky and scene
// reflected (reflection probe) more at grazing angles, the sun's glint,
// and a deep / shallow colour.
Shader "Atlas/Water"
{
    Properties
    {
        _DeepColor ("Deep", Color) = (0.02, 0.18, 0.32, 1)
        _ShallowColor ("Shallow", Color) = (0.1, 0.55, 0.6, 1)
        _WaveHeight ("Wave height", Float) = 0.25
        _WaveLength ("Wave length", Float) = 4
        _WaveSpeed ("Wave speed", Float) = 1
        _WaveTime ("Time", Float) = 0
        _Clarity ("Clarity", Range(0, 1)) = 0.75
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "AtlasWater"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _ShallowColor;
                float _WaveHeight;
                float _WaveLength;
                float _WaveSpeed;
                float _WaveTime;
                half _Clarity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float crest : TEXCOORD2; float fog : TEXCOORD3; };

            // Three travelling waves (height and slope).
            float3 Waves(float2 p, out float h)
            {
                float k = 6.2831853 / max(0.2, _WaveLength);
                float t = _WaveTime * _WaveSpeed;
                float2 d1 = normalize(float2(1, 0.3)), d2 = normalize(float2(-0.4, 1)), d3 = normalize(float2(0.7, -0.8));
                float a1 = dot(d1, p) * k + t * 1.3, a2 = dot(d2, p) * k * 1.7 + t * 1.7, a3 = dot(d3, p) * k * 3.1 + t * 2.3;
                h = sin(a1) * 0.6 + sin(a2) * 0.3 + sin(a3) * 0.1;
                float2 g = d1 * cos(a1) * 0.6 * k + d2 * cos(a2) * 0.3 * k * 1.7 + d3 * cos(a3) * 0.1 * k * 3.1;
                h *= _WaveHeight;
                g *= _WaveHeight;
                return normalize(float3(-g.x, 1, -g.y));
            }

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                float h;
                float3 n = Waves(ws.xz, h);
                ws.y += h;
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = n;
                o.crest = saturate(h / max(0.001, _WaveHeight) * 0.5 + 0.5);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                if (!front) n = -n;
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float fres = pow(1.0 - saturate(dot(n, v)), 4.0) * 0.85 + 0.08;
                half3 refl = GlossyEnvironmentReflection(reflect(-v, n), 0.05, 1.0);
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 body = lerp(_DeepColor.rgb, _ShallowColor.rgb, i.crest * 0.6) * (0.35 + 0.65 * saturate(dot(n, sun.direction))) * sun.color * sun.shadowAttenuation;
                body += _DeepColor.rgb * 0.25;
                float3 hv = normalize(sun.direction + v);
                half spec = pow(saturate(dot(n, hv)), 220.0) * 4.0 * sun.shadowAttenuation;
                half3 c = lerp(body, refl, fres) + sun.color * spec;
                c = MixFog(c, i.fog);
                half alpha = lerp(1.0 - _Clarity * 0.6, 1.0, fres);
                return half4(c, alpha);
            }
            ENDHLSL
        }
    }
}
