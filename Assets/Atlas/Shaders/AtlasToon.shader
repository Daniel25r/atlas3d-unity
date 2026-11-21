// ATLAS 3D - «Estilo dibujo» (anime / cartoon): flat light and shadow in
// bands (cel shading) with a soft rim, the scene's lights and shadows, and
// a dark outline drawn around the shape (inverted hull, same width on
// screen at any distance).
Shader "Atlas/Toon"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shade", Color) = (0.62, 0.58, 0.75, 1)
        _Steps ("Bands", Float) = 2
        _Rim ("Rim", Float) = 0.25
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        _OutlineWidth ("Outline width", Float) = 1
        _OutlineColor ("Outline color", Color) = (0.05, 0.04, 0.06, 1)
        _Cutoff ("Cutoff", Float) = 0
        _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpScale ("Normal strength", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);
        SAMPLER(sampler_BumpMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _ShadeColor;
            half _Steps;
            half _Rim;
            half4 _EmissionColor;
            float _OutlineWidth;
            half4 _OutlineColor;
            half _Cutoff;
            half _BumpScale;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2; float fog : TEXCOORD3; float4 tangentWS : TEXCOORD4; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                // The relief (normal map) bends the light inside each band.
                o.tangentWS = float4(TransformObjectToWorldDir(v.tangentOS.xyz), v.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half Band(half x)
            {
                half steps = max(1.0, _Steps);
                // Hard steps with a thin soft edge (no jaggies).
                half s = x * steps;
                half f = floor(s);
                return saturate((f + smoothstep(0.46, 0.54, frac(s))) / steps);
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                clip(tex.a - _Cutoff);
                float3 n = normalize(i.normalWS);
                if (_BumpScale > 0.001 && dot(i.tangentWS.xyz, i.tangentWS.xyz) > 1e-6)
                {
                    half3 nt = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                    float3 t = normalize(i.tangentWS.xyz);
                    float3 b = cross(n, t) * i.tangentWS.w;
                    n = normalize(nt.x * t + nt.y * b + nt.z * n);
                }
                float4 sc = TransformWorldToShadowCoord(i.positionWS);
                Light main = GetMainLight(sc);
                half ndl = saturate(dot(n, main.direction)) * main.shadowAttenuation;
                half lit = Band(ndl);
                half3 shade = tex.rgb * _ShadeColor.rgb;
                half3 col = lerp(shade, tex.rgb, lit) * main.color;
                #ifdef _ADDITIONAL_LIGHTS
                uint count = GetAdditionalLightsCount();
                for (uint li = 0u; li < count; li++)
                {
                    Light l = GetAdditionalLight(li, i.positionWS);
                    half a = Band(saturate(dot(n, l.direction)) * l.distanceAttenuation * l.shadowAttenuation);
                    col += tex.rgb * l.color * a;
                }
                #endif
                // Ambient and a soft rim of light on the edges.
                col += tex.rgb * SampleSH(n) * 0.35;
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1.0 - saturate(dot(n, v)), 4.0) * _Rim * (0.4 + 0.6 * lit);
                col += rim * main.color;
                col += _EmissionColor.rgb;
                col = MixFog(col, i.fog);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ToonOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float fog : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float4 cs = TransformObjectToHClip(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                float2 nCS = mul((float3x3)UNITY_MATRIX_VP, nWS).xy;
                float l = length(nCS);
                nCS = l > 1e-5 ? nCS / l : float2(0, 0);
                // Same thickness on screen near or far (about _OutlineWidth px at 1080p).
                cs.xy += nCS * _OutlineWidth * 0.0019 * cs.w * float2(_ScreenParams.y / _ScreenParams.x, 1.0);
                o.positionCS = _OutlineWidth > 0.001 ? cs : float4(0, 0, -10, 1);
                o.fog = ComputeFogFactor(cs.z);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                return half4(MixFog(_OutlineColor.rgb, i.fog), 1.0);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
