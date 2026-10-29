#!/usr/bin/env python3
"""초성 ㄱ(목) 24자 concepts (2026-10-30, the user: "이제 'ㄱ' 술식부터 전체 진행하자" -> 초성 ㄱ 24자, 90점 · 최대 6회차, 틀 하나마다 영상).
Codex built-in image generation (gpt-6-astra) on the lab plate (caster + bear 5 m ahead) + the wood KCISA atlas + the finished 구 ward (family look).
Concept TEST (D308-28).
  python Tools/Art/SpellVFX308/restart/concept_g308.py <key> [<key> ...]          # prompts only
  python Tools/Art/SpellVFX308/restart/concept_g308.py <key> ... --run             # codex exec per key (parallel)
Keys = SPELLS below (가_A, 가_B, ... ). Out: Art/SpellVFX308/Restart/g308/concepts/<key>.png"""
import re, subprocess, sys, time
from pathlib import Path

REPO = Path(__file__).resolve().parents[4]
OUT = REPO / 'Art/SpellVFX308/Restart/g308/concepts'
GEN = Path.home() / '.codex' / 'generated_images'
PLATE = REPO / 'Art/SpellVFX308/Restart/shots/가_plate/f_00000.jpg'
PAT = REPO / 'Art/SpellVFX308/Restart/u_H/h_patterns_wood.png'
WARD = REPO / 'Art/SpellVFX308/Restart/shots/구_H/f_00081.jpg'

HEAD = """You are being called non-interactively to produce ONE concept image file. Do exactly this and nothing else.
1. Use ONLY your built-in image generation tool to create ONE image (landscape 16:9). If you do not have it in this session, print the single line IMAGE_TOOL_UNAVAILABLE and stop. Do NOT draw it with code. Do not download anything. Do not read, change, move or delete any other file.
2. Picture 1 is a frame of our game: Korean ink-wash look, third-person camera behind the caster (a robed Taoist holding a big brush), red-brown ground, cream paper sky, and a bear (the enemy) standing about 5 m ahead. Keep that camera, that caster, that bear and that ground exactly. Picture 2 is a sheet of traditional Korean WOOD patterns (KCISA public-domain motifs: flower vines, lotus, plum branches) as white line drawings. Picture 3 is another spell of the same game (a translucent green hexagon ward with those patterns) - the same family of look: our spells are ink + translucent jade glass + fine cream engraved KCISA line patterns, glowing brightly at the moments of impact.
3. THE SPELL (element WOOD, colour jade green on black ink): """
LOOK = {
    'A': """
THE LOOK - "glass vine": the vine is made of translucent jade-green glass with fine cream-white engraved edges, one KCISA vine pattern faintly etched along it, leaves as thin glass blades. Bright pale-green glow only where it strikes. Black ink splashes at the impact. No realistic plants, no particles cloud, no text, no UI.""",
    'B': """
THE LOOK - "ink brush vine": the vine is one fast, sharp black sumi-ink brush stroke (dry-brush edges, flying ink droplets) that curls into tendrils; ONE accent colour only: jade-green light at the leaf tips and at the impact (a sharp burst like sparks of light). Monochrome ink + one accent, very fast, blade-sharp shapes. No realistic plants, no text, no UI.""",
    'C': """
THE LOOK - "ink core, glass leaves": a black ink vine core (brush texture) carrying translucent jade-glass leaves, each leaf with a cream KCISA line pattern engraved in it; the vine's seams glow pale green at the moment it bites. Black ink splatter at the impact. No realistic plants, no text, no UI.""",
}
SPELLS = {
    # ㅏ 경로 (path): from the brush tip to the aimed target along a narrow path
    '가': "\"가\" = one vine shot along a narrow path from the caster's brush tip straight to the bear; it wraps the bear once and binds it (slow). Show the instant of the bind: the vine line from the brush to the bear still visible, coiling tightly round the bear's chest, a single stem standing at the hit point, an ink splash. Nothing is left on the ground.",
    '각': "\"각\" (the strongest form) = the vine that reached the bear GROWS INTO A CAGE of branches round it, rising upward, a finished cage of curved branches; it will stay there as wood material. Show the cage fully grown round the bear, branches rising above it, one stem line back toward the caster.",
    '간': "\"간\" = the vine hits the bear and then SPREADS to two more enemies nearby (3 m) - thinner vines jumping sideways from the bear to two other bears, each bound; short flat vine marks on the ground at each spot. Show the spread: one main line to the first bear, two side jumps.",
    '감': "\"감\" = a vine SEED is planted on the bear: a small glowing jade seed lodged in the bear's chest with a thin vertical column of light/ink and a dot mark; it waits (no binding yet). Show it as a quiet, waiting mark: thin line from the brush to the bear already fading, the seed glowing, a faint vertical pillar.",
    '갓': "\"갓\" = the vine becomes a CUTTING line that passes along the path to the bear and through it: a sharp straight crack-line carved down into the ground along the path, the bear's armour/guard peeled away (shards of dark plates flying), vines binding at the peeled spot. Show the cut: a blade-straight crack line on the ground from caster to bear.",
    '강': "\"강\" = the vine goes OUT to the bear and RETURNS: a closed looping curve - an outgoing vine arc on one side and a returning arc on the other, forming a closed loop between the caster and the bear; small enemy shots near the loop knocked down. Show the loop at the moment the vine turns back at the bear.",
    # ㅓ 부착 (attachment): on the caster's own body / brush - nothing is shot; the bear only stands there
    '거': "\"거\" = a PARRY: the vine wraps once round the caster's own body and brush arm in a flash (0.5 s) - a tight spiral of ink vine with glass leaves round the torso and the brush, a sharp jade light along it, as if catching a blow. Nothing flies to the bear.",
    '걱': "\"걱\" (the strongest form) = SELF-HEALING: the vine coils gently round the caster's body from the ground up, glass leaves unfolding along it, small jade motes and leaf-light rising from the caster - calm, restoring. Nothing flies to the bear.",
    '건': "\"건\" = the vine CLINGS to the caster's body (for 6 s) and is ready to spread: a vine winding round the brush arm and shoulders with a few short tendrils reaching outward toward nearby enemies like feelers. Nothing flies to the bear.",
    '감2': "",
    '검': "\"검\" = a vine SEED is planted on the caster's OWN chest: a glowing jade seed at the chest with a small ink knot and two glass leaves, a thin vertical column of jade light rising from it - it waits to burst when the caster is hit. Nothing flies to the bear.",
    '것': "\"것\" = the vine becomes a BLADE on the caster's brush: an ink vine wound tightly along the whole brush shaft, its edge sharpened into a long jade cutting edge (like a sword edge of light) with glass leaves at the guard. Nothing flies to the bear.",
    '겅': "\"겅\" = the vine ORBITS the caster: a closed ring of ink vine with glass leaves circling round the caster at waist height (about 1.2 m radius), motion streaks showing it spinning, a faint jade arc on the ground under it. Nothing flies to the bear.",
    # ㅗ 면 (fan): the vine covers the area in FRONT of the caster (a wide wedge, about 6 m deep, 90 degrees)
    '고': "\"고\" = vines BURST across the whole fan-shaped area in front of the caster at once: many ink vines racing outward along the ground in a wide wedge, curling up and binding everything inside (the bear is caught). Show the wedge of vines spread on the ground with tendrils rising where they bind.",
    '곡': "\"곡\" (the strongest form) = the vines left in the front area GROW INTO A SMALL FOREST: thickets of vine stems and glass leaves rising from the ground over the wedge in front, the bear caught among them; the wedge is clearly wider than a normal cast.",
    '곤': "\"곤\" = the front-area vines SPREAD from the wedge's edge to nearby enemies and patches: the wedge of ground vines with thinner runners reaching out from its rim to two more bears at the sides, short vine marks on the ground where it spread.",
    '곰': "\"곰\" = vine SEEDS are planted across the front area: a wedge of small glowing jade seeds dotted on the ground in front, each with a tiny thin column of light - quiet, waiting for an enemy to step in. No vines yet.",
    '곳': "\"곳\" = a vine CUTS across the front area: straight crack lines carved into the ground fanning out in the wedge in front, sharp jade edges, the bear's armour plates flying off where a crack passes under it.",
    '공': "\"공\" = the front-area vines go OUT and COME BACK: a sweeping fan of vines flung out across the wedge and curling back toward the caster like a wave returning, motion streaks showing the out-and-back sweep.",
}
SPELLS.pop('감2')
TAIL = """
4. Save the image unchanged (PNG) as: %s
5. Print the saved file path and stop."""
PNG_RE = re.compile(r'generated_images[\\/]+([0-9a-f-]+)[\\/]+([^\\/\'"\s]+?\.png)', re.I)


def recover(key):
    dst = OUT / f'{key}.png'
    if dst.exists() and dst.stat().st_size > 10000:
        return True
    log = OUT / f'{key}.log.txt'
    cand = [GEN / m.group(1) / m.group(2) for m in PNG_RE.finditer(log.read_text(encoding='utf-8', errors='replace'))] if log.exists() else []
    cand = [p for p in cand if p.exists()]
    if cand:
        dst.write_bytes(cand[-1].read_bytes()); return True
    return False


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    keys = [a for a in sys.argv[1:] if not a.startswith('--')]
    for k in keys:
        letter, look = k.split('_')
        (OUT / f'{k}.prompt.txt').write_text(HEAD + SPELLS[letter] + LOOK[look] + TAIL % (OUT / f'{k}.png'), encoding='utf-8')
    if '--run' not in sys.argv:
        print('prompts written', keys); return
    procs = []
    for k in keys:
        if recover(k):
            continue
        cmd = ['codex', 'exec', '-m', 'gpt-6-astra', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(OUT), '-i', str(PLATE), '-i', str(PAT), '-i', str(WARD)]
        log = open(OUT / f'{k}.log.txt', 'w', encoding='utf-8')
        procs.append((k, subprocess.Popen(cmd, stdin=open(OUT / f'{k}.prompt.txt', 'rb'), stdout=log, stderr=subprocess.STDOUT, shell=True)))
        time.sleep(2)
    for k, p in procs:
        try: p.wait(timeout=1500)
        except subprocess.TimeoutExpired: p.kill()
        print('DONE', k, p.returncode, recover(k), flush=True)


if __name__ == '__main__':
    main()
