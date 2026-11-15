// ATLAS 3D - volumetric light: a glowing beam (spotlights) or halo (lamps)
// that adds light to what is behind it, brighter where the eye looks
// through more of it and fading towards the end of its reach. It also
// fades where it meets the scene's surfaces, so it never looks cut.
Shader "Atlas/VolumeBeam"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _Mode ("Mode (0 beam, 1 halo)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "AtlasVolume"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Intensity;
                half _Mode;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; float along : TEXCOORD2; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.along = i.uv.y;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float facing = abs(dot(normalize(i.normalWS), v));
                float k;
                if (_Mode > 0.5)
                {
                    k = pow(facing, 3.0) * 0.5;                    // halo: bright centre
                }
                else
                {
                    float fade = saturate(1.0 - i.along);
                    k = pow(facing, 1.6) * fade * fade * 0.28;    // beam: soft edges, fades with distance
                }
                return half4(_BaseColor.rgb * (_Intensity * k), 1.0);
            }
            ENDHLSL
        }
    }
}
