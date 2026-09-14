// Isolated macro terrain: copied lighting/ink response plus non-emissive realm pigment.
// Palette comes exclusively from WorldLookDriver. There are no material colors,
// emission, time-dependent marks, fog, or screen-space paper overlays. Realm pigment uses its own mask.
// This experiment owns ONE distance wash here; do not stack InkWorldPost wash.
Shader "Oheangbu/WorldMacroTerrain"
{
    Properties
    {
        [Header(Ink And Paper)]
        _InkDensity("Ink Density", Range(0.25, 3)) = 1.25
        _InkPoint("Ink Point", Range(0, 0.5)) = 0.035
        _PaperPoint("Paper Point", Range(0.3, 1.5)) = 0.8
        _AmbientLevel("Ambient Level", Range(0, 1)) = 0.15
        _LightResponse("Main Light Response", Range(0, 2)) = 0.9
        _AddLightGain("Additional Light Gain", Range(0, 4)) = 1
        _AoStrength("AO Strength", Range(0, 1)) = 0.7
        _ToneFloor("Tone Floor", Range(0, 1)) = 0.015
        _ToneCeiling("Tone Ceiling", Range(0, 1)) = 0.9

        [Header(Surface Brushwork)]
        _NoiseScale("Noise Scale (world units)", Float) = 0.65
        _NoiseStrength("Pigment Variation", Range(0, 0.5)) = 0.11
        _BrushStrength("Broken Vertical Brush Strength", Range(0, 0.5)) = 0.13
        _StrokeScale("Drawn Stroke Scale (per metre)", Float) = 0.72
        _StrokeStrength("Drawn Ink Stroke Strength", Range(0, 1)) = 0.6
        _GrainStrength("Pigment Grain Strength", Range(0, 0.15)) = 0.018
        _RimWidth("Silhouette Ink Width", Range(0.01, 0.6)) = 0.16
        _RimStrength("Silhouette Ink Strength", Range(0, 1)) = 0.22
        _UndersideInk("Underside Ink", Range(0, 1)) = 0.38

        [Header(Single Distance Wash)]
        _WashStart("Wash Start (metres)", Float) = 55
        _WashEnd("Wash End (metres)", Float) = 520
        _WashStrength("Wash Strength", Range(0, 1)) = 0.78

        [Header(Realm Ground Pigment)]
        [NoScaleOffset] _RealmPigment("Region pigment from palette", 2D) = "white" {}
        _RealmTintStrength("Subtle material tint", Range(0, 1)) = 0

        [Header(Palette Slot)]
        [ToggleUI] _UseWoodColor("Use Wood Palette Slot", Float) = 0
        _TintRetain("Retain Wood Pigment", Range(0, 1)) = 0
        [ToggleUI] _PathEdge("Broken Path Ribbon Edges", Float) = 0
        [Header(Ground Surface Path)]
        [ToggleUI] _GroundPath("Blend path pigment on this surface", Float) = 0
        [NoScaleOffset] _GroundPathMask("World XZ path mask (linear)", 2D) = "black" {}
        _GroundPathRect("Mask origin XZ and inverse size", Vector) = (0,0,1,1)
        _GroundPathTones("Path tone floor / ceiling", Vector) = (.26,.64,0,0)
        _GroundPathVariation("Path pigment variation", Range(0,.3)) = .09
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "InkNoise3D.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        CBUFFER_START(UnityPerMaterial)
            half _InkDensity;
            half _InkPoint;
            half _PaperPoint;
            half _AmbientLevel;
            half _LightResponse;
            half _AddLightGain;
            half _AoStrength;
            half _ToneFloor;
            half _ToneCeiling;
            float _NoiseScale;
            half _NoiseStrength;
            half _BrushStrength;
            float _StrokeScale;
            half _StrokeStrength;
            half _GrainStrength;
            half _RimWidth;
            half _RimStrength;
            half _UndersideInk;
            float _WashStart;
            float _WashEnd;
            half _WashStrength;
            half _UseWoodColor;
            half _TintRetain;
            half _PathEdge;
            half _GroundPath;
            float4 _GroundPathRect;
            float4 _GroundPathTones;
            half _GroundPathVariation;
            half _Cull;
            half _RealmTintStrength;
        CBUFFER_END
        TEXTURE2D(_RealmPigment);
        SAMPLER(sampler_RealmPigment);
        TEXTURE2D(_GroundPathMask);
        SAMPLER(sampler_GroundPathMask);

        // Global linear palette, deliberately absent from Properties/CBUFFER.
        float4 _OhInkColor;
        float4 _OhPaperColor;
        float4 _OhWoodColor;

        // The ribbon's UV.x is its width coordinate. Every pass clips the exact
        // same un-biased world surface, including the shadow and SSAO prepasses.
        // Other materials bypass this uniformly; the default remains fully opaque.
        void ClipPathEdge(float3 positionWS, float2 uv)
        {
            [branch] if (_PathEdge > 0.5)
            {
                float broadEdge = OhFbm3(positionWS * 0.75 + 6.17);
                float chips = OhValueNoise3(positionWS * 4.9 + 19.3);
                float edgeWidth = 0.065 + broadEdge * 0.055 + chips * 0.032;
                clip(min(uv.x, 1.0 - uv.x) - edgeWidth);
            }
        }
        ENDHLSL

        Pass
        {
            Name "CodexInkLandscapeForward"
            Tags { "LightMode" = "UniversalForwardOnly" }
            ZWrite On
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex LandscapeVertex
            #pragma fragment LandscapeFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings LandscapeVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            float ScalarLight(Light light, float3 normalWS)
            {
                // Only energy is admitted. Light hue cannot color the environment.
                return max(0.0, Luminance(light.color))
                     * light.distanceAttenuation * light.shadowAttenuation
                     * saturate(dot(normalWS, light.direction));
            }

            // A tapered, interrupted brush stroke rather than continuous procedural
            // stripes. Each column offsets its position, start, width and ink load.
            // Derivative filtering removes subpixel marks before they can shimmer.
            float DrawnStroke(float across, float along)
            {
                float column = floor(across);
                float columnSeed = OhHash3(float3(column, 5.71, 9.23));
                along += columnSeed * 1.73;
                float segment = floor(along);
                float segmentSeed = OhHash3(float3(column, segment, 4.19));
                float strokePosition = 0.5 + (columnSeed - 0.5) * 0.35;
                float width = lerp(0.026, 0.115, segmentSeed);
                float widthAA = max(fwidth(across) * 0.65, 0.009);
                float stroke = 1.0 - smoothstep(max(0.0, width - widthAA), width + widthAA,
                                               abs(frac(across) - strokePosition));
                float t = frac(along);
                float taper = smoothstep(0.025, 0.16, t) * (1.0 - smoothstep(0.63, 0.97, t));
                float interrupted = smoothstep(0.2, 0.36, segmentSeed);
                float visibility = 1.0 - smoothstep(0.13, 0.55, fwidth(across));
                return stroke * taper * interrupted * visibility;
            }

            half4 LandscapeFragment(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ClipPathEdge(input.positionWS, input.uv);
                float3 positionWS = input.positionWS;
                float3 normalWS = normalize(input.normalWS) * IS_FRONT_VFACE(facing, 1.0, -1.0);
                float3 viewWS = GetWorldSpaceNormalizeViewDir(positionWS);
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
                float mainEnergy = ScalarLight(mainLight, normalWS);
                float addedEnergy = 0.0;
                #if defined(_ADDITIONAL_LIGHTS)
                // URP 17 Forward+ macros require this InputData name and these fields.
                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalizedScreenSpaceUV = screenUV;
                uint pixelLightCount = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                for (uint dirIndex = 0; dirIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++dirIndex)
                {
                    Light directional = GetAdditionalLight(dirIndex, positionWS, half4(1, 1, 1, 1));
                    addedEnergy += ScalarLight(directional, normalWS);
                }
                #endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light additional = GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1));
                    addedEnergy += ScalarLight(additional, normalWS);
                LIGHT_LOOP_END
                #endif

                // Compress direct daylight before the ink ramp: a sun-facing rock
                // keeps visible pigment rather than becoming a flat paper cutout.
                // Local attraction lights can still push neighboring ink aside.
                float daylight = mainEnergy / (1.0 + mainEnergy) * _LightResponse;
                float localLight = 1.0 - exp2(-addedEnergy * _AddLightGain * 1.442695);
                float luminance = saturate(daylight + localLight + _AmbientLevel);
                #if defined(_SCREEN_SPACE_OCCLUSION)
                float ao = GetScreenSpaceAmbientOcclusion(screenUV).indirectAmbientOcclusion;
                luminance *= lerp(1.0, ao, _AoStrength);
                #endif

                // All marks live in world space; nothing crawls with camera motion.
                float3 p = positionWS * max(abs(_NoiseScale), 0.001);
                float pigment = OhFbm3(p * float3(0.83, 0.47, 0.83));
                float broadMark = (pigment - 0.5) * _NoiseStrength * 2.0;
                float tone = smoothstep(_InkPoint, max(_PaperPoint, _InkPoint + 0.001), luminance + broadMark);
                tone = pow(saturate(tone), max(_InkDensity, 0.05));

                // Sparse, stretched marks suggest vertical rock brushwork. Their
                // response vanishes below pixel size rather than aliasing on ridges.
                float footprint = max(length(ddx(p)), length(ddy(p)));
                float brushVisibility = 1.0 - smoothstep(0.06, 0.35, footprint);
                float fibre = OhValueNoise3(p * float3(5.2, 0.3, 5.2)
                            + pigment * float3(1.7, 0.0, 1.1));
                float brokenBrush = smoothstep(0.36, 0.68, fibre)
                                  * smoothstep(0.16, 0.52, pigment);
                float verticalFace = 1.0 - abs(normalWS.y);
                tone *= 1.0 - brokenBrush * _BrushStrength * verticalFace * brushVisibility;

                // Fine pigment granulation is confined to surfaces, never the sky.
                float grainVisibility = 1.0 - smoothstep(0.018, 0.09, footprint);
                float grain = OhValueNoise3(p * 22.0 + 31.7) - 0.5;
                tone += grain * _GrainStrength * grainVisibility * (4.0 * tone * (1.0 - tone));

                // Tonal limits make each mountain layer independently art-directable.
                tone = lerp(min(_ToneFloor, _ToneCeiling), max(_ToneFloor, _ToneCeiling), saturate(tone));

                // Layered rock ink: generous untouched areas, broader wet pigment,
                // then a few narrow vertical and slanting strokes. Position-only
                // domains cross mesh facets coherently instead of tracing triangles.
                float3 strokeP = positionWS * max(abs(_StrokeScale), 0.001);
                float wetPigment = OhFbm3(strokeP * float3(0.37, 0.21, 0.37) + 8.13);
                float drifting = (wetPigment - 0.5) * 1.6 + (pigment - 0.5) * 0.35;
                float across = dot(strokeP.xz, float2(0.83, 0.56));
                float verticalStroke = DrawnStroke(across + drifting, strokeP.y * 0.23);
                float diagonalStroke = DrawnStroke(across * 0.63 + strokeP.y * 0.24 + drifting * 0.7 + 12.4,
                                                   strokeP.y * 0.17 - across * 0.035);
                float wetWash = smoothstep(0.37, 0.70, wetPigment);
                float inkMarks = max(verticalStroke, diagonalStroke * 0.64);
                float faceWeight = lerp(0.16, 1.0, saturate(verticalFace * 1.35));
                float strokeLoad = saturate(_StrokeStrength) * faceWeight;
                tone *= 1.0 - saturate(inkMarks * 0.88 + wetWash * 0.29) * strokeLoad;

                // Dry bristles reveal small patches of paper even in a dark mine
                // wall. They use the same pigment field; no separate noise overlay.
                float dryBrush = smoothstep(0.46, 0.71, fibre) * (1.0 - smoothstep(0.39, 0.66, wetPigment));
                tone += dryBrush * strokeLoad * brushVisibility * 0.095 * (1.0 - inkMarks);

                float normalView = saturate(dot(normalWS, viewWS));
                float rimWidth = max(_RimWidth, max(fwidth(normalView), 0.001));
                float rim = 1.0 - smoothstep(0.0, rimWidth, normalView);
                tone *= 1.0 - rim * _RimStrength;
                tone *= 1.0 - saturate(-normalWS.y) * _UndersideInk;

                // Pigment only: the path uses the ground's position, normal, AO and light.
                // No displaced ribbon, alpha clipping, extra shadow caster or second depth surface.
                [branch] if (_GroundPath > .5)
                {
                    float2 pathUV = (positionWS.xz - _GroundPathRect.xy) * _GroundPathRect.zw;
                    float inMask = step(0, pathUV.x) * step(pathUV.x, 1) * step(0, pathUV.y) * step(pathUV.y, 1);
                    float mask = SAMPLE_TEXTURE2D(_GroundPathMask, sampler_GroundPathMask, pathUV).r * inMask;
                    float pathLight = smoothstep(_InkPoint, max(_PaperPoint, _InkPoint + .001),
                        luminance + (pigment - .5) * _GroundPathVariation);
                    float pathTone = lerp(_GroundPathTones.x, _GroundPathTones.y, pathLight);
                    tone = lerp(tone, pathTone, mask);
                }
                // Keep the road's local contrast while steep rock carries additional ink.
                // This is a material response, not an extra atmosphere pass.
                tone *= lerp(1.0, 0.44, smoothstep(0.12, 0.55, verticalFace));
                float woodSlot = saturate(_UseWoodColor);
                float3 paperEnd = lerp(_OhPaperColor.rgb, _OhWoodColor.rgb, woodSlot);
                tone *= lerp(1.0, 0.7 + luminance * 0.3, _TintRetain * woodSlot);
                float3 color = lerp(_OhInkColor.rgb, paperEnd, saturate(tone));

                // Weathered ground pigment, not emitted light. Preserve luminance and lighting;
                // broad patchiness and slope response keep it from reading as a territory overlay.
                [branch] if (_RealmTintStrength > .001)
                {
                    float2 realmUV = (positionWS.xz - _GroundPathRect.xy) * _GroundPathRect.zw;
                    float3 realm = SAMPLE_TEXTURE2D(_RealmPigment, sampler_RealmPigment, realmUV).rgb;
                    float3 chroma = clamp(realm / max(dot(realm, float3(.2126,.7152,.0722)), .015), .35, 1.9);
                    float3 tinted = color * chroma;
                    tinted *= dot(color,float3(.2126,.7152,.0722)) / max(dot(tinted,float3(.2126,.7152,.0722)),.001);
                    float soil = lerp(.28,1.0,smoothstep(.32,.86,normalWS.y));
                    float mottling = lerp(.5,1.0,smoothstep(.22,.78,OhFbm3(positionWS*.009 + 74.2)));
                    color = lerp(color,tinted,_RealmTintStrength*soil*mottling);
                }

                // One continuous wash, with no height fog or hard depth bands.
                // Material controls retain local tonal design while the shared
                // paper palette gives the distant valley its empty breathing room.
                float distanceWS = distance(GetCameraPositionWS(), positionWS);
                float wash = smoothstep(_WashStart, max(_WashEnd, _WashStart + 0.001), distanceWS);
                wash *= saturate(_WashStrength);
                color = lerp(color, _OhPaperColor.rgb, wash);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVertex(Attributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionWS = positionWS;
                output.uv = input.uv;
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                output.positionCS = ApplyShadowClamping(positionCS);
                return output;
            }

            half4 ShadowFragment(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ClipPathEdge(input.positionWS, input.uv);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVertex(Attributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half DepthFragment(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ClipPathEdge(input.positionWS, input.uv);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex NormalsVertex
            #pragma fragment NormalsFragment
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct NormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            NormalsVaryings NormalsVertex(Attributes input)
            {
                NormalsVaryings output = (NormalsVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = NormalizeNormalPerVertex(TransformObjectToWorldNormal(input.normalOS));
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            void NormalsFragment(
                NormalsVaryings input,
                FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC,
                out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ClipPathEdge(input.positionWS, input.uv);
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS) * IS_FRONT_VFACE(facing, 1.0, -1.0);
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
                outNormalWS = half4(packedNormalWS, 0.0);
                #else
                outNormalWS = half4(normalWS, 0.0);
                #endif
                #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
