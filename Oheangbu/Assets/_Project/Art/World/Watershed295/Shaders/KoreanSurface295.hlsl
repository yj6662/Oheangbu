// Opt-in candidate geology. All coordinates are metres, independent of mesh UV/scale.
float RuleWeight276(float distanceWS)
{
    return _StrataStrength276;
}
// Authored rock / colluvium / moisture / path weights from the final world surface.
float3 RuleMasks276(float3 p,float3 n,float path)
{
    float4 field=SAMPLE_TEXTURE2D(_StrataField276,sampler_RockNormal,saturate(p.xz/float2(4000,6000)));
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
// Whiteout-style triplanar perturbation. A flat normal map returns the geometric normal.
float3 RuleRockNormal276(float3 p,float3 n,float detail)
{
    float3 w=pow(abs(n),4);w/=max(.001,w.x+w.y+w.z);
    float3 q=RuleJointCoords276(p);
    float3 x=UnpackNormalScale(SAMPLE_TEXTURE2D(_StrataFractureNormal276,sampler_RockNormal,q.zy),1.25*detail);
    float3 y=UnpackNormalScale(SAMPLE_TEXTURE2D(_StrataFractureNormal276,sampler_RockNormal,q.xz),1.25*detail);
    float3 z=UnpackNormalScale(SAMPLE_TEXTURE2D(_StrataFractureNormal276,sampler_RockNormal,q.xy),1.25*detail);
    float3 perturb=float3(0,x.y,x.x)*w.x+float3(y.x,0,y.y)*w.y+float3(z.x,z.y,0)*w.z;
    if(detail>.01)
    {
        x=UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal,sampler_RockNormal,p.zy*1.2),.55*detail);
        y=UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal,sampler_RockNormal,p.xz*1.2),.55*detail);
        z=UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal,sampler_RockNormal,p.xy*1.2),.55*detail);
        perturb+=float3(0,x.y,x.x)*w.x+float3(y.x,0,y.y)*w.y+float3(z.x,z.y,0)*w.z;
    }
    return normalize(n+perturb-n*dot(n,perturb));
}
float3 RuleNormal276(float3 p,float3 n,float3 masks,float distanceWS)
{
    float detail=1-smoothstep(20,150,distanceWS);
    if(detail<=0)return n;
    float3 rock=RuleRockNormal276(p,n,detail);
    float3 dirt=UnpackNormalScale(SAMPLE_TEXTURE2D(_DirtNormal,sampler_DirtNormal,p.xz*.8),.8*detail);
    float3 gravel=UnpackNormalScale(SAMPLE_TEXTURE2D(_RebuildGravelNormal,sampler_RebuildGravelNormal,p.xz*.5),.9*detail);
    float3 tangent=normalize(abs(n.y)+abs(n.x)>.001?float3(n.y,-n.x,0):float3(1,0,0));
    float3 soilSample=normalize(lerp(dirt,gravel,masks.y));
    float3 soil=normalize(tangent*soilSample.x+normalize(cross(tangent,n))*soilSample.y+n*soilSample.z);
    return normalize(lerp(soil,rock,masks.x));
}
// #293 realm floors: mid-field hue from the floor's own average colour, near-field albedo with a rotated second tap
float3 FloorHue293(float3 avg,float3 tint,float chroma){return lerp(float3(1,1,1),avg/max(.05,CILuma(avg)),chroma)*tint;}
float3 FloorNear293(float3 f,float3 f2,float mixer,float3 tint,float chroma){f=lerp(f,f2,mixer);float fl=CILuma(f);return lerp(fl.xxx,f,chroma)*tint*1.3;}
float3 RuleColour276(float3 p,float3 n,float3 masks,float distanceWS)
{
    float3 w=pow(abs(n),4);w/=max(.001,w.x+w.y+w.z);
    float3 q=RuleJointCoords276(p);
    float3 rock=SAMPLE_TEXTURE2D(_RockMap,sampler_RockMap,q.zy).rgb*w.x+
        SAMPLE_TEXTURE2D(_RockMap,sampler_RockMap,q.xz).rgb*w.y+
        SAMPLE_TEXTURE2D(_RockMap,sampler_RockMap,q.xy).rgb*w.z;
    float cavity=SAMPLE_TEXTURE2D(_StrataFracture276,sampler_RockMap,q.zy).g*w.x+
        SAMPLE_TEXTURE2D(_StrataFracture276,sampler_RockMap,q.xz).g*w.y+
        SAMPLE_TEXTURE2D(_StrataFracture276,sampler_RockMap,q.xy).g*w.z;
    float3 soil=SAMPLE_TEXTURE2D(_DirtMap,sampler_DirtMap,p.xz*.8).rgb;
    float3 gravel=SAMPLE_TEXTURE2D(_RebuildGravel,sampler_RebuildGravel,p.xz*.5).rgb;
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
    float watershedDetailMask295=SAMPLE_TEXTURE2D_LOD(_WatershedToneMask295,sampler_GroundPathMask,saturate(watershedDetailUV295),0).r;
    watershedDetailMask295*=step(0,watershedDetailUV295.x)*step(watershedDetailUV295.x,1)*step(0,watershedDetailUV295.y)*step(watershedDetailUV295.y,1);
    resolve=max(resolve,.30*watershedDetailMask295*(1-smoothstep(500,750,distanceWS)));
    float macro=Noise(p.xz*.013+n.xz*.8);
    soilTone=lerp(.43+macro*.13,soilTone,resolve);
    rockTone=lerp(.53+macro*.15,rockTone,resolve);
    float tone=lerp(soilTone,rockTone,masks.x);
    float4 field=SAMPLE_TEXTURE2D(_StrataField276,sampler_RockNormal,saturate(p.xz/float2(4000,6000)));
    float3 mineral=lerp(float3(.94,.86,.72),float3(.90,.94,.94),masks.x);
    mineral=lerp(mineral,float3(.72,.81,.73),field.b*.38*(1-field.a));
    float3 realm=SAMPLE_TEXTURE2D(_Realm293,sampler_RockNormal,saturate(p.xz/float2(4000,6000))).rgb;
    mineral*=lerp(float3(1,1,1),realm/max(.1,CILuma(realm)),_RealmStrength293);
    float wetDarkening=1-field.b*.12;
    float3 inkPaper=lerp(_CIInk.rgb,_CIPaper.rgb,tone)*lerp(1,mineral,.45)*wetDarkening;
    // #293 near material: the realm's own forest floor (CC0) on soil, dissolving back into the
    // ink/paper model with distance so the mid and far fields keep their painted masses.
    [branch] if(_FloorStrength293>0)
    {
        // weights: r Cheongrim forest floor · g Jeokro burnt ground · b Cheolong layered scree · a Hyeongang riverbank mud ·
        // remainder Hwanggyeong dry stony ground (soft 60m realm borders). _FloorRealms293=0 keeps the Step 1 Cheongrim-only floor.
        float4 fw=SAMPLE_TEXTURE2D(_Floor293,sampler_RockNormal,saturate(p.xz/float2(4000,6000)));
        float wC=fw.r,wJ=fw.g*_FloorRealms293,wO=fw.b*_FloorRealms293,wH=fw.a*_FloorRealms293,wW=saturate(1-fw.r-fw.g-fw.b-fw.a)*_FloorRealms293;
        float total=wC+wJ+wO+wH+wW;float2 uv=p.xz*_FloorScale293;
        // mid field: the floor's own hue washed lightly over the ink/paper values (담채), fading out far away
        float mid=smoothstep(_FloorRange293.x,_FloorRange293.y,distanceWS)*(1-smoothstep(_FloorFar293.x,_FloorFar293.y,distanceWS))*total*_FloorWash293*masks.z;
        if(mid>.001)
        {
            float3 hue=wC*FloorHue293(SAMPLE_TEXTURE2D_LOD(_FloorMap293,sampler_DirtMap,uv,10).rgb,_FloorTint293.rgb,_FloorChroma293);
            [branch] if(wJ>.004)hue+=wJ*FloorHue293(SAMPLE_TEXTURE2D_LOD(_FloorMapJ293,sampler_DirtMap,uv,10).rgb,_FloorTintJ293.rgb,_FloorTintJ293.a);
            [branch] if(wO>.004)hue+=wO*FloorHue293(SAMPLE_TEXTURE2D_LOD(_FloorMapC293,sampler_DirtMap,uv,10).rgb,_FloorTintC293.rgb,_FloorTintC293.a);
            [branch] if(wH>.004)hue+=wH*FloorHue293(SAMPLE_TEXTURE2D_LOD(_FloorMapH293,sampler_DirtMap,uv,10).rgb,_FloorTintH293.rgb,_FloorTintH293.a);
            [branch] if(wW>.004)hue+=wW*FloorHue293(SAMPLE_TEXTURE2D_LOD(_FloorMapW293,sampler_DirtMap,uv,10).rgb,_FloorTintW293.rgb,_FloorTintW293.a);
            inkPaper*=lerp(float3(1,1,1),hue/max(1e-4,total)*.92,mid);
        }
        float nearFloor=(1-smoothstep(_FloorRange293.x,_FloorRange293.y,distanceWS))*total*_FloorStrength293*masks.z;
        if(nearFloor>.001)
        {
            float2 uv2=float2(uv.y,-uv.x)*.73+.37;float mixer=smoothstep(.35,.65,Noise(p.xz*.05));  // the second tap breaks the repeat
            float3 f=0;float used=0;
            [branch] if(wC>.004){f+=wC*FloorNear293(SAMPLE_TEXTURE2D(_FloorMap293,sampler_DirtMap,uv).rgb,SAMPLE_TEXTURE2D(_FloorMap293,sampler_DirtMap,uv2).rgb,mixer,_FloorTint293.rgb,_FloorChroma293);used+=wC;}
            [branch] if(wJ>.004){f+=wJ*FloorNear293(SAMPLE_TEXTURE2D(_FloorMapJ293,sampler_DirtMap,uv).rgb,SAMPLE_TEXTURE2D(_FloorMapJ293,sampler_DirtMap,uv2).rgb,mixer,_FloorTintJ293.rgb,_FloorTintJ293.a);used+=wJ;}
            [branch] if(wO>.004){f+=wO*FloorNear293(SAMPLE_TEXTURE2D(_FloorMapC293,sampler_DirtMap,uv).rgb,SAMPLE_TEXTURE2D(_FloorMapC293,sampler_DirtMap,uv2).rgb,mixer,_FloorTintC293.rgb,_FloorTintC293.a);used+=wO;}
            [branch] if(wH>.004){f+=wH*FloorNear293(SAMPLE_TEXTURE2D(_FloorMapH293,sampler_DirtMap,uv).rgb,SAMPLE_TEXTURE2D(_FloorMapH293,sampler_DirtMap,uv2).rgb,mixer,_FloorTintH293.rgb,_FloorTintH293.a);used+=wH;}
            [branch] if(wW>.004){f+=wW*FloorNear293(SAMPLE_TEXTURE2D(_FloorMapW293,sampler_DirtMap,uv).rgb,SAMPLE_TEXTURE2D(_FloorMapW293,sampler_DirtMap,uv2).rgb,mixer,_FloorTintW293.rgb,_FloorTintW293.a);used+=wW;}
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
        float2 bank=SAMPLE_TEXTURE2D(_BankMask294,sampler_GroundPathMask,bankUV).rg;
        float weight=bank.r*_BankStrength294*(1-smoothstep(200,700,distanceWS));
        [branch] if(weight>.002)
        {
            float wet=saturate(bank.g/max(.001,bank.r));float2 uv=p.xz*.48;
            float3 gravel=SAMPLE_TEXTURE2D(_BankGravel294,sampler_DirtMap,uv).rgb;
            float3 mud=SAMPLE_TEXTURE2D(_BankMud294,sampler_DirtMap,float2(-uv.y,uv.x)*.73).rgb;
            float3 sediment=lerp(gravel,mud,.3+wet*.5);
            float lum=CILuma(sediment);sediment=lerp(lum.xxx,sediment,.45)*lerp(1.45,.85,wet);
            inkPaper=lerp(inkPaper,sediment,weight*.9);
        }
    }
    return inkPaper;
}
