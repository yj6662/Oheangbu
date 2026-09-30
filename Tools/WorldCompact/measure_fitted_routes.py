"""Compare retained route identities/physical widths and fitted centerline lengths."""
from pathlib import Path
import json
import math

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/WorldMacro/Compact'
old=json.loads((OUT/'route_comparison.json').read_text(encoding='utf-8'))
fit=json.loads((OUT/'route_grade_fit.json').read_text(encoding='utf-8'))
assert fit['readyForSculpt']
before={r['id']:r for r in old['routes']}
assert set(before)=={r['id'] for r in fit['routes']}
rows=[]
for route in fit['routes']:
    source=before[route['id']]
    assert route['width']==source['width'] and route['carriage']==source['carriage']
    points=route['points']
    length=sum(math.dist(tuple(a[k] for k in ('x','y','z')),tuple(b[k] for k in ('x','y','z'))) for a,b in zip(points,points[1:]))
    rows.append(dict(id=route['id'],carriage=route['carriage'],width=route['width'],sourceMetres=source['before']['metres'],compactMetres=round(length,2),retainedRatio=round(length/source['before']['metres'],4)))
result=dict(scope='Authored route centerlines, not timed traversal. Summed routes can overlap and are not total unique road length.',sourceBoundsMetres=[8000,12000],compactBoundsMetres=[4000,6000],horizontalRatio=.5,rectangularAreaRatio=.25,all36IdsAndWidthsPreserved=True,routes=rows)
(OUT/'fitted_route_measurements.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
lines=['# 축소본 경로 측정','', '실제 보행 시간이 아닌 확정 중심선 길이 비교다. 건물·전투 공간은 유지하므로 각 길이 정확히 절반으로 줄어들지는 않는다.','', '| 경로 ID | 원본 m | 축소 m | 남은 길이 | 폭 m |','|---|---:|---:|---:|---:|']
for r in rows:lines.append(f"| {r['id']} | {r['sourceMetres']:.1f} | {r['compactMetres']:.1f} | {r['retainedRatio']:.1%} | {r['width']:g} |")
(OUT/'ROUTE_MEASUREMENTS.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print(json.dumps({'routeIds':len(rows),'widthsPreserved':True,'sourceRouteSumKm':round(sum(r['sourceMetres'] for r in rows)/1000,2),'compactRouteSumKm':round(sum(r['compactMetres'] for r in rows)/1000,2)}))
