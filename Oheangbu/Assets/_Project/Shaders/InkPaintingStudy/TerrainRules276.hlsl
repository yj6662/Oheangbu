// Opt-in candidate geology. All coordinates are metres, independent of mesh UV/scale.
float RuleWeight276(float distanceWS)
{
    return _StrataStrength276*(1-smoothstep(_StrataRange276.x,_StrataRange276.y,distanceWS));
}
float3 RuleMasks276(float3 p,float3 n,float path)
{
    float2 uv=p.xz/float2(4000,6000);
    float2 field=SAMPLE_TEXTURE2D(_StrataField276,sampler_GroundPathMask,uv).rg;
    float convex=smoothstep(.53,.80,field.r);
    float hollow=1-smoothstep(.22,.48,field.r);
    float exposed=smoothstep(.48,.85,field.g);
    float slope=1-saturate(n.y);
    // These rolling Korean foothills often have 15-30 degree slopes, not cliffs.
    float rock=smoothstep(.055,.235,slope)*(lerp(.40,1,exposed)+convex*.45);
    rock=saturate(rock)*(1-hollow*.7)*(1-path);
    float debris=saturate(hollow*.72+smoothstep(.03,.10,slope)*(1-smoothstep(.13,.30,slope))*.33);
    debris*=1-rock;
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
    float tone=lerp(soilTone,rockTone,masks.x);
    return lerp(_CIInk.rgb,_CIPaper.rgb,tone);
}
