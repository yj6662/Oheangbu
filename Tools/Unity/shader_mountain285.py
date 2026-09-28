"""Local URP Terrain derivative. Never modify the shared renderer or source shaders."""
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];A=ROOT/'Oheangbu/Assets/_Project/Art/World'
dst=A/'MountainTrail285';src=A/'HybridMountain284'
shader=(src/'HybridTerrain284.shader').read_text().replace('HybridTerrain284','MountainTerrain285').replace('HybridTerrainPasses284.hlsl','TerrainPasses285.hlsl').replace('Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitDepthNormalsPass.hlsl','TerrainNormals285.hlsl')
(dst/'MountainTerrain285.shader').write_text(shader)
body=(src/'HybridTerrainPasses284.hlsl').read_text();body='#include "GraniteSurface285.hlsl"\n'+body
body=body.replace('    normalTS = normalize(lerp(normalTS, half3(0,0,1), splatControl.r));','')
a=body.index('    // Stable world-space projection:');b=body.index('\n#endif',a)
body=body[:a]+'''    GraniteSample285 rock = Granite285(IN.positionWS, inputData.normalWS, 1.0/2.7, half3(1.05,1.08,1.05), TEXTURE2D_ARGS(_Splat0,sampler_Splat0), TEXTURE2D_ARGS(_Normal0,sampler_Normal0), TEXTURE2D_ARGS(_Mask0,sampler_Mask0));
    half3 soil = SAMPLE_TEXTURE2D(_Splat1,sampler_Splat0,IN.positionWS.xz/1.7).rgb;
    soil = lerp(dot(soil,half3(.3,.59,.11)).xxx,soil,.15);
    soil = lerp(half3(.16,.17,.155),soil,1-smoothstep(12,75,distance(IN.positionWS,_WorldSpaceCameraPos)));
    albedo = rock.albedo*splatControl.r+soil*splatControl.g;
    inputData.normalWS = normalize(lerp(inputData.normalWS,rock.normal,splatControl.r));
    smoothness = rock.smoothness*splatControl.r; metallic=0;
    occlusion = lerp(1,rock.occlusion,splatControl.r);
'''+body[b:]
body=body.replace('InitializeInputData(IN, normalTS, inputData);','InitializeInputData(IN, lerp(normalTS,half3(0,0,1),splatControl.r), inputData);')
body=body.replace('max(inputData.bakedGI, half3(.19,.20,.185))','clamp(inputData.bakedGI, half3(.24,.25,.24),half3(.36,.37,.36))')
(dst/'TerrainPasses285.hlsl').write_text(body)
package=next((ROOT/'Oheangbu/Library/PackageCache').glob('com.unity.render-pipelines.universal@*'))
normal=(package/'Shaders/Terrain/TerrainLitDepthNormalsPass.hlsl').read_text().replace('Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitPasses.hlsl','TerrainPasses285.hlsl')
normal=normal.replace('float4 clipPos                  : SV_POSITION;','float3 positionWS : TEXCOORD6;\n    float4 clipPos : SV_POSITION;')
normal=normal.replace('o.clipPos = attributes.positionCS;','o.positionWS = attributes.positionWS;\n    o.clipPos = attributes.positionCS;')
a=normal.index('    half3 normalTS =');b=normal.index('    outNormalWS =',a)
normal=normal[:a]+'''    half3 normalTS=half3(0,0,1);
    NormalMapMix(IN.uvSplat01,IN.uvSplat23,splatControl,normalTS);
    normalTS=lerp(normalTS,half3(0,0,1),splatControl.r);
    half3 normalWS;
#if defined(_NORMALMAP) && !defined(ENABLE_TERRAIN_PERPIXEL_NORMAL)
    normalWS=TransformTangentToWorld(normalTS,half3x3(-IN.tangent.xyz,IN.bitangent.xyz,IN.normal.xyz));
#elif defined(ENABLE_TERRAIN_PERPIXEL_NORMAL)
    float2 sampleCoords=(IN.uvMainAndLM.xy/_TerrainHeightmapRecipSize.zw+.5)*_TerrainHeightmapRecipSize.xy;
    half3 geometric=TransformObjectToWorldNormal(normalize(SAMPLE_TEXTURE2D(_TerrainNormalmapTexture,sampler_TerrainNormalmapTexture,sampleCoords).rgb*2-1));
    half3 tangent=cross(GetObjectToWorldMatrix()._13_23_33,geometric);
    normalWS=TransformTangentToWorld(normalTS,half3x3(-tangent,cross(geometric,tangent),geometric));
#else
    normalWS=IN.normal.xyz;
#endif
    normalWS=NormalizeNormalPerPixel(normalWS);
    GraniteSample285 rock=Granite285(IN.positionWS,normalWS,1.0/2.7,half3(1,1,1),TEXTURE2D_ARGS(_Splat0,sampler_Splat0),TEXTURE2D_ARGS(_Normal0,sampler_Normal0),TEXTURE2D_ARGS(_Mask0,sampler_Mask0));
    normalWS=normalize(lerp(normalWS,rock.normal,splatControl.r));
'''+normal[b:]
(dst/'TerrainNormals285.hlsl').write_text(normal)
(dst/'LICENSE-URP.md').write_text((src/'LICENSE-URP.md').read_text())
print('Local two-layer Terrain color/normal passes prepared.')
