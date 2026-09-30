#include "Assets/_Project/Art/World/Architecture296/Shaders/Stochastic296.hlsl"
// Opt-in candidate geology. All coordinates are metres, independent of mesh UV/scale.
float RuleWeight276(float distanceWS)
{
    return _StrataStrength276;
}
// Authored rock / colluvium / moisture / path weights from the final world surface.
float3 RuleMasks276(float3 p,float3 n,float path)
{
    float4 field=SAMPLE_TEXTURE2D(_StrataField276,sampler_Surface296_linear_repeat_aniso8,saturate(p.xz/float2(4000,6000)));
    float rock=saturate(field.r+smoothstep(.45,.8,1-n.y)*.35);
    rock*=1-field.a*.5;
    float debris=saturate(field.g)*(1-rock);
    return float3(rock,debris,1-rock-debris);
}
float3 RuleJointCoords276(float3 p)
{
    // Low-amplitude, continuous rock-joint deflection breaks the square repeat.
    // The same stationary field is used for cavity and normal; no screen/time warp.
    return p*.25+.23*float3(sin(p.y*.19+sin(p.z*.27)),
        sin(p.z*.21+sin(p.x*.23)),sin(p.x*.17+sin(p.y*.29)));
}
// #296 channel-aligned stochastic coordinates. Derivatives are evaluated before branches.
float3 RuleRockNormal276(float3 p,float3 n,float detail,float3 dx,float3 dy)
{
    float3 w=pow(abs(n),4);w/=max(.001,w.x+w.y+w.z);
    float3 q=RuleJointCoords276(p);
    // The joint warp is continuous; its gradient belongs to the original coordinates.
    float3 qdx=RuleJointCoords276(p+dx)-q,qdy=RuleJointCoords276(p+dy)-q;
    SurfaceFrame296 fx=SurfaceFrameGrad296(q.zy,qdx.zy,qdy.zy,19u,0);
    SurfaceFrame296 fy=SurfaceFrameGrad296(q.xz,qdx.xz,qdy.xz,19u,0);
    SurfaceFrame296 fz=SurfaceFrameGrad296(q.xy,qdx.xy,qdy.xy,19u,0);
    float3 x=SurfaceNormal296(TEXTURE2D_ARGS(_StrataFractureNormal276,sampler_Surface296_linear_repeat_aniso8),fx,1.25*detail);
    float3 y=SurfaceNormal296(TEXTURE2D_ARGS(_StrataFractureNormal276,sampler_Surface296_linear_repeat_aniso8),fy,1.25*detail);
    float3 z=SurfaceNormal296(TEXTURE2D_ARGS(_StrataFractureNormal276,sampler_Surface296_linear_repeat_aniso8),fz,1.25*detail);
    float3 perturb=float3(0,x.y,x.x)*w.x+float3(y.x,0,y.y)*w.y+float3(z.x,z.y,0)*w.z;
    // The fine rock normal and the rock colour share the same projection and seed.
    fx=SurfaceFrameGrad296(q.zy,qdx.zy,qdy.zy,17u,0);
    fy=SurfaceFrameGrad296(q.xz,qdx.xz,qdy.xz,17u,0);
    fz=SurfaceFrameGrad296(q.xy,qdx.xy,qdy.xy,17u,0);
    x=SurfaceNormal296(TEXTURE2D_ARGS(_RockNormal,sampler_Surface296_linear_repeat_aniso8),fx,.55*detail);
    y=SurfaceNormal296(TEXTURE2D_ARGS(_RockNormal,sampler_Surface296_linear_repeat_aniso8),fy,.55*detail);
    z=SurfaceNormal296(TEXTURE2D_ARGS(_RockNormal,sampler_Surface296_linear_repeat_aniso8),fz,.55*detail);
    perturb+=float3(0,x.y,x.x)*w.x+float3(y.x,0,y.y)*w.y+float3(z.x,z.y,0)*w.z;
    return normalize(n+perturb-n*dot(n,perturb));
}
float3 RuleNormal276(float3 p,float3 n,float3 masks,float distanceWS)
{
    float3 dx=ddx(p),dy=ddy(p);
    float detail=1-smoothstep(20,150,distanceWS);
    if(detail<=0)return n; // No far triplanar normal samples.
    float3 rock=n;
    [branch] if(masks.x>0)rock=RuleRockNormal276(p,n,detail,dx,dy);
    SurfaceFrame296 dirtFrame=SurfaceFrameGrad296(p.xz*.8,dx.xz*.8,dy.xz*.8,11u,1);
    SurfaceFrame296 gravelFrame=SurfaceFrameGrad296(p.xz*.5,dx.xz*.5,dy.xz*.5,13u,1);
    float3 dirt=SurfaceNormal296(TEXTURE2D_ARGS(_DirtNormal,sampler_Surface296_linear_repeat_aniso8),dirtFrame,.8*detail);
    float3 gravel=SurfaceNormal296(TEXTURE2D_ARGS(_RebuildGravelNormal,sampler_Surface296_linear_repeat_aniso8),gravelFrame,.9*detail);
    float3 soilSample=normalize(lerp(dirt,gravel,masks.y));
    // Realm floor colour and its CC0 normal use the same frame and realm weights.
    float4 fw=SAMPLE_TEXTURE2D(_Floor293,sampler_Surface296_linear_repeat_aniso8,saturate(p.xz/float2(4000,6000)));
    float wC=fw.r,wJ=fw.g*_FloorRealms293,wO=fw.b*_FloorRealms293,wH=fw.a*_FloorRealms293,wW=saturate(1-fw.r-fw.g-fw.b-fw.a)*_FloorRealms293;
    float total=wC+wJ+wO+wH+wW;
    float nearFloor=(1-smoothstep(_FloorRange293.x,_FloorRange293.y,distanceWS))*total*_FloorStrength293*masks.z;
    SurfaceFrame296 floorFrame=SurfaceFrameGrad296(p.xz*_FloorScale293,dx.xz*_FloorScale293,dy.xz*_FloorScale293,31u,1);
    if(nearFloor>.001)
    {
        float3 fn=0;float used=0;
        [branch] if(wC>.004){fn+=wC*SurfaceNormal296(TEXTURE2D_ARGS(_FloorNormal296,sampler_Surface296_linear_repeat_aniso8),floorFrame,.65*detail);used+=wC;}
        [branch] if(wJ>.004){fn+=wJ*SurfaceNormal296(TEXTURE2D_ARGS(_FloorNormalJ296,sampler_Surface296_linear_repeat_aniso8),floorFrame,.65*detail);used+=wJ;}
        [branch] if(wO>.004){fn+=wO*SurfaceNormal296(TEXTURE2D_ARGS(_FloorNormalC296,sampler_Surface296_linear_repeat_aniso8),floorFrame,.65*detail);used+=wO;}
        [branch] if(wH>.004){fn+=wH*SurfaceNormal296(TEXTURE2D_ARGS(_FloorNormalH296,sampler_Surface296_linear_repeat_aniso8),floorFrame,.65*detail);used+=wH;}
        [branch] if(wW>.004){fn+=wW*SurfaceNormal296(TEXTURE2D_ARGS(_FloorNormalW296,sampler_Surface296_linear_repeat_aniso8),floorFrame,.65*detail);used+=wW;}
        if(used>0)soilSample=normalize(lerp(soilSample,normalize(fn),nearFloor));
    }
    // Bank colour and normal share each scan's exact scale, rotation and seed.
    float2 bankUV=(p.xz*.5+.5)/float2(2001,3001);
    float2 bank=SAMPLE_TEXTURE2D(_BankMask294,sampler_SurfaceLinearClamp296,bankUV).rg;
    float bankWeight=bank.r*_BankStrength294*(1-smoothstep(200,700,distanceWS));
    SurfaceFrame296 bankGravel=SurfaceFrameGrad296(p.xz*.48,dx.xz*.48,dy.xz*.48,41u,1);
    float2 mudUV=float2(-p.z,p.x)*(.48*.73);
    SurfaceFrame296 bankMud=SurfaceFrameGrad296(mudUV,float2(-dx.z,dx.x)*(.48*.73),float2(-dy.z,dy.x)*(.48*.73),43u,1);
    if(bankWeight>.002)
    {
        float wet=saturate(bank.g/max(.001,bank.r));
        float3 gn=SurfaceNormal296(TEXTURE2D_ARGS(_BankGravelNormal296,sampler_Surface296_linear_repeat_aniso8),bankGravel,.65*detail);
        float3 mn=SurfaceNormal296(TEXTURE2D_ARGS(_BankMudNormal296,sampler_Surface296_linear_repeat_aniso8),bankMud,.55*detail);
        mn.xy=float2(mn.y,-mn.x); // Undo the original mud projection's 90-degree turn.
        soilSample=normalize(lerp(soilSample,normalize(lerp(gn,mn,.3+wet*.5)),bankWeight*.9));
    }
    float3 tangent=normalize(abs(n.y)+abs(n.x)>.001?float3(n.y,-n.x,0):float3(1,0,0));
    float3 soil=normalize(tangent*soilSample.x+normalize(cross(tangent,n))*soilSample.y+n*soilSample.z);
    return normalize(lerp(soil,rock,masks.x));
}
// #293 realm floors: mid-field hue from the floor's own average colour, near-field albedo with a rotated second tap
float3 FloorHue293(float3 avg,float3 tint,float chroma){return lerp(float3(1,1,1),avg/max(.05,CILuma(avg)),chroma)*tint;}
float3 FloorNear296(float3 f,float3 tint,float chroma){float fl=CILuma(f);return lerp(fl.xxx,f,chroma)*tint*1.3;}
float3 RuleColour276(float3 p,float3 n,float3 masks,float distanceWS)
{
    float3 worldDx296=ddx(p),worldDy296=ddy(p);
    float3 w=pow(abs(n),4);w/=max(.001,w.x+w.y+w.z);
    float3 q=RuleJointCoords276(p);
    float3 qdx=RuleJointCoords276(p+worldDx296)-q,qdy=RuleJointCoords276(p+worldDy296)-q;
    float3 rock=0,soil=0,gravel=1;float cavity=1;
    // Past the existing resolve range, these samples are mathematically unused.
    // Keep the original macro masses, realm hue and all distance fades below.
    if(distanceWS<750)
    {
        SurfaceFrame296 fx=SurfaceFrameGrad296(q.zy,qdx.zy,qdy.zy,17u,0);
        SurfaceFrame296 fy=SurfaceFrameGrad296(q.xz,qdx.xz,qdy.xz,17u,0);
        SurfaceFrame296 fz=SurfaceFrameGrad296(q.xy,qdx.xy,qdy.xy,17u,0);
        float3 rockMean=SAMPLE_TEXTURE2D_LOD(_RockMap,sampler_Surface296_linear_repeat_aniso8,float2(.5,.5),12).rgb;
        rock=SurfaceColourMean296(TEXTURE2D_ARGS(_RockMap,sampler_Surface296_linear_repeat_aniso8),fx,rockMean)*w.x+
            SurfaceColourMean296(TEXTURE2D_ARGS(_RockMap,sampler_Surface296_linear_repeat_aniso8),fy,rockMean)*w.y+
            SurfaceColourMean296(TEXTURE2D_ARGS(_RockMap,sampler_Surface296_linear_repeat_aniso8),fz,rockMean)*w.z;
        if(distanceWS<240)
        {
            fx=SurfaceFrameGrad296(q.zy,qdx.zy,qdy.zy,19u,0);
            fy=SurfaceFrameGrad296(q.xz,qdx.xz,qdy.xz,19u,0);
            fz=SurfaceFrameGrad296(q.xy,qdx.xy,qdy.xy,19u,0);
            cavity=SurfaceData296(TEXTURE2D_ARGS(_StrataFracture276,sampler_Surface296_linear_repeat_aniso8),fx).g*w.x+
                SurfaceData296(TEXTURE2D_ARGS(_StrataFracture276,sampler_Surface296_linear_repeat_aniso8),fy).g*w.y+
                SurfaceData296(TEXTURE2D_ARGS(_StrataFracture276,sampler_Surface296_linear_repeat_aniso8),fz).g*w.z;
        }
        SurfaceFrame296 dirtFrame=SurfaceFrameGrad296(p.xz*.8,worldDx296.xz*.8,worldDy296.xz*.8,11u,1);
        soil=SurfaceColour296(TEXTURE2D_ARGS(_DirtMap,sampler_Surface296_linear_repeat_aniso8),dirtFrame);
        if(distanceWS<110)
        {
            SurfaceFrame296 gravelFrame=SurfaceFrameGrad296(p.xz*.5,worldDx296.xz*.5,worldDy296.xz*.5,13u,1);
            gravel=SurfaceData296(TEXTURE2D_ARGS(_RebuildGravel,sampler_Surface296_linear_repeat_aniso8),gravelFrame).rgb;
        }
    }
    // No painted atlas in this path. Albedo and cavities retain real-scale features.
    float rockTone=saturate(.08+(CILuma(rock)-.10)*2.6);
    rockTone*=lerp(1,saturate((cavity-.58)/.42),1-smoothstep(50,240,distanceWS));
    float soilTone=saturate(.10+(CILuma(soil)-.09)*2.3);
    soilTone*=lerp(1,lerp(.65,1.18,gravel.r)*gravel.g,masks.y*(1-smoothstep(20,110,distanceWS)));
    // Resolve small material marks into stable broad pigment masses in the distance.
    // Near geometry keeps its scanned albedo; the far field must not show a tiled cloth pattern.
    float resolve=1-smoothstep(35,180,distanceWS);
    // Retain subdued existing soil/rock albedo on the candidate's authored lowland mask.
    // No additional material samples or normals; exact baseline resolve where the mask is zero.
    float2 watershedDetailUV295=p.xz/float2(4000,6000);
    float watershedDetailMask295=SAMPLE_TEXTURE2D_LOD(_WatershedToneMask295,sampler_SurfaceLinearClamp296,saturate(watershedDetailUV295),0).r;
    watershedDetailMask295*=step(0,watershedDetailUV295.x)*step(watershedDetailUV295.x,1)*step(0,watershedDetailUV295.y)*step(watershedDetailUV295.y,1);
    resolve=max(resolve,.30*watershedDetailMask295*(1-smoothstep(500,750,distanceWS)));
    float macro=Noise(p.xz*.013+n.xz*.8);
    soilTone=lerp(.43+macro*.13,soilTone,resolve);
    rockTone=lerp(.53+macro*.15,rockTone,resolve);
    float tone=lerp(soilTone,rockTone,masks.x);
    float4 field=SAMPLE_TEXTURE2D(_StrataField276,sampler_Surface296_linear_repeat_aniso8,saturate(p.xz/float2(4000,6000)));
    float3 mineral=lerp(float3(.94,.86,.72),float3(.90,.94,.94),masks.x);
    mineral=lerp(mineral,float3(.72,.81,.73),field.b*.38*(1-field.a));
    float3 realm=SAMPLE_TEXTURE2D(_Realm293,sampler_Surface296_linear_repeat_aniso8,saturate(p.xz/float2(4000,6000))).rgb;
    mineral*=lerp(float3(1,1,1),realm/max(.1,CILuma(realm)),_RealmStrength293);
    float wetDarkening=1-field.b*.12;
    float3 inkPaper=lerp(_CIInk.rgb,_CIPaper.rgb,tone)*lerp(1,mineral,.45)*wetDarkening;
    // #293 near material: the realm's own forest floor (CC0) on soil, dissolving back into the
    // ink/paper model with distance so the mid and far fields keep their painted masses.
    [branch] if(_FloorStrength293>0)
    {
        // weights: r Cheongrim forest floor · g Jeokro burnt ground · b Cheolong layered scree · a Hyeongang riverbank mud ·
        // remainder Hwanggyeong dry stony ground (soft 60m realm borders). _FloorRealms293=0 keeps the Step 1 Cheongrim-only floor.
        float4 fw=SAMPLE_TEXTURE2D(_Floor293,sampler_Surface296_linear_repeat_aniso8,saturate(p.xz/float2(4000,6000)));
        float wC=fw.r,wJ=fw.g*_FloorRealms293,wO=fw.b*_FloorRealms293,wH=fw.a*_FloorRealms293,wW=saturate(1-fw.r-fw.g-fw.b-fw.a)*_FloorRealms293;
        float total=wC+wJ+wO+wH+wW;float2 uv=p.xz*_FloorScale293;
        // mid field: the floor's own hue washed lightly over the ink/paper values (담채), fading out far away
        float mid=smoothstep(_FloorRange293.x,_FloorRange293.y,distanceWS)*(1-smoothstep(_FloorFar293.x,_FloorFar293.y,distanceWS))*total*_FloorWash293*masks.z;
        if(mid>.001)
        {
            float3 hue=wC*FloorHue293(SAMPLE_TEXTURE2D_LOD(_FloorMap293,sampler_Surface296_linear_repeat_aniso8,uv,10).rgb,_FloorTint293.rgb,_FloorChroma293);
            [branch] if(wJ>.004)hue+=wJ*FloorHue293(SAMPLE_TEXTURE2D_LOD(_FloorMapJ293,sampler_Surface296_linear_repeat_aniso8,uv,10).rgb,_FloorTintJ293.rgb,_FloorTintJ293.a);
            [branch] if(wO>.004)hue+=wO*FloorHue293(SAMPLE_TEXTURE2D_LOD(_FloorMapC293,sampler_Surface296_linear_repeat_aniso8,uv,10).rgb,_FloorTintC293.rgb,_FloorTintC293.a);
            [branch] if(wH>.004)hue+=wH*FloorHue293(SAMPLE_TEXTURE2D_LOD(_FloorMapH293,sampler_Surface296_linear_repeat_aniso8,uv,10).rgb,_FloorTintH293.rgb,_FloorTintH293.a);
            [branch] if(wW>.004)hue+=wW*FloorHue293(SAMPLE_TEXTURE2D_LOD(_FloorMapW293,sampler_Surface296_linear_repeat_aniso8,uv,10).rgb,_FloorTintW293.rgb,_FloorTintW293.a);
            inkPaper*=lerp(float3(1,1,1),hue/max(1e-4,total)*.92,mid);
        }
        float nearFloor=(1-smoothstep(_FloorRange293.x,_FloorRange293.y,distanceWS))*total*_FloorStrength293*masks.z;
        SurfaceFrame296 floorFrame=SurfaceFrameGrad296(uv,worldDx296.xz*_FloorScale293,worldDy296.xz*_FloorScale293,31u,1);
        if(nearFloor>.001)
        {
            float3 f=0;float used=0;
            [branch] if(wC>.004){f+=wC*FloorNear296(SurfaceColour296(TEXTURE2D_ARGS(_FloorMap293,sampler_Surface296_linear_repeat_aniso8),floorFrame),_FloorTint293.rgb,_FloorChroma293);used+=wC;}
            [branch] if(wJ>.004){f+=wJ*FloorNear296(SurfaceColour296(TEXTURE2D_ARGS(_FloorMapJ293,sampler_Surface296_linear_repeat_aniso8),floorFrame),_FloorTintJ293.rgb,_FloorTintJ293.a);used+=wJ;}
            [branch] if(wO>.004){f+=wO*FloorNear296(SurfaceColour296(TEXTURE2D_ARGS(_FloorMapC293,sampler_Surface296_linear_repeat_aniso8),floorFrame),_FloorTintC293.rgb,_FloorTintC293.a);used+=wO;}
            [branch] if(wH>.004){f+=wH*FloorNear296(SurfaceColour296(TEXTURE2D_ARGS(_FloorMapH293,sampler_Surface296_linear_repeat_aniso8),floorFrame),_FloorTintH293.rgb,_FloorTintH293.a);used+=wH;}
            [branch] if(wW>.004){f+=wW*FloorNear296(SurfaceColour296(TEXTURE2D_ARGS(_FloorMapW293,sampler_Surface296_linear_repeat_aniso8),floorFrame),_FloorTintW293.rgb,_FloorTintW293.a);used+=wW;}
            if(used>0)inkPaper=lerp(inkPaper,f/used,nearFloor);
        }
        // The hue normalisation above deliberately preserves paper value. Jeokro needs a separate,
        // opt-in charcoal mass: continuous in world space and confined to its existing soft realm mask.
        // Keep the scanned near floor readable; let the broad ink wash carry the burnt hillside at distance.
        [branch] if(_Scorch293.x>0 && wJ>0)
        {
            float mass=lerp(_Scorch293.y,_Scorch293.z,smoothstep(.2,.8,macro));
            float reach=lerp(.35,1,smoothstep(_FloorRange293.x,_FloorRange293.y,distanceWS));
            reach*=1-smoothstep(_FloorFar293.x,_FloorFar293.y,distanceWS);
            float surface=lerp(1,_Scorch293.w,masks.x);
            inkPaper*=1-saturate(wJ*_Scorch293.x*mass*reach*surface);
        }
    }
    [branch] if(_BankStrength294>0)
    {
        float2 bankUV=(p.xz*.5+.5)/float2(2001,3001);
        float2 bank=SAMPLE_TEXTURE2D(_BankMask294,sampler_SurfaceLinearClamp296,bankUV).rg;
        float weight=bank.r*_BankStrength294*(1-smoothstep(200,700,distanceWS));
        SurfaceFrame296 bankGravel=SurfaceFrameGrad296(p.xz*.48,worldDx296.xz*.48,worldDy296.xz*.48,41u,1);
        SurfaceFrame296 bankMud=SurfaceFrameGrad296(float2(-p.z,p.x)*(.48*.73),float2(-worldDx296.z,worldDx296.x)*(.48*.73),float2(-worldDy296.z,worldDy296.x)*(.48*.73),43u,1);
        [branch] if(weight>.002)
        {
            float wet=saturate(bank.g/max(.001,bank.r));float2 uv=p.xz*.48;
            float3 gravel=SurfaceColour296(TEXTURE2D_ARGS(_BankGravel294,sampler_Surface296_linear_repeat_aniso8),bankGravel);
            float3 mud=SurfaceColour296(TEXTURE2D_ARGS(_BankMud294,sampler_Surface296_linear_repeat_aniso8),bankMud);
            float3 sediment=lerp(gravel,mud,.3+wet*.5);
            float lum=CILuma(sediment);sediment=lerp(lum.xxx,sediment,.45)*lerp(1.45,.85,wet);
            inkPaper=lerp(inkPaper,sediment,weight*.9);
        }
    }
    return inkPaper;
}
