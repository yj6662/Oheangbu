#ifndef OH_INK_PAINTING_STUDY
#define OH_INK_PAINTING_STUDY

// Isolated study. Reuse the compact ground palette, slope policy and one atmosphere;
// the mountain alone receives the independent high-contrast ink/paper treatment.
#include "../CompactInkLandscape.hlsl"
#include "InkPaintingGeography.hlsl"
TEXTURE2D(_PaintedInkAtlas);
SAMPLER(sampler_PaintedInkAtlas);
TEXTURE2D(_PaintedRockWash);
SAMPLER(sampler_PaintedRockWash);
TEXTURE2D(_PaintedRoadBankField);
SAMPLER(sampler_PaintedRoadBankField);

// Prepare derivatives before clipping and per-pixel surface branches. Explicit
// gradients keep mip selection unchanged on coherent quads and defined at mass edges.
struct IPStudyDerivatives
{
    float3 positionDx,positionDy;
    float2 strokeDx,strokeDy;
    float2 reliefGradient;
    float4 geography;
    IPFormData form;
};
IPStudyDerivatives IPPrepareStudyDerivatives(float3 p,bool sampleGeography)
{
    IPStudyDerivatives result=(IPStudyDerivatives)0;
    result.positionDx=ddx(p);
    result.positionDy=ddy(p);
    // Read guidance and its derivatives while the quad is intact, before clipping.
    [branch] if(sampleGeography)result.geography=IPGeography(p);
    result.form=IPPrepareForm(p,result.geography);
    result.reliefGradient=float2(ddx(result.geography.a),ddy(result.geography.a))*256;
    // This condition is material-uniform, so all lanes prepare the legacy stroke
    // footprint together. The connected-wash mode does not pay for this noise.
    [branch] if(_PaintedWashEnabled<=.5)
    {
        float pigment=CINoise(p.xz*.005+p.y*.0015);
        float maxWidth=max(1,max(_PaintedInkScale.x,_PaintedInkScale.y));
        float2 q=float2(dot(p.xz,normalize(float2(.82,.572))),p.y);
        q.x+=(pigment-.5)*maxWidth*.22;
        result.strokeDx=ddx(q);
        result.strokeDy=ddy(q);
    }
    return result;
}
float IPStudyMountainMass(float4 geography,float3 n,float soil,float3 p,float distanceToCamera)
{
    float mass=max(CIMountainWeight(n,soil),geography.r*(1-saturate(soil)));
    [branch] if(_PaintedRoadBank>0&&distanceToCamera<_PaintedRoadBankParams.w)
    {
        float2 uv=(p.xz-_PaintedRoadBankRect.xy)/max(float2(1,1),_PaintedRoadBankRect.zw);
        float valid=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);
        float2 bank=SAMPLE_TEXTURE2D_LOD(_PaintedRoadBankField,sampler_PaintedRoadBankField,uv,0).rg;
        float height=bank.y/max(.0001,bank.x);
        float weight=saturate(bank.x)*valid*(1-CIProgress(abs(p.y-height),_PaintedRoadBankParams.xy))
            *(1-CIProgress(distanceToCamera,_PaintedRoadBankParams.zw));
        // Keep the existing near-bank support on gentle ground. On distant steep
        // faces only, release that support so a fitted road cannot paint a pale cliff.
        // Uses the same geometric normal, distance and bank fetch; no new sample.
        [branch] if(_PaintedRoadBankSteep>.5)
        {
            // Cosines are precomputed in the material, so this adds no trig ALU.
            float cosineSteep=saturate(_PaintedRoadBankSteepParams.y);
            float cosineGentle=max(cosineSteep+.0001,saturate(_PaintedRoadBankSteepParams.x));
            float steep=1-smoothstep(cosineSteep,cosineGentle,saturate(n.y));
            float start=max(30,_PaintedRoadBankSteepParams.z);
            float2 range=float2(start,max(start+1,_PaintedRoadBankSteepParams.w));
            weight*=1-steep*CIProgress(distanceToCamera,range);
        }
        mass*=1-weight*saturate(_PaintedRoadBank);
    }
    return mass;
}

float3 IPSampleCoat(float3 colour,float2 q,float2 qDx,float2 qDy,float2 cell,float2 tile)
{
    float a=CIHash(cell+7.19),b=CIHash(cell.yx+float2(13.7,31.4));
    float c=CIHash(cell*1.73+51.1);
    float2 centre=(cell+(float2(a,b)-.5)*clamp(_PaintedInkLayout.z,0,.35))*tile;
    float width=lerp(max(1,min(_PaintedInkScale.x,_PaintedInkScale.y)),max(1,max(_PaintedInkScale.x,_PaintedInkScale.y)),lerp(.50,1,b));
    float height=lerp(max(1,min(_PaintedInkScale.z,_PaintedInkScale.w)),max(1,max(_PaintedInkScale.z,_PaintedInkScale.w)),lerp(.45,1,c));
    float lean=(a-.5)*min(.12,width/height*.20);
    float2 local=q-centre;
    local.x-=local.y*lean;
    float2 uv=local/float2(width,height)+.5;
    float inside=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);
    float mirror=a>.5?-1:1;
    uv.x=(uv.x-.5)*mirror+.5;
    float2 cropLo=saturate(_PaintedInkTileCrop.xy),cropHi=saturate(_PaintedInkTileCrop.zw);
    float2 cropSize=max(.001,cropHi-cropLo);
    float2 uvDx=float2(qDx.x-qDx.y*lean,qDx.y)/float2(width,height)*cropSize*.5;
    float2 uvDy=float2(qDy.x-qDy.y*lean,qDy.y)/float2(width,height)*cropSize*.5;
    uvDx.x*=mirror;uvDy.x*=mirror;
    float index=min(3,floor(CIHash(cell+93.7)*4));
    float2 offset=float2(fmod(index,2),floor(index*.5))*.5;
    float2 atlasUV=offset+(cropLo+saturate(uv)*cropSize)*.5;
    // Use the stroke's continuous projected derivatives, not derivatives of hashed atlas
    // indices. Footprint-aware insets stop the neighbouring cell bleeding into thin margins.
    float2 pixelDx=uvDx*_PaintedInkAtlas_TexelSize.zw,pixelDy=uvDy*_PaintedInkAtlas_TexelSize.zw;
    float footprint=max(1,sqrt(max(dot(pixelDx,pixelDx),dot(pixelDy,pixelDy))));
    float2 inset=min(.08,_PaintedInkAtlas_TexelSize.xy*footprint*1.5);
    atlasUV=clamp(atlasUV,offset+inset,offset+.5-inset);
    float red=SAMPLE_TEXTURE2D_GRAD(_PaintedInkAtlas,sampler_PaintedInkAtlas,atlasUV,uvDx,uvDy).r;
    // R is linear mask data. Alpha is intentionally unused; white safely carries no ink.
    float mask=smoothstep(_PaintedInkResponse.x,max(_PaintedInkResponse.x+.001,_PaintedInkResponse.y),1-red)*inside;
    float load=lerp(max(0,_PaintedInkLoad.x),max(0,_PaintedInkLoad.y),c);
    float3 loadedInk=max(0,_PaintedInkBlack.rgb)*load;
    float3 result=lerp(colour,loadedInk,saturate(mask*_PaintedInkLoad.z));
    // Recover genuine dry-bristle reserves inside a broad ink body. A coarse mask
    // identifies loaded paint; high-frequency white fissures reopen paper there.
    // This is not a screen noise layer, and the original atlas remains unchanged.
    float averageRed=SAMPLE_TEXTURE2D_LOD(_PaintedInkAtlas,sampler_PaintedInkAtlas,atlasUV,4).r;
    float fissure=saturate((red-averageRed)*3.0)*smoothstep(.55,.85,1-averageRed)*inside;
    float3 dryPaper=_PaintedInkPaperTones.x*_PaintedInkPaperTint.rgb;
    return lerp(result,max(result,dryPaper),fissure*.60);
}

// Connected ink masses: bristle marks modulate pigment inside the large form;
// a complete stamp silhouette must never make an isolated white/black sticker.
// Texture-guided mixing of two adjacent ink tones only. The envelope vanishes
// at both ends, so flat loaded/middle/lit masses keep their original endpoints.
// A capped strength <=.45 keeps this blend monotone in t for a fixed texture value.
float IPAdjacentInkBlend(float t,float textureValue,float strength)
{
    float envelope=4*t*(1-t);
    return saturate(t+(saturate(textureValue)-.5)*envelope*clamp(strength,0,.45));
}

float3 IPLegacyMountain(float3 p,float3 n,IPStudyDerivatives derivatives)
{
    float pigment=CINoise(p.xz*.005+p.y*.0015);
    float wash=CINoise(p.xz*.0017+p.y*.0006);
    float broad=saturate(.46+dot(n,normalize(float3(-.55,.55,.36)))*.22+(pigment-.5)*.16);
    float first=smoothstep(.34,.46,broad);
    float second=smoothstep(.57,.68,broad);
    float tone=lerp(_PaintedInkMassTones.x,_PaintedInkMassTones.y,first);
    tone=lerp(tone,_PaintedInkMassTones.z,second);
    tone*=lerp(.8,1.16,wash);
    float3 colour=tone*_PaintedInkPaperTint.rgb;
    [branch] if(_PaintedWashEnabled>.5)
    {
        // Gravity-aligned brushwork on both vertical projection planes. A top-down
        // plane turns the long flanks into horizontal smears; do not use it for ink coats.
        float2 scale=rcp(max(float2(1,1),_PaintedWashTiling.xy));
        float2 weights=pow(max(abs(n.xz),.15),4);weights/=max(.001,weights.x+weights.y);
        float u=SAMPLE_TEXTURE2D_GRAD(_PaintedRockWash,sampler_PaintedRockWash,p.zy*scale+float2(.31,.17),derivatives.positionDx.zy*scale,derivatives.positionDy.zy*scale).r;
        float w=SAMPLE_TEXTURE2D_GRAD(_PaintedRockWash,sampler_PaintedRockWash,p.xy*scale+float2(.13,.59),derivatives.positionDx.xy*scale,derivatives.positionDy.xy*scale).r;
        float reserve=saturate((u*weights.x+w*weights.y-.035)*1.45);
        [branch] if(_PaintedEdgeWash>.5)
        {
            // Reuse the two existing rock-wash samples. Texture changes the
            // amount of neighbouring ink at each boundary, never adds white.
            float edgeTexture=saturate((reserve-saturate(_PaintedEdgeWashResponse.x))
                *max(0,_PaintedEdgeWashResponse.y)+.5);
            float edgeFirst=IPAdjacentInkBlend(first,edgeTexture,_PaintedEdgeWashStrengths.x);
            float edgeSecond=IPAdjacentInkBlend(second,edgeTexture,_PaintedEdgeWashStrengths.y);
            tone=lerp(_PaintedInkMassTones.x,_PaintedInkMassTones.y,edgeFirst);
            tone=lerp(tone,_PaintedInkMassTones.z,edgeSecond);
            tone*=lerp(.8,1.16,wash);
            colour=tone*_PaintedInkPaperTint.rgb;
        }
        // Loaded ink occupies most of the face; rare dry-brush reserves expose paper.
        // Keep the reserve shape from the physical brush bitmap, not a bright summit rim.
        // The bitmap deposits pigment inside the mountain's own light planes. Its white
        // paper is a reserve of the underlying mass, never a snowy white island.
        float paintTone=tone*lerp(.25,.90,smoothstep(.04,.75,reserve));
        paintTone+=smoothstep(.80,1,reserve)*min(.025,tone*.4);
        colour=lerp(colour,paintTone*_PaintedInkPaperTint.rgb,saturate(_PaintedWashTiling.z));
    }
    else
    {
    float maxWidth=max(1,max(_PaintedInkScale.x,_PaintedInkScale.y));
    float maxHeight=max(1,max(_PaintedInkScale.z,_PaintedInkScale.w));
    float2 q=float2(dot(p.xz,normalize(float2(.82,.572))),p.y);
    // Bend the bristles with a broad world-fixed wash; no camera-space movement.
    q.x+=(pigment-.5)*maxWidth*.22;
    float2 qDx=derivatives.strokeDx,qDy=derivatives.strokeDy;
    float2 tile=float2(maxWidth*max(.75,_PaintedInkLayout.x),maxHeight*max(.64,_PaintedInkLayout.y));
    float2 cell=floor(q/tile);
    float3 painted=colour;
    [unroll] for(int y=0;y<2;y++)
    {
        [unroll] for(int x=0;x<2;x++)
            painted=IPSampleCoat(painted,q,qDx,qDy,cell+float2(x,y),tile);
    }
    // Restrain the white bristle reserves to the current mass value, so the painting
    // never turns a whole mountainside into disconnected white paper rectangles.
    float lum=CILuma(painted);
    float grain=saturate((lum-.009)/.19);
    float darken=lerp(.80,1,first)*lerp(.62,1,grain);
    colour*=darken;
    colour+=_PaintedInkPaperTint.rgb*min(_PaintedInkMassTones.w,grain*_PaintedInkMassTones.w*1.7)*first;
    }
    return colour;
}

// Distinct form-led mode: the broad load comes from relief/aspect or an authored
// mask. The old rock bitmap can only open a little dry ink inside that load.
float3 IPFormMountain(float3 p,IPStudyDerivatives derivatives)
{
    IPFormData form=derivatives.form;
    float relief=saturate(derivatives.geography.a)*256;
    float2 direction=_PaintedFormDirection.xy;
    direction=dot(direction,direction)>.0001?normalize(direction):float2(.8,.6);
    float side=smoothstep(-.65,.65,dot(form.flow,direction));
    float loaded=max(0,_PaintedFormTones.x)*lerp(.65,1.6,side);
    float middle=max(loaded,_PaintedFormTones.y);
    float lower=max(middle,_PaintedFormTones.z);
    float tone=lerp(lower,middle,CIProgress(relief,float2(12,60)));
    tone=lerp(tone,loaded,form.load);
    // Relief is a terrain scalar potential, not a position multiplied by varying
    // flow angle. Both projections share it: no rotating world-origin phase seam.
    // This follows approximate downhill height change, not exact integrated flow.
    float2 scale=rcp(max(float2(1,1),_PaintedFormBrush.xy));
    float2 weights=pow(max(abs(form.flow),.08),4);weights/=weights.x+weights.y;
    float2 uvX=float2(p.z,relief)*scale+float2(.31,.17);
    float2 uvZ=float2(p.x,relief)*scale+float2(.13,.59);
    float2 dxX=float2(derivatives.positionDx.z,derivatives.reliefGradient.x)*scale;
    float2 dyX=float2(derivatives.positionDy.z,derivatives.reliefGradient.y)*scale;
    float2 dxZ=float2(derivatives.positionDx.x,derivatives.reliefGradient.x)*scale;
    float2 dyZ=float2(derivatives.positionDy.x,derivatives.reliefGradient.y)*scale;
    float u=SAMPLE_TEXTURE2D_GRAD(_PaintedRockWash,sampler_PaintedRockWash,uvX,dxX,dyX).r;
    float w=SAMPLE_TEXTURE2D_GRAD(_PaintedRockWash,sampler_PaintedRockWash,uvZ,dxZ,dyZ).r;
    // Opt-in broad brush bodies redistribute existing ink tones. Both sampled
    // projections already exist above; no new fetch, paper colour or surface mask.
    float bodyStrength=saturate(_PaintedFormStrokeBody)*smoothstep(12,45,relief);
    [branch] if(bodyStrength>0)
    {
        float brushValue=saturate(u*weights.x+w*weights.y);
        float inkToMiddle=smoothstep(.10,.52,brushValue);
        float middleToLower=smoothstep(.54,.88,brushValue);
        float formLoad=saturate(form.load);
        // Fully loaded authored forms remain dark: even the light brush pigment
        // reaches only 35% of the loaded-to-middle interval, never the lower tone.
        float loadedCeiling=lerp(loaded,middle,.35);
        float brushMiddle=lerp(middle,loadedCeiling,formLoad);
        float brushLower=lerp(lower,loadedCeiling,formLoad);
        float brushTone=lerp(loaded,brushMiddle,inkToMiddle);
        brushTone=lerp(brushTone,brushLower,middleToLower);
        tone=lerp(tone,brushTone,bodyStrength);
    }
    float dry=smoothstep(_PaintedFormBrush.z,max(_PaintedFormBrush.z+.001,_PaintedFormBrush.w),saturate(u*weights.x+w*weights.y));
    tone+=dry*form.load*form.dry*clamp(_PaintedFormTones.w,0,.025);
    return tone*_PaintedInkPaperTint.rgb;
}
float3 IPMountain(float3 p,float3 n,IPStudyDerivatives derivatives)
{
    float3 result=float3(0,0,0);
    [branch] if(derivatives.form.active>=1)result=IPFormMountain(p,derivatives);
    else
    {
        result=IPLegacyMountain(p,n,derivatives);
        [branch] if(derivatives.form.active>0)
            result=lerp(result,IPFormMountain(p,derivatives),derivatives.form.active);
    }
    return result;
}

#if defined(IP_STUDY_PATH_MOUNTAIN_EXCHANGE)
// Only the Ground shader offers this exchange. Its grass/dirt RGB no longer
// contributes after the complete detail fade, so their two fetches can fund the
// same two form-wash fetches on full-soil pixels. Form must be fully active;
// mixed legacy/form mode could require more than two mountain texture fetches.
float IPStudyPathMountainWeight(float soil,float geographyMass,float d)
{
    float weight=0;
    [branch] if(_PaintedPathMountain>.5&&_CIEnabled>.5&&_CIDarkNear>.5
        &&_PaintedInkEnabled>.5&&_PaintedFormEnabled>.5&&_PaintedGeoEnabled>.5
        &&soil>.001&&geographyMass>=.05)
    {
        float detailEnd=max(_CIDetailRange.y,_CIDetailRange.x+1);
        float start=max(max(250,detailEnd),_PaintedPathMountainRange.x);
        float2 range=float2(start,max(start+1,_PaintedPathMountainRange.y));
        weight=saturate(soil)*saturate(geographyMass)*CIProgress(d,range)
            *saturate(_PaintedPathMountainRange.z);
    }
    return weight;
}
#endif

float3 IPStudyGroundDark(float3 albedo,float3 p,float3 n,float soil,float shade,float detailShade,float d,float mountainWeight,float geographyMass,IPStudyDerivatives derivatives)
{
    // Same ground response as CIGroundDark, including near dirt/normal detail and chroma.
    float pigment=CINoise(p.xz*.045)*.7+CINoise(p.xz*.13+7.1)*.3;
    float pigmentContrast=_CIPainterly>.5?max(0,_CIPigmentContrast.y):.45;
    float tone=saturate(.45+(pigment-.5)*pigmentContrast+(shade-.5)*.32);
    float lo=lerp(_CITones.x,_CITones.z,soil),hi=lerp(_CITones.y,_CITones.w,soil);
    float nearDetail=1-CIProgress(d,_CIDetailRange.xy);
    float textureInk=max(0,.5-CILuma(albedo))*_CIDetailContrast;
    float normalInk=max(0,shade-detailShade)*.045;
    tone=lerp(lo,hi,tone)-(textureInk+normalInk)*nearDetail;
    float3 colour=lerp(_CIInk.rgb,_CIPaper.rgb,saturate(tone));
    float beforeLuma=CILuma(colour);
    float3 chroma=clamp(albedo/max(.02,CILuma(albedo)),.65,1.35);
    colour*=lerp(1,chroma,.20*nearDetail);
    colour*=beforeLuma/max(.001,CILuma(colour));
    float3 mountain=colour;
    float pathMountainWeight=0;
    #if defined(IP_STUDY_PATH_MOUNTAIN_EXCHANGE)
    pathMountainWeight=IPStudyPathMountainWeight(soil,geographyMass,d);
    #endif
    // Reuse this one mountain result at mixed soil edges. Full-soil Ground
    // pixels exchange two otherwise-unused albedo fetches for this one call.
    [branch] if(mountainWeight>0||pathMountainWeight>0)
    {
        [branch] if(_PaintedInkEnabled>.5)
        {
            // Preserve every nonzero near path blend, not only fully opaque soil.
            [branch] if(d<=250&&soil>0)mountain=IPLegacyMountain(p,n,derivatives);
            else mountain=IPMountain(p,n,derivatives);
        }
        else mountain=CIDarkMountain(p,n,d);
    }
    // Atmosphere is deliberately absent here. Both derived forward passes call the shared
    // CIAtmosphere once after their original regional pigment step, just like their parents.
    float3 result=lerp(colour,mountain,mountainWeight);
    [branch] if(pathMountainWeight>0)
    {
        // The exact same-coordinate form/body/dry RGB, before regional tint and
        // the single shared atmosphere. This branch replaces the constant cap.
        result=lerp(result,mountain,pathMountainWeight);
    }
    else [branch] if(_PaintedFarPath>.5&&d>30)
    {
        // Reuse the caller's geography sample. Roads suppress mountainWeight,
        // so that weight cannot identify a road crossing a mountain here.
        float start=max(30,_PaintedFarPathDistance.x);
        float2 range=float2(start,max(start+.001,_PaintedFarPathDistance.y));
        float weight=saturate(soil)*saturate(geographyMass)*CIProgress(d,range)
            *saturate(_PaintedFarPathAppearance.y);
        [branch] if(weight>0)
        {
            float luma=CILuma(result);
            float ceiling=max(.001,_PaintedFarPathAppearance.x);
            float scale=min(1,ceiling/max(.001,luma));
            // A hue-preserving ceiling, never a white wash or a path cutout.
            result=lerp(result,result*scale,weight);
        }
    }
    return result;
}
// The painting reference retains loaded black even on selected distant faces.
// Preserve the same continuous distance wash, but limit its strength in dense mountain
// pigment. The default-off reserve preserves this existing response. When enabled,
// its distant relative-relief selection is shared with foliage; near ground is unchanged.
// The surface mountain mask intentionally excludes soil. For distant path air
// only, reuse the already sampled mountain geography so roads do not behave as
// unrelated flat ground. Dense-ink retention still also depends on the path RGB.
float IPStudyAtmosphereMass(float mountainMass,float soil,float geographyMass,float d)
{
    float result=mountainMass;
    float pathMountainWeight=0;
    #if defined(IP_STUDY_PATH_MOUNTAIN_EXCHANGE)
    pathMountainWeight=IPStudyPathMountainWeight(soil,geographyMass,d);
    #endif
    [branch] if(pathMountainWeight>0)
    {
        // Couple surface and retention blends. IPStudyAtmosphere is still
        // called once by the original forward pass, including existing reserves.
        result=lerp(mountainMass,max(mountainMass,saturate(geographyMass)),pathMountainWeight);
    }
    else [branch] if(_PaintedFarPathAir>.5&&_CIDarkNear>.5&&d>30)
    {
        float start=max(30,_PaintedFarPathDistance.x);
        float2 range=float2(start,max(start+.001,_PaintedFarPathDistance.y));
        float road=saturate(soil)*CIProgress(d,range)*saturate(_PaintedFarPathAppearance.y);
        result=lerp(mountainMass,max(mountainMass,saturate(geographyMass)),road);
    }
    return result;
}
float3 IPStudyAtmosphere(float3 colour,float d,float mass,float4 geography,float3 p,IPFormData form)
{
    return IPGeographyInkAtmosphere(colour,d,mass,geography,p,form);
}
#endif
