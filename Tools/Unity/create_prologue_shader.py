from pathlib import Path
root=Path(__file__).resolve().parents[2]/'Oheangbu/Assets/_Project/Shaders'
source=(root/'CodexInkLandscape.shader').read_text(encoding='utf-8-sig')
source=source.replace('Shader "Oheangbu/CodexInkLandscape"','Shader "Oheangbu/PrologueTerrain"',1)
source=source.replace('float woodSlot = saturate(_UseWoodColor);',
'''// The authored walking surface stays pale; steep rock carries more ink.
                // This is a material response, not an extra atmosphere pass.
                tone *= lerp(1.0, 0.44, smoothstep(0.12, 0.55, verticalFace));
                float woodSlot = saturate(_UseWoodColor);''',1)
(root/'PrologueTerrain.shader').write_text(source,encoding='utf-8')
