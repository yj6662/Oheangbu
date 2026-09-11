from pathlib import Path
r=Path('Oheangbu/Assets/_Project/Scripts/App/SpellVFX120')
p=r/'Vfx120Effect.FireBolt.cs';s=p.read_text(encoding='utf-8').replace('foreach(var ps in _fireContactSystems)\n                    {','foreach(var ps in _fireContactSystems)\n                    {\n                        if(Profile.UseOriginalKtp&&!IsAttachedFlame)continue;')
s=s.replace('_fireHitPattern.SetPropertyBlock(_firePatternBlock);','_fireHitPattern.SetPropertyBlock(_firePatternBlock);\n                if(Profile.UseOriginalKtp&&!IsAttachedFlame&&!(IsEmberCharge&&hit<0))_fireHitPattern.enabled=false;');p.write_text(s,encoding='utf-8')
p=r/'Vfx120Effect.FireGuard.cs';s=p.read_text(encoding='utf-8').replace('if(hit>=0&&at>=hit)foreach','if(!Profile.UseOriginalKtp&&hit>=0&&at>=hit)foreach');p.write_text(s,encoding='utf-8')
