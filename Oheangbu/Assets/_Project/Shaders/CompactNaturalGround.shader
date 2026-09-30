// Compact-scene derivative. Provider textures and the original ink terrain stay untouched.
Shader "Oheangbu/CompactNaturalGround"
{
    Properties
    {
        [ToggleUI] _CIEnabled("Compact ink landscape",Float)=0
        [ToggleUI] _CIDarkNear("Dark foreground ink revision",Float)=0
        _CIMountainTones("Mountain ink tone interval",Vector)=(.06,.18,0,0)
        _CIMountainSlope("Mountain slope degrees",Vector)=(12,37,0,0)
        [ToggleUI] _CIBroadBrush("Broad mountain ink coats",Float)=0
        _CIBroadBrushScale("Broad stroke width/length metres",Vector)=(80,240,420,1000)
        _CIBroadBrushCoverage("Coat opacity/edge/second/warp",Vector)=(.90,.18,.76,.20)
        _CIBroadBrushTones("Broad dark/soft coat tones",Vector)=(.015,.07,0,0)
        _CIBroadBrushBands("Mountain band thresholds/softness/amount",Vector)=(.33,.66,.12,.8)
        [ToggleUI] _CIAtmosphereOverride("Independent compact atmosphere",Float)=0
        [ToggleUI] _CIPainterly("Painterly compact revision",Float)=0
        _CIMidAirRange("Middle atmosphere range",Vector)=(500,1500,0,0)
        _CIFarAirRange("Far atmosphere range",Vector)=(1500,3400,0,0)
        _CIAirStrengths("Middle/far atmosphere strengths",Vector)=(.18,.64,0,0)
        _CIStrokeSpacingWidth("Stroke spacing/width metres",Vector)=(20,40,2,7)
        _CIStrokeFadeStrength("Stroke fade range/ink strength",Vector)=(200,1400,.02,0)
        _CIPigmentContrast("Mountain/ground pigment contrast",Vector)=(.03,.65,0,0)
        _CIFoliagePainterly("Foliage transition/mip/contrast",Vector)=(30,180,1.5,.3)
        _CIInk("Compact ink",Color)=(.165,.149,.133,1)
        _CIPaper("Compact paper",Color)=(.969,.945,.894,1)
        _CIAir("Compact horizon atmosphere",Color)=(.9,.88,.83,1)
        _CIDetailRange("Near detail range",Vector)=(30,150,0,0)
        _CIRockRange("Rock detail range",Vector)=(150,350,0,0)
        _CIMountainRange("Mountain form range",Vector)=(350,900,0,0)
        _CIAirRange("Far atmosphere range",Vector)=(900,2200,0,0)
        _CITones("Ground/path tone intervals",Vector)=(.27,.56,.24,.48)
        _CIDetailContrast("Near texture contrast",Float)=.22
        _CINormalStrength("Near normal strength",Float)=.5

        _GrassMap("Grass surface",2D)="white"{}
        [Normal] _GrassNormal("Grass normal",2D)="bump"{}
        _DirtMap("Soil surface",2D)="white"{}
        [Normal] _DirtNormal("Soil normal",2D)="bump"{}
        _RockMap("Rock surface",2D)="white"{}
        [Normal] _RockNormal("Rock normal",2D)="bump"{}
        _GroundPathMask("Authored path mask",2D)="black"{}
        _RealmPigment("Existing realm pigment",2D)="white"{}
        _GroundPathRect("World mask origin / inverse size",Vector)=(0,0,1,1)
        _GroundPath("Use path mask",Float)=0
        _SourceUV("Use source soil UV",Float)=0
        _SourceUVTransform("Source soil UV scale and offset",Vector)=(1,1,0,0)
        _GroundKind("0 terrain, 1 soil ribbon",Float)=0
        _EdgeFade("Use vertex alpha edge",Float)=0
        _Tiling("Grass, soil, rock repeats per metre",Vector)=(.5,.33,.25,0)
        _BumpScale("Surface relief",Range(0,2))=.85
        _Brightness("Material brightness",Range(.25,2))=1.1
        _Saturation("Material saturation",Range(0,1))=.8
        _AmbientLevel("Diffuse floor",Range(0,1))=.3
        _LightResponse("Direct response",Range(0,2))=.7
        _RealmTintStrength("Region material tint",Range(0,1))=.15
        _WashStart("Existing distance wash start",Float)=800
        _WashEnd("Existing distance wash end",Float)=2400
        _WashStrength("Existing distance wash strength",Range(0,1))=.8
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        Cull Back ZWrite On
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        TEXTURE2D(_GrassMap);SAMPLER(sampler_GrassMap);
        TEXTURE2D(_GrassNormal);SAMPLER(sampler_GrassNormal);
        TEXTURE2D(_DirtMap);SAMPLER(sampler_DirtMap);
        TEXTURE2D(_DirtNormal);SAMPLER(sampler_DirtNormal);
        TEXTURE2D(_RockMap);SAMPLER(sampler_RockMap);
        TEXTURE2D(_RockNormal);SAMPLER(sampler_RockNormal);
        TEXTURE2D(_GroundPathMask);SAMPLER(sampler_GroundPathMask);
        TEXTURE2D(_RealmPigment);SAMPLER(sampler_RealmPigment);
        CBUFFER_START(UnityPerMaterial)
        float4 _GroundPathRect,_Tiling,_SourceUVTransform;float _SourceUV;
        float _GroundPath,_GroundKind,_EdgeFade,_BumpScale,_Brightness,_Saturation;
        float _AmbientLevel,_LightResponse,_RealmTintStrength,_WashStart,_WashEnd,_WashStrength;
        
            float _CIEnabled,_CIDarkNear,_CIDetailContrast,_CINormalStrength;
            float4 _CIInk,_CIPaper,_CIAir,_CIDetailRange,_CIRockRange,_CIMountainRange,_CIAirRange,_CITones;
            float4 _CIMountainTones,_CIMountainSlope;
            float _CIBroadBrush;
            float4 _CIBroadBrushScale,_CIBroadBrushCoverage,_CIBroadBrushTones,_CIBroadBrushBands;
            float _CIAtmosphereOverride,_CIPainterly;
            float4 _CIMidAirRange,_CIFarAirRange,_CIAirStrengths,_CIStrokeSpacingWidth,_CIStrokeFadeStrength,_CIPigmentContrast,_CIFoliagePainterly;
        CBUFFER_END
        #include "CompactInkLandscape.hlsl"
        float4 _OhPaperColor;
        struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 color:COLOR;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
        struct V {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float alpha:TEXCOORD2;float2 uv:TEXCOORD3;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
        V Vert(A i)
        {
            V o=(V)0;UNITY_SETUP_INSTANCE_ID(i);UNITY_TRANSFER_INSTANCE_ID(i,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);
            o.normal=TransformObjectToWorldNormal(i.normalOS);o.alpha=i.color.a;o.uv=i.uv*_SourceUVTransform.xy+_SourceUVTransform.zw;return o;
        }
        void ClipEdge(V i)
        {
            if(_EdgeFade>.5)clip(i.alpha-frac(52.9829189*frac(dot(floor(i.positionCS.xy),float2(.06711056,.00583715))))-.0001);
        }
        float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
        float Noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(a),Hash(a+float2(1,0)),f.x),lerp(Hash(a+float2(0,1)),Hash(a+1),f.x),f.y);}
        float3 SoilNormal(float3 geometric,float3 tangentNormal)
        {
            float3 t=normalize(float3(geometric.y,-geometric.x,0));
            float3 b=normalize(cross(t,geometric));
            return normalize(t*tangentNormal.x+b*tangentNormal.y+geometric*tangentNormal.z);
        }
        ENDHLSL
        Pass
        {
            Name "Forward" Tags {"LightMode"="UniversalForwardOnly"}
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            half4 Frag(V i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);ClipEdge(i);
                float3 n=normalize(i.normal),p=i.world;
                float2 uv=(p.xz-_GroundPathRect.xy)*_GroundPathRect.zw;
                float inside=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);
                float path=_GroundKind>.5?1:(_GroundPath>.5?SAMPLE_TEXTURE2D(_GroundPathMask,sampler_GroundPathMask,uv).r*inside:0);
                float soil=smoothstep(.08,.8,path);
                float grassSlope=smoothstep(.40,.72,n.y);
                bool darkNear=_CIEnabled>.5&&_CIDarkNear>.5;
                if(darkNear)grassSlope=max(grassSlope,soil);
                float3 albedo,detailNormal=n;
                float cameraDistance=distance(_WorldSpaceCameraPos,p);
                float detail=_CIEnabled>.5 ? (1-CIProgress(cameraDistance,_CIDetailRange.xy))*_CINormalStrength : 1-smoothstep(80,230,cameraDistance);
                // Keep a fixed normal sample and fade its subtractive pigment response below.
                // Fading the normal itself can turn a dark mark into a bright one along the way.
                if(darkNear)detail=cameraDistance<_CIDetailRange.y?_CINormalStrength:0;
                if(grassSlope>.001)
                {
                    float2 gUV=p.xz*_Tiling.x,dUV=_SourceUV>.5?i.uv:p.xz*_Tiling.y;
                    float macro=Noise(p.xz*.045);
                    float3 grass=SAMPLE_TEXTURE2D(_GrassMap,sampler_GrassMap,gUV).rgb;
                    float3 dirt=SAMPLE_TEXTURE2D(_DirtMap,sampler_DirtMap,dUV).rgb;
                    // Second frequency only near the viewer; broad modulation continues in the distance.
                    if(cameraDistance<180&&_SourceUV<.5)
                    {
                        grass=lerp(grass,SAMPLE_TEXTURE2D(_GrassMap,sampler_GrassMap,float2(-gUV.y,gUV.x)*.413+17).rgb,.18);
                        dirt=lerp(dirt,SAMPLE_TEXTURE2D(_DirtMap,sampler_DirtMap,float2(-dUV.y,dUV.x)*.413+7).rgb,.18);
                    }
                    albedo=lerp(grass,dirt,soil)*lerp(.80,1.13,macro);
                    if(detail>.001)
                    {
                        float3 gn=UnpackNormalScale(SAMPLE_TEXTURE2D(_GrassNormal,sampler_GrassNormal,gUV),_BumpScale*detail);
                        float3 dn=UnpackNormalScale(SAMPLE_TEXTURE2D(_DirtNormal,sampler_DirtNormal,dUV),_BumpScale*detail);
                        detailNormal=SoilNormal(n,normalize(lerp(gn,dn,soil)));
                    }
                }
                else albedo=0;
                if(grassSlope<.999)
                {
                    float3 w=pow(abs(n),4);w/=max(.001,w.x+w.y+w.z);float3 q=p*_Tiling.z;
                    float3 rock=SAMPLE_TEXTURE2D(_RockMap,sampler_RockMap,q.zy).rgb*w.x+
                        SAMPLE_TEXTURE2D(_RockMap,sampler_RockMap,q.xz).rgb*w.y+SAMPLE_TEXTURE2D(_RockMap,sampler_RockMap,q.xy).rgb*w.z;
                    albedo=lerp(rock*.65,albedo,grassSlope);
                    if(detail>.001)
                    {
                        float3 x=UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal,sampler_RockNormal,q.zy),_BumpScale*detail);
                        float3 y=UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal,sampler_RockNormal,q.xz),_BumpScale*detail);
                        float3 z=UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal,sampler_RockNormal,q.xy),_BumpScale*detail);
                        float3 rn=normalize(float3(x.z*sign(n.x),x.y,x.x)*w.x+float3(y.x,y.z*sign(n.y),y.y)*w.y+float3(z.x,z.y,z.z*sign(n.z))*w.z);
                        detailNormal=normalize(lerp(rn,detailNormal,grassSlope));
                    }
                }
                float lum=dot(albedo,float3(.2126,.7152,.0722));albedo=lerp(lum.xxx,albedo,_Saturation)*_Brightness;
                Light main=GetMainLight(TransformWorldToShadowCoord(p));
                float3 energy=main.color*main.shadowAttenuation*main.distanceAttenuation*saturate(dot(detailNormal,main.direction));
                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData=(InputData)0;inputData.positionWS=p;inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
                uint count=GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                for(uint k=0;k<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);k++)
                {Light l=GetAdditionalLight(k,p,half4(1,1,1,1));energy+=l.color*l.distanceAttenuation*l.shadowAttenuation*saturate(dot(detailNormal,l.direction));}
                #endif
                LIGHT_LOOP_BEGIN(count)
                Light l=GetAdditionalLight(lightIndex,p,half4(1,1,1,1));energy+=l.color*l.distanceAttenuation*l.shadowAttenuation*saturate(dot(detailNormal,l.direction));
                LIGHT_LOOP_END
                #endif
                float ao=1;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                ao=GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS)).indirectAmbientOcclusion;
                #endif
                float3 color=albedo*(_AmbientLevel*lerp(.5,1,ao)+_LightResponse*energy/(1+energy));
                if(_CIEnabled>.5)
                {
                    float shade=saturate(dot(detailNormal,main.direction))*lerp(.25,1,main.shadowAttenuation)*ao;
                    if(darkNear)
                    {
                        float broadShade=saturate(dot(n,main.direction))*lerp(.25,1,main.shadowAttenuation)*ao;
                        color=CIGroundDark(albedo,p,n,soil,broadShade,shade,cameraDistance);
                    }
                    else color=CIGround(albedo,p,n,soil,shade,cameraDistance);
                }
                if(_RealmTintStrength>.001)
                {
                    float3 realm=SAMPLE_TEXTURE2D(_RealmPigment,sampler_RealmPigment,uv).rgb;
                    float3 chroma=clamp(realm/max(dot(realm,float3(.2126,.7152,.0722)),.02),.6,1.4);
                    float beforeLuma=CILuma(color);
                    color*=lerp(1,chroma,_RealmTintStrength*inside);
                    if(_CIEnabled>.5)color*=beforeLuma/max(.001,CILuma(color));
                }
                if(_CIEnabled>.5)return half4(CIAtmosphere(color,cameraDistance),1);
                float wash=smoothstep(_WashStart,max(_WashStart+1,_WashEnd),cameraDistance)*_WashStrength;
                return half4(lerp(color,_OhPaperColor.rgb,wash),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags {"LightMode"="DepthOnly"} ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Depth
            #pragma multi_compile_instancing
            half4 Depth(V i):SV_Target {ClipEdge(i);return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags {"LightMode"="DepthNormalsOnly"}
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Normals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 Normals(V i):SV_Target
            {
                ClipEdge(i);float3 n=normalize(i.normal);
                #if defined(_GBUFFER_NORMALS_OCT)
                return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(n)*.5+.5)),0);
                #else
                return half4(n,0);
                #endif
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"} ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Shadow
            #pragma fragment Depth
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection,_LightPosition;
            V Shadow(A i)
            {
                V o=Vert(i);float3 direction=_LightDirection;
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                direction=normalize(_LightPosition-o.world);
                #endif
                o.positionCS=TransformWorldToHClip(ApplyShadowBias(o.world,normalize(o.normal),direction));
                #if UNITY_REVERSED_Z
                o.positionCS.z=min(o.positionCS.z,o.positionCS.w*UNITY_NEAR_CLIP_VALUE);
                #else
                o.positionCS.z=max(o.positionCS.z,o.positionCS.w*UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }
            half4 Depth(V i):SV_Target {ClipEdge(i);return 0;}
            ENDHLSL
        }
    }
}
