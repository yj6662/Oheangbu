# SPEC-SPELL-DEPLOY-308 section 6: the 120-row deploy map, produced from the glyph CSV (the only source of glyph effects).
# Nothing is typed by hand: category comes from the CSV column, frame from the medial, element from the initial, end
# treatment from the final. The editor importer (Deploy308 "deploy308-map") reads the JSON this writes and makes the same
# assertions again on its side (AC-D2).
#   python Tools/Art/deploy308_map.py            -> Art/SpellVFX120/Deploy308/Generated/deploy_map308.json
#   python Tools/Art/deploy308_map.py --check    -> assertions only, nothing written
# (2026-10-04: this is Tools/Unity/Stage308_forms2/_Tools/deploy308_map.py with the two path lines set back to the live places;
#  the previous tool is Tools/Art/_backup_forms2/deploy308_map.py.pre_forms2)
# #308 forms2 (Stage308_forms2/DESIGN.md 5-1): this is the stage's copy of Tools/Art/deploy308_map.py (base kept beside it as
# deploy308_map.py.orig). Only the legacy-body policy and the output place differ. It writes into the STAGE; the live JSON
# (Art/SpellVFX120/Deploy308/Generated) and the map asset are not touched here - the editor command forms308-map reads the
# stage's JSON. When the stage is accepted this file replaces Tools/Art/deploy308_map.py (forms2_copy.py does not do that).
import csv, hashlib, io, json, sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CSV = ROOT / 'Docs' / '오행부_작도어휘_v0_1.csv'
OUT = ROOT / 'Art' / 'SpellVFX120' / 'Deploy308' / 'Generated' / 'deploy_map308.json'

ELEMENT = {'목': 'Wood', '화': 'Fire', '토': 'Earth', '금': 'Metal', '수': 'Water'}
INITIAL = {'ㄱ': 'Wood', 'ㄴ': 'Fire', 'ㅁ': 'Earth', 'ㅅ': 'Metal', 'ㅇ': 'Water'}
FRAME = {'ㅏ': 'YangSingle', 'ㅓ': 'YinSingle', 'ㅗ': 'YangArea', 'ㅜ': 'YinArea'}
FRAME_NAME = {'ㅏ': '양·단일', 'ㅓ': '음·단일', 'ㅗ': '양·광역', 'ㅜ': '음·광역'}
FINAL = {'무': 'None', 'ㄱ': 'Giyeok', 'ㄴ': 'Nieun', 'ㅁ': 'Mieum', 'ㅅ': 'Siot', 'ㅇ': 'Ieung'}
EXPECTED = {'공격': 50, '상합 설치': 5, '패링': 5, '버프': 25, '소환': 5, '방벽': 5, '필드': 5, '공백': 20}
# what the game still reads from the old presentation when a row is switched on (Spec section 1 table):
#   Keep = the old body stays as it is, the deploy layer is drawn on top
#   Wrap = the old body stays, its entrance / exit is wrapped by the ink column
#   Retire = the deploy layer replaces the old body
# Review 2026-10-04: Buff and Field are Keep, not Retire. Their old body is what tells the player the spell is still
# running - EABuffRuntime stretches the buff visual over the buff's whole duration (no HUD item shows a buff), and a field's
# body marks the area it acts on - while the deploy layer's own presentation of them ends about 1.3 s after the cast
# (runtime-owned casts reach it without a host, so not even residue stays). Retiring them would leave a running buff /
# field with nothing to see. Back to Retire only once the layer shows them for their real lifetime (phase 2).
# #308 forms2 (D308-13b "the parry plates and the barriers are replaced", DESIGN.md section 3): the layer now shows these for
# their real lifetime, so their catalogue bodies retire:
#   Parry -> Retire   the guard stroke stands for the rule's own guard lifetime (wet in the window, dry afterwards)
#   Ward -> Retire    the ink fence stands for the ward's lifetime, in a standing slot no burst can push out
#   Buff stays Keep   (second pass, REVIEW.md F11) what the layer shows of a RUNNING buff - a 9 cm mark beside each footprint
#                     behind the player, a ring at the feet, drops when it ends - is not in the forward view of a first-person
#                     eye, and the user asked for the parry plates, the barriers and the basic five to be replaced, not the
#                     buff bodies. The condition of the note above ("only once the layer shows them for their real lifetime")
#                     is not met. The five EA buff rows can be retired by the editor command (forms308-map:buff) after the
#                     user has looked at the previews and decided; this table does not do it.
#   ComboInstall -> Retire   a row's own body is only built when the letter is drawn WITHOUT a target (a target goes to the
#                     presenter): nothing is installed then, and the loose mark says so
#   Summon stays Wrap (the model is the summon; in a scene without combat summons the static animal's lifetime times its
#                     release sound)   Field stays Keep (the tree and the bridge are things of the world)
LEGACY = {'AttackSingle': 'Retire', 'AttackArea': 'Retire', 'ComboInstall': 'Retire', 'Parry': 'Retire', 'Buff': 'Keep',
          'Summon': 'Wrap', 'Ward': 'Retire', 'Field': 'Keep', 'Blank': 'Retire'}
# D308-13b ("Vfx c3까지 교체"): the basic five (가 나 마 사 아) are replaced too - their Bolt300 flight body and impact are retired
# and the deploy layer's own main stroke (the flight) and hit splash (the impact) stand in. They are single attacks, so the
# category policy already says Retire; they are named here so a later change of that policy cannot quietly bring the old
# bodies back, and build() asserts it.
BASIC_FIVE = '가나마사아'
LEGACY_OVERRIDE = {'국': 'Keep'}   # the planted tree lift is read by the game (stays Keep whatever the Field policy becomes)
LEGACY_OVERRIDE.update({letter: 'Retire' for letter in BASIC_FIVE})


def category(row):
    kind, medial = row['분류'], row['중성']
    if kind == '공격':
        if medial == 'ㅏ': return 'AttackSingle'
        if medial == 'ㅗ': return 'AttackArea'
        raise SystemExit('attack row with medial %s: %s' % (medial, row['글자']))
    return {'상합 설치': 'ComboInstall', '패링': 'Parry', '버프': 'Buff', '소환': 'Summon', '방벽': 'Ward', '필드': 'Field',
            '공백': 'Blank'}[kind]


def grid_expectation(medial, final, initial):
    """Spec section 6 table: what the category must be for (medial, final); the same for all five elements."""
    if medial == 'ㅏ': return '상합 설치' if final == 'ㅁ' else '공격'
    if medial == 'ㅓ': return '패링' if final == '무' else '버프'
    if medial == 'ㅗ': return '소환' if final == 'ㅁ' else '공격'
    if final == '무': return '방벽'
    return '필드' if final == initial else '공백'


def build():
    raw = CSV.read_bytes()
    rows = list(csv.DictReader(io.StringIO(raw.decode('utf-8-sig'))))
    assert len(rows) == 120, 'CSV must have 120 rows, has %d' % len(rows)
    counts = Counter(r['분류'] for r in rows)
    assert dict(counts) == EXPECTED, 'category counts %s != %s' % (dict(counts), EXPECTED)
    out, seen = [], set()
    for r in rows:
        letter = r['글자']
        assert len(letter) == 1 and letter not in seen, 'bad or repeated letter %r' % letter
        seen.add(letter)
        assert ELEMENT[r['속성']] == INITIAL[r['초성']], 'element / initial mismatch at ' + letter
        assert FRAME_NAME[r['중성']] == r['프레임'], 'frame / medial mismatch at ' + letter
        want = grid_expectation(r['중성'], r['종성'], r['초성'])
        assert r['분류'] == want, 'grid mismatch at %s: CSV %s, Spec section 6 %s' % (letter, r['분류'], want)
        # the syllable itself must be the composition of its three jamo
        code = ord(letter) - 0xAC00
        assert 0 <= code < 11172, 'not a Hangul syllable: ' + letter
        cat = category(r)
        out.append({
            'letter': letter, 'category': cat, 'frame': FRAME[r['중성']], 'element': ELEMENT[r['속성']],
            'final': FINAL[r['종성']], 'impactFrame': cat in ('AttackSingle', 'AttackArea'),
            'impactOnTrigger': cat == 'ComboInstall',
            'legacyBody': LEGACY_OVERRIDE.get(letter, LEGACY[cat]), 'formOverride': '',
        })
    cats = Counter(o['category'] for o in out)
    assert cats['AttackSingle'] == 25 and cats['AttackArea'] == 25
    # impactFrame = the row is ELIGIBLE for impact frames (D308-10c: the runtime asks for them only when the judged target is groggy)
    assert sum(1 for o in out if o['impactFrame']) == 50
    by_letter = {o['letter']: o for o in out}
    # forms2 policy, asserted so that a later edit of the table above cannot quietly break it
    for o in out:
        want = 'Wrap' if o['category'] == 'Summon' else 'Keep' if o['category'] in ('Field', 'Buff') else 'Retire'
        assert o['legacyBody'] == want, 'forms2 policy: %s (%s) is %s, must be %s' % (o['letter'], o['category'], o['legacyBody'], want)
    assert by_letter['국']['legacyBody'] == 'Keep', 'the tree lift must stay Keep'
    for letter in BASIC_FIVE:
        row = by_letter[letter]
        assert row['category'] == 'AttackSingle' and row['final'] == 'None' and row['legacyBody'] == 'Retire', 'basic five (D308-13b): %s is %s / %s' % (letter, row['category'], row['legacyBody'])
    return {
        'version': 1, 'source': 'Docs/오행부_작도어휘_v0_1.csv', 'csvSha256': hashlib.sha256(raw).hexdigest(),
        'counts': {k: counts[k] for k in EXPECTED}, 'categories': dict(sorted(cats.items())), 'rows': out,
    }


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    data = build()
    text = json.dumps(data, ensure_ascii=False, indent=1) + '\n'
    if '--check' not in sys.argv:
        OUT.parent.mkdir(parents=True, exist_ok=True)
        OUT.write_bytes(text.encode('utf-8'))
    print('rows %d  counts %s' % (len(data['rows']), data['counts']))
    print('categories %s' % data['categories'])
    print('impact rows %d  on-trigger rows %d' % (sum(o['impactFrame'] for o in data['rows']), sum(o['impactOnTrigger'] for o in data['rows'])))
    print('legacy %s' % dict(Counter(o['legacyBody'] for o in data['rows'])))
    print('basic five (D308-13b) %s' % ' '.join('%s=%s' % (o['letter'], o['legacyBody']) for o in data['rows'] if o['letter'] in BASIC_FIVE))
    print('csv sha256 %s' % data['csvSha256'])
    print('json sha256 %s  %s' % (hashlib.sha256(text.encode('utf-8')).hexdigest(), 'not written (--check)' if '--check' in sys.argv else OUT.as_posix()))


if __name__ == '__main__':
    main()
