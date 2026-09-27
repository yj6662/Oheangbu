from pathlib import Path
import re
ROOT=Path(__file__).resolve().parents[2]/'Oheangbu'
source=ROOT/'Assets/_Project/Shaders/InkPaintingStudy'
target=ROOT/'Assets/_Project/Art/World/Reworld292/Shaders';target.mkdir(parents=True,exist_ok=True)
s=(source/'InkPaintingGround.shader').read_text(encoding='utf-8-sig')
s=s.replace('Oheangbu/Study/InkPaintingGround','Oheangbu/Reworld292/KoreanInkGround')
s=re.sub(r'#include "([^"\n]+)"',lambda m: '#include "'+str((source/m[1]).resolve().relative_to(ROOT)).replace('\\','/')+'"' if not m[1].startswith(('Packages/','Assets/')) else m[0],s)
s=s.replace('Assets/_Project/Shaders/InkPaintingStudy/TerrainRules276.hlsl','Assets/_Project/Art/World/Reworld292/Shaders/KoreanSurface292.hlsl')
s=s.replace('_StrataField276("Curvature and local exposure data",2D)="gray"{}','_StrataField276("Curvature and local exposure data",2D)="gray"{}\n        _Realm293("Authored realm pigments",2D)="gray"{}\n        _RealmStrength293("Realm pigment",Range(0,1))=0')
s=s.replace('TEXTURE2D(_StrataField276);','TEXTURE2D(_StrataField276); TEXTURE2D(_Realm293);')
s=s.replace('float _StrataStrength276;','float _RealmStrength293; float _StrataStrength276;')
r=(source/'TerrainRules276.hlsl').read_text(encoding='utf-8-sig')
start=r.index('float3 RuleMasks276(');end=r.index('float3 RuleJointCoords276',start)
r=r[:start]+'''// Authored rock / colluvium / moisture / path weights from the final world surface.
float3 RuleMasks276(float3 p,float3 n,float path)
{
    float4 field=SAMPLE_TEXTURE2D(_StrataField276,sampler_RockNormal,saturate(p.xz/float2(4000,6000)));
    float rock=saturate(field.r+smoothstep(.45,.8,1-n.y)*.35);
    rock*=1-field.a*.5;
    float debris=saturate(field.g)*(1-rock);
    return float3(rock,debris,1-rock-debris);
}
'''+r[end:]
r=r.replace('return lerp(_CIInk.rgb,_CIPaper.rgb,tone);','''float4 field=SAMPLE_TEXTURE2D(_StrataField276,sampler_RockNormal,saturate(p.xz/float2(4000,6000)));
    float3 mineral=lerp(float3(.94,.86,.72),float3(.90,.94,.94),masks.x);
    mineral=lerp(mineral,float3(.72,.81,.73),field.b*.38*(1-field.a));
    float wetDarkening=1-field.b*.12;
    return lerp(_CIInk.rgb,_CIPaper.rgb,tone)*lerp(1,mineral,.45)*wetDarkening;''')
r=r.replace('_StrataStrength276*(1-smoothstep(_StrataRange276.x,_StrataRange276.y,distanceWS))','_StrataStrength276')
r=r.replace('float wetDarkening=1-field.b*.12;','''float3 realm=SAMPLE_TEXTURE2D(_Realm293,sampler_RockNormal,saturate(p.xz/float2(4000,6000))).rgb;
    mineral*=lerp(float3(1,1,1),realm/max(.1,CILuma(realm)),_RealmStrength293);
    float wetDarkening=1-field.b*.12;''')
r=r.replace('float tone=lerp(soilTone,rockTone,masks.x);','''// Resolve small material marks into stable broad pigment masses in the distance.
    // Near geometry keeps its scanned albedo; the far field must not show a tiled cloth pattern.
    float resolve=1-smoothstep(35,180,distanceWS);
    float macro=Noise(p.xz*.013+n.xz*.8);
    soilTone=lerp(.43+macro*.13,soilTone,resolve);
    rockTone=lerp(.53+macro*.15,rockTone,resolve);
    float tone=lerp(soilTone,rockTone,masks.x);''')
(target/'KoreanInkGround.shader').write_text(s,encoding='utf8')
(target/'KoreanSurface292.hlsl').write_text(r,encoding='utf8')
architecture=(source.parent/'CliffProp278.shader').read_text(encoding='utf-8-sig').replace('Oheangbu/Prototype/CliffProp278','Oheangbu/Reworld292/KoreanArchitecture')
architecture=architecture.replace('return half4(lerp(colour, _PaperTint278.rgb, wash), 1);','''half value=dot(colour,half3(.2126,.7152,.0722));
                half tone=smoothstep(.025,.72,value);
                half3 pigment=lerp(half3(.055,.050,.042),half3(.88,.855,.79),tone);
                // Retain the source timber/paint hue and relief. Static UV textures,
                // soft ink tonal grouping and no second distance wash.
                colour=lerp(colour,pigment,.48);
                return half4(colour,1);''')
(target/'KoreanArchitecture.shader').write_text(architecture,encoding='utf8')
print('Isolated existing-ink shader derivative; all provider/shared shaders untouched')
