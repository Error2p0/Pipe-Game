// Room heatmap (01-architecture.md §Rendering): one RGFloat texture per floor at room-grid resolution. R holds the
// normalised value times the inside mask, G the mask, so bilinear filtering never blends in cells outside the rooms
// (value = R / G); a palette ramp texture colours it.
Shader "HeadLoss/RoomHeatmap"
{
    Properties
    {
        _Field ("Field (R value × mask, G mask)", 2D) = "black" {}
        _Ramp ("Palette ramp", 2D) = "white" {}
        _Alpha ("Opacity", Float) = 0.55
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "RoomHeatmap"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Field);
            SAMPLER(sampler_Field);
            TEXTURE2D(_Ramp);
            SAMPLER(sampler_Ramp);

            CBUFFER_START(UnityPerMaterial)
                float4 _Field_ST;
                float4 _Ramp_ST;
                half _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 f = SAMPLE_TEXTURE2D(_Field, sampler_Field, i.uv).rg;
                clip(f.g - 0.5);
                float t = saturate(f.r / max(f.g, 1e-4));
                half4 c = SAMPLE_TEXTURE2D(_Ramp, sampler_Ramp, float2(t, 0.5));
                c.a = _Alpha;
                return c;
            }
            ENDHLSL
        }
    }
}
