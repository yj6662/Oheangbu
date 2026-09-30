#ifndef OH_COMPACT_INK_LANDSCAPE
#define OH_COMPACT_INK_LANDSCAPE
// Shared by the opt-in ground, background and foliage materials. No screen-space fog pass.
float CIProgress(float d,float2 range){return smoothstep(range.x,max(range.x+1,range.y),d);}
float CILuma(float3 c){return dot(c,float3(.2126,.7152,.0722));}
float CIHash(float2 p){p=frac(p*float2(.1031,.11369));p+=dot(p,p.yx+19.19);return frac(p.x*p.y);}
float CINoise(float2 p)
{
    float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(CIHash(i),CIHash(i+float2(1,0)),f.x),lerp(CIHash(i+float2(0,1)),CIHash(i+1),f.x),f.y);
}
float3 CIMountain(float3 p,float3 n)
{
    float pigment=CINoise(p.xz*.006+p.y*.002)*.7+CINoise(p.xz*.017)*.3;
    float broad=saturate(dot(n,normalize(float3(-.35,.8,.28)))*.5+.2+(pigment-.5)*.18);
    return lerp(_CIInk.rgb,_CIPaper.rgb,lerp(.10,.37,broad));
}
float CIEvaluateMountainTone(float broad,float detailInk,float d)
{
    // The broad ink mass is present from the nearest surface. Only subtractive detail fades.
    // CPU parity: CompactInkLandscapeProfile.EvaluateDarkMountainTone.
    float nearDetail=1-CIProgress(d,float2(_CIDetailRange.x,_CIRockRange.y));
    return saturate(lerp(_CIMountainTones.x,_CIMountainTones.y,saturate(broad))-.035*saturate(detailInk)*nearDetail);
}
float CIDrawnStroke(float across,float along,float seedOffset)
{
    // Adapt the old interrupted/tapered DrawnStroke at a landscape-sized scale.
    // Fixed world-space edges avoid camera-dependent fwidth widening or screen-space crawl.
    float spacingLo=max(2,min(_CIStrokeSpacingWidth.x,_CIStrokeSpacingWidth.y));
    float spacingHi=max(spacingLo,max(_CIStrokeSpacingWidth.x,_CIStrokeSpacingWidth.y));
    float pitch=(spacingLo+spacingHi)*.5;
    float column=floor(across/pitch);
    float columnSeed=CIHash(float2(column,seedOffset));
    // Adjacent centre gaps stay between the configured spacing limits.
    float centre=(column+.5)*pitch+(columnSeed-.5)*(spacingHi-spacingLo)*.5;
    along=along/(pitch*2.4)+columnSeed*1.73;
    float segment=floor(along),segmentSeed=CIHash(float2(column+seedOffset,segment+seedOffset*2));
    float widthLo=max(.1,min(_CIStrokeSpacingWidth.z,_CIStrokeSpacingWidth.w));
    float widthHi=max(widthLo,max(_CIStrokeSpacingWidth.z,_CIStrokeSpacingWidth.w));
    float width=min(lerp(widthLo,widthHi,segmentSeed),pitch*.7);
    float t=frac(along);
    float taper=smoothstep(.025,.16,t)*(1-smoothstep(.63,.97,t));
    float halfWidth=width*.5*taper;
    float softEdge=max(.35,width*.18);
    float stroke=1-smoothstep(max(0,halfWidth-softEdge),halfWidth+softEdge,abs(across-centre));
    return stroke*taper*smoothstep(.2,.36,segmentSeed);
}
float CIPainterlyBrushInk(float3 p,float drySample)
{
    float across=dot(p.xz,normalize(float2(.83,.56)));
    float vertical=CIDrawnStroke(across,p.y,5.71);
    float diagonal=CIDrawnStroke(across*.94+p.y*.34+12.4,p.y*.94-across*.34,13.19);
    // Dry bristles open holes in the ink; they never add a bright layer that later vanishes.
    float dryPaper=smoothstep(.60,.85,drySample);
    return max(vertical,diagonal*.64)*(1-dryPaper);
}
float CIEvaluatePainterlyMountainTone(float broad,float detailInk,float pigment,float brushInk,float d)
{
    float baseTone=lerp(_CIMountainTones.x,_CIMountainTones.y,saturate(broad));
    baseTone=clamp(baseTone+(saturate(pigment)*2-1)*max(0,_CIPigmentContrast.x),
        min(_CIMountainTones.x,_CIMountainTones.y),max(_CIMountainTones.x,_CIMountainTones.y));
    float nearDetail=1-CIProgress(d,float2(_CIDetailRange.x,_CIRockRange.y));
    float brush=saturate(brushInk)*max(0,_CIStrokeFadeStrength.z)*(1-CIProgress(d,_CIStrokeFadeStrength.xy));
    return saturate(baseTone-.035*saturate(detailInk)*nearDetail-brush);
}
float CIBroadStroke(float2 q,float2 cell,float2 tile,float seed)
{
    float a=CIHash(cell+seed),b=CIHash(cell.yx+float2(seed*2,seed+9.7));
    float c=CIHash(cell*1.73+seed+21);
    float2 centre=(cell+(float2(a,b)-.5)*.40)*tile;
    float width=lerp(max(1,min(_CIBroadBrushScale.x,_CIBroadBrushScale.y)),
        max(1,max(_CIBroadBrushScale.x,_CIBroadBrushScale.y)),b);
    float strokeLength=lerp(max(1,min(_CIBroadBrushScale.z,_CIBroadBrushScale.w)),
        max(1,max(_CIBroadBrushScale.z,_CIBroadBrushScale.w)),c);
    float2 local=q-centre;
    float t=local.y/strokeLength+.5;
    float curve=(t-.5)*(t-.5)*width*(a-.5)*1.6;
    float tiltLimit=min(.28,width/strokeLength*.60);
    float across=local.x-local.y*(a-.5)*tiltLimit-curve;
    // Keep a loaded broad body for 42-64% of the stroke. Only the remaining tail tapers;
    // varying that point avoids a repeated triangle silhouette across the mountain face.
    float tailStart=lerp(.42,.64,b);
    float taper=lerp(1,.10,smoothstep(tailStart,1,t));
    float halfWidth=width*.5*taper;
    float edge=width*.5*clamp(_CIBroadBrushCoverage.y,.03,.35);
    float side=1-smoothstep(max(0,halfWidth-edge),halfWidth+edge,abs(across));
    float ends=smoothstep(0,.07,t)*(1-smoothstep(.90,1,t));
    return saturate(side*ends*lerp(.80,1,c));
}
float CIBroadBrushLayer(float2 q,float seed)
{
    // Four neighbouring jittered brush centres have bounded support. Including neighbours
    // makes the field continuous across cells and independent of mesh/chunk boundaries.
    float2 tile=float2(max(1,max(_CIBroadBrushScale.x,_CIBroadBrushScale.y))*1.4,
        max(1,max(_CIBroadBrushScale.z,_CIBroadBrushScale.w))*.65);
    float2 cell=floor(q/tile);
    float coat=0;
    [unroll] for(int y=0;y<2;y++)
    {
        [unroll] for(int x=0;x<2;x++)
        {
            float stroke=CIBroadStroke(q,cell+float2(x,y),tile,seed);
            coat+=stroke*(1-coat);
        }
    }
    return saturate(coat);
}
float CIBroadBrushInk(float3 p,float pigment)
{
    float warp=CINoise(p.xz*.0018+p.y*.001);
    float2 primary=float2(dot(p.xz,float2(.82,.572)),p.y+dot(p.xz,float2(-.572,.82))*.24);
    float2 secondary=float2(dot(p.xz,float2(-.48,.877))-p.y*.28,
        p.y*.96+dot(p.xz,float2(.877,.48))*.15);
    float width=max(1,max(_CIBroadBrushScale.x,_CIBroadBrushScale.y));
    float strokeLength=max(1,max(_CIBroadBrushScale.z,_CIBroadBrushScale.w));
    float bend=saturate(_CIBroadBrushCoverage.w);
    primary+=float2((pigment-.5)*width*bend,(warp-.5)*strokeLength*.06);
    secondary+=float2((warp-.5)*width*bend,(pigment-.5)*strokeLength*.06)+float2(173.7,291.3);
    float first=CIBroadBrushLayer(primary,5.71);
    float second=CIBroadBrushLayer(secondary,23.19)*saturate(_CIBroadBrushCoverage.z);
    return saturate(first+second*(1-first));
}
float CIEvaluateBroadMountainTone(float broad,float pigment,float coatCoverage)
{
    // The legacy mountain shader groups light into three ink/paper masses. Keep that
    // principle at landscape scale; the large painted coats supply the surface form.
    float shade=saturate(broad),halfWidth=max(.001,_CIBroadBrushBands.z*.5);
    float bands=.5*smoothstep(_CIBroadBrushBands.x-halfWidth,_CIBroadBrushBands.x+halfWidth,shade)
        +.5*smoothstep(_CIBroadBrushBands.y-halfWidth,_CIBroadBrushBands.y+halfWidth,shade);
    float banded=lerp(shade,bands,saturate(_CIBroadBrushBands.w));
    float baseTone=lerp(_CIMountainTones.x,_CIMountainTones.y,banded);
    pigment=saturate(pigment);
    baseTone=clamp(baseTone+(pigment-.5)*.012,min(_CIMountainTones.x,_CIMountainTones.y),max(_CIMountainTones.x,_CIMountainTones.y));
    float coatTone=lerp(_CIBroadBrushTones.x,_CIBroadBrushTones.y,saturate(banded*.65+pigment*.35));
    return saturate(lerp(baseTone,min(baseTone,coatTone),saturate(coatCoverage)*saturate(_CIBroadBrushCoverage.x)));
}
float3 CIDarkMountain(float3 p,float3 n,float d)
{
    float pigment=CINoise(p.xz*.006+p.y*.002)*.7+CINoise(p.xz*.017)*.3;
    float broad=saturate(dot(n,normalize(float3(-.35,.8,.28)))*.5+.2+(pigment-.5)*.18);
    float tone=0;
    [branch] if(_CIBroadBrush>.5)
    {
        tone=CIEvaluateBroadMountainTone(broad,pigment,CIBroadBrushInk(p,pigment));
    }
    else
    {
        // The broad-coat revision replaces both the small marks and the fine near pigment.
        float detailInk=CINoise(p.xz*.16+p.y*.035);
        tone=CIEvaluateMountainTone(broad,detailInk,d);
        [branch] if(_CIPainterly>.5)
        {
            float brushInk=0;
            [branch] if(d<max(_CIStrokeFadeStrength.x+1,_CIStrokeFadeStrength.y))
                brushInk=CIPainterlyBrushInk(p,detailInk);
            tone=CIEvaluatePainterlyMountainTone(broad,detailInk,pigment,brushInk,d);
        }
    }
    return lerp(_CIInk.rgb,_CIPaper.rgb,tone);
}
float CIMountainWeight(float3 n,float soil)
{
    // Use the geometric normal: normal maps must not turn a flat path into a hillside.
    float slope=degrees(acos(saturate(n.y)));
    return smoothstep(_CIMountainSlope.x,max(_CIMountainSlope.x+.01,_CIMountainSlope.y),slope)*(1-saturate(soil));
}
float3 CIGroundDark(float3 albedo,float3 p,float3 n,float soil,float shade,float detailShade,float d)
{
    float pigment=CINoise(p.xz*.045)*.7+CINoise(p.xz*.13+7.1)*.3;
    float pigmentContrast=_CIPainterly>.5?max(0,_CIPigmentContrast.y):.45;
    float tone=saturate(.45+(pigment-.5)*pigmentContrast+(shade-.5)*.32);
    float lo=lerp(_CITones.x,_CITones.z,soil),hi=lerp(_CITones.y,_CITones.w,soil);
    float nearDetail=1-CIProgress(d,_CIDetailRange.xy);
    // Retain soil texture and shallow normal relief as darker pigment, never a bright near layer
    // that would disappear into darker mid-distance terrain.
    float textureInk=max(0,.5-CILuma(albedo))*_CIDetailContrast;
    float normalInk=max(0,shade-detailShade)*.045;
    tone=lerp(lo,hi,tone)-(textureInk+normalInk)*nearDetail;
    float3 colour=lerp(_CIInk.rgb,_CIPaper.rgb,saturate(tone));
    float beforeLuma=CILuma(colour);
    float3 chroma=clamp(albedo/max(.02,CILuma(albedo)),.65,1.35);
    colour*=lerp(1,chroma,.20*nearDetail);
    colour*=beforeLuma/max(.001,CILuma(colour));
    // No distance-dependent replacement by a darker mountain palette in this revision.
    float mountainWeight=CIMountainWeight(n,soil);
    float3 mountain=colour;
    // Flat ground and full path masks pay no mountain pigment/brushwork cost.
    [branch] if(mountainWeight>0)mountain=CIDarkMountain(p,n,d);
    return lerp(colour,mountain,mountainWeight);
}
float3 CIGround(float3 albedo,float3 p,float3 n,float soil,float shade,float d)
{
    float pigment=CINoise(p.xz*.045)*.7+CINoise(p.xz*.13+7.1)*.3;
    float tone=saturate(.45+(pigment-.5)*.45+(shade-.5)*.32);
    float lo=lerp(_CITones.x,_CITones.z,soil),hi=lerp(_CITones.y,_CITones.w,soil);
    float nearDetail=1-CIProgress(d,_CIDetailRange.xy);
    tone=lerp(lo,hi,tone)+(CILuma(albedo)-.3)*_CIDetailContrast*nearDetail;
    float3 colour=lerp(_CIInk.rgb,_CIPaper.rgb,saturate(tone));
    float3 chroma=clamp(albedo/max(.02,CILuma(albedo)),.65,1.35);
    colour*=lerp(1,chroma,.20*nearDetail);
    // Broad mountain silhouettes replace close rock texture, not the terrain geometry.
    float rock=1-smoothstep(.4,.72,n.y);
    float rockTone=.075+shade*.18+(pigment-.5)*.05+(CILuma(albedo)-.3)*_CIDetailContrast*nearDetail;
    float3 rockColour=lerp(_CIInk.rgb,_CIPaper.rgb,saturate(rockTone));
    rockColour=lerp(rockColour,CIMountain(p,n),CIProgress(d,_CIRockRange.xy));
    colour=lerp(colour,rockColour,rock);
    return lerp(colour,CIMountain(p,n),CIProgress(d,_CIMountainRange.xy));
}
float3 CIAttenuateChroma(float3 c,float d)
{
    float l=CILuma(c);float3 neutral=lerp(_CIInk.rgb,_CIPaper.rgb,saturate(l));
    neutral*=l/max(.001,CILuma(neutral));
    return lerp(c,neutral,CIProgress(d,_CIMountainRange.xy));
}
float3 CIAtmosphere(float3 colour,float d)
{
    // One wash, shared by terrain and every foliage LOD; no double fogging.
    float wash=.24*CIProgress(d,float2(_CIRockRange.x,_CIMountainRange.y))+.68*CIProgress(d,_CIAirRange.xy);
    if(_CIAtmosphereOverride>.5)
        wash=max(0,_CIAirStrengths.x)*CIProgress(d,_CIMidAirRange.xy)+max(0,_CIAirStrengths.y)*CIProgress(d,_CIFarAirRange.xy);
    // A custom atmosphere darker than the surface must not reverse the distance hierarchy.
    float3 air=_CIDarkNear>.5?max(colour,_CIAir.rgb):_CIAir.rgb;
    return lerp(colour,air,saturate(wash));
}
#endif
