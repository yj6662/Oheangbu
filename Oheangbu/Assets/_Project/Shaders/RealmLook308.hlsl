#ifndef OH_REALM_LOOK_308
#define OH_REALM_LOOK_308
// #308 realm look (SPEC-WORLD-REALM-LOOK-308, TEST). One per-realm colour source for the ground, steep rock faces, grass and foliage.
// Every number is a shader global written by RealmLookDriver308 from the palette asset (Tools/Unity/Stage308_realmlook/realmlook308.json);
// no colour is typed here. _OhRealmLook defaults to 0 (no driver in the scene), and every caller branches on it, so a scene without the
// driver renders exactly as before. No emission: the wash only re-tints the albedo that the caller then lights.
// Map channels: r Cheongrim, g Jeokro, b Cheolong, a Hyeongang, remainder Hwanggyeong (soft, wandering borders baked into the map).
// A wash = (hue at unit luminance x value, share): the surface keeps its own lightness pattern and takes the realm's hue.
float _OhRealmLook;
float4 _OhRealmLookRect;      // origin x, origin z, 1 / size x, 1 / size z
float4 _OhRealmLookAmounts;   // ground, rock, grass, foliage
float4 _OhRealmLookSlope;     // as 1 - normal.y: rock starts, rock full, talus band starts, ragged edge amplitude
float4 _OhRealmLookStrata;    // bed thickness m, crease ink, vertical streak, bed-to-bed value variation
float4 _OhRealmLookFace;      // base value of a rock face, strata fade start m, fade end m, talus speckle
float4 _OhRealmLookLift;      // lightness added before the wash (linear): ground, rock, grass, foliage
float4 _OhRealmLookJoints;    // joint spacing m, joint ink, major bed = this many beds, broad wash blotch
float4 _OhRealmGround[5];
float4 _OhRealmRock[5];
float4 _OhRealmGrass[5];
float4 _OhRealmFoliage[5];
TEXTURE2D(_OhRealmLookMap);
// A shader near the 16 sampler limit names one of its own clamp samplers before including this file.
#ifndef REALMLOOK_SAMPLER
#define REALMLOOK_SAMPLER sampler_LinearClamp
#endif

struct RealmLookW308 { float4 m; float rest; };
RealmLookW308 RealmLookWeights308(float2 xz)
{
    RealmLookW308 w;
    float2 uv=saturate((xz-_OhRealmLookRect.xy)*_OhRealmLookRect.zw);
    w.m=SAMPLE_TEXTURE2D_LOD(_OhRealmLookMap,REALMLOOK_SAMPLER,uv,0);
    w.rest=saturate(1-dot(w.m,float4(1,1,1,1)));
    float total=max(1e-3,dot(w.m,float4(1,1,1,1))+w.rest);
    w.m/=total;w.rest/=total;
    return w;
}
#define REALMLOOK_MIX308(table,w) (table[0]*w.m.r+table[1]*w.m.g+table[2]*w.m.b+table[3]*w.m.a+table[4]*w.rest)

float RLHash308(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float RLNoise308(float2 p)
{
    float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(RLHash308(a),RLHash308(a+float2(1,0)),f.x),lerp(RLHash308(a+float2(0,1)),RLHash308(a+float2(1,1)),f.x),f.y);
}
float RLLuma308(float3 c){return dot(c,float3(.2126,.7152,.0722));}

// ---- vegetation (evaluated once per instance, at its origin): role 1 = foliage, 2 = grass, anything else = untouched
#define REALMLOOK_ROLE308 (_RealmLookRole308>.5?_RealmLookRole308:(_AlphaClip>.5?1.0:0.0))
half4 RealmLookVegetation308(float2 xz,float role)
{
    if(_OhRealmLook<=0||role<.5||role>2.5)return half4(1,1,1,0);
    RealmLookW308 w=RealmLookWeights308(xz);
    float4 wash=role>1.5?REALMLOOK_MIX308(_OhRealmGrass,w):REALMLOOK_MIX308(_OhRealmFoliage,w);
    wash.a*=(role>1.5?_OhRealmLookAmounts.z:_OhRealmLookAmounts.w)*_OhRealmLook;
    // plants of one stand are not one flat tone
    wash.rgb*=lerp(.86,1.12,RLHash308(floor(xz*4)));
    return half4(wash);
}
half3 RealmLookTint308(half3 albedo,half4 wash,float role)
{
    // the lift lets a near-black leaf atlas still carry the hue (it is lit like any albedo; nothing is emitted)
    float lift=role>1.5?_OhRealmLookLift.z:_OhRealmLookLift.w;
    return lerp(albedo,(RLLuma308(albedo)+lift)*wash.rgb,saturate(wash.a));
}

// ---- ground under forest masses: the canopy hue of the realm at unit luminance (a = share)
float4 RealmLookCanopy308(float2 xz,float amount)
{
    RealmLookW308 w=RealmLookWeights308(xz);
    float4 leaf=REALMLOOK_MIX308(_OhRealmFoliage,w);
    leaf.rgb/=max(.05,RLLuma308(leaf.rgb));
    leaf.a=saturate(leaf.a*_OhRealmLookAmounts.w*_OhRealmLook*amount);
    return leaf;
}

// Steepness of a ground point (0 soil .. 1 rock face) with the ragged edge; x = rock, y = slope value for the talus band.
float2 RealmLookSteep308(float3 p,float3 n)
{
    float ragged=RLNoise308(p.xz*.09)*.65+RLNoise308(p.xz*.33+7.3)*.35-.5;
    float slope=1-n.y+ragged*_OhRealmLookSlope.w;
    return float2(smoothstep(_OhRealmLookSlope.x,_OhRealmLookSlope.y,slope),slope);
}
// ---- ground: soil takes the realm's ground wash; steep faces become jointed, bedded rock in the realm's rock wash (beds of their
// own value, ink under each ledge, staggered joints, vertical dry-brush streaks); between them a ragged scree band so the face does
// not end in a knife line. Procedural only: one map tap, no material texture, no screen-space derivative (safe inside branches).
// One family of jointed beds seen along one horizontal axis u (metres). Returns (value multiplier, ink 0..1).
float2 RLBeds308(float u,float bed,float scale,float joints)
{
    bed+=.35*sin(bed*1.7)+.2*sin(bed*.61+1.3);                       // uneven bed thickness (still monotonic)
    float row=floor(bed),fr=bed-row;
    float h=RLHash308(float2(row,scale*7.3));
    u+=(RLNoise308(float2(u*.06/scale,bed*.45))-.5)*3.2*scale;       // joints wander
    float brush=RLNoise308(float2(u*.09/scale,row*3.7));
    float crease=(1-smoothstep(0,lerp(.03,.22,brush),fr))*smoothstep(.3,.62,brush);   // thick-thin, broken line under each bed
    float tone=RLHash308(float2(row,1.7));
    float ink=crease*_OhRealmLookStrata.y;
    [branch] if(joints>0)
    {
        // joints lean (each bed its own way), are staggered bed to bed, and many are missing: fractured rock, not masonry
        float ju=(u+(fr-.5)*(h-.5)*_OhRealmLookStrata.x*scale*2.2)/max(.5,_OhRealmLookJoints.x*scale)*lerp(.6,1.5,h)+h*13;
        float col=floor(ju),fc=ju-col;
        float joint=(1-smoothstep(0,lerp(.015,.05,RLHash308(float2(col+3,row))),fc))*step(.55,RLHash308(float2(col,row+scale)));
        joint*=smoothstep(.05,.3,fr+brush*.3)*smoothstep(.15,.45,RLNoise308(float2(u*.2/scale+5,bed*2.3)));
        tone=lerp(tone,RLHash308(float2(col,row)),.25);
        ink=max(ink,joint*_OhRealmLookJoints.y*joints);
    }
    float value=lerp(1-_OhRealmLookStrata.w*.5,1+_OhRealmLookStrata.w*.35,tone)*lerp(.74,1.12,fr);
    return float2(value,ink);
}
float3 RealmLookGround308(float3 inkPaper,float3 p,float3 n,float debris,float distanceWS,float amount)
{
    RealmLookW308 w=RealmLookWeights308(p.xz);
    float4 soilWash=REALMLOOK_MIX308(_OhRealmGround,w),rockWash=REALMLOOK_MIX308(_OhRealmRock,w);
    float lum=RLLuma308(inkPaper);
    float2 steepness=RealmLookSteep308(p,n);
    float steep=steepness.x,slope=steepness.y;
    float talus=saturate(max(smoothstep(_OhRealmLookSlope.z,_OhRealmLookSlope.x,slope),debris*.8))*(1-steep);
    float3 soil=(lum+_OhRealmLookLift.x)*soilWash.rgb;
    float3 result=soil;
    [branch] if(steep+talus>.002)
    {
        float keepMajor=1-smoothstep(_OhRealmLookFace.y,max(_OhRealmLookFace.y+1,_OhRealmLookFace.z),distanceWS);
        float keepMinor=1-smoothstep(_OhRealmLookFace.y*.3,_OhRealmLookFace.y,distanceWS);
        float fold=(RLNoise308(p.xz*.018)-.5)*26+(RLNoise308(p.xz*.07+3.1)-.5)*5;
        float bed=(p.y+p.x*.16+p.z*.10+fold)/max(.5,_OhRealmLookStrata.x);
        float major=max(1,_OhRealmLookJoints.z);
        float wx=n.x*n.x,wz=n.z*n.z;float sum=max(1e-4,wx+wz);wx/=sum;wz/=sum;
        if(wx<.04){wx=0;wz=1;}else if(wz<.04){wz=0;wx=1;}
        float2 big=0,fine=0;
        [branch] if(wx>0){big+=RLBeds308(p.z,bed/major,major,1)*wx;if(keepMinor>0)fine+=RLBeds308(p.z+31,bed,1,0)*wx;}
        [branch] if(wz>0){big+=RLBeds308(p.x,bed/major,major,1)*wz;if(keepMinor>0)fine+=RLBeds308(p.x+31,bed,1,0)*wz;}
        if(keepMinor<=0)fine=float2(1,0);
        float streak=RLNoise308(float2(p.x*.55+p.z*.43,p.y*.06))*.6+RLNoise308(float2(p.x*.37-p.z*.61+9.2,p.y*.11))*.4;
        // dry-brush grain: two octaves across the face, only near the eye
        float grain=RLNoise308(float2(p.x*1.9+p.z*1.3,p.y*.9))*.6+RLNoise308(float2(p.x*4.3-p.z*3.1,p.y*2.7)+3.7)*.4;
        float value=lerp(1,big.x,lerp(.45,1,keepMajor))*lerp(1,fine.x,.45*keepMinor);
        value*=lerp(1-_OhRealmLookStrata.z,1+_OhRealmLookStrata.z*.4,streak);
        value*=lerp(1,lerp(.80,1.12,grain),keepMinor);
        value*=lerp(1-_OhRealmLookJoints.w,1+_OhRealmLookJoints.w,RLNoise308(p.xz*.035+p.y*.021));
        float ink=max(big.y*lerp(.35,1,keepMajor),fine.y*.55*keepMinor);
        float face=(_OhRealmLookFace.x>0?lerp(lum,_OhRealmLookFace.x,.75):lum)+_OhRealmLookLift.y;
        float3 rock=face*value*(1-ink)*rockWash.rgb;
        float speckle=lerp(1-_OhRealmLookFace.w,1+_OhRealmLookFace.w*.5,RLNoise308(p.xz*1.3+p.y*.7));
        float3 scree=lerp(soil,face*rockWash.rgb*1.15,.55)*speckle;
        result=lerp(result,scree,talus);
        result=lerp(result,rock,steep);
    }
    float share=lerp(_OhRealmLookAmounts.x*soilWash.a,_OhRealmLookAmounts.y*rockWash.a,saturate(steep+talus*.5));
    return lerp(inkPaper,result,saturate(share*_OhRealmLook*amount));
}
#endif
