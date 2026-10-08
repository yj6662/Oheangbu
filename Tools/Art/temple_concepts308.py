"""#308 steam-temple prop concepts (D308-43, SPEC-ARCH-TEMPLE-308 §13): three-view concept sheets made with the Codex CLI's built-in
image generation (PROD-AIASSET, D308-28), shown to the user BEFORE any Meshy credit is spent.
User 2026-10-08: the first kit reads as "a thing that already existed with steampunk parts stuck on" - so every prop here is
designed as a machine from the ground up, with the temple's forms cast into the machine itself.
  python Tools/Art/temple_concepts308.py list
  python Tools/Art/temple_concepts308.py make <id> [<id> ...]      -> Art/World/Temple308/Concepts/<id>.png (+ <id>.log)
"""
import subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Temple308/Concepts'

RULE = ('DESIGN RULE: this object was designed and cast as a steam machine by the engineers of a 14th-century Korean Buddhist mountain temple. '
        'It is NOT a traditional object with pipes attached. The machine IS the object: pressure vessels, flues, valves, gears and pistons are its '
        'main structure, made of cast bronze, hammered copper and riveted black iron; carved granite appears only as the plinth it stands on. '
        'Korean Buddhist forms are cast into the machine parts themselves: lotus-petal collars around flanges, lotus-bud pressure domes, octagonal '
        'plans, bead-band rivet rows, cloud-shaped brackets, a wish-fulfilling-jewel finial. Aged surfaces: green verdigris on copper, soot near flues, '
        'dark oxidised bronze. Sober and heavy, hand-built, no neon, no glowing parts, no fantasy crystals, no Victorian ornament, no clock faces.')
SHEET = ('OUTPUT: one concept-art turnaround sheet, landscape 3:2. Exactly three orthographic views of the SAME single object, evenly spaced in one '
         'row and the same height: FRONT view on the left, SIDE view in the middle, BACK view on the right. Plain flat light-grey background, soft '
         'even studio light, no cast shadow on the ground, no people, no scenery, no text, no labels, no arrows, no border. The whole object is '
         'inside the frame in every view. Realistic painted concept-art rendering with clear material definition.')

PROPS = {
    'lantern': ('steam lantern (석등의 자리에 서는 등)', 'about 2 m tall',
                'An octagonal granite plinth. On it a short riveted iron fire-box with a small stoke door. Above it a slender copper steam column with a '
                'lotus-petal collar at top and bottom. The light chamber is a squat octagonal bronze pressure vessel with four round thick-glass '
                'portholes framed by bolted bronze rings (dark, unlit). Its roof is a hammered-copper octagonal canopy with upturned corners whose '
                'centre rises into a short flue ending in a lotus-bud cap. One small valve wheel shaped like an eight-petal lotus on the fire-box.'),
    'censer': ('pressure censer (향로)', 'about 1.4 m tall',
               'A three-legged cast-bronze vessel like a ritual incense burner built as a pressure cooker: a round riveted belly on three cloud-shaped '
               'legs, a domed lid clamped by eight wing bolts, a centrifugal ball governor on top of the lid, and three thin curved copper vent pipes '
               'rising from the shoulder like stems, each ending in a small lotus-seed-pod nozzle. A sight-glass tube on one side.'),
    'bell': ('steam-struck temple bell (범종)', 'about 3.2 m tall',
             'A large Korean bronze temple bell with a dragon-loop crown and a sound tube, hanging from a riveted iron gantry of two A-frames. Instead of a '
             'swinging log, a horizontal steam piston on the gantry drives an iron ram with a wooden striking head toward the lotus-shaped striking '
             'point of the bell. A spoked flywheel and a short crank beside the cylinder, a small upright boiler bolted to one A-frame, one exhaust pipe.'),
    'basin': ('pumped water basin (수조)', 'about 1.3 m tall, 2 m wide',
              'A wide shallow octagonal basin of hammered copper with a rolled rim on a low granite ring. In its centre a bronze hand-pump column shaped '
              'like a lotus stem, with a curved dragon-head spout and a long iron pump lever. Riveted seams, a brass overflow pipe, a drain cock.'),
    'sutra_wheel': ('geared sutra wheel (윤장대)', 'about 3.4 m tall',
                    'An octagonal revolving sutra case of copper panels and bronze frame turning on a vertical iron axle. Under it an exposed large '
                    'bronze bevel gear meshing with a small iron pinion on a horizontal drive shaft. The case has eight latticed panels and a tiered '
                    'copper canopy with a jewel finial. A heavy octagonal granite and iron base holds the bearing. Four push handles around the case.'),
    'iron_pagoda': ('cast-iron pagoda (철탑)', 'about 5 m tall',
                    'A five-storey square pagoda cast entirely in black iron plates bolted together, each storey a shallow riveted box with a copper roof '
                    'of upturned corners, wind-bells of brass at the corners. The body storeys are ringed by exposed gear teeth so that each storey can '
                    'turn; a central steam pipe rises through all storeys and ends in a tall finial of stacked bronze rings and a jewel. Granite base.'),
    'stoker_guardian': ('boiler guardian (금강역사 자리의 화부 상)', 'about 2.6 m tall',
                        'A standing guardian figure in the pose of a temple gate guardian, built as an iron and copper boiler automaton: a barrel-boiler torso '
                        'with a stoke door in the belly, riveted plate shoulders, piston forearms, a bronze head with a stern Korean guardian face and a '
                        'topknot shaped as a safety valve, one hand holding a long iron stoking rod like a vajra staff. A short stack rises from the back. '
                        'Standing on a low granite lotus pedestal. Static statue, not a robot toy.'),
    'drum': ('mechanical dharma drum (법고)', 'about 2.4 m tall',
             'A large barrel drum with ox-hide heads and bronze stud rows, resting on a cast-iron cradle shaped like two crouching tortoises. Two piston-driven '
             'drum arms with padded heads are mounted on the cradle, linked by rods to a small flywheel and cylinder underneath. Copper oil cups, one gauge.'),
}


PART = ('This is ONE reusable machine part of a kit; other parts bolt onto it. Its top and bottom mating faces are flat and horizontal, with a bolted '
        'flange ring where it joins the next part. Show the part alone, nothing attached that belongs to another part.')
PROPS.update({
    'part_firebox': ('kit part: fire-box module', '0.9 m tall, octagonal, 1.0 m across',
                     'A riveted black-iron octagonal fire-box: plate walls with bead-band rivet rows, one hinged stoke door with a lotus-shaped cast latch on '
                     'the front face, small draught slots near the bottom, a flat bolted flange on top. ' + PART),
    'part_vessel': ('kit part: pressure vessel', '0.8 m tall, octagonal, 1.0 m across',
                    'A squat octagonal bronze pressure vessel: four faces carry a round thick-glass porthole in a bolted bronze ring (dark glass), four '
                    'faces are plain riveted plate; a lotus-petal collar cast around the bottom edge, a flat bolted flange on top. ' + PART),
    'part_canopy': ('kit part: copper canopy with flue cap', '0.9 m tall, octagonal, 1.6 m across',
                    'A hammered-copper octagonal roof canopy with eight gently upturned corners and visible seams, cloud-shaped bronze brackets under the '
                    'eave, its centre rising into a short round flue that ends in a closed lotus-bud cap. Flat bolted flange underneath. ' + PART),
    'part_collar': ('kit part: lotus collar flange', '0.25 m tall, 0.6 m across',
                    'A cast-bronze ring collar: two rows of lotus petals (one row up, one row down) around a bolted pipe flange with eight bolt heads; '
                    'a round opening through the middle for a 0.35 m column. ' + PART),
    'part_flywheel': ('kit part: flywheel', '1.4 m diameter, 0.15 m thick',
                      'A cast-iron flywheel with a heavy rim and eight curved spokes; the hub is a bronze eight-petal lotus boss with a round shaft hole; '
                      'one crank pin on a spoke. Shown as a wheel standing upright: front view is the round face, side view is the thin edge.'),
    'part_gear': ('kit part: large gear', '2.0 m diameter, 0.14 m thick',
                  'A large cast-bronze spur gear with straight square teeth around the rim, eight spokes, a raised hub ring with a lotus-petal border and a '
                  'round shaft hole. Shown as a wheel standing upright: front view is the round face, side view is the thin toothed edge.'),
    'part_cylinder': ('kit part: steam cylinder', '1.2 m long, 0.45 m diameter, lying horizontal',
                      'A horizontal cast-iron steam cylinder with bolted end covers, a copper lagging band around its middle, a small slide-valve chest on '
                      'top with one pipe stub, a stuffing gland on one end cover with a round hole for a piston rod (no rod shown), two cast mounting feet.'),
    'part_bell': ('kit part: bronze temple bell', '1.9 m tall, 1.2 m across',
                  'A Korean bronze temple bell: a single dragon-loop crown with a sound tube on top, a shoulder band with four panels of nine studs, '
                  'a lotus-shaped striking point on the body, flying-figure relief, a plain rim. Dark oxidised bronze. Hanging freely, no frame shown. '
                  'The bell is ONLY a bell: no gears, no pipes, no valves, no pistons, no machinery of any kind on it or above it; nothing above the dragon loop.'),
    'part_stand': ('kit part: cast bearing pedestal', '1.4 m tall, 1.0 m wide, 0.3 m thick',
                   'A cast-iron A-frame bearing pedestal that carries the shaft of a flywheel: two splayed legs joined by a pierced web cast as a cloud scroll, '
                   'each leg ending in a bolted foot pad shaped as a lotus leaf, a pillow-block bearing on top with a bronze bearing cap held by two bolts and a '
                   'round shaft hole through it, one small bronze oil cup on the cap. Black cast iron with rust in the hollows, bronze cap with verdigris. '
                   'Front view is the wide A shape, side view is the thin edge. Nothing else: no shaft, no wheel, no base.'),
    'part_case': ('kit part: revolving sutra case', '1.6 m tall, octagonal, 1.5 m across',
                  'An octagonal sutra case that turns on a vertical axle: a cast-bronze frame of eight corner posts with bead-band rivet rows, each of the eight '
                  'faces filled by a tall pierced bronze lattice panel of a Korean flower-lattice (kkotsal) pattern over a dark interior, a low lotus-relief '
                  'panel under each lattice, a lotus-petal skirt cast around the bottom edge. Flat top with a bolted flange ring, flat bottom. No roof, no '
                  'canopy, no base, no handles, no gears, no pipes. ' + PART),
})


def prompt(pid):
    title, size, body = PROPS[pid]
    return ('Use your built-in image generation tool to create ONE image and save it as a PNG file named "%s.png" in the current directory. '
            'Do not write any code or other files. After saving, reply with only the file name.\n\n'
            'SUBJECT: %s, %s. %s\n\n%s\n\n%s') % (pid, title, size, body, RULE, SHEET)


def make(pid):
    OUT.mkdir(parents=True, exist_ok=True)
    r = subprocess.run(['codex', 'exec', '--skip-git-repo-check', '--sandbox', 'workspace-write', '-C', str(OUT), '-'], input=prompt(pid), text=True,
                       capture_output=True, encoding='utf-8', errors='replace', shell=True, timeout=900)
    (OUT / (pid + '.log')).write_text((r.stdout or '')[-6000:] + '\n--- stderr ---\n' + (r.stderr or '')[-3000:], encoding='utf-8')
    ok = (OUT / (pid + '.png')).exists()
    print(pid, 'OK' if ok else 'NO FILE', 'exit', r.returncode)
    return ok


if __name__ == '__main__':
    if len(sys.argv) < 2 or sys.argv[1] == 'list':
        for k, v in PROPS.items(): print(k, '-', v[0], v[1])
    elif sys.argv[1] == 'make':
        for pid in sys.argv[2:]: make(pid)
