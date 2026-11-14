// ATLAS 3D - «Cristal Aero»: glossy coloured glass in the Frutiger Aero
// style. Bright rim (fresnel), the sky and the scene reflected on it (from
// the reflection probe), a sharp highlight of the sun, and the light that
// goes through the glass glowing on the far side (the bright bottom of a
// glass marble lit from above). Original shader for ATLAS.
Shader "Atlas/AeroGlass"
{
    Properties
    {
        _BaseColor ("Color (alpha = body opacity)", Color) = (0.25, 0.55, 1, 0.35)
        _Glow ("Inner glow", Range(0, 2)) = 1
        _Smoothness ("Gloss", Range(0, 1)) = 0.95
        _Metallic ("(unused)", Range(0, 1)) = 0
        _EmissionColor ("(unused)", Color) = (0, 0, 0, 0)
        _BaseMap ("(unused)", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "AeroGlass"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Glow;
                half _Smoothness;
                half _Metallic;
                half4 _EmissionColor;
                float4 _BaseMap_ST;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }

            half3 Env(float3 dir, half mip)
            {
                half4 c = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, dir, mip);
                half k = unity_SpecCube0_HDR.x > 0 ? unity_SpecCube0_HDR.x : 1;
                return c.rgb * k;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 V = normalize(GetCameraPositionWS() - i.positionWS);
                float3 L = normalize(_MainLightPosition.xyz);
                half3 lightCol = _MainLightColor.rgb;
                half nv = saturate(dot(N, V));
                half fres = pow(1 - nv, 3);
                half3 tint = _BaseColor.rgb;
                // Sky and scene in the glass
                half rough = 1 - _Smoothness;
                half3 refl = Env(reflect(-V, N), rough * 6);
                half3 amb = Env(N, 6);
                // Sun highlight (sharp) and soft diffuse on the body
                float3 H = normalize(L + V);
                half spec = pow(saturate(dot(N, H)), lerp(16, 512, _Smoothness)) * (1 + _Smoothness * 6);
                half diff = saturate(dot(N, L) * 0.5 + 0.5);
                // Light through the glass, gathered on the far side
                half caustic = pow(saturate(dot(N, -L) * 0.6 + 0.4), 4) * (1 - fres) * _Glow;
                half3 body = tint * (amb * 0.45 + lightCol * diff * 0.35) + tint * lightCol * caustic * 1.6;
                half3 col = body * _BaseColor.a + refl * (0.12 + 0.88 * fres) + lightCol * spec;
                half a = saturate(_BaseColor.a + fres * 0.6 + spec * 0.5 + caustic * 0.35);
                // Premultiplied: the body is weighted by its opacity, the
                // reflections and highlights are added on top.
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
