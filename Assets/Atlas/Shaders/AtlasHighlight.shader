// ATLAS 3D - selection highlight, like the Godot version: a faint glow on
// the selected object (stronger at its edges) plus an outline of a few
// pixels around it, in the selection colour.
Shader "Atlas/Highlight"
{
    Properties
    {
        _Color ("Color", Color) = (0.94, 0.65, 0.25, 1)
        _Tint ("Tint", Range(0, 1)) = 0.10
        _Width ("Outline width (px)", Float) = 3
        _OutlineAlpha ("Outline opacity", Range(0, 1)) = 0.95
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Tint;
            float _Width;
            half _OutlineAlpha;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS : TEXCOORD0;
            float3 viewWS : TEXCOORD1;
        };
        ENDHLSL

        Pass
        {
            Name "AtlasTint"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetCameraPositionWS() - ws;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float facing = saturate(abs(dot(normalize(i.normalWS), normalize(i.viewWS))));
                float rim = 1.0 - facing;
                half a = saturate(_Tint + rim * rim * 0.6 * saturate(_Tint * 6.0));
                return half4(_Color.rgb, a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "AtlasOutline"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                float3 nws = TransformObjectToWorldNormal(i.normalOS);
                float4 cs = TransformWorldToHClip(ws);
                float2 ncs = mul((float3x3)GetWorldToHClipMatrix(), nws).xy;
                float len = max(length(ncs), 1e-5);
                cs.xy += (ncs / len) * _Width * 2.0 / _ScreenParams.xy * cs.w;
                o.positionCS = cs;
                o.normalWS = nws;
                o.viewWS = float3(0, 0, 1);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(_Color.rgb, _OutlineAlpha);
            }
            ENDHLSL
        }
    }
}
