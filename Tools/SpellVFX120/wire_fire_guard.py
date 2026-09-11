from pathlib import Path
r=Path('Oheangbu/Assets/_Project/Scripts')
p=r/'App/SpellVFX120/Vfx120Effect.cs';s=p.read_text(encoding='utf-8')
s=s.replace('|| IsFireBolt(Profile);','|| IsFireBolt(Profile) || IsFireGuard(Profile);')
s=s.replace('ClearFireBolt();','ClearFireBolt();\n            ClearFireGuard();').replace('BuildFireBolt();','BuildFireBolt();\n            BuildFireGuard();').replace('SampleFireBolt();','SampleFireBolt();\n            SampleFireGuard();')
s=s.replace('if(IsEmberCharge)return EmberChargeDetonatedAt;','if(IsFireGuard(Profile))return FireGuardClock;\n            if(IsEmberCharge)return EmberChargeDetonatedAt;')
p.write_text(s,encoding='utf-8')
p=r/'App/BrushStrokeFeedAdapter.cs';s=p.read_text(encoding='utf-8').replace('if (_pendingParry) SpellVFX120.Vfx120Effect.SelectBambooGuard(authored);','if (_pendingParry) { SpellVFX120.Vfx120Effect.SelectBambooGuard(authored); SpellVFX120.Vfx120Effect.SelectFireGuard(authored); }');p.write_text(s,encoding='utf-8')
p=r/'App/CombatLoopWiring.cs';s=p.read_text(encoding='utf-8').replace('outcome == ParryOutcome.Success && guardElement == Element.Wood\n                && SpellVFX120.Vfx120Effect.TrySignalBambooParry(impactPoint);','outcome == ParryOutcome.Success && ((guardElement == Element.Wood\n                && SpellVFX120.Vfx120Effect.TrySignalBambooParry(impactPoint)) || (guardElement == Element.Fire && SpellVFX120.Vfx120Effect.TrySignalFireParry(impactPoint)));');p.write_text(s,encoding='utf-8')
