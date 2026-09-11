from pathlib import Path
root=Path(__file__).resolve().parents[2]
src=root/'Oheangbu/Assets/_Project/Shaders/SpellVFX120/BotanicalSurface.shader'
s=src.read_text(encoding='utf-8').replace('Oheangbu/VFX120/BotanicalSurface','Oheangbu/VFX120/WetRoot')
s=s[:s.index('        Pass\n        {\n            Name "ShadowCaster"')]+ '    }\n}\n'
s=s.replace('half3 colour=albedo*(ambient+light.color*direct*.85);','half3 view=SafeNormalize(GetWorldSpaceViewDir(i.positionWS));\n                half sheen=pow(saturate(dot(n,SafeNormalize(light.direction+view))),48)*.24;\n                half3 colour=albedo*(ambient+light.color*direct*.85)+light.color*sheen*light.shadowAttenuation;')
(src.parent/'WetRoot.shader').write_text(s,encoding='utf-8')
