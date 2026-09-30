// Macro dressing derivative; original architecture shader is preserved.
// Macro architecture only: preserve authored colour/normal textures, with a local diffuse floor.
// No emission or environment writes. Distance wash reads the existing WorldLookDriver palette.
Shader "Oheangbu/Prototype/CliffProp278"
{
    Properties
    {
        _Leaf279("Leaf wrap light",Range(0,1))=0
        _PaperTint278("Local valley atmosphere",Color)=(.72,.72,.68,1)
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
            float4 _PaperTint278;float _Leaf279;
            float4 _BaseMap_ST;
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
            float _BillboardViews,_SimpleLighting;
            float _FadeInStart,_FadeInEnd,_FadeOutStart,_FadeOutEnd;
        CBUFFER_END

        #define _DressingTime _Time.y
        float _DressingAerial;
        // Renderer-local LOD policy. Provider materials and baseline sheets remain unchanged.
        static const float _DressingFadeOverride=0;
        float4 _DressingFadeRange;
        static const float _DressingCullRadius=0;

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
            { output.positionCS=float4(2,2,2,1);return output; }
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
            float rooted=saturate(max(0,world.y-centre.y)/max(.1,_Height));
            float phase=centre.x*.031+centre.z*.047;
            float gust=sin(_DressingTime*_WindSpeed+phase)+.35*sin(_DressingTime*_WindSpeed*1.73+phase*2);
            world.xz+=float2(.8,.35)*gust*_WindAmplitude*rooted*rooted;
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
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
            if(_SimpleLighting<.5)output.vertexLighting = VertexLighting(world, normal.normalWS);
            #endif
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
            float noise=frac(52.9829189*frac(dot(floor(input.positionCS.xy),float2(.06711056,.00583715))));
            // Complementary masks keep the two distance LODs from thinning out simultaneously.
            clip(entering-(1-noise)-.0001);
            clip(leaving-noise-.0001);
            if(_AlphaClip>.5)clip(saturate((distance(_WorldSpaceCameraPos,input.positionWS)-.35)/.45)-noise-.0001);
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
        half3 DiffuseLight(Light light, half3 normalWS)
        {
            #ifdef _LIGHT_LAYERS
            if (!IsMatchingLightLayer(light.layerMask, GetMeshRenderingLayer())) return 0;
            #endif
            return light.color * (light.distanceAttenuation * light.shadowAttenuation * saturate((dot(normalWS, light.direction)+_Leaf279*.4)/(1+_Leaf279*.4)));
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
            half3 albedo = ReadAlbedo(input.uv).rgb;
                // Lift dark source texels continuously; clamping before this would
                // collapse differently shaded needles into one flat canopy colour.
                albedo = lerp(albedo+.018h,sqrt(max(albedo,0)+.008h)*.8h,_Leaf279);
                half luminance = dot(albedo, half3(0.2126h, 0.7152h, 0.0722h));
                albedo = lerp(luminance.xxx, albedo, saturate(_Saturation));
                if(_SimpleLighting>.5)
                {
                    half3 simple=albedo*max(_AmbientFloor, .25h);
                    float fog=smoothstep(_WashStart,max(_WashStart+.001,_WashEnd),distance(_WorldSpaceCameraPos,input.positionWS))*saturate(_WashStrength);
                    return half4(lerp(simple,_PaperTint278.rgb,fog),1);
                }
                half3 normal = ReadNormal(input, IS_FRONT_VFACE(facing, 1.0h, -1.0h));
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                half3 direct = DiffuseLight(GetMainLight(shadowCoord, input.positionWS, half4(1,1,1,1)), normal);
                #if defined(_ADDITIONAL_LIGHTS)
                    #if USE_CLUSTER_LIGHT_LOOP
                    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                    {
                        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                        direct += DiffuseLight(GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1)), normal);
                    }
                    #endif
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        direct += DiffuseLight(GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1)), normal);
                    LIGHT_LOOP_END
                #endif
                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                direct += input.vertexLighting;
                #endif
                // The floor is diffuse illumination multiplied by the original texture, not emission.
                half3 ambient = max(SampleSH(normal), max(0.0h, _AmbientFloor).xxx);
                half3 colour = albedo * (ambient + direct * max(0.0h, _LightResponse));
                float distanceWS = distance(_WorldSpaceCameraPos, input.positionWS);
                float wash = smoothstep(_WashStart, max(_WashEnd, _WashStart + 0.001), distanceWS) * saturate(_WashStrength);
                return half4(lerp(colour, _PaperTint278.rgb, wash), 1);
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
