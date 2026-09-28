"""Build private #296 shader copies from immutable #293/#295 inputs.

This edits only Architecture296/Shaders. It never invokes Unity or touches a
material, texture, terrain, scene, or earlier candidate. --check is read-only.
"""
from pathlib import Path
import argparse
import hashlib
import json
import re

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "Oheangbu/Assets/_Project/Art/World"
OUT = ASSETS / "Architecture296/Shaders"


def replace_once(text, old, new):
    if text.count(old) != 1:
        raise ValueError(f"Expected one source anchor, found {text.count(old)}: {old[:100]}")
    return text.replace(old, new)


def between(text, start, end, replacement):
    a, b = text.index(start), text.index(end, text.index(start))
    return text[:a] + replacement + text[b:]


def ground_samplers(text, declarations=False):
    # DepthNormals can optimize away _GroundPathMask while its sampler is still
    # needed by the bank normal mask. Texture-named shared samplers then fail
    # Unity's per-pass binding validation. Two explicit inline states also leave
    # room for URP shadows/SSAO and the unchanged ink includes under D3D11's 16.
    names = ("GrassMap", "GrassNormal", "DirtMap", "DirtNormal", "RockMap", "RockNormal",
             "GroundPathMask", "RealmPigment", "RebuildGravel", "RebuildGravelNormal")
    if declarations:
        for name in names:
            text = replace_once(text, f"SAMPLER(sampler_{name});", "")
        text = replace_once(text, "        TEXTURE2D(_GrassMap);", "        SAMPLER(sampler_Surface296_linear_repeat_aniso8);\n        SAMPLER(sampler_SurfaceLinearClamp296);\n        TEXTURE2D(_GrassMap);")
    for name in sorted(names, key=len, reverse=True):
        state = "sampler_SurfaceLinearClamp296" if name in ("GroundPathMask", "RealmPigment") else "sampler_Surface296_linear_repeat_aniso8"
        text = re.sub(r"\bsampler_" + name + r"\b", state, text)
    return text


RULE_NORMAL = r'''// #296 channel-aligned stochastic coordinates. Derivatives are evaluated before branches.
float3 RuleRockNormal276(float3 p,float3 n,float detail,float3 dx,float3 dy)
{
    float3 w=pow(abs(n),4);w/=max(.001,w.x+w.y+w.z);
    float3 q=RuleJointCoords276(p);
    // The joint warp is continuous; its gradient belongs to the original coordinates.
    float3 qdx=RuleJointCoords276(p+dx)-q,qdy=RuleJointCoords276(p+dy)-q;
    SurfaceFrame296 fx=SurfaceFrameGrad296(q.zy,qdx.zy,qdy.zy,19u,0);
    SurfaceFrame296 fy=SurfaceFrameGrad296(q.xz,qdx.xz,qdy.xz,19u,0);
    SurfaceFrame296 fz=SurfaceFrameGrad296(q.xy,qdx.xy,qdy.xy,19u,0);
    float3 x=SurfaceNormal296(TEXTURE2D_ARGS(_StrataFractureNormal276,sampler_RockNormal),fx,1.25*detail);
    float3 y=SurfaceNormal296(TEXTURE2D_ARGS(_StrataFractureNormal276,sampler_RockNormal),fy,1.25*detail);
    float3 z=SurfaceNormal296(TEXTURE2D_ARGS(_StrataFractureNormal276,sampler_RockNormal),fz,1.25*detail);
    float3 perturb=float3(0,x.y,x.x)*w.x+float3(y.x,0,y.y)*w.y+float3(z.x,z.y,0)*w.z;
    // The fine rock normal and the rock colour share the same projection and seed.
    fx=SurfaceFrameGrad296(q.zy,qdx.zy,qdy.zy,17u,0);
    fy=SurfaceFrameGrad296(q.xz,qdx.xz,qdy.xz,17u,0);
    fz=SurfaceFrameGrad296(q.xy,qdx.xy,qdy.xy,17u,0);
    x=SurfaceNormal296(TEXTURE2D_ARGS(_RockNormal,sampler_RockNormal),fx,.55*detail);
    y=SurfaceNormal296(TEXTURE2D_ARGS(_RockNormal,sampler_RockNormal),fy,.55*detail);
    z=SurfaceNormal296(TEXTURE2D_ARGS(_RockNormal,sampler_RockNormal),fz,.55*detail);
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
    float3 dirt=SurfaceNormal296(TEXTURE2D_ARGS(_DirtNormal,sampler_DirtNormal),dirtFrame,.8*detail);
    float3 gravel=SurfaceNormal296(TEXTURE2D_ARGS(_RebuildGravelNormal,sampler_RebuildGravelNormal),gravelFrame,.9*detail);
    float3 soilSample=normalize(lerp(dirt,gravel,masks.y));
    // Realm floor colour and its CC0 normal use the same frame and realm weights.
    float4 fw=SAMPLE_TEXTURE2D(_Floor293,sampler_RockNormal,saturate(p.xz/float2(4000,6000)));
    float wC=fw.r,wJ=fw.g*_FloorRealms293,wO=fw.b*_FloorRealms293,wH=fw.a*_FloorRealms293,wW=saturate(1-fw.r-fw.g-fw.b-fw.a)*_FloorRealms293;
    float total=wC+wJ+wO+wH+wW;
    float nearFloor=(1-smoothstep(_FloorRange293.x,_FloorRange293.y,distanceWS))*total*_FloorStrength293*masks.z;
    SurfaceFrame296 floorFrame=SurfaceFrameGrad296(p.xz*_FloorScale293,dx.xz*_FloorScale293,dy.xz*_FloorScale293,31u,1);
    if(nearFloor>.001)
    {
        float3 fn=0;float used=0;
        [branch] if(wC>.004){fn+=wC*SurfaceNormal296(TEXTURE2D_ARGS(_FloorNormal296,sampler_DirtNormal),floorFrame,.65*detail);used+=wC;}
        [branch] if(wJ>.004){fn+=wJ*SurfaceNormal296(TEXTURE2D_ARGS(_FloorNormalJ296,sampler_DirtNormal),floorFrame,.65*detail);used+=wJ;}
        [branch] if(wO>.004){fn+=wO*SurfaceNormal296(TEXTURE2D_ARGS(_FloorNormalC296,sampler_DirtNormal),floorFrame,.65*detail);used+=wO;}
        [branch] if(wH>.004){fn+=wH*SurfaceNormal296(TEXTURE2D_ARGS(_FloorNormalH296,sampler_DirtNormal),floorFrame,.65*detail);used+=wH;}
        [branch] if(wW>.004){fn+=wW*SurfaceNormal296(TEXTURE2D_ARGS(_FloorNormalW296,sampler_DirtNormal),floorFrame,.65*detail);used+=wW;}
        if(used>0)soilSample=normalize(lerp(soilSample,normalize(fn),nearFloor));
    }
    // Bank colour and normal share each scan's exact scale, rotation and seed.
    float2 bankUV=(p.xz*.5+.5)/float2(2001,3001);
    float2 bank=SAMPLE_TEXTURE2D(_BankMask294,sampler_GroundPathMask,bankUV).rg;
    float bankWeight=bank.r*_BankStrength294*(1-smoothstep(200,700,distanceWS));
    SurfaceFrame296 bankGravel=SurfaceFrameGrad296(p.xz*.48,dx.xz*.48,dy.xz*.48,41u,1);
    float2 mudUV=float2(-p.z,p.x)*(.48*.73);
    SurfaceFrame296 bankMud=SurfaceFrameGrad296(mudUV,float2(-dx.z,dx.x)*(.48*.73),float2(-dy.z,dy.x)*(.48*.73),43u,1);
    if(bankWeight>.002)
    {
        float wet=saturate(bank.g/max(.001,bank.r));
        float3 gn=SurfaceNormal296(TEXTURE2D_ARGS(_BankGravelNormal296,sampler_DirtNormal),bankGravel,.65*detail);
        float3 mn=SurfaceNormal296(TEXTURE2D_ARGS(_BankMudNormal296,sampler_DirtNormal),bankMud,.55*detail);
        mn.xy=float2(mn.y,-mn.x); // Undo the original mud projection's 90-degree turn.
        soilSample=normalize(lerp(soilSample,normalize(lerp(gn,mn,.3+wet*.5)),bankWeight*.9));
    }
    float3 tangent=normalize(abs(n.y)+abs(n.x)>.001?float3(n.y,-n.x,0):float3(1,0,0));
    float3 soil=normalize(tangent*soilSample.x+normalize(cross(tangent,n))*soilSample.y+n*soilSample.z);
    return normalize(lerp(soil,rock,masks.x));
}
'''

RULE_ALBEDO = r'''    float3 worldDx296=ddx(p),worldDy296=ddy(p);
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
        float3 rockMean=SAMPLE_TEXTURE2D_LOD(_RockMap,sampler_RockMap,float2(.5,.5),12).rgb;
        rock=SurfaceColourMean296(TEXTURE2D_ARGS(_RockMap,sampler_RockMap),fx,rockMean)*w.x+
            SurfaceColourMean296(TEXTURE2D_ARGS(_RockMap,sampler_RockMap),fy,rockMean)*w.y+
            SurfaceColourMean296(TEXTURE2D_ARGS(_RockMap,sampler_RockMap),fz,rockMean)*w.z;
        if(distanceWS<240)
        {
            fx=SurfaceFrameGrad296(q.zy,qdx.zy,qdy.zy,19u,0);
            fy=SurfaceFrameGrad296(q.xz,qdx.xz,qdy.xz,19u,0);
            fz=SurfaceFrameGrad296(q.xy,qdx.xy,qdy.xy,19u,0);
            cavity=SurfaceData296(TEXTURE2D_ARGS(_StrataFracture276,sampler_RockMap),fx).g*w.x+
                SurfaceData296(TEXTURE2D_ARGS(_StrataFracture276,sampler_RockMap),fy).g*w.y+
                SurfaceData296(TEXTURE2D_ARGS(_StrataFracture276,sampler_RockMap),fz).g*w.z;
        }
        SurfaceFrame296 dirtFrame=SurfaceFrameGrad296(p.xz*.8,worldDx296.xz*.8,worldDy296.xz*.8,11u,1);
        soil=SurfaceColour296(TEXTURE2D_ARGS(_DirtMap,sampler_DirtMap),dirtFrame);
        if(distanceWS<110)
        {
            SurfaceFrame296 gravelFrame=SurfaceFrameGrad296(p.xz*.5,worldDx296.xz*.5,worldDy296.xz*.5,13u,1);
            gravel=SurfaceData296(TEXTURE2D_ARGS(_RebuildGravel,sampler_RebuildGravel),gravelFrame).rgb;
        }
    }
'''


def ground_surface():
    s = (ASSETS / "Watershed295/Shaders/KoreanSurface295.hlsl").read_text(encoding="utf-8")
    s = '#include "Assets/_Project/Art/World/Architecture296/Shaders/Stochastic296.hlsl"\n' + s
    s = between(s, "// Whiteout-style triplanar perturbation.", "// #293 realm floors:", RULE_NORMAL)
    s = replace_once(s, "float3 FloorNear293(float3 f,float3 f2,float mixer,float3 tint,float chroma){f=lerp(f,f2,mixer);float fl=CILuma(f);return lerp(fl.xxx,f,chroma)*tint*1.3;}",
                     "float3 FloorNear296(float3 f,float3 tint,float chroma){float fl=CILuma(f);return lerp(fl.xxx,f,chroma)*tint*1.3;}")
    start = s.index("float3 RuleColour276(")
    a = s.index("    float3 w=", start)
    b = s.index("    // No painted atlas", a)
    s = s[:a] + RULE_ALBEDO + s[b:]
    s = replace_once(s, "        if(nearFloor>.001)\n", "        SurfaceFrame296 floorFrame=SurfaceFrameGrad296(uv,worldDx296.xz*_FloorScale293,worldDy296.xz*_FloorScale293,31u,1);\n        if(nearFloor>.001)\n")
    s = replace_once(s, "            float2 uv2=float2(uv.y,-uv.x)*.73+.37;float mixer=smoothstep(.35,.65,Noise(p.xz*.05));  // the second tap breaks the repeat\n", "")
    for suffix, weight, tint, chroma in [
        ("", "wC", "_FloorTint293.rgb", "_FloorChroma293"),
        ("J", "wJ", "_FloorTintJ293.rgb", "_FloorTintJ293.a"),
        ("C", "wO", "_FloorTintC293.rgb", "_FloorTintC293.a"),
        ("H", "wH", "_FloorTintH293.rgb", "_FloorTintH293.a"),
        ("W", "wW", "_FloorTintW293.rgb", "_FloorTintW293.a")]:
        texture = f"_FloorMap{suffix}293"
        s = replace_once(s,
            f"FloorNear293(SAMPLE_TEXTURE2D({texture},sampler_DirtMap,uv).rgb,SAMPLE_TEXTURE2D({texture},sampler_DirtMap,uv2).rgb,mixer,{tint},{chroma})",
            f"FloorNear296(SurfaceColour296(TEXTURE2D_ARGS({texture},sampler_DirtMap),floorFrame),{tint},{chroma})")
    s = replace_once(s, "        [branch] if(weight>.002)\n", "        SurfaceFrame296 bankGravel=SurfaceFrameGrad296(p.xz*.48,worldDx296.xz*.48,worldDy296.xz*.48,41u,1);\n        SurfaceFrame296 bankMud=SurfaceFrameGrad296(float2(-p.z,p.x)*(.48*.73),float2(-worldDx296.z,worldDx296.x)*(.48*.73),float2(-worldDy296.z,worldDy296.x)*(.48*.73),43u,1);\n        [branch] if(weight>.002)\n")
    s = replace_once(s, "float3 gravel=SAMPLE_TEXTURE2D(_BankGravel294,sampler_DirtMap,uv).rgb;", "float3 gravel=SurfaceColour296(TEXTURE2D_ARGS(_BankGravel294,sampler_DirtMap),bankGravel);")
    s = replace_once(s, "float3 mud=SAMPLE_TEXTURE2D(_BankMud294,sampler_DirtMap,float2(-uv.y,uv.x)*.73).rgb;", "float3 mud=SurfaceColour296(TEXTURE2D_ARGS(_BankMud294,sampler_DirtMap),bankMud);")
    return ground_samplers(s)


def ground_shader():
    s = (ASSETS / "Watershed295/Shaders/KoreanInkGround295.shader").read_text(encoding="utf-8")
    s = replace_once(s, 'Shader "Oheangbu/Watershed295/KoreanInkGround"', 'Shader "Oheangbu/Architecture296/KoreanInkGround"')
    s = replace_once(s, '"Assets/_Project/Art/World/Watershed295/Shaders/KoreanSurface295.hlsl"', '"Assets/_Project/Art/World/Architecture296/Shaders/KoreanSurface296.hlsl"')
    properties = "".join(f'        {name}("Natural surface normal",2D)="bump"{{}}\n' for name in NORMAL_PROPERTIES)
    s = replace_once(s, '        _BankStrength294(', properties + '        _BankStrength294(')
    declarations = "        " + "".join(f"TEXTURE2D({name});" for name in NORMAL_PROPERTIES) + " // Existing repeat normal sampler.\n"
    s = replace_once(s, "        TEXTURE2D(_BankMask294);", declarations + "        TEXTURE2D(_BankMask294);")
    return ground_samplers(s, declarations=True)


NORMAL_PROPERTIES = ["_FloorNormal296", "_FloorNormalJ296", "_FloorNormalC296", "_FloorNormalH296", "_FloorNormalW296", "_BankGravelNormal296", "_BankMudNormal296"]


def highland_shader():
    s = (ASSETS / "Reworld292/Shaders/HighlandGranite293.shader").read_text(encoding="utf-8")
    s = replace_once(s, 'Shader "Oheangbu/Reworld293/HighlandGranite"', 'Shader "Oheangbu/Architecture296/HighlandNatural"')
    return replace_once(s, '"Assets/_Project/Art/World/Reworld292/Shaders/HighlandSurface293.hlsl"', '"Assets/_Project/Art/World/Architecture296/Shaders/HighlandSurface296.hlsl"')


def highland_surface():
    s = (ASSETS / "Reworld292/Shaders/HighlandSurface293.hlsl").read_text(encoding="utf-8")
    s = s.replace("HIGHLAND_SURFACE_293", "HIGHLAND_SURFACE_296")
    s = replace_once(s, "#define HIGHLAND_SURFACE_296\n", '#define HIGHLAND_SURFACE_296\n#include "Assets/_Project/Art/World/Architecture296/Shaders/Stochastic296.hlsl"\n')
    a = s.index("half3 TriColour293(")
    b = s.index("HighlandSample293 HighlandSurface293(", a)
    s = s[:a] + HIGHLAND_SAMPLES + s[b:]
    s = replace_once(s, " Tri293 t=Triplanar293(world,n,scale);", " Tri293 t=Triplanar293(world,n,scale);\n TriFrames296 frames=HighlandFrames296(t);\n float distanceWS=distance(world,_WorldSpaceCameraPos);")
    s = replace_once(s, " half3 col=lerp(average,TriColour293(t,TEXTURE2D_ARGS(diffuse,ds)),nearDetail*.85+.15);", " half3 sampled=average;\n if(distanceWS<750)sampled=lerp(average,TriColour296(t,frames,TEXTURE2D_ARGS(diffuse,ds)),1-smoothstep(500,750,distanceWS));\n half3 col=lerp(average,sampled,nearDetail*.85+.15);")
    s = replace_once(s, " half3 perturb=TriPerturb293(t,TEXTURE2D_ARGS(bump,bs));\n half3 packed=TriColour293(t,TEXTURE2D_ARGS(mask,ms));", " half3 perturb=0,packed=1;\n if(nearDetail>0)\n {\n  perturb=TriPerturb296(t,frames,TEXTURE2D_ARGS(bump,bs));\n  packed=TriData296(t,frames,TEXTURE2D_ARGS(mask,ms));\n }")
    s = replace_once(s, "  Tri293 m=Triplanar293(world,n,mossScale);", "  Tri293 m=Triplanar293(world,n,mossScale);\n  TriFrames296 mossFrames=HighlandFrames296(m);")
    s = replace_once(s, "  col=lerp(col,lerp(TriAverage293(m,TEXTURE2D_ARGS(moss,mds)),TriColour293(m,TEXTURE2D_ARGS(moss,mds)),nearDetail*.85+.15),cover);", "  half3 mossAverage=TriAverage293(m,TEXTURE2D_ARGS(moss,mds)),mossColour=mossAverage;\n  if(distanceWS<750)mossColour=lerp(mossAverage,TriColour296(m,mossFrames,TEXTURE2D_ARGS(moss,mds)),1-smoothstep(500,750,distanceWS));\n  col=lerp(col,lerp(mossAverage,mossColour,nearDetail*.85+.15),cover);")
    s = replace_once(s, "  perturb=lerp(perturb,TriPerturb293(m,TEXTURE2D_ARGS(mossBump,mbs)),cover);", "  if(nearDetail>0)perturb=lerp(perturb,TriPerturb296(m,mossFrames,TEXTURE2D_ARGS(mossBump,mbs)),cover);")
    return s


HIGHLAND_SAMPLES = r'''struct TriFrames296 { SurfaceFrame296 x,y,z; };
TriFrames296 HighlandFrames296(Tri293 t)
{
 TriFrames296 f;
 // No quarter-turns on bedded cliff rock: offset variation keeps its direction.
 f.x=SurfaceFrame296At(t.ux,59u,0);
 f.y=SurfaceFrame296At(t.uy,59u,0);
 f.z=SurfaceFrame296At(t.uz,59u,0);
 return f;
}
half3 TriColour296(Tri293 t,TriFrames296 f,TEXTURE2D_PARAM(tex,ss))
{
 float3 mean=SAMPLE_TEXTURE2D_LOD(tex,ss,float2(.5,.5),12).rgb;
 return SurfaceColourMean296(TEXTURE2D_ARGS(tex,ss),f.x,mean)*t.w.x+
  SurfaceColourMean296(TEXTURE2D_ARGS(tex,ss),f.y,mean)*t.w.y+
  SurfaceColourMean296(TEXTURE2D_ARGS(tex,ss),f.z,mean)*t.w.z;
}
half3 TriData296(Tri293 t,TriFrames296 f,TEXTURE2D_PARAM(tex,ss))
{
 return SurfaceData296(TEXTURE2D_ARGS(tex,ss),f.x).rgb*t.w.x+
  SurfaceData296(TEXTURE2D_ARGS(tex,ss),f.y).rgb*t.w.y+
  SurfaceData296(TEXTURE2D_ARGS(tex,ss),f.z).rgb*t.w.z;
}
half3 TriAverage293(Tri293 t,TEXTURE2D_PARAM(tex,ss))
{return SAMPLE_TEXTURE2D_LOD(tex,ss,t.ux,9).rgb*t.w.x+SAMPLE_TEXTURE2D_LOD(tex,ss,t.uy,9).rgb*t.w.y+SAMPLE_TEXTURE2D_LOD(tex,ss,t.uz,9).rgb*t.w.z;}
half3 TriPerturb296(Tri293 t,TriFrames296 f,TEXTURE2D_PARAM(bump,bs))
{
 half3 ax=SurfaceNormal296(TEXTURE2D_ARGS(bump,bs),f.x,1),ay=SurfaceNormal296(TEXTURE2D_ARGS(bump,bs),f.y,1),az=SurfaceNormal296(TEXTURE2D_ARGS(bump,bs),f.z,1);
 return float3(0,ax.y,ax.x*t.sg.x)*t.w.x+float3(ay.x,0,ay.y*t.sg.y)*t.w.y+float3(az.x*t.sg.z,az.y,0)*t.w.z;
}
'''


def expected():
    return {"KoreanInkGround296.shader": ground_shader(), "KoreanSurface296.hlsl": ground_surface(),
            "HighlandGranite296.shader": highland_shader(), "HighlandSurface296.hlsl": highland_surface()}


def run(check=False):
    rows = []
    for name, text in expected().items():
        data = text.replace("\r\n", "\n").encode("utf-8")
        path = OUT / name
        same = path.exists() and path.read_bytes() == data
        if not check and not same:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        rows.append({"file": name, "matches": same if check else path.read_bytes() == data,
                     "sha256": hashlib.sha256(data).hexdigest()})
    return {"check_only": check, "passed": all(row["matches"] for row in rows), "files": rows}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    result = run(args.check)
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result["passed"] else 1)
