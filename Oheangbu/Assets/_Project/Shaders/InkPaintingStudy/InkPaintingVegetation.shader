// Macro dressing derivative; original architecture shader is preserved.
// Macro architecture only: preserve authored colour/normal textures, with a local diffuse floor.
// No emission or environment writes. Distance wash reads the existing WorldLookDriver palette.
Shader "Oheangbu/Study/InkPaintingVegetation"
{
    Properties
    {
        _InkChromaRetain268("Candidate regional chroma retention",Range(0,1))=1
        _InkChromaRect268("Candidate regional XZ bounds",Vector)=(0,0,0,0)
        _ContactSoft265("Candidate soft contact", Float) = 0
        [ToggleUI] _PaintedFoliage("Painted foliage",Float)=1
        _PaintedLodRgbGain("Species LOD ink-input RGB gain",Range(.25,4))=1
        [ToggleUI] _PaintedStochasticFade("One-pixel stochastic distance fade",Float)=0
        [ToggleUI] _PaintedCanopyQuiet("Quiet canopy forward shading",Float)=0
        _PaintedCanopyQuietRange("Quiet near/full distance metres",Vector)=(2,14,0,0)
        _PaintedCanopyQuietSurface("Albedo flatten/normal retain/source mean/NdotL flatten",Vector)=(.80,.18,.18,.65)
        [ToggleUI] _PaintedCanopy("Grouped canopy ink tones",Float)=0
        _PaintedCanopyTones("Loaded/middle/pale/continuous shading",Vector)=(.018,.035,.075,.12)
        [NoScaleOffset] _PaintedGeoField("Actual terrain ink regions and flow",2D)="black"{}
        _PaintedGeoRect("Terrain field origin size XZ",Vector)=(0,0,1,1)
        [ToggleUI] _PaintedGeoEnabled("Use terrain field",Float)=0
        [ToggleUI] _PaintedFormEnabled("Form-led mountain ink",Float)=0
        [ToggleUI] _PaintedFormAuthored("Use authored form RGBA map",Float)=0
        [NoScaleOffset] _PaintedFormMap("Form R ink G paper B dry A override",2D)="black"{}
        _PaintedFormMapRect("Form map world origin XZ/size",Vector)=(-2000,-3000,4000,6000)
        _PaintedFormRelief("Upper load/foot reserve relief ramps",Vector)=(45,145,55,135)
        _PaintedFormDirection("Broad ink-load direction XZ",Vector)=(.8,.6,0,0)
        _PaintedFormTones("Loaded/middle/lower/dry ink tones",Vector)=(.006,.045,.16,.018)
        _PaintedFormBrush("Cross/relief metres/dry thresholds",Vector)=(180,480,.86,.99)
        _PaintedFormStrokeBody("Broad form brush body strength",Range(0,1))=0
        _PaintedFormAspectContrast("Upper form aspect contrast",Range(0,1))=0
        _PaintedFormPaper("Foot strength/paper fraction",Vector)=(.65,.82,0,0)
        _PaintedFormPaperDistance("Form reserve near/far metres",Vector)=(250,900,0,0)
        [ToggleUI] _PaintedValleyReserve("Distant low-relief paper reserve",Float)=0
        _PaintedValleyDistance("Reserve horizontal distance metres",Vector)=(250,900,0,0)
        _PaintedValleyRelief("Reserve relief ramps metres",Vector)=(10,30,55,125)
        _PaintedValleyAppearance("Reserve strength/paper fraction",Vector)=(.45,.72,0,0)
        [ToggleUI] _CIEnabled("Compact ink landscape",Float)=0
        [ToggleUI] _CIDarkNear("Dark foreground ink revision",Float)=0
        [ToggleUI] _CIAtmosphereOverride("Painterly atmosphere override",Float)=0
        [ToggleUI] _CIPainterly("Painterly landscape revision",Float)=0
        _CIMidAirRange("Middle atmosphere range",Vector)=(500,1500,0,0)
        _CIFarAirRange("Far atmosphere range override",Vector)=(1500,3400,0,0)
        _CIAirStrengths("Middle/far atmosphere strengths",Vector)=(.18,.64,0,0)
        _CIStrokeSpacingWidth("Stroke spacing/width metres",Vector)=(20,40,2,7)
        _CIStrokeFadeStrength("Stroke fade/strength",Vector)=(200,1400,.02,0)
        _CIPigmentContrast("Pigment contrast/detail scale",Vector)=(.03,.65,0,0)
        _CIFoliagePainterly("Foliage range/mip bias/contrast",Vector)=(30,180,1.5,.3)
        _CIMountainTones("Mountain ink tone interval",Vector)=(.06,.18,0,0)
        _CIMountainSlope("Mountain slope degrees",Vector)=(12,37,0,0)
        [ToggleUI] _CIBroadBrush("Broad mountain ink coats",Float)=0
        _CIBroadBrushScale("Broad stroke width/length metres",Vector)=(80,240,420,1000)
        _CIBroadBrushCoverage("Coat opacity/edge/second/warp",Vector)=(.90,.18,.76,.20)
        _CIBroadBrushTones("Broad dark/soft coat tones",Vector)=(.015,.07,0,0)
        _CIBroadBrushBands("Mountain band thresholds/softness/amount",Vector)=(.33,.66,.12,.8)
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

        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Range(0,2)) = 1
        _Saturation("Texture Saturation", Range(0,1)) = 0.5
        _AmbientFloor("Local Diffuse Ambient Floor", Range(0,1)) = 0.4
        _LightResponse("Direct Light Response", Range(0,2)) = 0.75
        _WashStart("Wash Start (metres)", Float) = 800
        _WashEnd("Wash End (metres)", Float) = 9500
        _WashStrength("Wash Strength", Range(0,1)) = 0.58
        [ToggleUI] _AlphaClip("Alpha Clipping", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        _WindAmplitude("Leaf Wind (metres)", Float) = 0
        _LeafFlutter("Fine leaf motion (metres)", Float) = 0
        _WindExternalClock("Use engine clock for static renderers", Float) = 0
        _WindAnchor("Assembly root in part space", Vector) = (0,0,0,0)
        _WindSpeed("Wind Speed", Float) = 1
        _Height("Rooted Height", Float) = 8
        _Billboard("Upright Billboard", Float) = 0
        _BillboardViews("Atlas Direction Views", Float) = 1
        _SimpleLighting("Card Simple Lighting", Float) = 0
        _FadeInStart("Fade In Start", Float) = -1
        _FadeInEnd("Fade In End", Float) = 0
        _FadeOutStart("Fade Out Start", Float) = 700
        _FadeOutEnd("Fade Out End", Float) = 800
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        // Identical material layout in every pass, including depth and shadow rendering.
        CBUFFER_START(UnityPerMaterial)
            float _InkChromaRetain268;float4 _InkChromaRect268;
            float4 _BaseMap_ST;
            float4 _BaseMap_TexelSize;
            half4 _BaseColor;
            half _BumpScale;
            half _Saturation;
            half _AmbientFloor;
            half _LightResponse;
            float _WashStart;
            float _WashEnd;
            half _WashStrength;
            half _AlphaClip;
            half _Cutoff;
            half _Cull;
            float _WindAmplitude,_WindSpeed,_Height,_Billboard;
            float _LeafFlutter,_WindExternalClock;float4 _WindAnchor;
            float _BillboardViews,_SimpleLighting;
            float _FadeInStart,_FadeInEnd,_FadeOutStart,_FadeOutEnd;
        
            float _PaintedFoliage;float _PaintedLodRgbGain;
            float _PaintedStochasticFade;
            float _PaintedCanopyQuiet;
            float4 _PaintedCanopyQuietRange,_PaintedCanopyQuietSurface;
            float _PaintedCanopy;
            float4 _PaintedCanopyTones;
            float4 _PaintedGeoRect;float _PaintedGeoEnabled;
            float _PaintedFormEnabled,_PaintedFormAuthored;
            float _PaintedFormStrokeBody;
            float _PaintedFormAspectContrast;
            float4 _PaintedFormMapRect,_PaintedFormRelief,_PaintedFormDirection;
            float4 _PaintedFormTones,_PaintedFormBrush,_PaintedFormPaper,_PaintedFormPaperDistance;
            float _PaintedValleyReserve;
            float4 _PaintedValleyDistance,_PaintedValleyRelief,_PaintedValleyAppearance;
            float _CIEnabled,_CIDarkNear,_CIDetailContrast,_CINormalStrength;
            float4 _CIInk,_CIPaper,_CIAir,_CIDetailRange,_CIRockRange,_CIMountainRange,_CIAirRange,_CITones;
            float4 _CIMountainTones,_CIMountainSlope;
            float _CIBroadBrush;
            float4 _CIBroadBrushScale,_CIBroadBrushCoverage,_CIBroadBrushTones,_CIBroadBrushBands;
            float _CIAtmosphereOverride,_CIPainterly;
            float4 _CIMidAirRange,_CIFarAirRange,_CIAirStrengths,_CIStrokeSpacingWidth,_CIStrokeFadeStrength,_CIPigmentContrast,_CIFoliagePainterly;
        CBUFFER_END
        #include "../InkChroma268.hlsl"
        float _ContactEnabled265;
        float4 _ContactPoints265[8];
        #include "../CompactInkLandscape.hlsl"
        #include "../CompactPainterlyFoliage.hlsl"
        #include "InkPaintingFoliage.hlsl"
        #include "InkPaintingFoliageFade.hlsl"
        #include "InkPaintingGeography.hlsl"
        float4 _OhPaperColor;
        float _DressingTime;
        float _DressingAerial;
        // Renderer-local LOD policy. Provider materials and baseline sheets remain unchanged.
        float _DressingFadeOverride;
        float4 _DressingFadeRange;
        float _DressingCullRadius;

        struct SurfaceAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct SurfaceVaryings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float2 uv : TEXCOORD1;
            half3 normalWS : TEXCOORD2;
            half4 tangentWS : TEXCOORD3;
            half3 vertexLighting : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        SurfaceVaryings SurfaceVertex(SurfaceAttributes input)
        {
            SurfaceVaryings output = (SurfaceVaryings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float3 objectCentre = TransformObjectToWorld(float3(0,0,0));
            float4 lodFade = _DressingFadeOverride>.5?_DressingFadeRange:float4(_FadeInStart,_FadeInEnd,_FadeOutStart,_FadeOutEnd);
            float lodDistance=length(_WorldSpaceCameraPos.xz-objectCentre.xz);
            // Cached packets include a movement safety band. Fully faded instances
            // can skip lighting/wind and rasterization while the packet stays reusable.
            if(_DressingCullRadius>0 && (lodDistance>lodFade.w+_DressingCullRadius || lodDistance<lodFade.x-_DressingCullRadius))
            {
                // Explicit fields and one return keep D3D's early-cull analysis fully
                // initialized in every depth/shadow/vertex-lighting variant.
                output.positionCS=float4(2,2,2,1);
                output.positionWS=float3(0,0,0);
                output.uv=float2(0,0);
                output.normalWS=half3(0,0,0);
                output.tangentWS=half4(0,0,0,0);
                output.vertexLighting=half3(0,0,0);
            }
            else
            {
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                float3 centre = TransformObjectToWorld(float3(0,0,0));
                float3 world = position.positionWS;
                if(_Billboard>.5)
                {
                    float3 facing = _WorldSpaceCameraPos-centre; facing.y=0;
                    facing = normalize(facing+float3(0,0,.00001));
                    float3 right=cross(float3(0,1,0),facing);
                    float3 scale=float3(length(unity_ObjectToWorld._m00_m10_m20),length(unity_ObjectToWorld._m01_m11_m21),length(unity_ObjectToWorld._m02_m12_m22));
                    world=centre+right*input.positionOS.x*scale.x+float3(0,input.positionOS.y*scale.y,0)+facing*input.positionOS.z*scale.z;
                    normal.normalWS=facing;
                }
                float3 rootCentre=TransformObjectToWorld(_WindAnchor.xyz);
                float rooted=saturate(max(0,world.y-rootCentre.y)/max(.1,_Height));
                float phase=rootCentre.x*.031+rootCentre.z*.047;
                float windTime=_WindExternalClock>.5?_Time.y:_DressingTime;
                float gust=sin(windTime*_WindSpeed+phase)+.35*sin(windTime*_WindSpeed*1.73+phase*2);
                // Root pinned, broad gust shared by each assembly, higher frequency leaf flutter.
                // Every depth/shadow pass calls this same deformation function.
                float flutter=sin(windTime*3.7+world.x*.91+world.z*.73)*sin(windTime*2.1+world.y*1.67+phase);
                world.xz+=float2(.8,.35)*gust*_WindAmplitude*rooted*rooted;
                world+=float3(.6,.15,.35)*flutter*_LeafFlutter*rooted;
            if(_ContactEnabled265>.5){
                float2 bend=0;
                [unroll] for(int ci=0;ci<8;ci++){
                    float4 contact=_ContactPoints265[ci];float2 away=rootCentre.xz-contact.xz;
                    float distanceToContact=length(away);float weight=saturate(1-distanceToContact/max(.001,contact.w));
                    weight*=saturate(1-abs(rootCentre.y-contact.y)/2.0);
                    bend+=away/max(.15,distanceToContact)*weight*weight;
                }
                float amount=min(1,length(bend));world.xz+=normalize(bend+float2(.00001,0))*amount*.42*rooted*rooted;
                world.y-=amount*.12*rooted;
            }

                output.positionCS = TransformWorldToHClip(world);
                output.positionWS = world;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                if(_Billboard>.5&&_BillboardViews>1.5)
                {
                    float3 objectView=mul((float3x3)unity_WorldToObject,_WorldSpaceCameraPos-centre);
                    float turns=atan2(objectView.x,-objectView.z)/6.283185307;
                    float index=floor(frac(turns+1+.5/_BillboardViews)*_BillboardViews);
                    output.uv.x=(saturate(output.uv.x)*.996+.002+index)/_BillboardViews;
                }
                output.normalWS = normal.normalWS;
                output.tangentWS = half4(normal.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.vertexLighting = half3(0,0,0);
                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                if(_SimpleLighting<.5)output.vertexLighting = VertexLighting(world, normal.normalWS);
                #endif
            }
            return output;
        }
        void DistanceClip(SurfaceVaryings input)
        {
            // Same horizontal metric as CPU residency/LOD selection, including valley and fly views.
            float d=length(_WorldSpaceCameraPos.xz-input.positionWS.xz);
            float4 fade=_DressingFadeOverride>.5?_DressingFadeRange:float4(_FadeInStart,_FadeInEnd,_FadeOutStart,_FadeOutEnd);
            float entering=saturate((d-fade.x)/max(.01,fade.y-fade.x));
            float leaving=1-saturate((d-fade.z)/max(.01,fade.w-fade.z));
            // Only leaf cards fade immediately around the eye; solid trunks retain collision readability.
            float noise=IPFoliageFadeNoise(input.positionCS.xy,_PaintedStochasticFade);
            // Complementary masks keep the two distance LODs from thinning out simultaneously.
            // Fully visible coverage must not punch hash-extreme pinholes. Keep
            // the original clip arithmetic for every partial-coverage fragment.
            if(entering<1)clip(entering<=0?-1:entering-(1-noise)-.0001);
            if(leaving<1)clip(leaving<=0?-1:leaving-noise-.0001);
            if(_AlphaClip>.5)
            {
                float nearVisibility=saturate((distance(_WorldSpaceCameraPos,input.positionWS)-.35)/.45);
                if(nearVisibility<1)clip(nearVisibility<=0?-1:nearVisibility-noise-.0001);
            }
        }
        half4 ReadAlbedo(float2 uv)
        {
            half4 colour = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
            if (_AlphaClip > 0.5h) clip(colour.a - _Cutoff);
            return colour;
        }
        half3 ReadNormal(SurfaceVaryings input, half faceSign)
        {
            half3 normal = NormalizeNormalPerPixel(input.normalWS);
            // Imported meshes without tangents retain geometric normals rather than a broken TBN.
            if (_SimpleLighting>.5||dot(input.tangentWS.xyz, input.tangentWS.xyz) < 0.01h) return normal * faceSign;
            half3 tangent = normalize(input.tangentWS.xyz);
            half3 bitangent = input.tangentWS.w * cross(normal, tangent);
            half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
            return NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, half3x3(tangent, bitangent, normal))) * faceSign;
        }
        half3 DiffuseLight(Light light, half3 normalWS, float quietWeight)
        {
            #ifdef _LIGHT_LAYERS
            if (!IsMatchingLightLayer(light.layerMask, GetMeshRenderingLayer())) return 0;
            #endif
            // Keep original arithmetic exactly when the experiment is disabled.
            half3 result=light.color * (light.distanceAttenuation * light.shadowAttenuation * saturate(dot(normalWS, light.direction)));
            [branch] if(quietWeight>0)
            {
                half response=lerp(saturate(dot(normalWS,light.direction)),.5h,quietWeight*saturate(_PaintedCanopyQuietSurface.w));
                result=light.color*(light.distanceAttenuation*light.shadowAttenuation*response);
            }
            return result;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend One Zero
            ColorMask RGBA
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex SurfaceVertex
            #pragma fragment SurfaceFragment
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            half4 SurfaceFragment(SurfaceVaryings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                DistanceClip(input);
                float distanceWS=distance(_WorldSpaceCameraPos,input.positionWS);
                half3 albedo = ReadAlbedo(input.uv).rgb;
                albedo=CIPainterlyFoliageAlbedo(input.uv,albedo,distanceWS,_Billboard>.5?_BillboardViews:1);
                float quietWeight=0;
                [branch] if(_CIEnabled>.5&&_PaintedFoliage>.5&&_PaintedCanopyQuiet>.5&&_AlphaClip>.5)
                    quietWeight=IPCanopyQuietWeight(distanceWS,_PaintedCanopyQuietRange.xy);
                albedo=IPCanopyQuietAlbedo(albedo,_BaseColor.rgb,quietWeight,_PaintedCanopyQuietSurface);
                half luminance = dot(albedo, half3(0.2126h, 0.7152h, 0.0722h));
                albedo = lerp(luminance.xxx, albedo, saturate(_Saturation));
                if(_SimpleLighting>.5)
                {
                    half3 simple=albedo*max(_AmbientFloor, .25h);
                    if(_CIEnabled>.5){simple=_PaintedFoliage>.5?IPFoliageInkColour(simple*clamp(_PaintedLodRgbGain,.25,4),1,_PaintedCanopy,_PaintedCanopyTones):CIPainterlyFoliageColour(simple,distanceWS);return half4(IPGeographyAtmosphere(CIAttenuateChroma(InkChroma268(simple,input.positionWS),distanceWS),distanceWS,input.positionWS),1);}
                    float fog=smoothstep(_WashStart,max(_WashStart+.001,_WashEnd),distance(_WorldSpaceCameraPos,input.positionWS))*saturate(_WashStrength);
                    return half4(lerp(InkChroma268(simple,input.positionWS),_OhPaperColor.rgb,fog),1);
                }
                half3 normal = ReadNormal(input, IS_FRONT_VFACE(facing, 1.0h, -1.0h));
                [branch] if(quietWeight>0)
                    normal=IPCanopyQuietNormal(normal,NormalizeNormalPerPixel(input.normalWS)*IS_FRONT_VFACE(facing,1.0h,-1.0h),quietWeight,_PaintedCanopyQuietSurface.y);
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                half3 direct = DiffuseLight(GetMainLight(shadowCoord, input.positionWS, half4(1,1,1,1)), normal, quietWeight);
                #if defined(_ADDITIONAL_LIGHTS)
                    #if USE_CLUSTER_LIGHT_LOOP
                    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                    {
                        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                        direct += DiffuseLight(GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1)), normal, quietWeight);
                    }
                    #endif
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        direct += DiffuseLight(GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1)), normal, quietWeight);
                    LIGHT_LOOP_END
                #endif
                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                direct += input.vertexLighting;
                #endif
                // The floor is diffuse illumination multiplied by the original texture, not emission.
                half3 ambient = max(SampleSH(normal), max(0.0h, _AmbientFloor).xxx);
                half3 colour = albedo * (ambient + direct * max(0.0h, _LightResponse));
                if(_CIEnabled>.5){colour=_PaintedFoliage>.5?IPFoliageInkColour(colour*clamp(_PaintedLodRgbGain,.25,4),1,_PaintedCanopy,_PaintedCanopyTones):CIPainterlyFoliageColour(colour,distanceWS);return half4(IPGeographyAtmosphere(CIAttenuateChroma(InkChroma268(colour,input.positionWS),distanceWS),distanceWS,input.positionWS),1);}
                float wash = smoothstep(_WashStart, max(_WashEnd, _WashStart + 0.001), distanceWS) * saturate(_WashStrength);
                return half4(lerp(InkChroma268(colour,input.positionWS), _OhPaperColor.rgb, wash), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            SurfaceVaryings ShadowVertex(SurfaceAttributes input)
            {
                SurfaceVaryings output = SurfaceVertex(input);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirectionWS = normalize(_LightPosition - output.positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(output.positionWS, output.normalWS, lightDirectionWS)));
                return output;
            }
            half4 ShadowFragment(SurfaceVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                DistanceClip(input);
                ReadAlbedo(input.uv);
                return 0;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex SurfaceVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            half DepthFragment(SurfaceVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                DistanceClip(input);
                ReadAlbedo(input.uv);
                return input.positionCS.z;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex SurfaceVertex
            #pragma fragment NormalsFragment
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            void NormalsFragment(SurfaceVaryings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC,
                out half4 outNormalWS : SV_Target0
                #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
                #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                DistanceClip(input);
                ReadAlbedo(input.uv);
                float3 normal = ReadNormal(input, IS_FRONT_VFACE(facing, 1.0h, -1.0h));
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormal = PackNormalOctQuadEncode(normal);
                outNormalWS = half4(PackFloat2To888(saturate(octNormal * 0.5 + 0.5)), 0);
                #else
                outNormalWS = half4(normal, 0);
                #endif
                #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
