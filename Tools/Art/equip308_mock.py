"""#308 장비·소지품 화면 concept mock (SPEC-UI-EQUIPMENT-308). Offline: Pillow + numpy only, deterministic, no editor.

Every screen is 1920x1080 in the #304 ink language: the same textures Unity uses (Art/UI304/design/FINAL/assets = the
UI304 sprites, Equipment246 slot art, the #308 stage marks from equip308_assets.py) and the project's UI fonts.
Numbers are not typed here: the equipment catalog, the economy profile and the combat config are parsed from the
project's own .asset / .cs files, the spell list and the 석경 bundles from WorldMacroCollectionCatalog.cs, and the
displayed values are computed the way RuntimePlayerStats / EquipmentText308 compute them. Only SAVE (what this sample
player owns, wears and has learnt) is a sample.

Writes Art/UI308/Equipment/:
  equip308_a_stone.png      (a) 오행 마석 칸 초점 (쉼터 곁)          equip308_b_empty.png     (b) 빈 장비 칸 초점
  equip308_c_fragment.png   (c) 석경 탭, 조각 초점                   equip308_f_candidate.png (f) 후보 줄 초점 (견줌)
  equip308_g_offshelter.png (g) 쉼터 밖의 오행 마석 칸               equip308_d_ref.png       (d) 레퍼런스 1920x1080
  equip308_h_gear.png       (h) 낀 장비 칸 초점 (범례 셋)            equip308_i_virtue.png    (i) 오덕 칸 초점
  equip308_e_grid_200.png / equip308_e_status_200.png  (e) 1080p 화소 2배 확대
  equip308_a_stone_16x10.png  같은 화면을 1728x1080에 맞춘 것 (페이지 x .90, 위아래 띠 54 px)
  equip308_mock_sheet.png   전체 시트 (한국어 설명) + equip308_mock_sheet_half.jpg (절반 크기 미리보기)
usage: python Tools/Art/equip308_mock.py

#308 theme (SPEC-UI-THEME-308, D308-15): render(view, at_shop, theme=THEME) draws the same screen with the theme kit, read
by slot name through Tools/Art/theme308_kit.py (lattice sashes, lacquer board + one najeon chrysanthemum, porcelain plaque
with a lotus band, nacre petals, nacre kept frame, paper bed under the focus ring). theme=None (the default) is the #304
look, byte for byte the first concept sheet; the files above stay that look ("before theme"). The themed sheet and its
measurements are made by Tools/Art/equip308_theme.py."""
import glob
import json
import os
import re
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
sys.path.insert(0, HERE)
import equip308_assets as A308  # noqa: E402  (the stage's own mark / frame generator: same pixels as the staged PNGs)

PROJ = os.path.join(ROOT, 'Oheangbu', 'Assets', '_Project')
AS = os.path.join(ROOT, 'Art', 'UI304', 'design', 'FINAL', 'assets')
TEX = os.path.join(PROJ, 'Art', 'UI', 'UI304', 'Textures')
FONTS = os.path.join(PROJ, 'Art', 'UI', 'UI304', 'Fonts')
EQART = os.path.join(PROJ, 'Art', 'UI', 'Equipment246')
WORLD = os.path.join(ROOT, 'Art', 'UI304', 'clean', 'palace.png')
REF = os.path.join(ROOT, 'Docs', 'Refs', 'UI308', 'equipment_menu_ref_20261004.webp')
OUT = os.path.join(ROOT, 'Art', 'UI308', 'Equipment')
W, H = 1920, 1080

# ---------------------------------------------------------------- #304 tokens (DESIGN §2.1)
INK, VEIL, ASH, MIST = (0x14, 0x14, 0x13), (0x0F, 0x0F, 0x0E), (0x5A, 0x57, 0x4F), (0xA7, 0xA3, 0x98)
PAPER, SHEET, CHIP = (0xE6, 0xE2, 0xD7), (0xDF, 0xDB, 0xD0), (0xD6, 0xD1, 0xC4)
CIN, CINL, OFF, KEYLIP = (0xB8, 0x39, 0x2B), (0xD6, 0x5A, 0x43), (0x6E, 0x6B, 0x64), (0xB8, 0xB3, 0xA7)


def mix(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


PENDING = mix(VEIL, MIST, .35)      # a step not reached yet: Mist a.35 pre-composited on the veil (Content304Precomposite)

# ---------------------------------------------------------------- #308 theme: the kit, read by name (never copied here)
KITDIR = os.path.join(ROOT, 'Tools', 'Unity', 'Stage308_theme', '_ProjectAssets', 'Art', 'UI', 'UI308', 'Theme')
TH = None        # the theme of the render in progress; None = the #304 look
# the default answers of the theme's six questions: lacquer-dominant, 정자살 slot grid, shimmer off, no iron-red inlay.
#   grid   'sash'  each row of 칸 = one 교창 (plain frame, panes seated in the bars, names under the sill)   <- picked
#          'leaf'  one door leaf 5 x 4 with a plain frame, the names in bands inside it
#          'table' the kit review's first composite: bars in the middle of the gaps, torn chips, stepped frame
#   focus  'swell' a 한지 band under the cinnabar ring (the bars round the focused 칸 are masked)             <- picked
#          'veil'  a veil-coloured band under the ring        'none' the ring straight over the bars
#   phase2 menu-wide parts (rail = cut-shell line, filled keycaps = porcelain): built, off until every page takes them
#   motif  the two figure motifs (one najeon chrysanthemum on the lacquer board, the lotus band on the plaque's foot)
THEME = dict(grid='sash', focus='swell', phase2=False, motif=True)
_kit = {}


def kit():
    if 'kit' not in _kit:
        from theme308_kit import Kit
        with open(os.path.join(KITDIR, 'theme308_tokens.json'), encoding='utf-8') as fh:
            tok = json.load(fh)
        _kit['col'] = {k: tuple(int(v[i:i + 2], 16) for i in (1, 3, 5)) for k, v in tok['colours'].items()}
        _kit['sizes'], _kit['limits'] = tok['sizes'], tok['limits']
        _kit['kit'] = Kit(KITDIR)
    return _kit['kit']


def tcol(name):
    """a theme token (theme308_tokens.json): Wood, Lacquer, Porcelain, PorcelainShade, InlayDark, InlayIron, Nacre"""
    kit()
    return _kit['col'][name]


def tsize(name):
    kit()
    return _kit['sizes'][name]

# ---------------------------------------------------------------- type roles (UiStyle304SO): face, px, line-height, tracking em
FACE = {'S9': 'NotoSerifKR-900.ttf', 'S8': 'NotoSerifKR-800.ttf', 'S6': 'NotoSerifKR-600.ttf',
        'N4': 'NotoSansKR-Regular.ttf', 'N7': 'NotoSansKR-Bold.ttf'}
ROLE = {
    'speaker60': ('S9', 60, 1.1, 0), 'heading44': ('S9', 44, 1.2, 0), 'chip56': ('S9', 56, 1.1, 0),
    'fig36': ('S8', 36, 1.0, 0), 'title34': ('S8', 34, 1.0, 0), 'title32': ('S8', 32, 1.2, 0),
    'label26': ('S6', 26, 1.25, 0), 'label24': ('S6', 24, 1.25, 0), 'label22': ('S6', 22, 1.25, 0),
    'body24': ('N4', 24, 1.55, 0), 'val24': ('N7', 24, 1.2, 0), 'body22': ('N4', 22, 1.55, 0),
    'meta20': ('N4', 20, 1.35, .02), 'metab20': ('N7', 20, 1.35, .02), 'key16': ('N7', 16, 1.0, 0),
    'hanja28': ('S9', 28, 1.0, 0), 'hanja20': ('S8', 20, 1.35, 0), 'hanja22': ('S8', 22, 1.25, 0),
}
_fonts = {}


def font(face, px):
    k = (face, px)
    if k not in _fonts:
        _fonts[k] = ImageFont.truetype(os.path.join(FONTS, FACE.get(face, face)), px)
    return _fonts[k]


def rfont(role):
    r = ROLE[role] if isinstance(role, str) else role
    return font(r[0], r[1]), r


def advances(s, role, tab=False):
    f, r = rfont(role)
    tr = r[3] * r[1]
    dw = max(f.getlength(d) for d in '0123456789') if tab else 0
    out = []
    for ch in s:
        w = f.getlength(ch)
        out.append((ch, (dw if tab and ch.isdigit() else w) + tr, w))
    return out


def tw(s, role, tab=False):
    f, r = rfont(role)
    if not tab and r[3] == 0:
        return f.getlength(s)
    return sum(a for _, a, _ in advances(s, role, tab))


def baseline(css_top, role):
    """CSS line box (the #304 mockups, Content304AtCss): the content area is centred in a box of line-height x size."""
    f, r = rfont(role)
    asc, desc = f.getmetrics()
    return css_top + (r[2] * r[1] - (asc + desc)) / 2.0 + asc


# ---------------------------------------------------------------- game data (parsed, not typed)
def read(p):
    with open(p, encoding='utf-8') as fh:
        return fh.read()


def le_ints(hexs):
    return [int.from_bytes(bytes.fromhex(hexs[i:i + 8]), 'little') for i in range(0, len(hexs), 8)]


def asset_by_guid(guid):
    for meta in glob.glob(os.path.join(PROJ, '**', '*.asset.meta'), recursive=True):
        if ('guid: ' + guid) in read(meta):
            return meta[:-5]
    raise FileNotFoundError(guid)


def load_data():
    content = read(os.path.join(PROJ, 'Scenes', 'World', 'Main', 'WorldContent_Main.asset'))
    cat = read(asset_by_guid(re.search(r'EquipmentCatalog: \{fileID: \d+, guid: ([0-9a-f]+)', content).group(1)))
    eco = read(asset_by_guid(re.search(r'Economy: \{fileID: \d+, guid: ([0-9a-f]+)', content).group(1)))
    d = {'upgrade_costs': le_ints(re.search(r'UpgradeCosts: ([0-9a-f]+)', cat).group(1)),
         'upgrade_step': float(re.search(r'UpgradeStep: ([\d.]+)', cat).group(1)), 'items': {}, 'order': []}
    for m in re.finditer(r'- Id: (\S+)\s+Name: "([^"]+)"\s+Slot: (\d+)\s+Effect: (\d+)\s+Bonus: ([\d.]+)\s+Price: (\d+)\s+Starter: (\d)', cat):
        name = m.group(2).encode('ascii').decode('unicode_escape')
        d['items'][m.group(1)] = dict(id=m.group(1), name=name, slot=int(m.group(3)), effect=int(m.group(4)),
                                      bonus=float(m.group(5)), price=int(m.group(6)), starter=m.group(7) == '1')
        d['order'].append(m.group(1))
    d['stone_costs'] = le_ints(re.search(r'StoneCosts: ([0-9a-f]+)', eco).group(1))
    d['cap_costs'] = le_ints(re.search(r'CapacityCosts: ([0-9a-f]+)', eco).group(1))
    d['stone_bonus'] = [float(x) for x in re.findall(r'- ([\d.]+)', eco.split('StoneTotalBonuses:')[1].split('CapacityCosts')[0])]
    d['cap_bonus'] = [float(x) for x in re.findall(r'- ([\d.]+)', eco.split('CapacityTotalBonuses:')[1])]
    cfg = read(os.path.join(PROJ, 'Scripts', 'Combat', 'CombatConfigSO.cs'))
    d['max_hp'] = float(re.search(r'_playerMaxHp = ([\d.]+)f', cfg).group(1))
    d['ink_regen'] = float(re.search(r'_inkRegenPerSecond = ([\d.]+)f', cfg).group(1))
    for p in glob.glob(os.path.join(PROJ, 'Art', '**', '*Combat*.asset'), recursive=True):      # every serialized config agrees
        t = read(p)
        m1, m2 = re.search(r'_playerMaxHp: ([\d.]+)', t), re.search(r'_inkRegenPerSecond: ([\d.]+)', t)
        if (m1 and float(m1.group(1)) != d['max_hp']) or (m2 and float(m2.group(1)) != d['ink_regen']):
            print('note: combat config differs in', os.path.relpath(p, ROOT))
    col = read(os.path.join(PROJ, 'Scripts', 'App', 'World', 'UI', 'WorldMacroCollectionCatalog.cs'))
    elem = ['Wood', 'Fire', 'Earth', 'Metal', 'Water']
    d['spells'] = [dict(id=m.group(1), letter=m.group(2), elem=elem.index(m.group(3)), desc=m.group(4))
                   for m in re.finditer(r'Spell\("(\w+)","(.)",SpellKind\.\w+,Element\.(\w+),"([^"]+)"', col)]
    d['bundles'] = [dict(id=m.group(1), title=m.group(2), spells=re.findall(r'"(\w+)"', m.group(3)))
                    for m in re.finditer(r'Bundle\("(\w+)","([^"]+)","\w+",new Vector3\([^)]*\)((?:,"\w+")+)\)', col)]
    assert len(d['items']) == 8 and len(d['spells']) == 20 and len(d['bundles']) == 5, 'data parse'
    return d


DATA = load_data()
INK_SCALE = 100.0          # EquipmentScreen308SO.DefaultInkDisplayScale (display only, question 5)
SLOT_NAMES = ['붓', '머리', '몸', '손', '발', '장신구']
SLOT_ART = ['brush', 'head', 'body', 'hands', 'feet', 'accessory']
SLOT_KIND = ['붓', '의복 · 머리', '의복 · 몸', '의복 · 손', '의복 · 발', '장신구']
HANJA, ENAME = '木火土金水', '목화토금수'
ELEM_ART = ['wood', 'fire', 'earth', 'metal', 'water']
VIRTUES, VREAD = '仁禮義智信', '인예의지신'
VNAMES = ['인 · 목', '예 · 화', '의 · 금', '지 · 수', '신 · 토']        # 차패 page names (PlaytestUiRoot.Collections.cs)
ARTISAN = '목공 장인'                                                # Content304Artisan
EFFECT = ['모든 속성 위력', '최대 먹', '최대 체력', '목 속성 위력']     # EquipmentEffect order: AllDamage, MaxInk, MaxHealth, WoodDamage

# the sample player (the only values that are not game data)
SAVE = dict(
    currency=1240, drop=0,
    owned={'old_brush': 0, 'old_robe': 0, 'pine_brush': 1, 'linen_head': 0, 'work_robe': 0, 'work_wrap': 0, 'straw_boot': 0, 'wood_bead': 0},
    equipped=['pine_brush', 'linen_head', 'work_robe', None, 'straw_boot', 'wood_bead'],   # Brush, Head, Body, Hands, Feet, Accessory
    levels=[2, 1, 0, 0, 1, 1, 0],                                                          # 木 火 土 金 水 체력 먹
    bundles=['start', 'exit', 'inn'], virtues=['仁'], ren_used=False, hp01=.73, ink01=.68,
)


def pct(v):
    return '%d' % int(round(v * 100 + 1e-6))


def signed(v):
    return ('+' if v >= -1e-9 else '-') + pct(abs(v))


def coins(n):
    return '조선통보 ' + format(n, ',')


def josa(word, a, b):
    c = ord(word[-1]) - 0xAC00
    return a if 0 <= c < 11172 and c % 28 != 0 else b


def gear_bonus(i):
    it = DATA['items'][i]
    return it['bonus'] + SAVE['owned'].get(i, 0) * DATA['upgrade_step']


def gear_name(i):
    lv = SAVE['owned'].get(i, 0)
    return DATA['items'][i]['name'] + (' +%d' % lv if lv > 0 else '')


def gear_effect(i):
    return EFFECT[DATA['items'][i]['effect']] + ' +' + pct(gear_bonus(i)) + '%'


def stone_bonus(track):
    lv = SAVE['levels'][track]
    table = DATA['stone_bonus'] if track < 5 else DATA['cap_bonus']
    return 0.0 if lv == 0 else table[lv - 1]


def stats():
    """RuntimePlayerStats: 1 + equipment (all / wood / hp / ink) + the track's total bonus."""
    allb = wood = hp = ink = 0.0
    for i in SAVE['equipped']:
        if not i:
            continue
        b, e = gear_bonus(i), DATA['items'][i]['effect']
        if e == 0:
            allb += b
        elif e == 1:
            ink += b
        elif e == 2:
            hp += b
        else:
            wood += b
    dmg = [1 + allb + (wood if k == 0 else 0) + stone_bonus(k) for k in range(5)]
    return dict(dmg=dmg, hp=1 + hp + stone_bonus(5), ink=1 + ink + stone_bonus(6))


def known_letters():
    ids = [s for b in DATA['bundles'] if b['id'] in SAVE['bundles'] for s in b['spells']]
    return [sp['letter'] for i in ids for sp in DATA['spells'] if sp['id'] == i]


# ---------------------------------------------------------------- image helpers
_cache = {}


def alpha_of(path):
    if path not in _cache:
        im = Image.open(path)
        _cache[path] = im.getchannel('A') if im.mode == 'RGBA' else im.convert('L')
    return _cache[path]


def amask(name):
    for base in (AS, os.path.join(TEX, 'content')):
        p = os.path.join(base, name + '.png')
        if os.path.exists(p):
            return alpha_of(p)
    raise FileNotFoundError(name)


def arr_mask(a):
    return Image.fromarray((np.clip(a, 0, 1) * 255 + .5).astype(np.uint8), 'L')


MARKS = {name: arr_mask(A308.build_mark(fn, seed)) for name, fn, seed in A308.MARKS}      # the staged textures, pixel for pixel
FRAME = arr_mask(A308.build_frame())


class Screen:
    def __init__(self, w=W, h=H, bg=VEIL):
        self.im = Image.new('RGBA', (w, h), bg + (255,))
        # measurement records (they never change a pixel): text boxes, themed rectangles, nacre area in px2
        self.texts, self.marks, self.nacre, self.notext = [], [], 0.0, False
        self.rings, self.noring = [], False      # cinnabar focus frames drawn (and "leave them out": the ground under a ring)

    # ---- compositing
    def blit(self, layer, x, y):
        x, y = int(round(x)), int(round(y))
        sx, sy = max(0, -x), max(0, -y)
        if sx >= layer.width or sy >= layer.height or x >= self.im.width or y >= self.im.height:
            return
        self.im.alpha_composite(layer, dest=(max(0, x), max(0, y)), source=(sx, sy, layer.width, layer.height))

    def mask(self, a, color, x, y, op=1.0):
        if op != 1.0:
            a = a.point(lambda v: int(v * op + .5))
        layer = Image.new('RGBA', a.size, color + (255,))
        layer.putalpha(a)
        self.blit(layer, x, y)

    def sprite(self, a, color, x, y, w, h, op=1.0, rot=0.0, flip=False):
        """white+alpha mask stretched to w x h, tinted; rot in CSS degrees (clockwise) about the box centre"""
        a = a.resize((max(1, int(round(w))), max(1, int(round(h)))), Image.LANCZOS)
        if flip:
            a = a.transpose(Image.FLIP_LEFT_RIGHT)
        if rot:
            r = a.rotate(-rot, resample=Image.BICUBIC, expand=True)
            x, y = x + (a.width - r.width) / 2.0, y + (a.height - r.height) / 2.0
            a = r
        self.mask(a, color, x, y, op)

    def rgba(self, im, x, y, w, h, op=1.0):
        im = im.resize((int(round(w)), int(round(h))), Image.LANCZOS)
        if op != 1.0:
            im = im.copy()
            im.putalpha(im.getchannel('A').point(lambda v: int(v * op + .5)))
        self.blit(im, x, y)

    def rect(self, x, y, w, h, color, op=1.0):
        self.mask(Image.new('L', (int(round(w)), int(round(h))), 255), color, x, y, op)

    # ---- #308 theme kit (float RGBA from theme308_kit, laid out the way Unity's Image lays a sprite out)
    def kblit(self, rgba, x, y, cls=None):
        """a kit sprite; cls = 'structure' | 'ornament' | ... records its box for the 8 / 16 px distance rules. Shell
        pixels (bright on lacquer) are added to the screen's nacre area for the baked cells."""
        im = Image.fromarray((np.clip(rgba, 0, 1) * 255 + .5).astype(np.uint8), 'RGBA')
        self.blit(im, x, y)
        if cls:
            self.marks.append((cls, x, y, im.width, im.height))
        return im

    def nacre_add(self, rgba):
        lum = .2126 * rgba[..., 0] + .7152 * rgba[..., 1] + .0722 * rgba[..., 2]
        self.nacre += float((rgba[..., 3] * (lum > .45)).sum())

    def trect(self, x, y, w, h, color, cls='structure', op=1.0):
        self.rect(x, y, w, h, color, op)
        self.marks.append((cls, x, y, w, h))

    def band(self, x, y, w, h, ox, oy, inn, color, cls=None):
        """a hollow band round the box (x, y, w, h): from ox / oy px outside its edge to `inn` px inside (four rectangles)"""
        tx, ty = ox + inn, oy + inn
        for r in ((x - ox, y - oy, w + 2 * ox, ty), (x - ox, y + h - inn, w + 2 * ox, ty),
                  (x - ox, y - oy + ty, tx, h + 2 * oy - 2 * ty), (x + w - inn, y - oy + ty, tx, h + 2 * oy - 2 * ty)):
            self.rect(r[0], r[1], r[2], r[3], color)
            if cls:
                self.marks.append((cls,) + r)

    def frame9(self, a, border, color, x, y, w, h, op=1.0):
        """Unity Sliced with pixelsPerUnitMultiplier 1: corners 1:1, edges stretched"""
        b, n = border, a.width
        out = Image.new('L', (w, h), 0)
        xs, ys = [(0, b, 0, b), (b, n - b, b, w - b), (n - b, n, w - b, w)], [(0, b, 0, b), (b, n - b, b, h - b), (n - b, n, h - b, h)]
        for sx0, sx1, dx0, dx1 in xs:
            for sy0, sy1, dy0, dy1 in ys:
                out.paste(a.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.LANCZOS), (dx0, dy0))
        self.mask(out, color, x, y, op)

    # ---- text
    def text_bl(self, x, bl, s, role, color, anchor='l', op=1.0, tab=False, rub=False):
        f, r = rfont(role)
        asc, desc = f.getmetrics()
        adv = advances(s, role, tab)
        width = sum(a for _, a, _ in adv)
        x = x - width if anchor == 'r' else x - width / 2.0 if anchor == 'c' else x
        pad = 4
        m = Image.new('L', (int(width) + 2 * pad + 2, asc + desc + 2 * pad), 0)
        dr = ImageDraw.Draw(m)
        fx = x - int(np.floor(x))
        if not tab and r[3] == 0:
            dr.text((pad + fx, pad + asc), s, font=f, fill=255, anchor='ls')
        else:
            cx = pad + fx
            for ch, a, w0 in adv:
                dr.text((cx + (a - r[3] * r[1] - w0) / 2.0, pad + asc), ch, font=f, fill=255, anchor='ls')
                cx += a
        if rub:      # 탁본: the glyph times rubbing_mask (TMP Face Texture), tile 360 px
            t = amask('rubbing_mask').resize((360, 360), Image.LANCZOS)
            tile = Image.new('L', m.size)
            for ty in range(0, m.height, 360):
                for tx in range(0, m.width, 360):
                    tile.paste(t, (tx, ty))
            m = Image.fromarray((np.asarray(m, np.float32) * np.asarray(tile, np.float32) / 255.0 + .5).astype(np.uint8), 'L')
        ox, oy = int(np.floor(x)) - pad, int(round(bl)) - asc - pad
        box = m.point(lambda v: 255 if v >= 128 else 0).getbbox()
        if box:      # record: class, colour, the ink box (measured) and the CSS line box (the layout table's "글 상자")
            line = r[1] * r[2]
            self.texts.append(dict(role=role if isinstance(role, str) else '%s%d' % (r[0], r[1]), color=color, op=op, text=s, rub=rub,
                                   ink=(ox + box[0], oy + box[1], box[2] - box[0], box[3] - box[1]),
                                   line=(x, bl - asc - (line - (asc + desc)) / 2.0, width, line)))
        if not self.notext:
            self.mask(m, color, ox, oy, op)
        return width

    def text(self, x, y, s, role, color, anchor='l', op=1.0, tab=False):
        return self.text_bl(x, baseline(y, role), s, role, color, anchor, op, tab)

    def runs(self, x, bl, parts, anchor='l'):
        """[(text, role, colour, tabular)] on one baseline"""
        total = sum(tw(p[0], p[1], p[3] if len(p) > 3 else False) for p in parts)
        cx = x - total if anchor == 'r' else x
        for p in parts:
            cx += self.text_bl(cx, bl, p[0], p[1], p[2], tab=p[3] if len(p) > 3 else False)
        return total

    def wrap(self, x, y, s, role, color, width):
        """어절 단위 줄바꿈 (word-break: keep-all); returns the bottom y"""
        _, r = rfont(role)
        line = ''
        for word in s.split(' '):
            trial = (line + ' ' + word).strip()
            if line and tw(trial, role) > width:
                self.text(x, y, line, role, color)
                y += r[1] * r[2]
                line = word
            else:
                line = trial
        self.text(x, y, line, role, color)
        return y + r[1] * r[2]

    # ---- #304 components
    def stroke(self, name, color, x, y, w, h, op=1.0, rot=0.0, flip=False):
        self.sprite(amask(name), color, x, y, w, h, op, rot, flip)

    def dab(self, x, y, w=26, h=20, color=CIN, op=1.0):
        if self.noring and color == CIN:      # the focus 방점 is part of the focus mark, not of the ground under the ring
            return
        self.sprite(amask('dab'), color, x, y, w, h, op, rot=-14)

    def steps(self, x, y, level, maximum, w=28, h=21, pitch=36, on=PAPER, off=PENDING):
        if TH:      # 강화 단계 = 자개 꽃잎 (Pip.Petal): reached = the baked shell, not reached = the same petal in Mist a.35
            k = 1.0 if h <= 14 else 1.5 if h <= 17 else 2.0          # 7 x 10 px petal at 1x / 1.5x / 2x (texel-exact at 1 and 2)
            pet, ghost = kit().simple('pip_petal', k), kit().simple('pip_petal', k, tint=np.array(MIST) / 255.0)
            ghost[..., 3] *= .35
            for j in range(maximum):
                px, py = x + j * pitch + (w - pet.shape[1]) / 2.0, y + (h - pet.shape[0]) / 2.0
                self.kblit(pet if j < level else ghost, px, py, 'symbol' if j < level else None)
                if j < level:
                    self.nacre_add(pet)
            return
        for k in range(maximum):
            self.dab(x + k * pitch, y, w, h, on if k < level else off)

    def keycap(self, x, y, label, hollow=False):
        """sm keycap 34: the one crisp rectilinear element (2 px ink edge, 4 px lip)"""
        h = 34
        w = max(34, int(round(tw(label, 'key16'))) + 20)
        if TH and TH.get('phase2') and not hollow:      # phase 2 (menu-wide): the filled key = porcelain button (Key.Porcelain)
            self.kblit(kit().rect('keycap_porcelain', w, h), x, y)
            self.text(x + w / 2.0, y + 6, label, 'key16', tcol('InlayDark'), anchor='c')
            return w
        if hollow:
            self.rect(x, y, w, h, PAPER)
            self.rect(x + 2, y + 2, w - 4, h - 4, VEIL)
            self.rect(x + 2, y + h - 6, w - 4, 4, mix(VEIL, PAPER, .28))
            col = PAPER
        else:
            self.rect(x, y, w, h, INK)
            self.rect(x + 2, y + 2, w - 4, h - 4, PAPER)
            self.rect(x + 2, y + h - 6, w - 4, 4, KEYLIP)
            col = INK
        self.text(x + w / 2.0, y + 7, label, 'key16', col, anchor='c')
        return w

    def hint(self, x, y, key, label, role='label22', color=PAPER, hollow=False):
        """건반 + 동사구 (V.Hint / the map's control row): row height 48"""
        kw = self.keycap(x, y + 7, key, hollow)
        lw = self.text(x + kw + 10, y + (48 - ROLE[role][1] * ROLE[role][2]) / 2.0, label, role, color)
        return kw + 10 + lw

    def chip(self, x, y, size=104, op=1.0):
        """찢긴 한지 칩. Above 104 the tile is 9-sliced (border 24 of 128 at the 104 scale) so the torn edge stays crisp."""
        tile = Image.open(os.path.join(AS, 'tile_chip.png')).convert('RGBA')
        if size <= 104:
            self.rgba(tile, x, y, size, size, op)
            return
        b, n, db = 24, tile.width, 20
        out = Image.new('RGBA', (size, size), (0, 0, 0, 0))
        cuts = [(0, b, 0, db), (b, n - b, db, size - db), (n - b, n, size - db, size)]
        for sx0, sx1, dx0, dx1 in cuts:
            for sy0, sy1, dy0, dy1 in cuts:
                out.paste(tile.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.LANCZOS), (dx0, dy0))
        self.rgba(out, x, y, size, size, op)

    def gear(self, slot, x, y, size, ghost=False):
        key = ('gear', slot, size, ghost)
        if key not in _cache:
            im = Image.open(os.path.join(EQART, SLOT_ART[slot] + '-v1.png')).convert('RGBA').resize((size, size), Image.LANCZOS)
            if ghost:      # 빈 칸의 부위 실루엣: the art's alpha tinted paper, a.2
                g = Image.new('RGBA', im.size, PAPER + (255,))
                lum = np.asarray(im.convert('L'), np.float32) / 255.0
                a = np.asarray(im.getchannel('A'), np.float32) * (1.0 - lum * .55) * .2
                g.putalpha(Image.fromarray(a.astype(np.uint8), 'L'))
                im = g
            _cache[key] = im
        self.blit(_cache[key], x, y)

    def stamp(self, e, x, y, size, color, hanja_px=None):
        """오행 형상 도장 + 한자 (no five-colour fill: DESIGN §2.5)"""
        self.sprite(amask('elem_' + ELEM_ART[e]), color, x, y, size, size)
        if hanja_px:
            role = ('S9', hanja_px, 1.0, 0)
            self.text(x + size / 2.0, y + (size - hanja_px) / 2.0 + (size * .04 if e == 1 else 0), HANJA[e], role, color, anchor='c')

    def seal(self, x, y, size, color):
        self.sprite(amask('seal_frame'), color, x, y, size, size)

    def cell_focus(self, x, y, w, h, color=CIN, op=1.0):
        """four short brush strokes crossing 12 px past the corners (build.py cell_focus)"""
        if color == CIN:
            self.rings.append((x, y, w, h))
            if self.noring:
                return
        a = amask('stroke_short')
        self.sprite(a, color, x - 12, y - 14, w + 26, 22, op)
        self.sprite(a, color, x - 8, y + h - 6, w + 24, 22, op, flip=True)
        self.sprite(a, color, x - (h + 20) / 2.0 - 2, y + h / 2.0 - 10, h + 20, 20, op, rot=90)
        self.sprite(a, color, x + w - (h + 20) / 2.0 + 2, y + h / 2.0 - 10, h + 20, 20, op, rot=-90)

    def mark(self, name, x, y, color=MIST, size=24):
        self.sprite(MARKS[name], color, x, y, size, size)

    def head(self, x, y, mark, label, extra=None):
        """절 머리: 먹 그림 기호 24 + 메타 20 (the pictogram never stands without its word)"""
        self.mark(mark, x, y - 1)
        w = self.text(x + 32, y, label, 'meta20', MIST)
        if extra:
            self.text(x + 32 + w + 10, y, extra, 'meta20', MIST)


# ---------------------------------------------------------------- the page
GX, GY, PX, PY, CS = 96, 316, 124, 156, 104           # grid origin, pitches, chip (Spec §10)
C0, C1, C2, R0, RR = 776, 1104, 1404, 1476, 1856      # centre column, its right sub-column, its end, status column, right edge
TABS = ['일시정지', '소지품', '술식', '차패', '지도', '설정', '조작']
FLAT_WORLD = None      # a colour = a flat world under the veil (measurement only)

# #308 theme geometry (Spec §10 "테마"): the panes sit IN the bars, so the pitch is chip + bar
ROWS = (2, 4, 5, 5)
TG = dict(GX=102, GY=308, PX=108, PY=160, BAR=4, FRAME=6, NAME=118, BOARD=(2, 0, 0, 0))     # 'sash': one 교창 per row
TLEAF = dict(GX=102, GY=308, PX=108, PY=153, BAR=4, RAIL=3, FRAME=6, NAME=115)              # 'leaf' (alternative, not picked)
# focus bed (한지 band) and the rect the cinnabar frame is laid on (x offset, width change), per cell size. Measured ink
# (alpha >= .5) of the four strokes: pane 104 with (2, -6) = 10 px left, 12 px right, 10 px up / down, 6 px inside;
# row chip 56 with (1, -2) = 11 / 15 / 10 / 10, 3 px inside. The bed is 2 px wider than the ink on every side, so every
# stroke end lies on 한지 and no bar, no lattice and no dark pane touches the ring. Kept moat: 8 px of lacquer.
BED = {104: dict(x=14, y=12, inn=8, ring=(2, -6)), 56: dict(x=17, y=12, inn=5, ring=(1, -2))}
MOAT = 8


def grid_geo():
    """origin, pitches and the name's dy of the slot grid in the render in progress"""
    if TH and TH['grid'] == 'sash':
        return TG['GX'], TG['GY'], TG['PX'], TG['PY'], TG['NAME']
    if TH and TH['grid'] == 'leaf':
        return TLEAF['GX'], TLEAF['GY'], TLEAF['PX'], TLEAF['PY'], TLEAF['NAME']
    return GX, GY, PX, PY, 110


def frame_page(s, at_shop, fragment_tab):
    """world + the one-stroke veil (lift x1960) + rail + sub-tabs + 조선통보: unchanged #304 scaffold"""
    if FLAT_WORLD is None:
        s.blit(Image.open(WORLD).convert('RGBA').resize((W, H), Image.LANCZOS), 0, 0)
    else:           # measurement: the worst world under the veil (DESIGN 2.3: the brightest sky)
        s.rect(0, 0, W, H, FLAT_WORLD)
    veil = Image.open(os.path.join(AS, 'veil_wash.png')).convert('L').crop((40, 0, 40 + W, H))
    s.mask(veil, VEIL, 0, 0)
    s.stroke('stroke_sweep', INK, -380, 640, 1700, 620, .3, rot=-8)
    # rail (slot widths fixed at the selected size, MenuRail304)
    if TH and TH.get('phase2'):      # phase 2 (menu-wide): one cut-shell line (Inlay.Line, 1792 = 14 x 128 px: whole pieces)
        line = kit().strip('line_najeon', 1792)
        s.kblit(line, 64, 112, 'rail')
        s.nacre_add(line)
    else:
        s.stroke('stroke_line', PAPER, 64, 106, 1792, 16, .45)
    x = 64
    x += s.keycap(x, 60, 'Q') + 44
    for t in TABS + (['정비'] if at_shop else []):
        slot = tw(t, 'title34')
        if t == '소지품':
            s.stroke('stroke_swell', PAPER, x - 26, 86, slot + 56, 36, .95)
            s.text_bl(x, 90, t, 'title34', PAPER)
        else:
            s.text_bl(x, 90, t, 'label24', MIST)
        x += slot + 44
    x += s.keycap(x, 60, 'E') + 40
    x += s.keycap(x, 60, 'Esc') + 10
    s.text_bl(x, 86, '닫기', 'meta20', MIST)
    # sub-tabs (baseline 189): 장비 n / 석경 n
    held = len(known_letters())
    x = 64
    for name, n, sel in (('장비', len(SAVE['owned']), not fragment_tab), ('석경', held, fragment_tab)):
        role = 'title32' if sel else 'label24'
        lw = tw(name, role)
        if sel:
            s.stroke('stroke_swell', PAPER, x - 20, 192, max(150, lw + 6 + tw(str(n), 'meta20') + 44), 30, .9)
        s.text_bl(x, 189, name, role, PAPER if sel else MIST)
        nw = s.text_bl(x + lw + 6, 189, str(n), 'meta20', MIST)
        x += lw + 6 + nw + 44
    # 조선통보 (right x1856, y160): coin + word + figure
    bl = baseline(160, 'fig36')
    vw = s.text_bl(RR, bl, format(SAVE['currency'], ','), 'fig36', PAPER, anchor='r', tab=True)
    mw = s.text_bl(RR - vw - 14, bl, '조선통보', 'meta20', MIST, anchor='r')
    s.sprite(amask('coin'), MIST, RR - vw - 14 - mw - 10 - 26, bl - 21, 26, 26)
    # column rules
    if TH:          # 용자살: the three columns are one door leaf, the rules are its bars (White x Wood, 3 px)
        s.trect(739, 232, 3, 708, tcol('Wood'))
        s.trect(1439, 232, 3, 708, tcol('Wood'))
        return
    s.rect(740, 232, 1, 708, PAPER, .14)
    s.rect(1440, 232, 1, 708, PAPER, .14)


def slot_cells():
    """16 칸: (kind, index, x, y)"""
    GX, GY, PX, PY, _ = grid_geo()
    cells = [('gear', 0, GX, GY), ('gear', 5, GX + PX, GY)]
    cells += [('gear', 1 + i, GX + PX * i, GY + PY) for i in range(4)]
    cells += [('stone', i, GX + PX * i, GY + PY * 2) for i in range(5)]
    cells += [('virtue', i, GX + PX * i, GY + PY * 3) for i in range(5)]
    return cells


# ---------------------------------------------------------------- #308 theme: lattice windows, panes, the two states
def pane(s, x, y, kind='paper', size=CS):
    """a pane seated in the bars. paper = flat 창호지 to the bar edge + the chip's grain on it (the torn edge has the flat
    value, so nothing floats); empty = the same paper at a.16 on the veil; locked = no paper, bare 빗살 (Lattice.Bit)"""
    if kind == 'paper':
        s.rect(x, y, size, size, CHIP)
        s.chip(x, y, size)
    elif kind == 'empty':
        s.rect(x, y, size, size, mix(VEIL, CHIP, .16))
    else:
        m = kit().lattice('bit', size, size, 1.0)
        s.mask(Image.fromarray((m * 255 + .5).astype(np.uint8), 'L'), tcol('Wood'), x, y)
        s.marks.append(('structure', x, y, size, size))


def sash(s, x, y, n, board=0):
    """one 교창 (transom window): a plain frame round n panes, a bar between panes, `board` pane widths of lacquer board"""
    F, B, P, wood = TG['FRAME'], TG['BAR'], TG['PX'], tcol('Wood')
    w = (n + board) * P - B
    s.trect(x - F, y - F, w + 2 * F, F, wood)
    s.trect(x - F, y + CS, w + 2 * F, F, wood)
    s.trect(x - F, y, F, CS, wood)
    s.trect(x + w, y, F, CS, wood)
    for i in range(1, n + (1 if board else 0)):
        s.trect(x + P * i - B, y, B, CS, wood)
    if board:
        lacquer_board(s, x + P * n, y, board * P - B, CS)


def lacquer_board(s, x, y, w, h, flower=True):
    """궁판: a flat black-lacquer board in the window frame; the screen's one najeon figure (Motif.Chrys) sits in it"""
    s.trect(x, y, w, h, tcol('Lacquer'), 'board')
    if flower and TH.get('motif'):
        ch = kit().simple('motif_chrys', 1.0)
        s.kblit(ch, x + (w - ch.shape[1]) / 2.0, y + (h - ch.shape[0]) / 2.0, 'ornament')
        s.nacre_add(ch)


def leaf_frame(s):
    """alternative B: ONE door leaf. Plain 6 px frame round 5 x 4, paper seated, the names in a band under every row
    (3 px rails), the part of a row that has no 칸 = lacquer board."""
    g, wood = TLEAF, tcol('Wood')
    x0, y0, P, PYL, B, R, F = g['GX'], g['GY'], g['PX'], g['PY'], g['BAR'], g['RAIL'], g['FRAME']
    w, h = 5 * P - B, 4 * PYL - R
    s.trect(x0 - F, y0 - F, w + 2 * F, F, wood)
    s.trect(x0 - F, y0 + h, w + 2 * F, F, wood)
    s.trect(x0 - F, y0, F, h, wood)
    s.trect(x0 + w, y0, F, h, wood)
    for r, n in enumerate(ROWS):
        y = y0 + r * PYL
        for i in range(1, n + (1 if n < 5 else 0)):
            s.trect(x0 + P * i - B, y, B, CS, wood)
        s.trect(x0, y + CS, w, R, wood)
        if r < 3:
            s.trect(x0, y + PYL - R, w, R, wood)
        if n < 5:
            lacquer_board(s, x0 + P * n, y, (5 - n) * P - B, CS, flower=(r == 0))


def table_bars(s):
    """alternative A (the kit review's first composite): bars in the middle of the 20 px gaps, a 6 px frame that steps
    with the row lengths, torn chips floating 8 px off the bars"""
    BAR_V, BAR_H, FRAME_W, wood = 4, 3, 6, tcol('Wood')
    half = (PX - CS) // 2
    xl = GX - half - BAR_V // 2
    right = lambda n: GX + PX * (n - 1) + CS + half - BAR_V // 2
    ys = [GY + PY * r - half - BAR_H for r in range(len(ROWS))] + [GY + PY * (len(ROWS) - 1) + CS + 39]
    for r, n in enumerate(ROWS):
        for i in range(1, n):
            s.trect(GX + PX * i - half - BAR_V // 2, ys[r], BAR_V, ys[r + 1] - ys[r], wood)
    for k, y in enumerate(ys):
        n = max(ROWS[k - 1] if k > 0 else 0, ROWS[k] if k < len(ROWS) else 0)
        s.trect(xl, y, right(n) + BAR_V - xl, FRAME_W if k in (0, len(ROWS)) else BAR_H, wood)
    s.trect(xl - (FRAME_W - BAR_V), ys[0], FRAME_W, ys[-1] + FRAME_W - ys[0], wood)
    for r, n in enumerate(ROWS):
        y1 = ys[r + 1] + (FRAME_W if r == len(ROWS) - 1 else BAR_H)
        s.trect(right(n), ys[r], FRAME_W, y1 - ys[r], wood)


def focus_mark(s, x, y, w, h):
    """초점 = the strongest mark: a 한지 band masks the bars round the 칸 (12 px out, 4 px in), the cinnabar brush frame
    lies wholly on it"""
    mode = TH['focus'] if TH else 'none'
    if mode == 'none':
        s.cell_focus(x, y, w, h)
        return
    b = BED[w]
    s.band(x, y, w, h, b['x'], b['y'], b['inn'], PAPER if mode == 'swell' else VEIL, 'bed')
    s.cell_focus(x + b['ring'][0], y, w + b['ring'][1], h)


def kept_mark(s, x, y, size=CS):
    """선택 유지 = 자개 끊음질 테 (Frame.Select, rect = 칸 + 10 = 24 + 10 n) in a lacquer moat that masks the bars"""
    if not TH:
        s.cell_focus(x, y, size, size, MIST, .9)
        return
    if TH['grid'] != 'table':
        s.band(x, y, size, size, MOAT, MOAT, 0, tcol('Lacquer'), 'board')
    fr = kit().rect('frame_select', size + 10, size + 10)
    s.kblit(fr, x - 5, y - 5, 'state')
    s.nacre_add(fr)


def themed_grid(s, focus, kept=None):
    mode = TH['grid']
    GX, GY, PX, PY, name_dy = grid_geo()
    seated = mode != 'table'
    if mode == 'sash':
        for r, n in enumerate(ROWS):
            sash(s, GX, GY + PY * r, n, TG['BOARD'][r])
    elif mode == 'leaf':
        leaf_frame(s)
    else:
        table_bars(s)
    for kind, i, x, y in slot_cells():
        foc = focus == (kind, i)
        label_col = PAPER if foc else MIST
        if kind == 'gear':
            worn = SAVE['equipped'][i]
            if seated:
                pane(s, x, y, 'paper' if worn else 'empty')
            else:
                s.chip(x, y, op=1.0 if worn else .16)
            if worn:
                s.gear(i, x + 8, y + 8, 88)
            else:
                s.gear(i, x + 12, y + 12, 80, ghost=True)
            lv = SAVE['owned'][worn] if worn else 0
            s.text(x, y + name_dy, SLOT_NAMES[i] + (' +%d' % lv if lv else ''), 'meta20', label_col)
        elif kind == 'stone':
            pane(s, x, y) if seated else s.chip(x, y)
            s.stamp(i, x + 20, y + 20, 64, INK, 28)
            lw = s.text(x, y + name_dy, ENAME[i], 'meta20', label_col)
            if seated:      # petals 7 px wide at pitch 13: name + petals end 67 px into the 108 px pitch, clear of the next 방점
                s.steps(x + lw + 10, y + name_dy + 7, SAVE['levels'][i], 3, 12, 14, 13)
            else:
                s.steps(x + lw + 10, y + name_dy + 7, SAVE['levels'][i], 3, 18, 14, 19)
        else:
            have = VIRTUES[i] in SAVE['virtues']
            if have:
                pane(s, x, y) if seated else s.chip(x, y)
                s.seal(x + 8, y + 8, 88, INK)
                s.text(x + CS / 2.0, y + 26, VIRTUES[i], ('S9', 46, 1.1, 0), INK, anchor='c')
                if VIRTUES[i] == '仁' and SAVE['ren_used']:
                    s.stroke('stroke_dry', INK, x - 6, y + 40, 116, 24, .8, rot=-8)
            else:           # 잠긴 칸: no paper, bare 빗살 + the 관변 테 in the disabled colour
                if seated:
                    pane(s, x, y, 'locked')
                else:
                    pane(s, x + 8, y + 8, 'locked', 88)
                s.seal(x + 8, y + 8, 88, OFF)
            s.text(x, y + name_dy, VREAD[i], 'meta20', (PAPER if foc else MIST) if have else OFF)
    for kind, i, x, y in slot_cells():      # the two states lie over every bar and every neighbour
        if focus == (kind, i):
            focus_mark(s, x, y, CS, CS)
            s.dab(x - 30, y + name_dy + 3, 24, 18)
        elif kept == (kind, i):
            kept_mark(s, x, y)


def draw_grid(s, focus, kept=None):
    if TH:
        return themed_grid(s, focus, kept)
    for kind, i, x, y in slot_cells():
        foc = focus == (kind, i)
        label_col = PAPER if foc else MIST
        if kind == 'gear':
            worn = SAVE['equipped'][i]
            if worn:
                s.chip(x, y)
                s.gear(i, x + 8, y + 8, 88)
            else:
                s.chip(x, y, op=.16)
                s.gear(i, x + 12, y + 12, 80, ghost=True)
            lv = SAVE['owned'][worn] if worn else 0       # 강화 단계: "+N" after the slot name (the item name's own notation)
            s.text(x, y + 110, SLOT_NAMES[i] + (' +%d' % lv if lv else ''), 'meta20', label_col)
        elif kind == 'stone':
            s.chip(x, y)
            s.stamp(i, x + 20, y + 20, 64, INK, 28)
            lw = s.text(x, y + 110, ENAME[i], 'meta20', label_col)
            s.steps(x + lw + 10, y + 117, SAVE['levels'][i], 3, 18, 14, 19)
        else:
            have = VIRTUES[i] in SAVE['virtues']
            if have:
                s.chip(x, y)
                s.seal(x + 8, y + 8, 88, INK)
                s.text(x + CS / 2.0, y + 26, VIRTUES[i], ('S9', 46, 1.1, 0), INK, anchor='c')
                if VIRTUES[i] == '仁' and SAVE['ren_used']:
                    s.stroke('stroke_dry', INK, x - 6, y + 40, 116, 24, .8, rot=-8)
            else:
                s.seal(x + 8, y + 8, 88, OFF)
            s.text(x, y + 110, VREAD[i], 'meta20', (PAPER if foc else MIST) if have else OFF)
        if foc:
            s.cell_focus(x, y, CS, CS)
            s.dab(x - 30, y + 113, 24, 18)
        elif kept == (kind, i):
            s.cell_focus(x, y, CS, CS, MIST, .9)


def slot_lines(s, kind_text, item_text, item_col=PAPER):
    s.text(96, 232, kind_text, 'meta20', MIST)
    s.text(96, 258, item_text, 'label26', item_col)


def detail_head(s, kind, name, state, level=None, maximum=3, name_col=PAPER):
    s.text(C0, 232, kind, 'meta20', MIST)
    if tw(name, 'speaker60') <= 420:
        s.text(C0 - 4, 258, name, 'speaker60', name_col)
    else:
        s.text(C0 - 2, 270, name, 'heading44', name_col)
    if state:
        s.text(C0, 338, state, 'label26', MIST)
    if level is not None:
        s.steps(C0 + 2, 382, level, maximum)
    s.stroke('stroke_dry', PAPER, 770, 446, 640, 28, .38)


def themed_picture(s, kind, i):
    """백자 상감 판 (Plaque.Porcelain 200): the picture register over a foot register that carries the lotus band
    (Sanggam.Lotus, 10 x 16 px, InlayDark) between an inlaid line and the frame's inner line. Stamp / seal / 한자 = InlayDark."""
    x, y, n = 1204, 232, 200
    ink = tcol('InlayDark')
    s.kblit(kit().rect('plaque_porcelain', n, n), x, y, 'plaque')
    if not TH.get('motif'):
        if kind == 'gear':
            s.gear(i, x + 20, y + 18, 160)
        elif kind == 'stone':
            s.stamp(i, x + 36, y + 34, 128, ink, 58)
        else:
            s.seal(x + 30, y + 28, 140, ink)
            s.text(x + n / 2.0, y + 48, VIRTUES[i], ('S9', 92, 1.1, 0), ink, anchor='c')
        return
    s.trect(x + 13, y + 163, n - 26, 1, ink, 'inlay')
    band = kit().strip('sanggam_lotus', 160, 1.0, tint=np.array(ink) / 255.0)
    s.kblit(band, x + 20, y + 168, 'inlay')
    if kind == 'gear':
        s.gear(i, x + 30, y + 18, 140)
    elif kind == 'stone':
        s.stamp(i, x + 42, y + 30, 116, ink, 52)
    else:
        s.seal(x + 38, y + 26, 124, ink)
        s.text(x + n / 2.0, y + 44, VIRTUES[i], ('S9', 80, 1.1, 0), ink, anchor='c')


def picture(s, kind, i=0, letter=None, empty=False):
    x, y, n = 1204, 232, 200
    if TH and not empty and kind != 'glyph':
        return themed_picture(s, kind, i)
    if empty:
        s.chip(x, y, n, op=.16)
        s.gear(i, x + 24, y + 24, 152, ghost=True)
        return
    if kind == 'glyph':        # 탁본: the empty-chip weight under a paper frame, the glyph in paper through rubbing_mask
        s.chip(x, y, n, op=.16)
        s.frame9(FRAME.resize((256, 256)), A308.FRAME_BORDER, PAPER, x + 6, y + 6, n - 12, n - 12, .5)
        f = ('S9', 138, 1.0, 0)
        s.text_bl(x + n / 2.0, baseline(y + (n - 138) / 2.0, f), letter, f, PAPER, anchor='c', rub=True)
        return
    s.chip(x, y, n)
    s.frame9(FRAME.resize((256, 256)), A308.FRAME_BORDER, INK, x + 6, y + 6, n - 12, n - 12, .5)
    if kind == 'gear':
        s.gear(i, x + 16, y + 16, 168)
    elif kind == 'stone':
        s.stamp(i, x + 36, y + 36, 128, INK, 58)
    elif kind == 'virtue':
        s.seal(x + 30, y + 30, 140, INK)
        s.text(x + n / 2.0, y + 50, VIRTUES[i], ('S9', 92, 1.1, 0), INK, anchor='c')


def section(s, y, mark, head, lines, x=C0, extra=None):
    """one 절: head + value lines [(text, role, colour)], stacked by measured height; returns the bottom y"""
    s.head(x, y, mark, head, extra)
    y += 30
    for text, role, col in lines:
        y = s.wrap(x, y, text, role, col, 300)
    return y


def link_row(s, y, label, meta, enabled=True):
    """연결 줄 (보조 버튼 문법, DESIGN §5.2): Serif 600 26 + 결과 메타 20 + 마른 밑획. 비활성 = Off + 사유 메타"""
    lw = s.text(C0, y, label, 'label26', PAPER if enabled else OFF)
    s.text(C0 + lw + 16, y + 4, meta, 'meta20', MIST)
    if enabled:
        s.stroke('stroke_dry', PAPER, C0 - 8, y + 34, lw + 60, 18, .6)
    return y + 56


def candidates(s, slot, focus_id=None):
    ids = [i for i in SAVE['owned'] if DATA['items'][i]['slot'] == slot]
    ids.sort(key=lambda i: (SAVE['equipped'][slot] != i, DATA['order'].index(i)))
    s.head(C1, 486, 'mark_candidates', '바꿔 낄 것', str(len(ids)))
    for k, i in enumerate(ids):
        y = 524 + k * 76
        cx = C1 + 30
        s.chip(cx, y, 56)
        s.gear(slot, cx + 4, y + 4, 48)
        foc = focus_id == i
        lx = cx + (80 if TH else 72)        # theme: the label stands 8 px further right, clear of the focus bed
        s.text(lx, y - 1, gear_name(i), 'label24', PAPER)
        s.text(lx, y + 29, '착용 중' if SAVE['equipped'][slot] == i else gear_effect(i), 'meta20', PAPER if foc else MIST)
        if foc and TH:
            focus_mark(s, cx, y, 56, 56)
            s.dab(C1 - 6, y + 18)
        elif foc:
            s.cell_focus(cx, y, 56, 56)
            s.dab(C1 - 6, y + 18)
    return ids


def status_column(s):
    st = stats()
    known = known_letters()
    bl = lambda y: baseline(y, 'label24')
    val = lambda y, value: s.text_bl(RR, bl(y), value, 'body24', PAPER, anchor='r', tab=True)
    row = lambda y, name, value: (s.text(R0, y, name, 'label24', PAPER), val(y, value))
    # 몸과 먹
    s.head(R0, 232, 'mark_body', '몸과 먹')
    max_hp = DATA['max_hp'] * st['hp']
    for y, name, cur, top in ((264, '체력', SAVE['hp01'] * max_hp, max_hp), (304, '먹', SAVE['ink01'] * st['ink'] * INK_SCALE, st['ink'] * INK_SCALE)):
        s.text(R0, y, name, 'label24', PAPER)
        s.runs(RR, bl(y), [('%d' % round(cur), 'val24', PAPER), ('  /  ', 'body24', MIST), ('%d' % round(top), 'body24', PAPER)], anchor='r')
    regen = DATA['ink_regen'] * 1.0 * INK_SCALE          # GradeMultiplier is x1 today (nothing calls InkRegenerator.SetGrade)
    row(344, '먹 회복', '초당 ' + ('%g' % round(regen, 1)))
    # 속성 위력
    s.head(R0, 404, 'mark_power', '속성 위력')
    for k in range(5):
        y = 436 + k * 36
        s.stamp(k, R0, y + 3, 24, PAPER)
        s.text_bl(R0 + 36, bl(y), HANJA[k], 'hanja22', PAPER)
        s.text(R0 + 68, y, ENAME[k], 'label24', PAPER)
        val(y, signed(st['dmg'][k] - 1) + '%')
    # 보강
    s.head(R0, 640, 'mark_tier', '보강')
    for y, name, track in ((672, '체력 보강', 5), (708, '먹 용량 보강', 6)):
        s.text(R0, y, name, 'label24', PAPER)
        s.steps(R0 + 164, y + 7, SAVE['levels'][track], 2, 22, 17, 28)
        val(y, '+' + pct(stone_bonus(track)) + '%')
    # 익힌 것
    s.head(R0, 768, 'mark_learned', '익힌 것')
    row(800, '술식', '%d / %d' % (len(known), len(DATA['spells'])))
    s.text(R0, 836, '오덕', 'label24', PAPER)
    for k in range(5):
        s.text_bl(R0 + 164 + k * 26, bl(836), VIRTUES[k], 'hanja20', PAPER if VIRTUES[k] in SAVE['virtues'] else OFF)
    val(836, '%d / 5' % len(SAVE['virtues']))


def legend(s, items):
    x = 96
    for key, label in items:
        x += s.hint(x, 972, key, label) + 34


def fragment_grid(s, ids, sel):
    GX, GY, PX, PY, name_dy = grid_geo()
    seated = TH and TH['grid'] == 'sash'
    if seated:
        for r in range((len(ids) + 4) // 5):
            sash(s, GX, GY + PY * r, min(5, len(ids) - 5 * r))
    hit = None
    for k, fid in enumerate(ids):
        sp = next(z for z in DATA['spells'] if z['id'] == fid)
        x, y = GX + (k % 5) * PX, GY + (k // 5) * PY
        pane(s, x, y) if seated else s.chip(x, y)
        s.text(x + CS / 2.0, y + 19, sp['letter'], 'chip56', INK, anchor='c')
        s.text(x, y + name_dy, sp['letter'] + '의 석경', 'meta20', PAPER)
        if fid == sel:
            hit = (x, y)
    if hit:
        if TH:      # the names are 78 px wide in a 108 px pitch: a smaller 방점 (20 x 15) fits between two names
            focus_mark(s, hit[0], hit[1], CS, CS)
            s.dab(hit[0] - 27, hit[1] + name_dy + 5, 20, 15)
        else:
            s.cell_focus(hit[0], hit[1], CS, CS)
            s.dab(hit[0] - 30, hit[1] + name_dy + 3, 24, 18)


def render(view, at_shop=True, theme=None, measure=False, notext=False, noring=False):
    """theme=None: the #304 look (the first concept sheet). theme=THEME (or a variant of it): the #308 theme.
    measure=True returns the Screen (its records: text boxes, themed rectangles, nacre area) instead of the picture;
    notext=True leaves the glyphs out (the ground under the text), noring=True the cinnabar focus frames (the ground
    under the ring): both for the contrast tables of Tools/Art/equip308_theme.py."""
    global TH
    TH = dict(THEME, **theme) if theme is not None else None
    try:
        s = render_view(view, at_shop, notext, noring)
    finally:
        TH = None
    return s if measure else s.im.convert('RGB')


def render_view(view, at_shop=True, notext=False, noring=False):
    s = Screen()
    s.notext, s.noring = notext, noring
    frag = view == 'fragment'
    frame_page(s, at_shop, frag)
    known = known_letters()
    if view in ('stone', 'stone_off'):
        e = 0
        lv = SAVE['levels'][e]
        draw_grid(s, ('stone', e))
        slot_lines(s, '오행 마석 · ' + ENAME[e], ENAME[e] + ' 마석')
        detail_head(s, '오행 마석', ENAME[e] + ' 마석', '%d단' % lv, lv)
        picture(s, 'stone', e)
        y = section(s, 486, 'mark_effect', '효과', [(ENAME[e] + ' 속성 위력 +' + pct(stone_bonus(e)) + '%', 'title32', PAPER)])
        if lv < 3:
            cost = DATA['stone_costs'][lv]
            y = section(s, y + 28, 'mark_upgrade', '다음 강화',
                        [('%d단 +%s%%' % (lv + 1, pct(DATA['stone_bonus'][lv])), 'body24', PAPER),
                         (coins(cost), 'meta20', MIST if SAVE['currency'] >= cost else OFF)])
        else:
            y = section(s, y + 28, 'mark_upgrade', '다음 강화', [('강화 완료', 'body24', MIST)])
        link_row(s, y + 40, '정비', '오행 마석 · 속성 위력' if at_shop else '쉼터 곁에서만 열린다', at_shop)
        spells = [sp for sp in DATA['spells'] if sp['elem'] == e]
        got = [sp for sp in spells if sp['letter'] in known]
        s.head(C1, 486, 'mark_learned', '익힌 ' + ENAME[e] + ' 술식', '%d / %d' % (len(got), len(spells)))
        for k, sp in enumerate(spells):
            x = C1 + k * 72
            if sp['letter'] in known:
                s.text(x, 524, sp['letter'], 'heading44', PAPER)
            else:
                s.sprite(amask('mukdeung'), PAPER, x + 2, 532, 40, 40, .16)
        legend(s, [('Enter', '정비')] if at_shop else [])
    elif view == 'empty':
        slot = 3
        draw_grid(s, ('gear', slot))
        slot_lines(s, SLOT_KIND[slot], '비어 있음', MIST)
        detail_head(s, SLOT_NAMES[slot], '비어 있음', None, name_col=MIST)
        picture(s, 'gear', slot, empty=True)
        ids = candidates(s, slot)
        legend(s, ([('Enter', '바꿔 끼기')] if ids else []) + [('끌기', '칸에 놓아 착용')])
    elif view == 'candidate':
        slot, cand = 0, 'old_brush'
        worn = SAVE['equipped'][slot]
        draw_grid(s, None, kept=('gear', slot))
        slot_lines(s, SLOT_KIND[slot], gear_name(worn))
        lv = SAVE['owned'][cand]
        detail_head(s, SLOT_NAMES[slot], DATA['items'][cand]['name'], ('+%d 강화' % lv) if lv else '강화 전', lv)
        picture(s, 'gear', slot)
        y = section(s, 486, 'mark_effect', '효과', [(gear_effect(cand), 'title32', PAPER),
                    ('기본 +%s%% · 강화 +%s%%' % (pct(DATA['items'][cand]['bonus']), pct(lv * DATA['upgrade_step'])), 'meta20', MIST)])
        delta = gear_bonus(cand) - gear_bonus(worn)
        wname = DATA['items'][worn]['name']
        y = section(s, y + 28, 'mark_compare', '견줌', [('교체 ' + signed(delta) + '%p', 'val24', PAPER),
                    ('착용 중인 ' + wname + josa(wname, '과', '와') + ' 비교', 'body22', MIST)])
        if lv < len(DATA['upgrade_costs']):
            section(s, y + 28, 'mark_upgrade', '다음 강화', [('+%d 강화' % (lv + 1), 'body24', PAPER),
                    (ARTISAN + '에게서 ' + coins(DATA['upgrade_costs'][lv]), 'meta20', MIST)])
        candidates(s, slot, cand)
        legend(s, [('Enter', '착용'), ('Esc', '칸으로')])
    elif view == 'gear':
        slot = 0
        worn = SAVE['equipped'][slot]
        lv = SAVE['owned'][worn]
        draw_grid(s, ('gear', slot))
        slot_lines(s, SLOT_KIND[slot], gear_name(worn))
        detail_head(s, SLOT_NAMES[slot], DATA['items'][worn]['name'], (('+%d 강화' % lv) if lv else '강화 전') + ' · 착용 중', lv)
        picture(s, 'gear', slot)
        y = section(s, 486, 'mark_effect', '효과', [(gear_effect(worn), 'title32', PAPER),
                    ('기본 +%s%% · 강화 +%s%%' % (pct(DATA['items'][worn]['bonus']), pct(lv * DATA['upgrade_step'])), 'meta20', MIST)])
        if lv < len(DATA['upgrade_costs']):
            section(s, y + 28, 'mark_upgrade', '다음 강화', [('+%d 강화' % (lv + 1), 'body24', PAPER),
                    (ARTISAN + '에게서 ' + coins(DATA['upgrade_costs'][lv]), 'meta20', MIST)])
        else:
            section(s, y + 28, 'mark_upgrade', '다음 강화', [('강화 완료', 'body24', MIST)])
        candidates(s, slot)
        legend(s, [('Enter', '바꿔 끼기'), ('X', '빼기'), ('끌기', '칸에 놓아 착용')])
    elif view == 'virtue':
        i = 0
        have = VIRTUES[i] in SAVE['virtues']
        draw_grid(s, ('virtue', i))
        slot_lines(s, '오덕 · ' + VIRTUES[i], VNAMES[i])
        detail_head(s, '오덕', VNAMES[i], '쓰였다 · 쉼터에서 쉬면 돌아온다' if have and SAVE['ren_used'] else '새겨짐' if have else '새기지 못함',
                    name_col=PAPER if have else MIST)
        picture(s, 'virtue', i)
        link_row(s, 506, '차패', '')
        legend(s, [('Enter', '차패')])
    elif view == 'fragment':
        ids = [x for b in DATA['bundles'] if b['id'] in SAVE['bundles'] for x in b['spells']]
        sel = 'ga'
        if TH:
            fragment_grid(s, ids, sel)
        else:
            for k, fid in enumerate(ids):
                sp = next(z for z in DATA['spells'] if z['id'] == fid)
                x, y = GX + (k % 5) * PX, GY + (k // 5) * PY
                s.chip(x, y)
                s.text(x + CS / 2.0, y + 19, sp['letter'], 'chip56', INK, anchor='c')
                s.text(x, y + 110, sp['letter'] + '의 석경', 'meta20', PAPER)
                if fid == sel:
                    s.cell_focus(x, y, CS, CS)
                    s.dab(x - 30, y + 113, 24, 18)
        sp = next(z for z in DATA['spells'] if z['id'] == sel)
        slot_lines(s, '석경 조각', sp['letter'] + '의 석경')
        detail_head(s, '석경 조각', sp['letter'] + '의 석경 조각', '지닌 수 1')
        picture(s, 'glyph', letter=sp['letter'])
        src = [b['title'] for b in DATA['bundles'] if sel in b['spells']]
        y = section(s, 486, 'mark_learned', '나온 곳', [(t, 'body24', PAPER) for t in src[:3]])
        link_row(s, y + 40, '술식 도감에서 보기', '')
        legend(s, [('Enter', '술식 도감에서 보기')])
    status_column(s)
    return s


# ---------------------------------------------------------------- sheet
def caption(sheet, x, y, tag, title, lines, width):
    d = ImageDraw.Draw(sheet)
    d.text((x, y), tag, font=font('S9', 44), fill=CINL)
    d.text((x + 70, y + 4), title, font=font('S8', 38), fill=PAPER)
    for k, ln in enumerate(lines):
        d.text((x + 70, y + 60 + k * 36), ln, font=font('N4', 25), fill=MIST)


def build_sheet(shots):
    M, G, CAP = 70, 70, 150
    cw = W * 2 + G
    rows = [H, H, H, H, 1480]
    sw, sh = M * 2 + cw, M + 170 + sum(r + CAP + G for r in rows) + 20
    sheet = Image.new('RGB', (sw, sh), (0x1B, 0x1B, 0x1A))
    d = ImageDraw.Draw(sheet)
    d.text((M, M - 10), '오행부 #308  장비·소지품 화면 개편 시안', font=font('S9', 64), fill=PAPER)
    d.text((M, M + 78), 'SPEC-UI-EQUIPMENT-308 (TEST) · D308-12 · 2026-10-04 · 모든 화면 1920x1080, 화면 속 수치는 게임 자료에서 계산 (표본 저장 상태는 맨 아래 칸)',
           font=font('N4', 26), fill=MIST)
    y = M + 170
    x2 = M + W + G

    def put(img, x, yy):
        sheet.paste(img, (x, yy + CAP))
        d.rectangle([x - 1, yy + CAP - 1, x + img.width, yy + CAP + img.height], outline=(0x3A, 0x39, 0x36))

    caption(sheet, M, y, '(a)', '기본 화면: 오행 마석 칸 초점 (목 마석 2단, 쉼터 곁)',
            ['왼쪽 칸 16개(붓·장신구 / 머리·몸·손·발 / 오행 마석 5 / 오덕 5) · 가운데 고른 것의 상세 · 오른쪽 도사 상태 · 아래 조작 범례',
             '초점은 주사 붓 테 4획과 방점 하나. 여기서는 강화하지 않고 정비로 이어진다 (쉼터 곁이라 레일에 정비 탭이 보인다)'], cw)
    put(shots['a'], M, y)
    caption(sheet, x2, y, '(d)', '레퍼런스 (같은 크기)',
            ['제목 줄 = 레일 · 슬롯 격자 = 칸 격자 · 항목 상세 = 가운데 열 · Character Status = 도사 상태 · 조작 안내 = 범례',
             '레벨·지구력·무게·방어/내성·능력치 요구는 우리 자료에 없어 넣지 않았다. "Select slot to equip" 같은 지시문도 없다'], cw)
    put(shots['d'], x2, y)
    y += H + CAP + G
    caption(sheet, M, y, '(b)', '빈 칸 초점: 손 (비어 있음)',
            ['빈 칸은 칩 α.16 + 부위 실루엣. 이름 자리에 "비어 있음", 오른쪽 작은 열에 이 부위에 맞는 가진 장비(바꿔 낄 것 1)',
             '낀 것이 없어 범례에 [X] 빼기가 없다. 손 칸이 비어 모든 속성 위력은 붓의 +6%뿐이다 (오른쪽 열과 같은 계산)'], cw)
    put(shots['b'], M, y)
    caption(sheet, x2, y, '(c)', '석경 탭: 석경 조각 초점',
            ['소모품·재료(마석 원석)·열쇠 물건은 지닐 수 있는 자료가 없다. 장비 말고 지니는 것은 석경 조각뿐이라 그 탭을 그렸다',
             '같은 3단 틀: 왼쪽 글자 칩 5열(칸 격자와 같은 피치와 초점), 가운데 조각 상세(탁본 글자·지닌 수·나온 곳), 오른쪽은 같은 상태 열'], cw)
    put(shots['c'], x2, y)
    y += H + CAP + G
    caption(sheet, M, y, '(f)', '후보 줄 초점: 붓 칸에서 Enter, 닳은 붓을 견줌',
            ['초점이 가운데 열로 가면 붓 칸에는 같은 테가 안개색으로 남는다. 격자 위 두 줄은 지금 낀 것(송연필 +1)을 계속 보인다',
             '효과(기본 + 강화) · 견줌(교체 -6%p) · 다음 강화(값은 카탈로그) · 범례가 [Enter] 착용 / [Esc] 칸으로 바뀐다'], cw)
    put(shots['f'], M, y)
    caption(sheet, x2, y, '(g)', '쉼터 밖의 오행 마석 칸',
            ['정비 줄은 비활성색 + 사유("쉼터 곁에서만 열린다"), 레일에 정비 탭이 없고 범례 줄도 비어 있다',
             '이 화면에서 통보가 줄어드는 길은 없다 (강화·구매는 정비와 장인·상인 창이 맡는다)'], cw)
    put(shots['g'], x2, y)
    y += H + CAP + G
    caption(sheet, M, y, '(h)', '낀 장비 칸 초점: 붓 (송연필 +1)',
            ['효과(기본 + 강화) · 다음 강화(읽기 전용) · 바꿔 낄 것 2(낀 것이 맨 위). 범례 셋: [Enter] 바꿔 끼기 / [X] 빼기 / [끌기] 칸에 놓아 착용',
             '칸을 누르거나 Enter를 치면 초점이 바꿔 낄 것으로 간다. 그동안 다른 칸은 마우스가 지나가도 초점을 가져가지 않는다 (누르면 고른다)'], cw)
    put(shots['h'], M, y)
    caption(sheet, x2, y, '(i)', '오덕 칸 초점: 仁 (새겨짐)',
            ['종류·이름·상태 한 줄과 차패 연결 줄만 둔다. 효과문은 쓰지 않는다 (런타임 자료 없음)',
             '못 새긴 칸은 이름이 안개색, 상태는 "새기지 못함", 그림 자리는 비활성색 테만. 오덕과 자동차 호출은 차패 화면이 맡는다'], cw)
    put(shots['i'], x2, y)
    y += H + CAP + G
    caption(sheet, M, y, '(e)', '확대 200%: 칸 격자 · 상태 열 ((a)의 1080p 화소를 2배로)',
            ['칸 상태: 낀 것 = 한지 칩 + 원화(강화했으면 이름 옆 +N) · 빈 칸 = 옅은 칩 + 실루엣 · 마석 = 형상 도장 + 한자 + 단계 방점 3',
             '오덕: 새김 = 한지 칩 + 관변 테 + 한자, 못 새김 = 비활성색 테만. 발광·그림자·오방색 면 채움 없음'], cw)
    put(shots['e1'], M, y)
    put(shots['e2'], M + shots['e1'].width + G, y)
    nx = M + shots['e1'].width + G + shots['e2'].width + G
    st = stats()
    notes = [
        ('표본 저장 상태 (이 값만 표본, 나머지는 자료)', None),
        ('지닌 장비 8: ' + ', '.join(gear_name(i) for i in SAVE['owned']), None),
        ('낀 것: ' + ' / '.join(SLOT_NAMES[k] + ' ' + (gear_name(i) if i else '없음') for k, i in enumerate(SAVE['equipped'])), None),
        ('오행 마석 단계 木%d 火%d 土%d 金%d 水%d · 체력 보강 %d · 먹 용량 보강 %d' % tuple(SAVE['levels']), None),
        ('석경 묶음 3곳(조각 %d) · 오덕 仁 · 조선통보 %s · 체력 %d%% · 먹 %d%%' % (len(known_letters()), format(SAVE['currency'], ','), SAVE['hp01'] * 100, SAVE['ink01'] * 100), None),
        ('', None),
        ('자료에서 읽은 것', None),
        ('장비 카탈로그(EquipmentTEST.asset): 효과 4종, 기본값, 강화 단계당 +%s%%, 강화 값 %s' % (pct(DATA['upgrade_step']), ' / '.join(map(str, DATA['upgrade_costs']))), None),
        ('경제(Economy_TEST.asset): 마석 누적 %s, 값 %s · 보강 누적 %s' % (' / '.join('+' + pct(b) + '%' for b in DATA['stone_bonus']), ' / '.join(map(str, DATA['stone_costs'])), ' / '.join('+' + pct(b) + '%' for b in DATA['cap_bonus'])), None),
        ('전투 설정(CombatConfigSO): 기본 최대 체력 %d, 먹 회복 초당 용량의 %g (먹 눈금 100은 표시 전용)' % (DATA['max_hp'], DATA['ink_regen']), None),
        ('술식 20 · 석경 묶음 5 (WorldMacroCollectionCatalog.cs)', None),
        ('', None),
        ('오른쪽 열의 계산 (RuntimePlayerStats와 같은 식)', None),
        ('최대 체력 %d x %.2f = %d · 먹 용량 100 x %.2f = %d' % (DATA['max_hp'], st['hp'], round(DATA['max_hp'] * st['hp']), st['ink'], round(st['ink'] * 100)), None),
        ('木 = 1 + 붓 %s%% + 염주 %s%% + 마석 %s%% → %s%%' % (pct(gear_bonus('pine_brush')), pct(gear_bonus('wood_bead')), pct(stone_bonus(0)), signed(st['dmg'][0] - 1)), None),
    ]
    ny = y + CAP + 10
    for text, _ in notes:
        head = text.endswith('자료)') or text in ('자료에서 읽은 것', '오른쪽 열의 계산 (RuntimePlayerStats와 같은 식)')
        f = font('S8', 30) if head else font('N4', 24)
        # wrap to the notes column
        line = ''
        for word in text.split(' '):
            trial = (line + ' ' + word).strip()
            if line and f.getlength(trial) > sw - M - nx:
                d.text((nx, ny), line, font=f, fill=PAPER if head else MIST)
                ny += 36
                line = word
            else:
                line = trial
        d.text((nx, ny), line, font=f, fill=PAPER if head else MIST)
        ny += 48 if head else 40
    return sheet


def main():
    os.makedirs(OUT, exist_ok=True)
    shots = {'a': render('stone'), 'b': render('empty'), 'c': render('fragment'), 'f': render('candidate'), 'g': render('stone_off', at_shop=False),
             'h': render('gear'), 'i': render('virtue')}
    shots['d'] = Image.open(REF).convert('RGB').resize((W, H), Image.LANCZOS)
    shots['e1'] = shots['a'].crop((56, 222, 716, 934)).resize((1320, 1424), Image.LANCZOS)
    shots['e2'] = shots['a'].crop((1456, 140, 1876, 880)).resize((840, 1480), Image.LANCZOS)
    wide = Image.new('RGB', (1728, 1080), VEIL)       # 16:10: the page as a whole x .90, the veil covers the bands
    wide.paste(shots['a'].resize((1728, 972), Image.LANCZOS), (0, 54))
    names = {'a': 'equip308_a_stone', 'b': 'equip308_b_empty', 'c': 'equip308_c_fragment', 'd': 'equip308_d_ref', 'f': 'equip308_f_candidate',
             'g': 'equip308_g_offshelter', 'h': 'equip308_h_gear', 'i': 'equip308_i_virtue', 'e1': 'equip308_e_grid_200', 'e2': 'equip308_e_status_200'}
    for k, n in names.items():
        shots[k].save(os.path.join(OUT, n + '.png'))
    wide.save(os.path.join(OUT, 'equip308_a_stone_16x10.png'))
    sheet = build_sheet(shots)
    sheet.save(os.path.join(OUT, 'equip308_mock_sheet.png'))
    sheet.resize((sheet.width // 2, sheet.height // 2), Image.LANCZOS).save(os.path.join(OUT, 'equip308_mock_sheet_half.jpg'), quality=90)
    print('sheet', sheet.size, '->', os.path.join(OUT, 'equip308_mock_sheet.png'))
    st = stats()
    print('status: hp max', round(DATA['max_hp'] * st['hp']), 'ink max', round(st['ink'] * INK_SCALE), 'dmg', [signed(v - 1) for v in st['dmg']])


if __name__ == '__main__':
    main()
