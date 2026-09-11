Shader "Oheangbu/VFX120/BotanicalSurface"
{
    Properties
    {
        _BaseMap("Source colour and leaf cutout",2D)="white"{}
        _BumpMap("Source normal",2D)="bump"{}
        _BumpScale("Normal strength",Range(0,2))=.7
        _BaseColor("Base colour",Color)=(1,1,1,1)
        _Tint("Pigment tint",Color)=(.38,.48,.39,1)
        _TintStrength("Pigment strength",Range(0,1))=.18
        _Saturation("Saturation",Range(0,1))=.65
        _AlphaClip("Leaf cutout",Float)=0
        _Cutoff("Leaf cutoff",Range(0,1))=.4
        _Visibility("Visibility",Range(0,1))=1
        _Formation("Bridge assembly from near edge",Range(0,1))=1
        _GroundY("Ground clipping height",Float)=-10000
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _BaseColor, _Tint;
                float _BumpScale, _TintStrength, _Saturation, _AlphaClip, _Cutoff, _Visibility, _GroundY, _Formation;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float4 tangentWS:TEXCOORD2; float2 uv:TEXCOORD3; float fog:TEXCOORD4; float localZ:TEXCOORD5; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n=GetVertexNormalInputs(v.normalOS,v.tangentOS);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS; o.normalWS=n.normalWS;
                o.tangentWS=float4(n.tangentWS,v.tangentOS.w*GetOddNegativeScale());
                o.uv=TRANSFORM_TEX(v.uv,_BaseMap); o.fog=ComputeFogFactor(p.positionCS.z); o.localZ=v.positionOS.z; return o;
            }
            half4 Frag(Varyings i, FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
            {
                clip(i.positionWS.y-_GroundY);
                clip(_Visibility-.001);
                // A fixed spatial dissolve avoids translucent bark, sorting layers and glow.
                float tooth=frac(sin(dot(floor(i.positionWS*240),float3(12.9898,78.233,39.425)))*43758.5453);
                clip(_Visibility-tooth);
                if(_Formation<.9999)
                {
                    clip(_Formation-.001);
                    // Only the normalized bridge opts in. Keep original stone UVs
                    // and rigid scale as its construction advances along +Z.
                    float front=_Formation*1.12-.12;
                    float assembled=saturate((front-(i.localZ+.5))/.12+1);
                    clip(assembled-tooth-.0001);
                }
                half4 source=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv);
                if(_AlphaClip>.5) clip(source.a-_Cutoff);
                half3 albedo=source.rgb*_BaseColor.rgb;
                half luminance=dot(albedo,half3(.2126,.7152,.0722));
                albedo=lerp(luminance.xxx,albedo,_Saturation);
                albedo=lerp(albedo,albedo*_Tint.rgb*2,_TintStrength);
                half3 n=normalize(i.normalWS);
                half3 tangent=normalize(i.tangentWS.xyz);
                half3 bitangent=cross(n,tangent)*i.tangentWS.w;
                half3 tangentNormal=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,i.uv),_BumpScale);
                n=normalize(mul(tangentNormal,half3x3(tangent,bitangent,n)))*IS_FRONT_VFACE(face,1,-1);
                Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 ambient=max(SampleSH(n),half3(.22,.22,.20));
                half direct=saturate(dot(n,light.direction))*(.35+.65*light.shadowAttenuation);
                half3 colour=albedo*(ambient+light.color*direct*.85);
                return half4(MixFog(colour,i.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Off ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _BaseColor, _Tint;
                float _BumpScale, _TintStrength, _Saturation, _AlphaClip, _Cutoff, _Visibility, _GroundY, _Formation;
            CBUFFER_END
            float3 _LightDirection, _LightPosition;
            struct ShadowAttributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct ShadowVaryings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float2 uv:TEXCOORD1; float localZ:TEXCOORD2; };
            ShadowVaryings ShadowVert(ShadowAttributes v)
            {
                ShadowVaryings o;
                o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                float3 n=TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 direction=normalize(_LightPosition-o.positionWS);
                #else
                    float3 direction=_LightDirection;
                #endif
                o.positionCS=ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(o.positionWS,n,direction)));
                o.uv=TRANSFORM_TEX(v.uv,_BaseMap); o.localZ=v.positionOS.z;
                return o;
            }
            half4 ShadowFrag(ShadowVaryings i):SV_Target
            {
                // Use the same ground, construction and visibility masks as colour;
                // unfinished or expired stone must not cast a complete shadow.
                clip(i.positionWS.y-_GroundY); clip(_Visibility-.001);
                float tooth=frac(sin(dot(floor(i.positionWS*240),float3(12.9898,78.233,39.425)))*43758.5453);
                clip(_Visibility-tooth);
                if(_AlphaClip>.5) clip(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a-_Cutoff);
                if(_Formation<.9999)
                {
                    clip(_Formation-.001);
                    float front=_Formation*1.12-.12;
                    clip(saturate((front-(i.localZ+.5))/.12+1)-tooth-.0001);
                }
                return 0;
            }
            ENDHLSL
        }
    }
}
