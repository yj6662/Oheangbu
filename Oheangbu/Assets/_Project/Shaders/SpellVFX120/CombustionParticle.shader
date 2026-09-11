Shader "Oheangbu/VFX120/CombustionParticle"
{
    Properties
    {
        _BaseMap("Source",2D)="white"{}
        _Smoke("Smoke mode",Float)=0
        _Intensity("Radiance",Float)=1.6
        _AtlasColumns("Atlas columns",Float)=1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Source blend",Float)=5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Destination blend",Float)=1
    }
    SubShader
    {
        Tags {"RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline"}
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float _Smoke,_Intensity,_AtlasColumns;
            CBUFFER_END
            struct A{float4 positionOS:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
            struct V{float4 positionCS:SV_POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
            V vert(A a){V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.color=a.color;o.uv=a.uv;return o;}
            half4 frag(V i):SV_Target
            {
                half4 tex=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv);
                if(_Smoke>.5)
                {
                    float2 p=i.uv*2-1;
                    float edge=saturate(1-dot(p,p));
                    return half4(i.color.rgb,edge*edge*lerp(.25,1,tex.r)*i.color.a);
                }
                // Preserve the authored 8-frame fire atlas temperature variation.
                // Vertex tint cools each particle during its own finite lifetime.
                float2 cell=float2(frac(i.uv.x*_AtlasColumns),i.uv.y);
                float2 border=min(cell,1-cell);
                float feather=smoothstep(0,.12,border.x)*smoothstep(0,.12,border.y);
                return half4(tex.rgb*i.color.rgb*_Intensity,tex.a*i.color.a*feather);
            }
            ENDHLSL
        }
    }
}
