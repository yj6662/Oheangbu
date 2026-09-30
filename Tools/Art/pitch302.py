"""#302 startup-pitch deck (7 slides, 5 minutes) in the layout of the 2026.8 deck (2026_오행부_발표자료_최종2.pptx).

Same canvas (20 x 11.25 in), same tokens as that deck: white page, Noto Sans KR, blue section label "NN · 섹션",
48 pt bold title, black rules over columns, arrows between steps, grey-framed images, grey footer. Only the content
is new (user outline, 2026-09-29). Text, numbers and charts are native; the saved deck embeds its fonts.

usage: python Tools/Art/pitch302.py [--trailer path.mp4] [--no-embed]
Output: Art/Presentation302/오행부_피칭_v4.pptx
"""
import subprocess
import sys
from pathlib import Path

import imageio_ffmpeg
from PIL import Image
from pptx import Presentation
from pptx.chart.data import CategoryChartData
from pptx.dml.color import RGBColor
from pptx.enum.chart import XL_CHART_TYPE, XL_LABEL_POSITION
from pptx.enum.shapes import MSO_SHAPE
from pptx.enum.text import MSO_ANCHOR, PP_ALIGN
from pptx.oxml.ns import qn
from pptx.util import Inches, Pt

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/Presentation302'
IMG = OUT / 'img'
SRC = OUT / 'src'
SHOTS = ROOT / 'Art/Presentation297/Screenshots'
STILLS = ROOT / 'Art/Presentation297/Stills'
TRAILER = Path(sys.argv[sys.argv.index('--trailer') + 1]) if '--trailer' in sys.argv else ROOT / 'Art/Presentation297/Trailer/오행부_트레일러_1분_드라이브용.mp4'
FONT = 'Noto Sans KR'
DECK = OUT / '오행부_피칭_v4.pptx'

# ---------------------------------------------------------------- tokens of the 2026.8 deck
W, H = 20.0, 11.25
L = 1.0                                   # left edge of every block
R = 19.0                                  # right edge
TXT, BODY = '201E1D', '201F1D'
BLUE, BLUE_DARK = '2E5F8F', '24496E'
GREY, FRAME, SOFT_LINE, PANEL = '777777', 'C9C9C9', 'CCCCCC', 'F7F6F3'
RED = 'C00000'


def rgb(h):
    return RGBColor.from_string(h)


def set_font(run, size, bold=False, colour=BODY):
    f = run.font
    f.size = Pt(size); f.bold = bold; f.color.rgb = rgb(colour); f.name = FONT
    rpr = run._r.get_or_add_rPr()
    for tag in ('a:ea', 'a:cs'):
        el = rpr.find(qn(tag))
        if el is None:
            el = rpr.makeelement(qn(tag), {}); rpr.append(el)
        el.set('typeface', FONT)


def text(slide, x, y, w, h, paras, align=PP_ALIGN.LEFT, anchor=MSO_ANCHOR.TOP, spacing=1.15, after=0):
    """paras: paragraphs, each one run tuple (text, size, bold, colour) or a list of them."""
    tb = slide.shapes.add_textbox(Inches(x), Inches(y), Inches(w), Inches(h))
    tf = tb.text_frame
    tf.word_wrap = True
    tf.margin_left = tf.margin_right = tf.margin_top = tf.margin_bottom = 0
    tf.vertical_anchor = anchor
    for i, para in enumerate(paras):
        p = tf.paragraphs[0] if i == 0 else tf.add_paragraph()
        p.alignment = align; p.line_spacing = spacing; p.space_after = Pt(after)
        for spec in (para if isinstance(para, list) else [para]):
            s, size, bold, colour = (tuple(spec) + (False, BODY)[len(spec) - 2:])[:4]
            r = p.add_run(); r.text = s
            set_font(r, size, bold, colour)
    return tb


def rect(slide, x, y, w, h, fill, line=None, shape=MSO_SHAPE.RECTANGLE):
    s = slide.shapes.add_shape(shape, Inches(x), Inches(y), Inches(w), Inches(h))
    if fill:
        s.fill.solid(); s.fill.fore_color.rgb = rgb(fill)
    else:
        s.fill.background()
    if line:
        s.line.color.rgb = rgb(line); s.line.width = Pt(.75)
    else:
        s.line.fill.background()
    s.shadow.inherit = False
    return s


def rule(slide, x, y, w, colour='201E1E', h=.02):
    return rect(slide, x, y, w, h, colour)


def arrow(slide, x, y, size=22.5, colour='201E1E'):
    return text(slide, x, y, .4, .5, [('→', size, True, colour)], align=PP_ALIGN.CENTER)


def picture(slide, path, x, y, w, h, frame=True):
    """Image cropped to the box, framed with the deck's grey hairline."""
    iw, ih = Image.open(path).size
    pic = slide.shapes.add_picture(str(path), Inches(x), Inches(y), Inches(w), Inches(h))
    if iw / ih > w / h:
        c = (1 - (w / h) / (iw / ih)) / 2; pic.crop_left = pic.crop_right = c
    else:
        c = (1 - (iw / ih) / (w / h)) / 2; pic.crop_top = pic.crop_bottom = c
    if frame:
        pic.line.color.rgb = rgb(FRAME); pic.line.width = Pt(.75)
    return pic


def page(prs, n, heading):
    """Page number and a plain section heading (no claim line, no summary)."""
    s = prs.slides.add_slide(prs.slide_layouts[6])
    text(s, L, .79, 12, .41, [(f'{n:02d}', 18, False, BLUE)])
    text(s, L, 1.42, R - L, .88, [(heading, 48, True, TXT)], spacing=1.0)
    rule(s, L, 10.18, 18, SOFT_LINE, .01)
    text(s, L, 10.29, 10, .29, [('팀 미적(微跡) · 오행부 五行符', 12.5, False, GREY)])
    return s


def notes(slide, s):
    slide.notes_slide.notes_text_frame.text = s


# ---------------------------------------------------------------- slides

def slide1(prs):
    s = prs.slides.add_slide(prs.slide_layouts[6])
    text(s, L, .79, 12, .41, [('01', 18, False, BLUE)])
    text(s, L, 1.42, R - L, .88, [('제품 소개', 48, True, TXT)], spacing=1.0)
    vw = 11.2; vh = vw * 9 / 16; vx = (W - vw) / 2; vy = 2.55
    poster = IMG / 'poster.jpg'
    subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), '-y', '-loglevel', 'error', '-ss', '52', '-i', str(TRAILER),
                    '-frames:v', '1', '-q:v', '3', str(poster)], check=True)
    mv = s.shapes.add_movie(str(TRAILER), Inches(vx), Inches(vy), Inches(vw), Inches(vh), poster_frame_image=str(poster), mime_type='video/mp4')
    mv.line.color.rgb = rgb(FRAME); mv.line.width = Pt(.75)
    cols = [('제품명', [('오행부 ', 26, True, TXT), ('五行符', 18, False, GREY)], '한글 작도 액션 어드벤처'),
            ('제품 형태', [('PC(Steam) 싱글플레이', 24, True, TXT)], '1인칭 한글 작도 전투 · 29,000원 · 플레이 20~30시간'),
            ('핵심 경험', [('붓 한 자루를 든 도사', 24, True, TXT)], '마법과 전설이 살아 숨 쉬는 조선을 누비는 경험')]
    cw = (vw - 2 * .5) / 3; y = vy + vh + .3
    for i, (lab, head, body) in enumerate(cols):
        x = vx + i * (cw + .5)
        rule(s, x, y, cw)
        text(s, x, y + .16, cw, .35, [(lab, 15, False, BLUE)])
        text(s, x, y + .5, cw, .55, [head])
        text(s, x, y + 1.08, cw, .5, [(body, 16, False, BODY)])
    notes(s, '[트레일러 약 1분] 오행부는 붓으로 한글을 써서 술식을 발동하는 PC 싱글플레이 액션 어드벤처입니다. '
             '플레이어는 붓 한 자루를 든 도사가 되어 마법과 전설이 살아 숨 쉬는 조선을 누빕니다.')


def slide2(prs):
    s = page(prs, 2, '전통문화 활용 및 차별성')
    cols = [
        ('훈민정음 해례본 · 제자해', '초성 = 오행, 중성 = 음양·천지인, 종성 = 초성 재사용', SHOTS / '02_작도_나_완성.jpg',
         [('제자해의 오행 배정에 따라 ', 18), ('ㄱ·ㄴ·ㅁ·ㅅ·ㅇ은 목·화·토·금·수', 18, True), (' 속성을 갖습니다. 자모를 조합하는 규칙이 그대로 술식 문법입니다.', 18)],
         '자모를 조합해 새 술식을 찾아냅니다.'),
        ('한국 서예 · 수묵담채', '필세 = 형(정확도) × 세(속도)', SRC / 'key302_capital.png',
         [('붓으로 쓴 획이 ', 18), ('정확하고 빠를수록 위력이 셉니다', 18, True), ('. 가까운 산은 짙은 먹, 먼 산은 옅은 먹으로 칠해 세계를 수묵담채로 그립니다.', 18)],
         '수묵화 속을 걸으며 직접 붓으로 씁니다.'),
        ('역사 · 사상 · 종교', '대체역사 조선 · 오행 상생상극 · 민간신앙과 설화', SRC / 'mech_buddha_2026-08.png',
         [('마석으로 산업화한 조선이 배경이고, ', 18), ('오행의 상생·상극이 전투 규칙', 18, True), ('입니다. 성황당, 기계 불상, 도깨비, 불가사리가 세계와 적으로 나옵니다.', 18)],
         '설화 속 도깨비와 불가사리를 적으로 만납니다.'),
    ]
    cw = 5.5
    for i, (head, sub, im, body, exp) in enumerate(cols):
        x = L + i * 6.0
        rule(s, x, 2.6, cw)
        text(s, x, 2.8, cw, .45, [(head, 20.25, True, TXT)])
        picture(s, im, x, 3.45, cw, cw * 9 / 16)
        text(s, x, 6.72, cw, 1.1, [[(t[0], t[1], len(t) > 2, BODY) for t in body]], spacing=1.2)
        text(s, x, 7.95, cw, .4, [[('고객 경험   ', 16, True, BLUE_DARK), (exp, 16, False, BLUE_DARK)]])
        if i < 2:
            arrow(s, x + cw + .05, 2.7)
    rule(s, L, 8.95, 17.5)
    text(s, L, 9.18, 1.3, .41, [('차별성', 18, False, BLUE)])
    text(s, 2.36, 9.12, 16.6, 1.0, [[('전통 원리를 게임 규칙으로 옮기고 아트 스타일까지 같은 원리로 묶는 ', 20, False, TXT), ('설계·구현 노하우', 20, True, TXT),
                                    ('가 차별성입니다. 원전 해석부터 구현까지 한 사람이 해 왔기 때문에 90일 안에 따라 만들기 어렵습니다.', 20, False, TXT)]], spacing=1.2)
    notes(s, '훈민정음 해례본 제자해는 소리 나는 자리에 따라 자음에 오행을 배정했습니다. 오행부는 이 배정을 그대로 술식 문법으로 씁니다. '
             '붓글씨의 필세는 술식의 위력이 되고, 수묵담채는 화면을 그리는 방식이 되며, 역사와 민간신앙은 세계와 적이 됩니다. '
             '원전 해석부터 구현까지 한 사람이 해 왔기 때문에 90일 안에 따라 만들기 어렵습니다.')


def slide3(prs):
    s = page(prs, 3, '목표 고객 및 구매 시나리오')
    steps = [('기존 행동', '여러 장르를 즐기고, 주말이면 스팀 신작·할인 목록에서 혼자 할 게임을 찾습니다.'),
             ('구매 동기', '서양 판타지가 아닌 세계와 처음 보는 조작 방식을 찾습니다.'),
             ('오행부를 선택하는 이유', '스팀 페이지 트레일러에서 붓으로 한글을 써서 싸우는 장면을 봅니다.'),
             ('구매 저해 요인', '새 시스템을 익혀야 한다는 부담. "글자를 모두 외워야 하나?"')]
    for i, (t, d) in enumerate(steps):
        x = L + i * 4.5
        last = i == 3
        rule(s, x, 3.1, 4.0, RED if last else '201E1E')
        text(s, x, 3.3, 4.2, .45, [[(f'{i + 1} ', 20.25, True, RED if last else BLUE), (t, 20.25, True, TXT)]])
        text(s, x, 3.85, 4.0, 1.3, [(d, 17, False, BODY)], spacing=1.25)
        if i < 3:
            arrow(s, x + 4.05, 3.3)
    text(s, L + 13.5, 5.25, 4.5, .4, [('→ 04에서 데모 빌드로 검증', 16, True, BLUE_DARK)])
    # profile | the moment of discovery
    rule(s, L, 6.0, 7.2)
    text(s, L, 6.2, 8.0, .5, [('고객 프로필', 23.25, True, TXT)])
    prof = [('30대 · 직장인 · 남성', '구매력이 있고 혼자 하는 게임을 선호'), ('다양한 장르', '액션·RPG부터 인디까지'),
            ('스팀에서 탐색', '신작·할인 목록과 트레일러를 보고 게임을 고름')]
    y = 6.85
    for t, d in prof:
        text(s, L, y, 8.0, .45, [(t, 20.25, True, TXT)])
        text(s, L, y + .4, 8.0, .45, [(d, 18, False, BODY)])
        y += 1.0
    rule(s, 9.0, 6.0, 10.0)
    text(s, 9.0, 6.2, 10, .5, [('스팀 페이지 트레일러 장면', 23.25, True, TXT)])
    picture(s, SRC / 'key302_inn.png', 9.0, 6.85, 5.2, 2.93)
    picture(s, SHOTS / '04_작도_곰.jpg', 14.45, 6.85, 4.55, 2.93)
    notes(s, '목표 고객은 다양한 게임을 즐기는 30대 남성 직장인입니다. 주말 여가를 혼자 즐길 게임을 찾다가 스팀 페이지에서 오행부를 발견합니다. '
             '처음 보는 세계와 조작 방식에 끌리지만, 새 시스템을 익혀야 한다는 부담 때문에 구매를 망설입니다. 이 부담은 다음 장의 데모 빌드로 검증합니다.')


def slide4(prs):
    s = page(prs, 4, 'MVP 검증 결과')
    cols = [
        ('스크린샷 설문 · 10명', [[('룩데브 스크린샷을 본 10명 중 8명이 ', 20.25), ('"체험판이 나오면 해 보고 싶다"', 20.25, True), ('고 답했습니다.', 20.25)]],
         SRC / 'key302_palace.png'),
        ('라이브 시연 · 5명', [[('호기심   ', 18, True), ('마석 자동차와 기계 불상이 궁금하다', 18)],
                            [('난이도   ', 18, True), ('생각보다 쉬워 보인다', 18)],
                            [('부담   ', 18, True, RED), ('술식을 모두 외워야 하는지 걱정된다', 18)]], STILLS / 'b1_car_250.png'),
        ('데모 빌드 검증 · 2027. 2', [[('가설   ', 18, True), ('처음에 술식을 제한하고 플레이하며 풀어 주면 암기 부담이 줄어든다', 18)],
                                   [('해금   ', 18, True), ('초성 5자로 시작해 강토를 깰 때마다 종성을 얻고 술식 10종이 한 번에 열린다', 18)],
                                   [('측정   ', 18, True), ('시연 때와 같은 질문으로 암기 부담을 비교한다', 18)]], SHOTS / '06_작도_뭄.jpg'),
    ]
    for i, (head, paras, im) in enumerate(cols):
        x = L + i * 6.0
        rule(s, x, 2.88, 5.5)
        text(s, x, 3.06, 6.0, .55, [(head, 24.98, True, BODY)])
        text(s, x, 3.72, 5.6, 2.6, [[(t[0], t[1], len(t) > 2 and t[2], t[3] if len(t) > 3 else BODY) for t in p] for p in paras], spacing=1.25, after=6)
        picture(s, im, x, 6.55, 5.5, 3.09)
        if i < 2:
            arrow(s, x + 5.55, 3.1)
    notes(s, '룩데브 스크린샷을 본 10명 중 8명이 체험판이 나오면 해 보고 싶다고 답했습니다. 5명에게 라이브로 시연하자 마석 자동차와 기계 불상이 궁금하다는 반응, '
             '생각보다 쉬워 보인다는 반응이 나왔고, 술식을 모두 외워야 하느냐는 걱정도 나왔습니다. 그래서 처음에는 술식을 제한하고 플레이하며 풀어 주는 데모 빌드를 만들어, '
             '암기 부담이 실제로 줄어드는지 확인하겠습니다.')


# ---- 5. revenue: every number derives from these assumptions
PRICE, VAT, FEE = 29000, 1.1, .30
NET = PRICE / VAT                                    # supply value (VAT excluded)
UNIT = NET * (1 - FEE)                               # kept per copy after the platform fee
MONTHS, HOURS_WEEK, WAGE = 19, 80, 12000
COSTS = [('본인 인건비', WAGE * HOURS_WEEK * MONTHS * 52 / 12, f'시급 {WAGE:,}원 × 주 {HOURS_WEEK}시간 × {MONTHS}개월'),
         ('AI 생산성 도구', 400000 * MONTHS, f'월 40만 원 × {MONTHS}개월'), ('사운드 외주', 2800000, ''),
         ('일러스트 외주', 200000, ''), ('기타 등록비', 2000000, '플랫폼 등록·심의 등')]
TOTAL = sum(c[1] for c in COSTS)
BEP = TOTAL / UNIT
TARGET = 10000


def profit(n):
    return n * UNIT - TOTAL


def margin(n):
    return profit(n) / (n * NET)


def won(v):
    man = round(v / 10000)
    eok, rest = divmod(man, 10000)
    return (f'{eok}억 ' if eok else '') + (f'{rest:,}만' if rest else '') + ' 원'


def slide5(prs):
    s = page(prs, 5, '매출 구조 및 수익성')
    # costs (the 2026.8 table grammar)
    rule(s, L, 3.2, 7.22)
    text(s, L, 3.4, 7.5, .51, [[('비용 구조', 23.25, True, TXT), (f'   총 {won(TOTAL)}', 23.25, False, TXT)]])
    y = 4.15
    for name, v, d in COSTS:
        text(s, L, y, 4.5, .43, [(name, 19.5, True, TXT)])
        text(s, L + 4.5, y, 2.72, .43, [(won(v), 19.5, False, TXT)], align=PP_ALIGN.RIGHT)
        if d:
            text(s, L, y + .42, 7.2, .3, [(d, 13, False, GREY)])
        y += .8
        rule(s, L, y - .1, 7.22, SOFT_LINE, .01)
    text(s, L, y + .15, 7.22, .7, [[('1장당 수익 ', 16, True, TXT), (f'{UNIT:,.0f}원', 16, True, BLUE)],
                                  (f'판매가 {PRICE:,}원에서 부가세와 판매 수수료 30%를 뺀 금액', 13, False, GREY)], spacing=1.3)
    # margin by sales
    cx = 8.73; cw = R - cx
    rule(s, cx, 3.2, cw)
    text(s, cx, 3.4, cw, .51, [('판매량별 마진율 (순이익 ÷ 부가세 제외 매출)', 23.25, True, TXT)])
    counts = list(range(5000, TARGET + 1, 1000))
    data = CategoryChartData()
    data.categories = [f'{n:,}장' for n in counts]
    data.add_series('마진율', [round(margin(n) * 100, 1) for n in counts])
    gf = s.shapes.add_chart(XL_CHART_TYPE.LINE_MARKERS, Inches(cx - .1), Inches(4.0), Inches(cw + .1), Inches(3.9), data)
    ch = gf.chart
    ch.has_title = False; ch.has_legend = False
    ch.font.size = Pt(13); ch.font.color.rgb = rgb(GREY); ch.font.name = FONT
    plot = ch.plots[0]
    plot.has_data_labels = True
    dl = plot.data_labels
    dl.number_format = '0.0"%"'; dl.number_format_is_linked = False; dl.position = XL_LABEL_POSITION.ABOVE
    dl.font.size = Pt(15); dl.font.bold = True; dl.font.color.rgb = rgb(TXT)
    ser = plot.series[0]
    ser.format.line.color.rgb = rgb(TXT); ser.format.line.width = Pt(3); ser.smooth = False
    ser.marker.style = 8; ser.marker.size = 9
    ser.marker.format.fill.solid(); ser.marker.format.fill.fore_color.rgb = rgb(TXT); ser.marker.format.line.color.rgb = rgb(TXT)
    last = ser.points[len(counts) - 1]
    last.marker.size = 15; last.marker.format.fill.solid(); last.marker.format.fill.fore_color.rgb = rgb(BLUE)
    last.marker.format.line.color.rgb = rgb(BLUE)
    last.data_label.font.color.rgb = rgb(BLUE); last.data_label.font.size = Pt(19); last.data_label.font.bold = True
    last.data_label.position = XL_LABEL_POSITION.ABOVE
    lbl = last.data_label._dLbl                     # a point label override drops the series number format
    lbl.insert(1, lbl.makeelement(qn('c:numFmt'), {'formatCode': '0.0"%"', 'sourceLinked': '0'}))
    va = ch.value_axis
    va.maximum_scale = 45; va.minimum_scale = 0; va.major_unit = 10
    va.has_major_gridlines = True; va.major_gridlines.format.line.color.rgb = rgb('E6E6E6'); va.major_gridlines.format.line.width = Pt(.5)
    va.format.line.fill.background(); va.tick_labels.font.size = Pt(12); va.tick_labels.number_format = '0"%"'; va.tick_labels.number_format_is_linked = False
    ca = ch.category_axis
    ca.format.line.color.rgb = rgb(SOFT_LINE); ca.tick_labels.font.size = Pt(13); ca.has_major_gridlines = False
    # the three numbers to remember
    stats = [(f'{BEP:,.0f}장', '손익분기 판매량'), (f'{margin(TARGET) * 100:.1f}%', f'목표 {TARGET:,}장 마진율'), (won(profit(TARGET)), f'목표 {TARGET:,}장 순이익')]
    sw = cw / 3
    for i, (v, t) in enumerate(stats):
        x = cx + i * sw
        rule(s, x, 8.25, sw - .4, SOFT_LINE, .01)
        text(s, x, 8.4, sw - .3, .6, [(v, 28, True, BLUE if i else TXT)])
        text(s, x, 9.05, sw - .3, .4, [(t, 16, False, GREY)])
    notes(s, f'판매가 {PRICE:,}원에서 부가세와 판매 수수료 30%를 제외하면 1장당 {UNIT:,.0f}원이 남습니다. 19개월간의 인건비, AI 도구, 외주비, 등록비를 합한 총비용은 '
             f'{won(TOTAL)}이며, 약 5,000장에서 손익분기에 도달합니다. 이후 판매량이 1,000장 늘어날 때마다 마진율이 오르며, 목표 {TARGET:,}장에서는 '
             f'마진율 {margin(TARGET) * 100:.1f}%, 순이익 {won(profit(TARGET))}입니다.')


PLATFORMS = [  # name, fee, fee note, trait, customers, revenue share (assumed), colour
    ('텀블벅', '8%', '플랫폼 5% + 결제 3%, VAT 별도', '창작자 크라우드펀딩 플랫폼. 데모와 함께 선판매와 후원을 받습니다.', '전통문화·인디 콘텐츠 후원자', 20, BLUE),
    ('스팀', '30%', '매출 1천만 달러 이하 구간, 등록비 $100', 'PC 게임 유통 플랫폼. 위시리스트와 넥스트 페스트로 알립니다.', '전 세계 액션·인디 게이머', 70, TXT),
    ('스토브 인디', '15%', '스마일게이트 발표(2023) 기준', '스마일게이트의 국내 인디 게임 플랫폼. 기획전과 커뮤니티가 있습니다.', '국내 인디 게임 이용자', 10, '9DB3C8'),
]


def slide6(prs):
    s = page(prs, 6, '사업화 계획 및 판매 채널')
    # timeline (the 2026.8 grammar: date, dot on a line, stage, detail, goal)
    miles = [('2027. 2', '데모 · 펀딩', '텀블벅 펀딩 · 스팀 데모 공개', '목표  초기 수요와 암기 부담 검증'),
             ('2027. 8', '얼리 액세스', '스팀 · 스토브 인디 선판매', '목표  판매 시작, 피드백 반영'),
             ('2028. 2', '정식 출시', '5개 강토 · 3개 결말', '목표  10,000장 판매')]
    rule(s, L, 3.35, 17.5, '444141', .01)
    for i, (d, t, sub, goal) in enumerate(miles):
        x = L + i * 6.0
        text(s, x, 2.6, 3, .41, [(d, 18, i == 0, BLUE if i == 0 else TXT)])
        rect(s, x, 3.27, .17, .17, BLUE if i == 0 else '444141')
        text(s, x, 3.6, 5.5, .5, [(t, 22.5, True, TXT)])
        text(s, x, 4.2, 5.5, .39, [(sub, 18, False, TXT)])
        text(s, x, 4.65, 5.5, .41, [(goal, 18, False, BLUE_DARK)])
    # platforms
    for i, (name, fee, note, trait, cust, share, c) in enumerate(PLATFORMS):
        x = L + i * 4.5
        rule(s, x, 5.55, 4.0)
        text(s, x, 5.75, 4.2, .45, [(name, 20.25, True, TXT)])
        text(s, x, 6.25, 4.2, .6, [[('수수료 ', 16, False, GREY), (fee, 26, True, TXT)]])
        text(s, x, 6.9, 4.2, .3, [(note, 12.5, False, GREY)])
        text(s, x, 7.35, 4.0, 1.0, [(trait, 16, False, BODY)], spacing=1.25)
        text(s, x, 8.45, 4.0, .8, [[('고객   ', 16, True, BLUE_DARK), (cust, 16, False, BLUE_DARK)]], spacing=1.25)
    px_ = L + 13.5
    rule(s, px_, 5.55, R - px_)
    text(s, px_, 5.75, R - px_, .45, [('예상 매출 비율', 20.25, True, TXT)])
    data = CategoryChartData()
    data.categories = [p[0] for p in PLATFORMS]
    data.add_series('예상 매출 비율', [p[5] / 100 for p in PLATFORMS])
    d_ = 2.9
    gf = s.shapes.add_chart(XL_CHART_TYPE.PIE, Inches(px_), Inches(6.35), Inches(d_), Inches(d_), data)
    ch = gf.chart
    ch.has_title = False; ch.has_legend = False
    plot = ch.plots[0]
    plot.has_data_labels = False
    for i, p in enumerate(PLATFORMS):
        pt = plot.series[0].points[i]
        pt.format.fill.solid(); pt.format.fill.fore_color.rgb = rgb(p[6])
        pt.format.line.color.rgb = rgb('FFFFFF'); pt.format.line.width = Pt(1.5)
    y = 6.75
    for p in sorted(PLATFORMS, key=lambda q: -q[5]):
        rect(s, px_ + 3.15, y + .12, .17, .17, p[6])
        text(s, px_ + 3.45, y, 1.6, .4, [[(p[0] + '  ', 15, False, BODY), (f'{p[5]}%', 15, True, TXT)]])
        y += .55
    notes(s, '2027년 2월 데모 공개와 텀블벅 펀딩으로 초기 수요를 확인하고, 8월 얼리 액세스를 거쳐 2028년 2월에 정식 출시합니다. '
             '텀블벅에서는 후원자에게 먼저 팔고, 스팀에서는 전 세계 게이머에게, 스토브 인디에서는 국내 인디 게임 이용자에게 팝니다. 매출의 70%는 스팀에서 나올 것으로 봅니다.')


def slide7(prs):
    s = page(prs, 7, '팀 역량 및 보완 계획')
    groups = [
        ('대표 한예준 · 기획, 개발, 아트', [('유학동양학 전공', '동양철학의 현대적 의미를 주제로 졸업논문 작성'),
                                          ('게임 개발 부트캠프 수료', '수료 후 외주 개발로 실무 경험'),
                                          ('오행부 개발 현황', '5개 강토 월드, 한글 작도 인식, 전투와 요괴, 마석 자동차, 수묵 렌더링까지 플레이할 수 있는 빌드')]),
        ('보완 계획 · 2027년 1분기까지', [('사운드 · 마케팅', 'AI로 시안을 만든 뒤 외주로 완성'), ('창업 프로그램', '멘토링과 펀딩으로 사업화 역량 보강'),
                                     ('게임 행사', '행사와 전시에 나가 노출과 피드백 확보')]),
    ]
    for i, (head, items) in enumerate(groups):
        x = L + i * 9.0
        rule(s, x, 2.83, 8.5)
        text(s, x, 3.02, 8.8, .51, [(head, 23.25, True, TXT)])
        y = 3.75
        for t, d in items:
            text(s, x, y, 8.6, .45, [(t, 20.25, True, TXT)])
            text(s, x, y + .43, 8.5, .9, [(d, 18, False, BODY)], spacing=1.2)
            y += 1.42
    notes(s, '저는 유학동양학을 전공하며 동양철학의 현대적 의미를 주제로 졸업논문을 썼고, 게임 개발 부트캠프를 수료한 뒤 외주 개발을 진행했습니다. '
             '현재 오행부는 다섯 강토와 작도 인식, 전투, 수묵 렌더링까지 플레이할 수 있습니다. '
             '부족한 사운드와 마케팅은 AI로 시안을 만든 뒤 외주로 완성하고, 창업 프로그램과 게임 행사를 활용해 2027년 1분기 안에 채우겠습니다.')


def embed_fonts(path):
    """Re-save through PowerPoint with the fonts embedded (python-pptx cannot embed fonts)."""
    ps = (f'$app = New-Object -ComObject PowerPoint.Application; '
          f'$p = $app.Presentations.Open("{path}", $false, $false, $false); '
          f'$p.SaveAs("{path}", 24, -1); $p.Close()')
    subprocess.run(['powershell', '-NoProfile', '-Command', ps], check=True)


def main():
    IMG.mkdir(parents=True, exist_ok=True)
    prs = Presentation()
    prs.slide_width, prs.slide_height = Inches(W), Inches(H)
    for f in (slide1, slide2, slide3, slide4, slide5, slide6, slide7):
        f(prs)
    prs.save(DECK)
    if '--no-embed' not in sys.argv:
        embed_fonts(DECK)
    print(DECK)
    print(f'unit {UNIT:,.1f}  total {TOTAL:,.0f}  bep {BEP:,.1f}  '
          + '  '.join(f'{n}:{margin(n) * 100:.1f}%/{profit(n):,.0f}' for n in range(5000, TARGET + 1, 1000)))


if __name__ == '__main__':
    main()
