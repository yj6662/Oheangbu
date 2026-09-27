"""Edit only named study material controls; source materials/scenes are untouched."""
from pathlib import Path
import argparse
import math
import re

ROOT = Path(__file__).resolve().parents[2]
FOLDER = ROOT / 'Oheangbu/Assets/_Project/Art/World/WorldCompact/InkLandscape/InkPaintingStudy/FlowRevision'

def assign(text, section, name, value):
    expression = rf'(?m)^(    - {re.escape(name)}: ).*$'
    if re.search(expression, text):
        return re.sub(expression, lambda m: m.group(1) + value, text)
    marker = f'  {section}:\n'
    if text.count(marker) != 1:
        raise ValueError(f'Expected one material section {section}')
    return text.replace(marker, marker + f'    - {name}: {value}\n', 1)

parser = argparse.ArgumentParser()
parser.add_argument('--valley', type=int, choices=(0, 1), required=True)
parser.add_argument('--strength', type=float, default=.45)
parser.add_argument('--edge', type=int, choices=(0, 1))
parser.add_argument('--far-path', type=int, choices=(0, 1))
parser.add_argument('--canopy', type=int, choices=(0, 1))
parser.add_argument('--stochastic-fade', type=int, choices=(0, 1))
parser.add_argument('--canopy-quiet', type=int, choices=(0, 1))
parser.add_argument('--canopy-quiet-surface', type=float, nargs=4,
                    metavar=('COLOUR_BLEND', 'NORMAL_RETAIN', 'LINEAR_CENTRE', 'LIGHT_BLEND'))
parser.add_argument('--foliage-mip-bias', type=float)
parser.add_argument('--form', type=int, choices=(0, 1))
parser.add_argument('--form-brush-threshold', type=float, nargs=2)
parser.add_argument('--form-brush-scale', type=float, nargs=2, default=(180,480))
parser.add_argument('--form-dry', type=float)
parser.add_argument('--form-stroke-body', type=float)
parser.add_argument('--form-aspect-contrast', type=float)
parser.add_argument('--form-paper', type=float, nargs=2, metavar=('STRENGTH', 'PAPER_FRACTION'))
parser.add_argument('--far-path-air', type=int, choices=(0, 1))
parser.add_argument('--path-mountain', type=int, choices=(0, 1))
parser.add_argument('--path-mountain-range', type=float, nargs=2, metavar=('START', 'END'))
parser.add_argument('--path-mountain-strength', type=float)
parser.add_argument('--far-path-cap', type=float, default=.10)
parser.add_argument('--far-path-range', type=float, nargs=2, metavar=('START', 'END'))
parser.add_argument('--bank-range', type=float, nargs=2, metavar=('START', 'END'))
parser.add_argument('--bank-steep', type=int, choices=(0, 1))
parser.add_argument('--bank-steep-slope', type=float, nargs=2, metavar=('START_DEGREES', 'FULL_DEGREES'))
parser.add_argument('--bank-steep-range', type=float, nargs=2, metavar=('START_METRES', 'FULL_METRES'))
args = parser.parse_args()
if not 0 <= args.strength <= 1:
    raise ValueError('Blend strength must be within 0..1')
if args.form_brush_threshold is not None and not 0 <= args.form_brush_threshold[0] < args.form_brush_threshold[1] <= 1:
    raise ValueError('Dry-brush thresholds must increase within 0..1')
if any(value < 1 for value in args.form_brush_scale):
    raise ValueError('Brush projection metres must be positive')
if args.form_dry is not None and not 0 <= args.form_dry <= .025:
    raise ValueError('Dry pigment must stay within the shader limit 0..0.025')
if any(value is not None and not 0 <= value <= 1 for value in (args.form_stroke_body, args.form_aspect_contrast)):
    raise ValueError('Form stroke/aspect strengths must stay within 0..1')
if args.form_paper is not None and any(not 0 <= value <= 1 for value in args.form_paper):
    raise ValueError('Form paper strength and fraction must stay within 0..1')
if args.path_mountain_range is not None and not 250 <= args.path_mountain_range[0] < args.path_mountain_range[1]:
    raise ValueError('Mountain path sample exchange must start at or beyond250metres')
if args.path_mountain_strength is not None and not 0 <= args.path_mountain_strength <= 1:
    raise ValueError('Mountain path strength must stay within0..1')
if args.far_path_range is not None and not 30 <= args.far_path_range[0] < args.far_path_range[1]:
    raise ValueError('Far path range must increase and start at or beyond30metres')
if args.bank_range is not None and not 30 <= args.bank_range[0] < args.bank_range[1]:
    raise ValueError('Bank distance range must increase and preserve the first 30 metres')
if args.bank_steep_slope is not None and not 0 <= args.bank_steep_slope[0] < args.bank_steep_slope[1] <= 90:
    raise ValueError('Steep bank slope must increase within 0..90 degrees')
if args.bank_steep_range is not None and not 30 <= args.bank_steep_range[0] < args.bank_steep_range[1]:
    raise ValueError('Steep bank range must increase and preserve the first30metres')
if not 0 < args.far_path_cap <= 1:
    raise ValueError('Far path cap must be within 0..1')
if args.foliage_mip_bias is not None and not 0 <= args.foliage_mip_bias <= 2:
    raise ValueError('Foliage RGB mip bias must be within 0..2')
if args.canopy_quiet_surface is not None and any(not 0 <= value <= 1 for value in args.canopy_quiet_surface):
    raise ValueError('Canopy quiet surface values must stay within 0..1')
files = sorted((FOLDER / 'Materials').glob('*.mat')) + sorted((FOLDER / 'FoliageMaterials').glob('*.mat'))
if len(files) != 205:
    raise ValueError(f'Expected the 205 prepared study materials, got {len(files)}')
changes = []
for path in files:
    original = path.read_text(encoding='utf-8-sig')
    result = assign(original, 'm_Floats', '_PaintedValleyReserve', str(args.valley))
    result = assign(result, 'm_Colors', '_PaintedValleyDistance', '{r: 250, g: 900, b: 0, a: 0}')
    result = assign(result, 'm_Colors', '_PaintedValleyRelief', '{r: 10, g: 30, b: 55, a: 125}')
    result = assign(result, 'm_Colors', '_PaintedValleyAppearance', f'{{r: {args.strength}, g: 0.72, b: 0, a: 0}}')
    if args.form is not None:
        result = assign(result, 'm_Floats', '_PaintedFormEnabled', str(args.form))
        result = assign(result, 'm_Floats', '_PaintedFormAuthored', '0')
    if args.form_brush_threshold is not None:
        low, high = args.form_brush_threshold
        cross, relief = args.form_brush_scale
        result = assign(result, 'm_Colors', '_PaintedFormBrush', f'{{r: {cross}, g: {relief}, b: {low}, a: {high}}}')
    if args.form_dry is not None:
        result = assign(result, 'm_Colors', '_PaintedFormTones', f'{{r: 0.006, g: 0.045, b: 0.16, a: {args.form_dry}}}')
    if args.form_stroke_body is not None:
        result = assign(result, 'm_Floats', '_PaintedFormStrokeBody', str(args.form_stroke_body))
    if args.form_aspect_contrast is not None:
        result = assign(result, 'm_Floats', '_PaintedFormAspectContrast', str(args.form_aspect_contrast))
    if args.form_paper is not None:
        strength, fraction = args.form_paper
        result = assign(result, 'm_Colors', '_PaintedFormPaper', f'{{r: {strength}, g: {fraction}, b: 0, a: 0}}')
    if args.edge is not None and path.parent.name == 'Materials':
        result = assign(result, 'm_Floats', '_PaintedEdgeWash', str(args.edge))
        result = assign(result, 'm_Colors', '_PaintedEdgeWashStrengths', '{r: 0.30, g: 0.25, b: 0, a: 0}')
        result = assign(result, 'm_Colors', '_PaintedEdgeWashResponse', '{r: 0.5, g: 1, b: 0, a: 0}')
    if args.far_path is not None and path.parent.name == 'Materials':
        result = assign(result, 'm_Floats', '_PaintedFarPath', str(args.far_path))
        result = assign(result, 'm_Colors', '_PaintedFarPathAppearance', f'{{r: {args.far_path_cap}, g: 1, b: 0, a: 0}}')
    if args.far_path_range is not None and path.parent.name == 'Materials':
        start, end = args.far_path_range
        result = assign(result, 'm_Colors', '_PaintedFarPathDistance', f'{{r: {start}, g: {end}, b: 0, a: 0}}')
    if args.bank_range is not None and path.parent.name == 'Materials':
        start, end = args.bank_range
        result = assign(result, 'm_Colors', '_PaintedRoadBankParams', f'{{r: 2, g: 6, b: {start}, a: {end}}}')
    if args.bank_steep is not None and path.parent.name == 'Materials':
        result = assign(result, 'm_Floats', '_PaintedRoadBankSteep', str(args.bank_steep))
    if (args.bank_steep_slope is not None or args.bank_steep_range is not None) and path.parent.name == 'Materials':
        match = re.search(r'(?m)^    - _PaintedRoadBankSteepParams: \{r: ([^,]+), g: ([^,]+), b: ([^,]+), a: ([^}]+)\}', result)
        values = list(map(float, match.groups())) if match else [math.cos(math.radians(45)),math.cos(math.radians(60)),80,140]
        if args.bank_steep_slope is not None: values[:2] = [math.cos(math.radians(v)) for v in args.bank_steep_slope]
        if args.bank_steep_range is not None: values[2:] = args.bank_steep_range
        a,b,c,d = values
        result = assign(result, 'm_Colors', '_PaintedRoadBankSteepParams', f'{{r: {a}, g: {b}, b: {c}, a: {d}}}')
    # Ground only: the background Terrain shader has no albedo fetch budget to exchange.
    if path.parent.name == 'Materials' and re.search(r'm_Shader: .*guid: 60eee8feffce1ac4c91a72d79c03f495[, }]', original):
        if args.path_mountain is not None:
            result = assign(result, 'm_Floats', '_PaintedPathMountain', str(args.path_mountain))
        if args.path_mountain_range is not None or args.path_mountain_strength is not None:
            match = re.search(r'(?m)^    - _PaintedPathMountainRange: \{r: ([^,]+), g: ([^,]+), b: ([^,]+), a: ([^}]+)\}', result)
            values = list(map(float, match.groups())) if match else [250,550,.9,0]
            if args.path_mountain_range is not None: values[:2] = args.path_mountain_range
            if args.path_mountain_strength is not None: values[2] = args.path_mountain_strength
            a,b,c,d = values
            result = assign(result, 'm_Colors', '_PaintedPathMountainRange', f'{{r: {a}, g: {b}, b: {c}, a: {d}}}')
    if args.far_path_air is not None and path.parent.name == 'Materials':
        result = assign(result, 'm_Floats', '_PaintedFarPathAir', str(args.far_path_air))
    if args.stochastic_fade is not None and path.parent.name == 'FoliageMaterials':
        result = assign(result, 'm_Floats', '_PaintedStochasticFade', str(args.stochastic_fade))
    if args.canopy is not None and path.parent.name == 'FoliageMaterials':
        result = assign(result, 'm_Floats', '_PaintedCanopy', str(args.canopy))
        result = assign(result, 'm_Colors', '_PaintedCanopyTones', '{r: 0.018, g: 0.035, b: 0.075, a: 0.12}')
        result = assign(result, 'm_Colors', '_CIFoliagePainterly', '{r: 10, g: 80, b: 1.5, a: 0.3}' if args.canopy else '{r: 30, g: 180, b: 1.5, a: 0.3}')
    if args.foliage_mip_bias is not None and path.parent.name == 'FoliageMaterials':
        result = assign(result, 'm_Colors', '_CIFoliagePainterly', f'{{r: 10, g: 80, b: {args.foliage_mip_bias}, a: 0.3}}')
    if args.canopy_quiet is not None and path.parent.name == 'FoliageMaterials' and '88d03a6dd752d70478be7cfc1e371a96' in original:
        result = assign(result, 'm_Floats', '_PaintedCanopyQuiet', str(args.canopy_quiet))
        result = assign(result, 'm_Colors', '_PaintedCanopyQuietRange', '{r: 2, g: 14, b: 0, a: 0}')
        result = assign(result, 'm_Colors', '_PaintedCanopyQuietSurface', '{r: 0.8, g: 0.18, b: 0.18, a: 0.65}')
    if args.canopy_quiet_surface is not None and path.parent.name == 'FoliageMaterials' and '88d03a6dd752d70478be7cfc1e371a96' in original:
        colour, normal, centre, light = args.canopy_quiet_surface
        result = assign(result, 'm_Colors', '_PaintedCanopyQuietSurface', f'{{r: {colour}, g: {normal}, b: {centre}, a: {light}}}')
    changes.append((path, result))
for path, result in changes:
    path.write_text(result, encoding='utf-8', newline='\n')
print(f'Updated {len(changes)} derived materials: valley={args.valley}, strength={args.strength}. Unity import/capture still required.')
