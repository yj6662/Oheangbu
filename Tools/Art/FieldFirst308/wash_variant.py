# Build LookProfile308_PBZInkWash.asset from PBZInk (SPEC-FIELD-FIRST-HOUR-308 §5 D, TEST values)
# usage: python Tools/Art/FieldFirst308/wash_variant.py <PBZInk.asset> <PBZInkWash.asset>  (run in Assets/_Project/Art/World/Look308/Data; new GUID each run)
import re, uuid, sys, os
src, dst = sys.argv[1], sys.argv[2]
t = open(src, encoding='utf-8').read()
PAPER = (0.97, 0.95, 0.90); AIRP = (0.66, 0.63, 0.57)
def mix(m, target, w):
    r, g, b = (float(m.group(i)) for i in (2, 3, 4))
    r, g, b = (round(v + (tv - v) * w, 3) for v, tv in zip((r, g, b), target))
    return f"{m.group(1)}{{r: {r}, g: {g}, b: {b}, a: 1}}"
col = r"(\s+%s: )\{r: ([\d.]+), g: ([\d.]+), b: ([\d.]+), a: 1\}"
t = re.sub(col % "AmbientTint", lambda m: mix(m, PAPER, .6), t)
t = re.sub(col % "FogTint", lambda m: mix(m, PAPER, .6), t)
t = re.sub(col % "Air", lambda m: mix(m, AIRP, .5), t)
def num(name, f):
    global t
    t = re.sub(r"(\n      %s: )(-?[\d.]+)" % name, lambda m: m.group(1) + str(round(f(float(m.group(2))), 3)), t)
num("Exposure", lambda v: max(v, -0.55) if v < -0.5 else v)   # realms only (6-space indent); Post.Exposure untouched
num("Saturation", lambda v: min(0.8, max(0.7, v * 1.5)))
num("SkySaturation", lambda v: 0.5)
num("PaperWash", lambda v: 0.4)
num("HighlightPaper", lambda v: 0.8)
t = t.replace("m_Name: LookProfile308_PBZInk", "m_Name: LookProfile308_PBZInkWash")
open(dst, 'w', encoding='utf-8', newline='\n').write(t)
meta = open(src + '.meta', encoding='utf-8').read()
meta = re.sub(r"guid: [0-9a-f]{32}", "guid: " + uuid.uuid4().hex, meta)
open(dst + '.meta', 'w', encoding='utf-8', newline='\n').write(meta)
print("ok")
