from pathlib import Path
p=Path('Oheangbu/Assets/_Project/Scripts/Editor/SpellVFX120/Vfx120FireJetBuilder.cs');s=p.read_text(encoding='utf-8').replace('e.PreviewControlled=true;e.Begin','e.PreviewControlled=true;e.SetImpactClock(.6f);e.Begin').replace('e.SetImpactClock(.31f);e.Sample(.32f);','e.Sample(.62f);');p.write_text(s,encoding='utf-8')
