"""Repeat hand comparison with diagnostic brush visibility off, exposing the repaired roots."""
from pathlib import Path
text=Path(__file__).with_name('render_hand_self_fold_correction.py').read_text()
text=text.replace("OUT=ART/'Inspect/HandsSelfFolds/Views'", "OUT=ART/'Inspect/HandsSelfFolds/RootViews'")
text=text.replace("(['combined_reach'] if label=='before' else ['rest','grip_down','grip_up','combined_reach'])", "['combined_reach']")
text=text.replace("or o.name=='DosaV2_SourceSurface'", "or o.name=='DosaV2_SourceSurface' or o.name.startswith('DosaBrushV2_')")
exec(compile(text,'root_surface_render','exec'))
