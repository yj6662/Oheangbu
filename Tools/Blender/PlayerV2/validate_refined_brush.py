"""Run the original exported physical-tip contract against the derivative FBX."""
import importlib.util
import json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'Art/PlayerV2/Staging/BrushRefined'
report=json.loads((OUT/'tuft-build-report.json').read_text())
compat=dict(report);compat['triangles']=report['totalTriangles']
(OUT/'brush-build-report.json').write_text(json.dumps(compat,indent=2))
spec=importlib.util.spec_from_file_location('original_brush_contract',Path(__file__).with_name('build_brush.py'))
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
module.OUT=OUT
module.validate_export()
