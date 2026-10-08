"""#308 steam-temple building concepts, second pass (D308-46, SPEC-ARCH-TEMPLE-308 §13b): the image model is shown the building as it
is in the project (Unity stills, front and back) and the part kit as it exists (renders with letters and sizes), and paints the best
arrangement using ONLY those parts, so the concept can be built exactly by KitDress308.
  python Tools/Art/temple_building_concepts308.py Kirim_Dae|Song_Jong   -> Art/World/Temple308/Concepts/Buildings2/concept_<name>.png
"""
import subprocess, sys
from pathlib import Path
OUT = Path(__file__).resolve().parents[2] / 'Art/World/Temple308/Concepts/Buildings2'
RULE = ('ATTACHED IMAGE 1 shows an existing game building twice: its front three-quarter view on the left and its back three-quarter view on the right. '
        'ATTACHED IMAGE 2 is the complete kit of machine parts that exists, each with a letter, a name and its real size. '
        'TASK: paint ONE image, landscape 2:1, that shows the SAME building in the SAME two camera views side by side (front view left, back view right), '
        'with the kit parts built into it as working steam machinery. Both views must show the same single design. '
        'HARD RULES: (1) The building itself is unchanged: same roof, same tiles, same pillars, walls, doors, plinth, colours and proportions as in image 1; add no new '
        'roofs, no lean-tos, no extra rooms. (2) Use ONLY kit parts A to J, drawn exactly as they look in image 2 (shape, material, colour); they may be repeated '
        'and uniformly scaled. (3) The only other things allowed are: round copper pipes with brass bands held by small iron brackets, round copper flue tubes, '
        'plain iron shafts, plain square granite footing blocks, plain iron stands. Nothing else: no gauges, no tanks, no extra boilers, no chains, no belts, no smoke, '
        'no glow, no people, no text or letters in the picture. (4) Everything follows the architecture: parts sit on the pillar grid, pipes run in straight '
        'horizontal and vertical lines along beams and down pillars, machines stand on their own granite footing on the plinth. It must read as a building that was '
        'designed with its machinery, calm and orderly, not as props scattered on it. (5) Keep real scale: use the sizes written in image 2 against the pillars '
        '(the pillars are about 3.3 m tall). Overcast daylight, plain pale background, realistic rendering matching image 1.')
JOBS = {
 'Kirim_Dae': 'A main worship hall, 20 m wide, with six pillars along the front and a tall plain gable wall at each end. Decide the most fitting arrangement yourself; it should '
              'include one boiler (A with B on top, flue tube up to a C cap), lotus collars D on pillars, C caps on flue tubes on the ridge, one flywheel E with a gear F, '
              'and two steam lanterns J in front of the plinth.',
 'Song_Jong': 'An open bell pavilion with a raised wooden deck, a cross-shaped plan, and a bronze bell already hanging in the middle (keep that bell). Decide the most fitting '
              'arrangement yourself; it should include the steam cylinder G on the deck driving a ram toward the bell, the flywheel E and gear F beside it on one shaft, '
              'one boiler (A with B on top, flue tube up to a C cap) standing on the ground plinth, and lotus collars D on the deck pillars.',
}
def make(k):
    p = ('Use your built-in image generation tool to create ONE image and save it as a PNG file named "concept_%s.png" in the current directory. Do not write any code '
         'or other files. After saving, reply with only the file name.\n\n%s\n\nBUILDING: %s') % (k, RULE, JOBS[k])
    r = subprocess.run(['codex', 'exec', '--skip-git-repo-check', '--sandbox', 'workspace-write', '-C', str(OUT), '-i', 'ref_%s.png' % k, '-i', 'ref_parts.png', '-'], input=p, text=True,
                       capture_output=True, encoding='utf-8', errors='replace', shell=True, timeout=900, cwd=str(OUT))
    (OUT / ('concept_%s.log' % k)).write_text((r.stdout or '')[-4000:] + '\n--- stderr ---\n' + (r.stderr or '')[-3000:], encoding='utf-8')
    print(k, 'OK' if (OUT / ('concept_%s.png' % k)).exists() else 'NO FILE', r.returncode)
if __name__ == '__main__':
    make(sys.argv[1])
