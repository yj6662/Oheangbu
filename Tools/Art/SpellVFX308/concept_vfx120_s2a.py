#!/usr/bin/env python3
"""vfx120 stage 2 (concept sheets), sheet group A: 1a 1b 2a 2b 3 4 5 8 9  (Docs/Research/vfx120/plan_v10_final.md J-1,
stage2_sheet_list.md).  Concepts only (D308-28).  Codex's native landscape image is 1672x941, so the plan's '4K / 960 px per
panel' is not reachable: every sheet is split up front into images of at most 4 panels (2x2 = about 836x470 per panel; the
3-panel / 6-panel ones are 3x1 / 3x2) and the split is recorded in Docs/Research/vfx120/stage2_A_ledger.md.

  python Tools/Art/SpellVFX308/concept_vfx120_s2a.py                       # write prompts only
  python Tools/Art/SpellVFX308/concept_vfx120_s2a.py --run [ids]           # generate round 1 (parallel, 3 tries each)
  python Tools/Art/SpellVFX308/concept_vfx120_s2a.py --run --round 2 [ids] # redraw: previous round's PNG is attached + <id>.fix2.txt
  python Tools/Art/SpellVFX308/concept_vfx120_s2a.py --judge --round 1 [ids]   # Codex judge (read-only) -> <id>_r1.judge.md

Outputs: Art/SpellVFX308/Concepts/vfx120_s2/<id>_r<N>.png (+ .prompt.txt / .log.txt / .judge.md)."""
import re, subprocess, sys, time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import concept_reactions308 as base

REPO = base.REPO
OUT = REPO / 'Art/SpellVFX308/Concepts/vfx120_s2'
MARKS = REPO / 'Art/SpellVFX308/Concepts/tier1/t1_v2_verb_marks3.png'
V2 = REPO / 'Art/SpellVFX308/Concepts/vol2'
FRAMES2, BODIES2, GEOK2 = V2 / 'v2_frames2.png', V2 / 'v2_bodies2.png', V2 / 'v2_geok2.png'
RIPOSTE = REPO / 'Art/SpellVFX308/Concepts/riposte/rp_stroke5_board.png'
GEN = Path.home() / '.codex' / 'generated_images'

# ------------------------------------------------------------------ shared text
REF_TXT = {
    'shoulder': "a game screenshot: use it ONLY for the camera (third person, behind and slightly above the caster's right shoulder, looking down about 15 degrees) and the ink-wash tone. Do not copy the user interface.",
    'brush': "the player character: a scholar-mage in a worn grey-brown hanbok-like coat, messy dark curly hair, holding a great calligraphy brush (black lacquered staff about 1.25 m, pale bone ferrule, black bristle head, small red tassel). Keep that character and that brush.",
    'jeokro': "the region 적로: a dry red-earth valley of cracked ochre-red ground - use it for the ground and sky.",
    'marks': "the game's accepted board of spell MARKS (what a spell leaves behind): rising branches, flat spreading strokes, a single vertical post, a sunken crack, a closed curve. Keep that mark language.",
    'frames2': "the accepted FRAME concept (narrow path lane, brush-and-sleeve binding, fan, hollow ring band): keep its dry, hollow, thin-stroke drawing of the caster's magic and its camera. Its layout (tall strips) is NOT to be copied.",
    'bodies2': "the accepted BODY concept: five bodies readable by how the stroke ENDS move. Keep that logic.",
    'geok2': "the accepted 걱 concept (a few thin dry strokes winding around the brush shaft and right forearm, re-drawn instead of growing): keep its thin, sparse, close-to-the-body drawing. Its layout is NOT to be copied.",
    'riposte': "the accepted riposte brush-stroke (앞잡) concept: a huge dry-brush black ink ribbon swept from the great brush through an armoured soldier, then drying out in grey and cracking into falling fragments. Keep that stroke language (dry-brush gaps, torn edges); the setting here is the red-earth valley instead of the plaza.",
}
REF_FILES = {'shoulder': base.SHOULDER, 'brush': base.BRUSH, 'jeokro': base.JEOKRO, 'marks': MARKS,
             'frames2': FRAMES2, 'bodies2': BODIES2, 'geok2': GEOK2, 'riposte': RIPOSTE}

GRAMMAR = """--- THE SPELL GRAMMAR (the most important rules; a failure of any of them fails the picture) ---
Every spell = BODY (one of five elements = the material of the ink) + FRAME (where it acts) + VERB (what it leaves / does). All magic is matte INK and soaked-in mineral pigment, never light.
OWNERSHIP: everything the CASTER makes is drawn as DRY, HOLLOW bundles of thin separate brush strokes: each stroke has a dark near-black ink reading edge and a drier lighter mineral-pigment layer in the body colour, a broken dry rim, and ground-coloured gaps (clearly visible, at least a stroke-width) between the strokes; the INSIDE of a shape is EMPTY ground. NEVER a solid filled shape, a disc, a wall, a thick vine armour, a glowing line. Enemies (or world pollution) are drawn the OPPOSITE way: rimless, wet, solid, filled patches with no separate dry rim.
No effect covers an enemy: every enemy stays at least 70 percent visible. Nothing glows, no light, no bloom, no sparks, no flames."""

BODY = {
    'wood': "WOOD (목): body colour teal-green #368B75 with dry pigment layer #4B9F88 over a dark ink edge. First-glance cue = LONG TORN FIBRE ENDS. When a wood stroke ends, fine rootlets unravel FORWARD (the way it was drawn).",
    'fire': "FIRE (화): body colour brick-red #BE543B with a dry pigment layer #DF7155 only along the stroke edges, over a dark charred ink edge. First-glance cue = BURNT-THROUGH HOLES and SAW-TOOTH ends (charred black, only a small part is red). When it ends, grey ash rises UPWARD. No flame, no glow, no bright red fill.",
    'earth': "EARTH (토): body colour ochre #B68A3D (dry pigment #B5893C). First-glance cue = LOW PLATES OVERLAPPING LIKE STEPS (stair-stepped slabs). When it ends it sinks while the ground plates around it lift.",
    'metal': "METAL (금): body colour pale grey #A6B0AD (dry pigment #899390), matte and never brighter than the paper, no sparkle. First-glance cue = STRAIGHT CUT ENDS and short transverse breaks. When it ends it snaps short and parts SIDEWAYS.",
    'water': "WATER (수): body colour blue-grey, edge #4F6A84 with core #2C465A (dry pigment #7893AF). First-glance cue = OPEN HOOKED ENDS and HOLLOW PARALLEL flowing strands. It clings to the surface and runs DOWNWARD along it.",
}

FRAMEDEF = """--- THE FOUR FRAMES (exact sizes) ---
ㅏ PATH: one narrow lane 0.3 m wide and up to 12 m long from the brush tip to the first thing it touches.
ㅓ ATTACH: the ink is bound ONLY on the caster's own brush shaft and right sleeve (never a world patch, never a long reaching arc); an outside mark appears only at a real contact.
ㅗ FAN: apex at the caster, 5 m deep, far chord 4 m wide (half-angle about 23.6 degrees): drawn as two thin radial lines, a far arc and bundles of strokes in front - never a filled disc or fan plate.
ㅜ RING: a HOLLOW band around the caster's own position, centre radius 2 m, only 0.6 m wide, centre completely empty, nothing outside it; front arc visible, side and back arcs partly out of view; two thin standing strokes about 1.0-1.2 m high rise from the front-left and front-right of the band (20 degrees off the forward direction)."""

LAYOUT_TXT = {
    '2x2': "LAYOUT: ONE landscape image split into a 2 x 2 grid of FOUR EQUAL landscape panels (each about 836 x 470 px), thin paper-coloured gutters. Reading order: top-left = panel 1, top-right = panel 2, bottom-left = panel 3, bottom-right = panel 4. Identical camera and ground in all panels unless a panel says otherwise.",
    '3x2': "LAYOUT: ONE landscape image split into a 3 x 2 grid of SIX EQUAL panels (3 columns, 2 rows, each about 557 x 470 px), thin paper-coloured gutters. Reading order: top row left to right = panels 1, 2, 3; bottom row left to right = panels 4, 5, 6.",
    '3x1': "LAYOUT: ONE landscape image split into THREE EQUAL tall panels side by side (each about 557 x 941 px), thin paper-coloured gutters. Reading order left to right = panels 1, 2, 3.",
}

TWO_THIRD_ENEMY = "Where enemies appear they are two gaunt matte soldiers about 1.8 m tall, 6-8 m ahead, drawn plainly with no effect over their head or torso."


def look(bodies):
    return "--- THE LOOK ---\nA game concept paint-over in the style of picture 1: warm grey-beige, slightly desaturated, ink wash over a 3D scene, matte, dry-brush. BODY CODES used in this picture:\n" + \
        '\n'.join(BODY[b] for b in bodies) + "\nTwo lightness layers on every caster stroke: a dark ink reading edge (near-black grey) and a drier lighter pigment layer in the body colour, both at least 3 px wide at the picture's native size. Colour supports the form, it never replaces it: every body must stay readable from its stroke SHAPE alone."


# ------------------------------------------------------------------ the 15 pictures
S = {}


def add(pid, sheet, layout, refs, bodies, panels, judge_proof, plan_rows):
    S[pid] = dict(sheet=sheet, layout=layout, refs=refs, bodies=bodies, panels=panels, proof=judge_proof, plan=plan_rows)


# ---- 1a : four frames x leave-behind (남긴다) / spread (번진다), wood, 2 images
add('s2a_1a_1', '1a', '2x2', ['shoulder', 'brush', 'jeokro', 'marks', 'frames2'], ['wood'], [
    ("ㅏ + 남긴다 (P1)", "Shoulder camera. The caster's narrow lane (0.3 m, a few thin parallel dry strokes) runs from the brush tip to the nearer enemy 6 m ahead and stops there. At the landing point at the enemy's feet a small cluster of LOW UPWARD-BRANCHING branches STANDS UP (about 0.5 m high, each branch forking into two or three tips at its TOP end; dry hollow strokes). Nothing lies flat or spreads sideways."),
    ("ㅏ + 번진다 (P2)", "Same shoulder camera, same lane, same enemy. At the landing point a FLAT BUNDLE of five or six short strokes lies ALONG THE GROUND, about 0.6 m long and 0.6 m wide, with OPEN frayed ends that point outward; nothing stands above ankle height. The picture must show at a glance: flat spreading bundle, not branches."),
    ("ㅓ + 남긴다 (A1)", "Closer shoulder camera on the caster's right arm, brush held up. From the brush shaft and the right sleeve cuff a few thin dry branches stand OUTWARD, away from the shaft, each forking at its tip; tied at the root. Nothing reaches the ground and nothing reaches the enemy (4 m away, untouched)."),
    ("ㅓ + 번진다 (A2)", "Same closer camera. Thin dry strokes lie BUNDLED ALONG the brush shaft and sleeve (parallel, hugging the wood, not standing outward). The brush tip touches the enemy's arm at the right edge of the panel, and only there a small FLAT bundle of strokes (about 0.3 m) lies on the ground at the contact point as an outside mark."),
], "가지(위로 갈라지는 윗끝, 서 있음) / 평면 다발(바닥에 눕고 열린 끝)이 같은 몸·같은 틀에서 한눈에 갈린다. 틀 둘(ㅏ·ㅓ)×동사 둘.",
    "P1 남긴다=착점에서 위로 갈라지는 가지(<=3 m에서 최고 .4 m), 10 s. P2 번진다=평평한 획 다발·열린 끝, 외부 자국 3 s. A1 남긴다=밖으로 선 부착 가지(붓대·소매에만). A2 번진다=부착 다발+실제 접촉 때만 외부 접점 획. 몸=목(#368B75, 안료층 #4B9F88).")
add('s2a_1a_2', '1a', '2x2', ['shoulder', 'brush', 'jeokro', 'marks', 'frames2'], ['wood'], [
    ("ㅗ + 남긴다 (F1)", "Shoulder camera. A fan in front of the caster: two thin radial dry lines from the brush and a far arc 4 m wide, 5 m deep. Inside the fan six or seven small UPWARD-FORKING branch clusters STAND on the ground (low near the caster, about 0.9 m high at the far arc). No flat sheet, no filled fan; ground and both enemies' legs clearly visible between the clusters."),
    ("ㅗ + 번진다 (F2)", "Same camera and same fan lines. Inside the fan the strokes lie FLAT as bundles on the ground and connect across the fan boundary to neighbouring bundles with OPEN ends; nothing stands up; no filled disc or fan plate."),
    ("ㅜ + 남긴다 (R1)", "Camera behind the caster's right shoulder (caster in the lower left third). A HOLLOW ring band (radius 2 m, 0.6 m wide, thin dry strokes) around the caster; small UPWARD-FORKING branches stand on the band (about 0.4 m high); the two thin standing strokes ahead (1.0-1.2 m) FORK at their upper ends like real branches and connect down to the band's branches. The centre of the ring and the ground outside are empty."),
    ("ㅜ + 번진다 (R2)", "Same camera and band. FLAT open bundles spread OUTWARD from the band across the ground; the two thin standing strokes ahead OPEN OUTWARD at their upper ends (a pair opening like a bracket) and connect down into those flat bundles; no branches standing on the band. The centre of the ring is empty."),
], "가지 / 평면 다발이 ㅗ·ㅜ에서도 같은 구별을 유지한다. 부채·띠는 원판이 아니고, ㅜ 중앙은 비어 있고 세운 획 쌍이 동사 단서를 준다.",
    "F1 남긴다=부채 안 가지 군집(거리별 높이 제한 .4/.65/.9 m). F2 번진다=부채 경계에서 다발 연결·원판 추가 금지. R1 남긴다=띠 위 가지·뒤 호, 세운 획 윗끝이 실제 가지로 분기. R2 번진다=띠에서 바깥으로 열린 다발, 세운 획 윗끝이 바깥으로 벌어짐. 중앙 비움. 몸=목.")

# ---- 1b : wood peaks (남긴다 family) and fire peaks (번진다 family)
add('s2a_1b_1', '1b', '2x2', ['shoulder', 'brush', 'jeokro', 'marks', 'frames2'], ['wood'], [
    ("각 (ㅏ+남긴다 극치)", "Shoulder camera. The lane ends at the nearer enemy; around the enemy's LOWER LEGS ONLY (knee height) a LOW CAGE of crossing dry branches stands, a cage made of thin separate branch strokes. The enemy's head, torso and arms are completely free; nothing above the waist."),
    ("걱 (ㅓ+남긴다 극치: 자기 회복)", "Closer camera on the caster's right forearm. A few thin dry strokes are FOLDED BACK INTO THE SLEEVE: they lie bent along the forearm with their tips tucked INSIDE the cuff, none stands outward, none touches the ground or an enemy, nothing is aimed anywhere. Quiet, close to the body."),
    ("곡 (ㅗ+남긴다 극치)", "Shoulder camera. A fan like an ordinary branch fan but its connected branch clusters are about 1.2 times WIDER and run into each other (one continuous colony), and at one enemy's ankle one branch tip curls around the leg as a single hold point. No cage, no flat sheet."),
    ("국 (ㅜ+남긴다 극치)", "Camera behind the caster. The hollow ring band (radius 2 m, 0.6 m wide, centre empty) with small branches on it plus three thicker dry bundled branch COLUMNS about 0.9 m high standing on the band like the legs of a platform. One layer only, no stacking, no wall; the centre empty."),
], "기반 동사(남긴다=서 있는 가지)를 유지한 채 각=낮은 새장(무릎까지), 걱=안으로 접힘, 곡=이어지는 군집, 국=띠 위 지지 기둥의 네 구조가 서로 구별된다.",
    "각=낮은 가지 새장(머리·상체 봉쇄 금지). 걱=소매 안으로 접힌 회복 획(공격·적 부착·재료 생성 0). 곡=이어지는 가지 군집(면적 x1.5 = 선형 1.225배, 적당 붙듦 1회). 국=띠 가지의 지지 기둥(반복 중첩 금지). 몸=목.")
add('s2a_1b_2', '1b', '2x2', ['shoulder', 'brush', 'jeokro', 'marks', 'frames2'], ['fire', 'wood'], [
    ("난 (ㅏ+번진다 극치)", "Shoulder camera. The lane ends at the nearer enemy; from the single landing point THREE flat strands of EXACTLY THE SAME thickness and darkness fan out along the ground (left, forward, right), open ends, charred saw-tooth tips. No branches stand up."),
    ("넌 (ㅓ+번진다 극치)", "Closer camera on the right arm. A charred bundle of strokes is bound along the brush shaft and sleeve and, at the point where the brush touches the enemy, the bundle LEAVES the shaft as a clear exit stroke onto the contact point (the carried bundle). Burnt holes and saw-tooth ends."),
    ("논 (ㅗ+번진다 극치)", "Shoulder camera. A fan; flat bundles spread forward across the ground, and from the far side ONE strand comes BACK and reconnects to the earlier bundle (a re-spread back toward where it came from). No small closed loop, no second generation of strands, no filled fan."),
    ("눈 (ㅜ+번진다 극치)", "Camera behind the caster. The hollow ring band is made of WOOD material (teal-green dry vine strokes with torn fibre ends) and BLACK CHARRED fire strokes with burnt holes run ALONG those vine strokes, connecting them outward. No fire rim, no red ring, no flame; red only as a small dull accent on the charred edges."),
], "기반 동사(번진다=평평한 열린 다발)를 유지한 채 난=같은 농도 세 갈래, 넌=부착 다발+접촉 출구, 논=되번짐 연결, 눈=목 재료를 따라가는 탄 획의 네 구조가 구별된다. 불테 없음.",
    "난=착점의 같은 농도 세 갈래(대상당 .5, 공유 합 1.5). 넌=탄 획의 부착 다발(두 휘두르기가 번짐 1.5 공유). 논=평면 다발의 되번짐(작은 폐곡선·손자 없음). 눈=목 재료를 따라 이어지는 탄 획(불테 없음). 몸=화(#BE543B, 안료층 #DF7155) 단 눈은 목 재료 위.")

# ---- 2a : seeds (심는다) waiting / acting, fire
add('s2a_2a_1', '2a', '2x2', ['shoulder', 'brush', 'jeokro', 'marks', 'frames2'], ['fire'], [
    ("ㅏ 씨앗 - 대기 (남)", "Shoulder camera. ONE isolated vertical dry post (about 0.45 m high, hollow thin strokes, no branches, no fork, no spread) stands planted at the nearer enemy's feet. No lane is left, no other mark anywhere."),
    ("ㅏ 씨앗 - 작동 (소)", "Same camera and enemy. A second narrow lane stroke has just arrived at the same enemy, and the post is FOLDING DOWN into its own contact point: shortened to about 0.15 m, its strokes collapsing toward the base. No explosion, no new mark, no ring."),
    ("ㅓ 씨앗 - 대기 (넘)", "Closer camera on the caster's right arm. ONE tiny dry dot (about 10 cm) sits at the sleeve cuff, nothing else on the brush or sleeve; an enemy stands 4 m away at rest."),
    ("ㅓ 씨앗 - 작동", "Same closer camera. The enemy's weapon has just struck the caster's shoulder; the sleeve dot has discharged: it has collapsed into the cuff and a FEW SHORT sparse dry strokes lie around the caster within about 2 m on the ground (no ring, no wall, thin and brief)."),
], "대기=분기 없는 고립된 세로점 / 작동=소비(접점으로 접힘)가 같은 틀에서 갈린다. 점은 가지·다발과 구별된다.",
    "심는다=고립된 세로점·짧은 기둥(분기 없는 점), 대기 8 s, 후속 직접 명중/실제 HP 피해로 작동. 소=소비 부분 무효화, <=0.15 s 접점으로 접힘. ㅓ는 소매점. 몸=화.")
add('s2a_2a_2', '2a', '2x2', ['shoulder', 'brush', 'jeokro', 'marks', 'frames2'], ['fire'], [
    ("ㅗ 씨앗 - 대기 (놈)", "Shoulder camera. Only the fan's two hair-thin radial dry lines and its far arc are drawn (no fill), and ONE isolated vertical dry post (about 0.45 m, hollow thin strokes, no branches) stands inside it at mid depth. Two enemies far at the edge, untouched."),
    ("ㅗ 씨앗 - 작동", "Same camera. One enemy has stepped onto the post's place; the post has FOLDED DOWN into its base (about 0.15 m left, strokes collapsing toward the ground contact), only a small dry contact mark remains. The fan lines are still faintly there."),
    ("ㅜ 씨앗 - 대기 (눔)", "Camera behind the caster. The hollow ring band (radius 2 m, 0.6 m wide, centre empty) with ONE isolated unbranched vertical dry post with a small round dot head standing on the band ahead of the caster (about 0.45 m high). No other strokes."),
    ("ㅜ 씨앗 - 작동", "Same camera and band. An enemy has just crossed the band at the post's place; the post has folded into the band (low, collapsed toward the band, about 0.15 m) leaving a small hollow fold mark on the band. Centre still empty."),
], "ㅗ·ㅜ에서도 대기점(고립 세로점)과 작동(접힘)이 갈린다. 부채·띠는 윤곽선만이고 원판이 아니다.",
    "F3 부채의 세로점(적 진입 때 작동). R3 띠 위 세로점(일반형은 도약 통과도 작동). 대기 8 s. 소=<=0.15 s 접점으로 접힘. 원판 추가 금지. 몸=화.")

# ---- 2b : earth peaks 맘 멈 몸 뭄
add('s2a_2b', '2b', '2x2', ['shoulder', 'brush', 'jeokro', 'marks', 'bodies2'], ['earth'], [
    ("맘 (ㅏ+심는다 극치)", "Shoulder camera. At the nearer enemy's feet a vertical dry point is being PRESSED DOWN by a flat dry-edged PRESSING PLATE made of thin overlapping stair-stepped slabs (plate about 0.3 m thick, lying low over the enemy's ankles, not covering the legs or body). The post is crushed under the plate."),
    ("멈 (ㅓ+심는다 극치)", "Closer camera on the caster's right arm. Only a small sleeve dot and a LOW ground-contact stroke of thin stepped earth plates (under 0.2 m high) around the caster's feet. 6 m behind, small and far, a hollow earth figure about 1.3 m tall and, under the caster's feet, a low hardened platform exist already; the 멈 itself adds no tall plate."),
    ("몸 (ㅗ+심는다 극치)", "Shoulder camera. An earth GIANT built from a column: stair-stepped dry plates form shoulders and arms, hollow see-through torso, no more than 1.3 m tall (shorter than the 1.8 m enemy next to it); a thin dry strand connects it to the caster so it plainly belongs to the caster. It is NOT an enemy."),
    ("뭄 (ㅜ+심는다 극치)", "Camera behind the caster. The caster stands on a SUPPORT SURFACE of hardened earth: thin stair-stepped plates with visible thickness, a dry broken rim and gaps, joined up from two support points / short columns; the ground around is natural plain cracked earth; no enemy stands on it."),
], "대기·작동 조건과 토 구조가 네 글자에서 서로 다르게 읽힌다: 맘=압판, 멈=소매점+낮은 접지, 몸=기둥이 어깨·팔, 뭄=점·기둥이 잇는 지지면.",
    "맘=세로점이 눌리는 압판(능동 격발, 압축 표현 <=0.12 s). 멈=소매점에서 접지 구조(거인·뭄이 서 있으면 소매점+낮은 지면 접촉 획으로만; 높은 판 금지). 몸=기둥이 어깨·팔로 완성(높이 <=1.3 m, 자율 추적·어그로 없음). 뭄=점·기둥이 잇는 지지면(시전자 탑승 시 굳은 땅, 적 통과 기폭처럼 표시 금지). 토 낮은 판 계단. 몸=토.")

# ---- 3 : ordinary 가른다 x4 frames / metal peaks 삿 섯 솟 숫
ARMOUR = "The enemies wear dark lamellar armour (plain, matte)."
add('s2a_3_1', '3', '2x2', ['shoulder', 'brush', 'jeokro', 'marks', 'frames2'], ['wood'], [
    ("ㅏ + 가른다 (갓)", "Shoulder camera. A narrow ENGRAVED GROOVE 0.3 m wide runs along the ground from the caster to the nearer armoured enemy (recessed pale ground, two DRY LIPS of lifted earth along its two sides). Where it reaches the enemy's armour a PAIR of short parallel cut-plane marks (two short lips about 0.2 m long, a hand-width apart) is drawn on the armour plate. " + ARMOUR),
    ("ㅓ + 가른다 (것)", "Closer camera on the right arm. A razor-thin INCISED line with two dry lips runs along the brush shaft only (no outside trace on the ground); the brush has just slashed the armoured enemy and a PAIR of short parallel cut-plane marks stays on its armour plate."),
    ("ㅗ + 가른다 (곳)", "Shoulder camera. Inside the fan area five or six parallel ENGRAVED GROOVES (each with two dry lips) radiate from the caster; at the nearer armoured enemy a PAIR of short cut-plane marks is on its armour. No filled fan."),
    ("ㅜ + 가른다 (굿)", "Camera behind the caster. The hollow ring band carries one continuous ENGRAVED LINE with dry lips; in front, a PAIR of thin standing LIPS (about 1.0 m, facing each other, a hand-width apart) rise from the band with the groove running between them down into the ground; NOT a wall. Centre empty."),
], "가른다=음각과 양쪽 마른 입술 한 쌍(가지·번짐 다발과 구별), 벗김=적 갑주 접점의 짧은 절단면 한 쌍(세계 음각과 별개).",
    "P4 갓=경로 음각·절단면, 세계 흔적 <=0.3 s, 벗김 3 s. A4 것=붓대의 얇은 음각 날 획(외부 흔적 <=0.2 s). F4 곳=부채 안 음각 다발. R4 굿=띠의 음각선·절단면, 세운 입술 쌍(칼날 벽 없음). 적 신체 절단 금지. 몸=목.")
add('s2a_3_2', '3', '2x2', ['shoulder', 'brush', 'jeokro', 'marks', 'frames2'], ['metal'], [
    ("삿 (ㅏ+가른다 극치)", "Shoulder camera. An engraved groove with dry lips runs from the caster through THREE armoured enemies standing in a row about 5, 7 and 9 m ahead: the first has a clear pair of cut-plane marks on its armour, the other two only a shorter pair. Metal straight cut ends."),
    ("섯 (ㅓ+가른다 극치)", "Camera pulled back so one enemy ahead and one enemy BEHIND the caster are both visible. Two thin engraved grooves with dry lips, one forward and one backward, are drawn from the brush; each enemy has a pair of cut-plane marks on its armour; the grooves are on the ground only."),
    ("솟 (ㅗ+가른다 극치)", "Shoulder camera. A fan of engraved parallel grooves with dry lips; out of one groove rises ONE LOW matte metal BLADE (not higher than 0.6 m, straight cut ends, pale grey #A6B0AD, not brighter than the paper, no shine, no sparkle). It is the only metal object; the rest is ink."),
    ("숫 (ㅜ+가른다 극치)", "Camera behind the caster. The hollow ring band has a CRACK with a pair of cut faces across it; beyond it 3 m ahead a rock slab / iron door has a clean cut line splitting it along the same crack. No wall of blades, no blade fence."),
], "금 극치 넷이 같은 가른다 윤곽(음각·입술 쌍)을 유지하며 삿=세 대상 이어짐, 섯=앞뒤 동시, 솟=낮은 금 날(유일한 재료 예외), 숫=띠 균열 절단·바위 문으로 구별되고 칼날 벽이 없다.",
    "삿=세 대상까지 이어지는 음각(첫 대상 벰만 x1.5, 뒤는 벗김만). 섯=앞·뒤 동시 가름. 솟=음각 위 낮은 금 날(재료 생성 예외, 날 <=0.6 m, 금 4 s). 숫=띠 균열의 절단면(벗김 4.5 s, 바위·쇠문 게이트 절단, 칼날 벽 금지). 금=종이보다 밝지 않음·반짝임 없음. 몸=금.")

# ---- 4 : ordinary 돌아온다 x4 / water peaks 앙 엉 옹 웅
add('s2a_4_1', '4', '2x2', ['shoulder', 'brush', 'jeokro', 'marks', 'frames2'], ['wood'], [
    ("ㅏ + 돌아온다 (강)", "Shoulder camera. The lane reaches the nearer enemy and at the landing point a SMALL THIN CLOSED CURVE (diameter under 1 m, one hand-drawn dry stroke closed on itself, interior empty ground) lies, with a faint trailing return stroke back along the lane."),
    ("ㅓ + 돌아온다 (겅)", "Closer camera on the right arm. A small thin CLOSED loop of dry stroke circles the sleeve cuff / brush shaft (attached, about 20 cm), interior empty; nothing on the ground."),
    ("ㅗ + 돌아온다 (공)", "Shoulder camera. The fan's thin radial lines and far arc are drawn and, in the middle of the fan, a small thin CLOSED CURVE (under 1 m) is travelling outward, its tail stroke running back toward the caster. No filled fan."),
    ("ㅜ + 돌아온다 (궁)", "Camera behind the caster. The thick hollow band (0.6 m, the PLACE) and ON it one thin small closed ring (under 1 m across, very thin, interior empty = the VERB); a lighter dry ghost ring about 4 m radius around it shows the extent it travelled to. The band's centre stays empty."),
], "돌아온다=지름 1 m 이하의 가는 닫힌 폐곡선(닫힌 내부). ㅜ에서는 두꺼운 띠(자리)와 가는 고리(동사)가 갈린다. 가지·번짐 다발·세운 입술과 혼동되지 않는다.",
    "P5 강=작은 폐곡선 왕복(전체 <=1.2 s). A5 겅=소매의 작은 폐곡선(부착 순환 6 s). F5 공=부채 자체·작은 폐곡선 이동. R5 궁=굵은 띠 위 가는 고리(반경 2→4→2 m). 두꺼운 띠는 자리, 가는 고리는 동사. 몸=목.")
add('s2a_4_2', '4', '2x2', ['shoulder', 'brush', 'jeokro', 'marks', 'bodies2'], ['water', 'wood'], [
    ("앙 (ㅏ+돌아온다 극치)", "Shoulder camera. A thin closed loop of water-body strokes (open hooked ends, hollow parallel strands) carries ONE small wood-material mark (a few teal-green dry branch strokes, same shape as its original at the lane's far end) about 2 m forward; a faint ghost of the original place is behind it."),
    ("엉 (ㅓ+돌아온다 극치)", "Closer camera on the right arm. A small closed water-stroke loop circles the sleeve cuff and, where it touches the enemy's blade, ONE stroke is BENT BACK sharply at an angle (a rebound kink), pointing away from the enemy; nothing on the ground; no damage mark."),
    ("옹 (ㅗ+돌아온다 극치)", "Shoulder camera. Water-body strands (open hooked ends, hollow parallel flows) in the fan CONVERGE INWARD toward one gathering line near the caster, hooks pulling inward; the fan lines are thin, the ground and enemies visible."),
    ("웅 (ㅜ+돌아온다 극치)", "Camera behind the caster. A thin returning water-stroke ring sweeps across the band; on its far side a few small dark product marks lie on the ground, on the side it has already passed the ground is CLEAN; the thick hollow band is the place, the ring the verb."),
], "수 극치 넷이 같은 돌아온다 윤곽(닫힌 폐곡선)을 유지하며 앙=한 재료 운반, 엉=접점의 꺾임, 옹=안으로 모이는 다발, 웅=산물 문양을 닦음으로 구별된다. 수 몸=열린 갈고리·속 빈 평행 물살.",
    "앙=폐곡선 안 운반 문양(내 재료 하나를 앞 2 m 운반, ID·수명 보존). 엉=순환 접점의 탄(반사) 꺾임(귀환 피해 없음). 옹=귀환면 안으로 모이는 다발(왕복 <=1.2 s). 웅=귀환 띠가 산물 문양을 닦음(제거 재료 산물 0). 몸=수(테 #4F6A84, 심 #2C465A, 안료층 #7893AF).")

# ---- 5 : 걱 (self recovery) sustained / cancelled / A1 comparison, 6 panels
add('s2a_5', '5', '3x2', ['shoulder', 'brush', 'jeokro', 'geok2'], ['wood', 'fire'], [
    ("걱 시작", "Closer camera on the caster's right forearm and brush. A few thin dry teal-green strokes wind loosely round the brush shaft and the right forearm, small and light, not armour. Nothing is aimed at anything."),
    ("걱 유지 (3 s)", "Same camera 1.5 s later. The SAME few strokes are being RE-DRAWN: one stroke is fading while a new one is drawn right beside it along the sleeve, and the strokes lie folded INWARD over the forearm and into the cuff. Nothing grows bigger, nothing stands outward."),
    ("걱 성공 종료", "Same camera. The strokes lie dry and still, tucked INSIDE the sleeve cuff, a faint dried line on the sleeve; no scattering, no leaves, no ground mark."),
    ("걱 피격 취소", "Same camera. An enemy blow lands on the caster's shoulder. EVERYTHING STOPS: the strokes are frozen in place exactly as they were (no new stroke), they do NOT fly apart and do NOT unravel forward; and a short DRY SCRATCH mark (a few thin pale scratches) lies on the brush shaft and sleeve at the point of the blow."),
    ("A1 일반 (넉, 불)", "Same camera. The ordinary A1 attached branches of FIRE body (brick-red #BE543B charred strokes with saw-tooth ends): thin dry branches stand OUTWARD from the brush shaft and sleeve like twigs and fork at their tips. They point away from the caster."),
    ("비교: 걱 / A1", "Close side-by-side on ONE sleeve picture split down the middle: left half = the 걱 strokes folded INWARD into the cuff (teal-green); right half = the A1 branches standing OUTWARD from the shaft (brick-red). Same camera and cuff on both halves."),
], "걱 = 안으로 접힌 같은 획의 재추적(자기 회복)이고 A1 = 밖으로 선 가지다. 피격 때 모든 것이 정지하고 흩어지지 않는다. 취소 표시는 붓대·소매의 마른 긁힘이다.",
    "걱 유지=소매 안으로 접힌 같은 획을 3 s 재추적. 걱 피격=회복·재추적·잔여 움직임을 해당 프레임에 정지, 목 방향 해체 없음. 걱 종료 성공=소매 안 건조. 공통 취소 표시=붓대·소매의 마른 긁힘(<=0.1 s, 신규 입자 없음). A1=밖으로 선 부착 가지. 혼동쌍 6 걱/A1=안으로 접힘/밖으로 선 가지. 몸=목(걱), 화(비교용 A1).")

# ---- 8 : fields + earth follow-up materials
add('s2a_8_1', '8', '2x2', ['shoulder', 'brush', 'jeokro', 'marks', 'frames2'], ['wood', 'fire', 'earth', 'metal'], [
    ("국 (필드)", "Shoulder camera, a low ravine edge ahead. Thick dry teal-green branch bundles are completed into PILLARS that hold up a narrow wooden footing platform over the gap (wood body, torn fibre ends); the caster's ring band remains on the ground nearby, hollow. Only the pillars are tall."),
    ("눈 (필드)", "Shoulder camera. A gate of old vines stands across a path (wood vines, teal-green). BLACK CHARRED dry strokes with burnt holes crawl ALONG the vines of the gate (fire body, only a small dull red accent). Beyond the gate on the ground there is NO ember patch and NO fire."),
    ("뭄 (필드)", "Shoulder camera. Two earth support points on both sides of a small gap are joined into a BRIDGE of hardened earth (thin stair-stepped plates with thickness, a dry broken rim and gaps); the caster is about to step onto it; the rest of the ground is natural."),
    ("숫 (필드)", "Shoulder camera. A tall rock slab and an iron door beside it have a long engraved cut (metal body, two dry lips) that is OPENING along the slab and the door; the cut is a groove with lips, NOT a blade wall, NOT a fence of blades."),
], "필드 글자는 게이트·발판·재료로 읽히고, 내 것은 마른 테·속 빈 획, 게이트 구조물만 높다. 새 불 장판·새 칼날 벽이 없다.",
    "국=가지 기둥이 발판 지지대로 완성(띠 잔존·수명 연장). 눈=덩굴 문을 따라 탄 획 이동(문 밖 불씨 장판 없음). 뭄=지지점이 다리·굳은 땅으로 연결(탑승 조건·재료 수명 보존). 숫=바위·쇠문 음각이 벌어짐(일반 가름에 게이트 절단 확대 금지, 칼날 벽 금지). 게이트 구조물만 높이 면제.")
add('s2a_8_2', '8', '3x1', ['shoulder', 'brush', 'jeokro', 'marks', 'bodies2'], ['water', 'earth'], [
    ("웅 (필드)", "Shoulder camera. A shrine path with a patch of POLLUTION on the ground: a rimless wet dark solid patch (world pollution, enemy-style, no dry rim). The caster's thin dry returning water-stroke ring touches it and the pollution is being WALKED BACK from the contact point: the near side of the patch is already clean ground, the far side still wet and dark. No new water puddle, no product marks."),
    ("몸 붕괴 뒤 토 재료 (10 s)", "Shoulder camera. Where the earth giant has just collapsed: LOW stair-stepped dry earth plates and EMPTY connecting dry strokes lie on the ground (about 0.3 m high), still plainly present as a material mark with a dry broken rim and gaps; nothing tall, no giant."),
    ("뭄 굳음 뒤 토 재료 (8 s)", "Shoulder camera. The hardened support surface after the 뭄: thin stair-stepped plates with clearly visible THICKNESS, a dry broken rim and gaps, a pale dry mark along its edge (the owner's dry rim); natural cracked ground around it; no enemy on it."),
], "필드 웅=오염의 젖은 면(채움·테 없음)을 접촉부부터 걷음. 몸·뭄 후속 재료는 낮은 판·빈 연결 획 / 판 두께·마른 테로 구별되고 재료가 존재한다.",
    "웅=오염의 젖은 면을 접촉부부터 걷음(새 물 장판·반응 산물 없음). 몸 붕괴 뒤=토 재료 10 s·작용량 0·낮은 판·빈 연결 획. 뭄 굳음 뒤=토 재료 8 s·판 두께·마른 테·공극. 내 것=마른 테·속 빈 획, 오염=테 없는 젖은 면.")

# ---- 9 : riposte stroke (앞잡)
add('s2a_9', '9', '2x2', ['shoulder', 'brush', 'jeokro', 'riposte'], ['fire'], [
    ("앞잡 - 준비", "Shoulder camera. The caster has parried and starts the sweep: the great brush is raised back and the first part of a huge DRY-BRUSH black ink ribbon starts from the bristle head (torn dry-brush edges, visible gaps between the bristle strokes). An armoured enemy staggers 6 m ahead, open and untouched."),
    ("앞잡 - 관통", "Same camera. The ribbon, whose head is about 1.5 m wide, sweeps through the armoured enemy's body from upper left to lower right; the dry-brush gaps (ground-coloured holes between the bristle strokes) are clearly visible; the enemy is still readable through the gaps."),
    ("앞잡 - 마름", "Same camera. The ribbon and the enemy are DESATURATED to grey (colour drained), the ribbon is drying and CRACKING along its length, and the first fragments start to fall to the ground; the enemy is dazed, still visible."),
    ("앞잡 - 혼전", "Wider shoulder camera in a melee. Two enemies stand ahead, both at least 70 percent clear; the riposte ribbon is mid-sweep on one; elsewhere a small hollow ring band and a few standing branches of the caster's earlier spells (teal-green, dry, hollow) and ONE rimless wet dark enemy patch on the ground. The ribbon must not hide the second enemy."),
], "앞잡 한 획의 폭(머리 약 1.5 m)·갈필 공극·마름(색 빠짐→균열→파편 낙하)이 한눈에 읽히고, 혼전에서도 적 실루엣이 70% 이상 보인다.",
    "앞잡: 휘두름 공개 0-0.15 s, 관통·피해 약 0.17-0.20 s, 색 빠짐(채도 -65, 노출 0), 균열 0.60-0.85 s, 조각 낙하 0.85-1.20 s, 머리 폭 약 1.5 m, 갈필 공극, 파편 가림. 혼전 칸에서 적 실루엣>=70%, 앞잡 점유<=6%.")

# simplified structure (plan: after 2 redraws, simplify once): the same picture reduced to TWO panels, each 836 x 941 px
SIMPLE = {'s2a_1a_1': [0, 1], 's2a_1a_2': [2, 3], 's2a_1b_1': [0, 1], 's2a_1b_2': [0, 1], 's2a_2a_1': [0, 1], 's2a_2a_2': [2, 3],
          's2a_2b': [0, 2], 's2a_3_1': [0, 1], 's2a_3_2': [2, 3], 's2a_4_1': [0, 3], 's2a_4_2': [0, 3], 's2a_5': [1, 3],
          's2a_8_1': [1, 2], 's2a_8_2': [1, 2], 's2a_9': [1, 2]}
LAYOUT_TXT['2x1'] = "LAYOUT: ONE landscape image split into TWO EQUAL tall panels side by side (each about 836 x 941 px), thin paper-coloured gutter. Reading order left to right = panel 1, panel 2. Use the extra panel size to draw the stroke details large and clear."
for _pid, _idx in SIMPLE.items():
    _s = dict(S[_pid])
    _s['layout'] = '2x1'
    _s['panels'] = [S[_pid]['panels'][i] for i in _idx]
    S[_pid + 'x'] = _s

PICTURES = {k: v for k, v in S.items() if not k.endswith('x')}
SIMPLE_PICTURES = {k: v for k, v in S.items() if k.endswith('x')}


# ------------------------------------------------------------------ prompt builders
def ref_block(refs, prev=None):
    lines = ["--- THE ATTACHED PICTURES ---"]
    for i, r in enumerate(refs, 1):
        lines.append(f"Picture {i} is {REF_TXT[r]}")
    if prev:
        lines.append(f"Picture {len(refs) + 1} is the PREVIOUS version of THIS picture: keep what already works (layout, camera, ground, character, what is correct in each panel) and change ONLY what the FIXES list below names.")
    return '\n'.join(lines)


def panel_block(pid):
    s = S[pid]
    lines = [f"--- THE PICTURE: {len(s['panels'])} panels ---", LAYOUT_TXT[s['layout']], "Draw NO captions, numbers or letters in the picture."]
    for i, (t, d) in enumerate(s['panels'], 1):
        lines.append(f"Panel {i} [{t}]: {d}")
    return '\n'.join(lines)


def prompt(pid, rnd=1):
    s = S[pid]
    prev = OUT / f'{pid}_r{rnd - 1}.png' if rnd > 1 else None
    refs = s['refs']
    parts = [base.HEAD, ref_block(refs, prev), GRAMMAR, FRAMEDEF if s['sheet'] not in ('8', '9', '5') else "", look(s['bodies']), TWO_THIRD_ENEMY, panel_block(pid)]
    fx = OUT / f'{pid}.fix{rnd}.txt'
    if pid.endswith('x') and rnd == 1:   # simplified retry: reuse the base picture's last judge fixes, no previous image
        jf = OUT / f'{pid[:-1]}_r3.judge.md'
        if jf.exists():
            t = jf.read_text(encoding='utf-8', errors='replace').split('tokens used')[0]
            m = re.search(r'고칠 것[:：]\s*(.+)', t, re.S)
            fx = OUT / f'{pid}.fix1.txt'
            fx.write_text((m.group(1).strip() if m else '') + '\n' + STANDING_FIX + '\nPanel numbers in the fixes above refer to the OLD four/six-panel version: apply the ideas to the corresponding panel of this two-panel picture.', encoding='utf-8')
    if (rnd > 1 or pid.endswith('x')) and fx.exists():
        parts.append("--- FIXES (apply exactly these, nothing else changes) ---\n" + fx.read_text(encoding='utf-8').strip())
    parts += [base.BAN, base.TAIL % (OUT / f'{pid}_r{rnd}.png').as_posix()]
    return '\n\n'.join(p for p in parts if p)


def ref_paths(pid, rnd=1):
    paths = [REF_FILES[r] for r in S[pid]['refs']]
    if rnd > 1:
        paths.append(OUT / f'{pid}_r{rnd - 1}.png')
    return paths


# ------------------------------------------------------------------ generation
PNG_RE = re.compile(r'generated_images[\\/]+([0-9a-f-]+)[\\/]+([^\\/\'"\s]+?\.png)', re.I)


def recover(pid, rnd, started):
    """Codex sometimes fails to copy: take the png named in the log (or the newest one since `started`)."""
    dst = OUT / f'{pid}_r{rnd}.png'
    if dst.exists() and dst.stat().st_size > 10000:
        return True
    log = OUT / f'{pid}_r{rnd}.log.txt'
    cand = []
    if log.exists():
        for m in PNG_RE.finditer(log.read_text(encoding='utf-8', errors='replace')):
            p = GEN / m.group(1) / m.group(2)
            if p.exists():
                cand.append(p)
    if cand:
        dst.write_bytes(cand[-1].read_bytes())
        print('RECOVERED', pid, cand[-1])
        return True
    return False


def run_gen(pids, rnd):
    OUT.mkdir(parents=True, exist_ok=True)
    pending = list(pids)
    for attempt in range(1, 4):
        procs = []
        for pid in pending:
            (OUT / f'{pid}_r{rnd}.prompt.txt').write_text(prompt(pid, rnd), encoding='utf-8')
            cmd = ['codex', 'exec', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(OUT)]
            for r in ref_paths(pid, rnd):
                cmd += ['-i', str(r)]
            log = open(OUT / f'{pid}_r{rnd}.log.txt', 'w', encoding='utf-8')
            procs.append((pid, time.time(), subprocess.Popen(cmd, stdin=open(OUT / f'{pid}_r{rnd}.prompt.txt', 'rb'),
                                                             stdout=log, stderr=subprocess.STDOUT, shell=True)))
        failed = []
        for pid, t0, p in procs:
            try:
                p.wait(timeout=1500)
            except subprocess.TimeoutExpired:
                p.kill()
            ok = recover(pid, rnd, t0)
            print('DONE', pid, f'round{rnd}', f'try{attempt}', p.returncode, ok, flush=True)
            if not ok:
                failed.append(pid)
        pending = failed
        if not pending:
            break
    for pid in pending:
        print('FAILED', pid)


# ------------------------------------------------------------------ judge
RUBRIC = """채점표(정지 시안용, 합계 100): ① 틀·동사 형태 30 (하한 29) ② 몸·소유 구별 25 (하한 24) ③ 먹 룩 20 (하한 19) ④ 극치·반응 구조 15 (하한 14) ⑤ 구성·가림 위험 10 (하한 9). 장 통과 = 합계 >= 97 이고 모든 항목이 하한 이상.
점수와 무관한 즉시 실패(하나라도 있으면 실패): 소유 역전(내 것이 젖은 면·적 것이 마른 테) · 가짜 범위(실제 판정 치수와 다른 범위를 그림) · 내 면 채움(내 술식이 속 찬 면·원판) · 불벽(칼날·불·덩굴 벽) · 원판 결계(부채·띠를 채운 판으로 그림)."""

JUDGE_HEAD = """너는 오행부(五行符, 수묵 1인칭 작도 소울라이크)의 컨셉 시안 판정자다. 첨부 그림은 정지 컨셉 시안 한 장이다. 파일을 만들지 말고 답으로만, 한국어로 쓴다.
시안이 확인하는 것은 형태·소유 문법·먹 인상뿐이다(투영·픽셀·점유·가시율·움직임은 확인 대상이 아니다). 비판적 검수자로서 채점하라: 그림에서 직접 보이는 것만 점수로 인정하고, 보이지 않는 의도는 인정하지 않는다. 칸이 작아 읽히지 않으면 구성 항목에서 감점한다. 후한 점수를 주지 마라.

술식 = 몸(오행 5) + 틀(ㅏ 경로 폭 0.3 m·길이 12 m / ㅓ 붓대·소매 부착 / ㅗ 전방 5 m 깊이·먼 현 폭 4 m 부채꼴 / ㅜ 반경 2 m·두께 0.6 m 속 빈 띠, 중앙 비움, 앞 좌우 20도에 1.0-1.2 m 세운 획 쌍) + 동사(한 번·남긴다·번진다·심는다·가른다·돌아온다).
소유: 내 것=마른 테두리·속 빈 획 다발(획 사이 땅이 보임), 적/오염=테두리 없는 젖은 채운 면. 몸 첫 단서: 목=길게 찢긴 섬유 끝(끝은 앞으로 풀림), 화=탄 공극·톱니 끝(재는 위로), 토=계단처럼 겹친 낮은 판(가라앉고 둘레 땅판이 들림), 금=곧은 절단 끝·짧은 횡단 끊김(옆으로 분리, 종이보다 밝지 않음), 수=열린 갈고리·속 빈 평행 물살(표면 따라 아래로). 먹·무광·발광 0, 적 실루엣 >=70% 보임.
동사 정지 윤곽: 남긴다=위로 갈라지는 서 있는 가지 / 번진다=평평한 열린 끝 다발 / 심는다=분기 없는 고립 세로점 / 가른다=음각과 양쪽 마른 입술 한 쌍(벗김=적 갑주 접점의 절단면 한 쌍) / 돌아온다=지름 <=1 m 가는 닫힌 폐곡선(ㅜ의 두꺼운 띠=자리, 가는 고리=동사)."""

JUDGE_TAIL = """답 형식(정확히 이 줄 머리를 지켜라):
칸별 판독: 칸 번호마다 한 줄(무엇이 보이는지·정답 여부)
점수: 틀·동사 NN/30 | 몸·소유 NN/25 | 먹 룩 NN/20 | 극치·반응 NN/15 | 구성·가림 NN/10 | 합계 NNN/100
즉시 실패: 없음 또는 해당 항목과 칸 번호
고유 정지 판정: 통과 또는 미통과 — 한 문장 이유
고칠 것: 1) ... 2) ... 3) ... (각각 다음 그림 생성 프롬프트에 그대로 넣을 수 있는 영어 지시문 한 문장 + 칸 번호, 최대 3개, 하한 미달 항목 우선)"""


ITEM4_NOTE = "[항목 ④ 해석(2회차부터 명시)] '극치·반응 구조'는 극치/반응 칸이 있는 그림이면 그 구조 완성(기반 동사 유지+고유 구조)으로, 없는 그림이면 각 칸의 필수 구조 요소(분기 윗끝·열린 끝·공극·닫힌 내부·접힘·접점 연결 등)가 구조적으로 완결돼 보이는지로 채점한다. '해당 없음'을 이유로 감점하지 않는다."
STANDING_FIX = ("Standing notes: keep the body-colour dry pigment layer visible on the strokes (teal-green wood, brick-red charred fire, ochre earth, pale grey metal, blue-grey water) over the dark ink edge - do not turn everything black. "
                "Keep every caster stroke thin, separate and hollow with ground visible between strokes; enemies stay plain. Panels must stay at least as large and readable as before.")


def make_fix(pids, rnd):
    """Write <id>.fix<rnd>.txt from the previous round's judge answer ('고칠 것')."""
    for pid in pids:
        jf = OUT / f'{pid}_r{rnd - 1}.judge.md'
        if not jf.exists():
            continue
        t = jf.read_text(encoding='utf-8', errors='replace').split('tokens used')[0]
        m = re.search(r'고칠 것[:：]\s*(.+)', t, re.S)
        fixes = m.group(1).strip() if m else ''
        (OUT / f'{pid}.fix{rnd}.txt').write_text(fixes + '\n' + STANDING_FIX, encoding='utf-8')
        print('FIX', pid, rnd, len(fixes))


def judge_prompt(pid, rnd=1):
    s = S[pid]
    lay = {'2x2': '2x2 격자(왼쪽 위=1, 오른쪽 위=2, 왼쪽 아래=3, 오른쪽 아래=4)', '3x2': '3x2 격자(윗줄 왼→오 1·2·3, 아랫줄 4·5·6)', '3x1': '가로 3칸(왼→오 1·2·3)', '2x1': '가로 2칸(왼→오 1·2)'}[s['layout']]
    cells = '\n'.join(f"  칸 {i}: {t} — {d}" for i, (t, d) in enumerate(s['panels'], 1))
    return '\n\n'.join([JUDGE_HEAD, RUBRIC] + ([ITEM4_NOTE] if (rnd > 1 or pid.endswith('x')) else []) + [
                        f"[이 그림] 장 {s['sheet']} ({pid}), 레이아웃 {lay}. 그림은 1672x941 원본이고 칸이 작다는 점은 감안하되 읽히지 않으면 감점.\n[의도한 칸]\n{cells}",
                        f"[이 장의 고유 정지 판정] {s['proof']}\n[계획서 발췌] {s['plan']}",
                        JUDGE_TAIL])


def run_judge(pids, rnd):
    procs = []
    for pid in pids:
        img = OUT / f'{pid}_r{rnd}.png'
        if not img.exists():
            print('NOIMG', pid)
            continue
        pf = OUT / f'{pid}_r{rnd}.judge_prompt.txt'
        pf.write_text(judge_prompt(pid, rnd), encoding='utf-8')
        cmd = ['codex', 'exec', '--skip-git-repo-check', '-s', 'read-only', '-C', str(REPO), '-i', str(img), '-']
        out = open(OUT / f'{pid}_r{rnd}.judge_raw.txt', 'w', encoding='utf-8')
        procs.append((pid, subprocess.Popen(cmd, stdin=open(pf, 'rb'), stdout=out, stderr=subprocess.STDOUT, shell=True)))
    for pid, p in procs:
        p.wait(timeout=1500)
        raw = (OUT / f'{pid}_r{rnd}.judge_raw.txt').read_text(encoding='utf-8', errors='replace')
        # keep only the last model answer (after the final "codex" marker)
        ans = raw.split('\ncodex\n')[-1]
        (OUT / f'{pid}_r{rnd}.judge.md').write_text(ans, encoding='utf-8')
        print('JUDGED', pid, summarize(ans), flush=True)


FLOORS = [29, 24, 19, 14, 9]


def summarize(ans):
    m = re.search(r'점수[:：]\s*틀[^\d]*(\d+)\s*/\s*30[^\d]+(\d+)\s*/\s*25[^\d]+(\d+)\s*/\s*20[^\d]+(\d+)\s*/\s*15[^\d]+(\d+)\s*/\s*10[^\d]+(\d+)', ans)
    if not m:
        return 'UNPARSED'
    v = [int(x) for x in m.groups()]
    items, tot = v[:5], v[5]
    below = [i + 1 for i, (a, f) in enumerate(zip(items, FLOORS)) if a < f]
    ff = re.search(r'즉시 실패[:：]\s*(.+)', ans)
    ff = ff.group(1).strip() if ff else '?'
    ok = tot >= 97 and not below and ff.startswith('없음')
    return f'{items} total={tot} below_floor={below} instant_fail={ff[:60]} PASS={ok}'


def ledger_rows(pids, rnd, note=''):
    rows = []
    for pid in pids:
        jf = OUT / f'{pid}_r{rnd}.judge.md'
        if not jf.exists():
            continue
        t = jf.read_text(encoding='utf-8', errors='replace').split('tokens used')[0]
        m = re.search(r'점수[:：]\s*틀[^\d]*(\d+)\s*/\s*30[^\d]+(\d+)\s*/\s*25[^\d]+(\d+)\s*/\s*20[^\d]+(\d+)\s*/\s*15[^\d]+(\d+)\s*/\s*10[^\d]+(\d+)', t)
        if not m:
            rows.append(f'| {pid} | {rnd} | ? | | | | | | | UNPARSED | | |')
            continue
        v = [int(x) for x in m.groups()]
        ff = re.search(r'즉시 실패[:：]\s*(.+)', t)
        ff = (ff.group(1).strip() if ff else '?')[:50]
        below = [i + 1 for i, (a, f) in enumerate(zip(v[:5], FLOORS)) if a < f]
        ok = v[5] >= 97 and not below and ff.startswith('없음')
        rows.append(f"| {pid} | {rnd} | {v[0]} | {v[1]} | {v[2]} | {v[3]} | {v[4]} | **{v[5]}** | {ff} | {'PASS' if ok else '미통과'} | `{pid}_r{rnd}.png` | {note} |")
    print('\n'.join(rows))


def main():
    args = sys.argv[1:]
    rnd = int(args[args.index('--round') + 1]) if '--round' in args else 1
    only = [a for a in args if not a.startswith('--') and not a.isdigit()]
    pids = [p for p in S if (p in only) or (not only and not p.endswith('x'))]
    OUT.mkdir(parents=True, exist_ok=True)
    for pid in pids:
        (OUT / f'{pid}_r{rnd}.prompt.txt').write_text(prompt(pid, rnd), encoding='utf-8')
        print('PROMPT', pid, rnd)
    if '--ledger' in args:
        ledger_rows(pids, rnd)
    if '--mkfix' in args:
        make_fix(pids, rnd)
    if '--run' in args:
        run_gen(pids, rnd)
    if '--judge' in args:
        run_judge(pids, rnd)


if __name__ == '__main__':
    main()
