Shader "Oheangbu/VisualCorridorPath"
{
    Properties
    {
        [MainTexture] _BaseMap("Owned dirt surface",2D)="white"{}
        [MainColor] _BaseColor("Muted earth",Color)=(.62,.57,.48,.78)
    }
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Geometry+30" "RenderType"="Transparent"}
        Pass
        {
            Tags{"LightMode"="UniversalForward"}
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1,-1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;half4 _BaseColor;
            CBUFFER_END
            struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;half4 color:COLOR;};
            struct V {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;half3 normal:TEXCOORD1;float2 uv:TEXCOORD2;half alpha:TEXCOORD3;};
            V Vert(A a){V v;v.world=TransformObjectToWorld(a.positionOS.xyz);v.positionCS=TransformWorldToHClip(v.world);v.normal=TransformObjectToWorldNormal(a.normalOS);v.uv=a.uv;v.alpha=a.color.a;return v;}
            half4 Frag(V v):SV_Target
            {
                half4 tex=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,v.uv);
                half grey=dot(tex.rgb,half3(.2126,.7152,.0722));
                half3 albedo=lerp(grey.xxx,tex.rgb,.26)*_BaseColor.rgb;
                Light sun=GetMainLight(TransformWorldToShadowCoord(v.world));
                half lighting=.55+saturate(dot(normalize(v.normal),sun.direction))*.5*sun.shadowAttenuation;
                half opacity=v.alpha*_BaseColor.a*lerp(.72,1,saturate(grey*1.8));
                return half4(albedo*lighting,opacity);
            }
            ENDHLSL
        }
    }
}
