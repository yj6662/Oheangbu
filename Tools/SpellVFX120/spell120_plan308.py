"""SPEC-SPELL-120-308 planning sheets (read-only against the project; writes notes under Art/SpellVFX120/Spell120_308/).

Builds, from the canonical vocabulary CSV plus the plan below:
  handler_map308.csv  one row per glyph: grammar cell, category, work package, handler id, unlock gate, state
  clauses308.csv      the clause checklist: every clause quotes a literal fragment of the CSV effect text
and fails when the plan and the CSV disagree (120 rows, category counts, grammar grid, one handler per assigned glyph,
blank rows without a handler, every quoted fragment present in the canonical text).

usage: python Tools/SpellVFX120/spell120_plan308.py [--check]     (--check validates only, writes nothing)
"""
import csv, hashlib, io, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CSV_PATH = ROOT / 'Docs' / '오행부_작도어휘_v0_1.csv'
OUT = ROOT / 'Art' / 'SpellVFX120' / 'Spell120_308'
EXPECTED_SHA = '467f1dc904cf261bf8a1b04213fcc46b04080037ee5caf6ec1252eb4b11bc23f'
COUNTS = {'공격': 50, '상합 설치': 5, '패링': 5, '버프': 25, '소환': 5, '방벽': 5, '필드': 5, '공백': 20}

# handler id -> (work package, glyphs). 'legacy' handlers wrap code that exists today (the 36 resolvable glyphs).
HANDLERS = [
    # existing 36 (wrapped by WP-01B, defects fixed by WP-00, three replaced by WP-11)
    ('core.single', 'legacy', '가나마사아'), ('core.circle', 'legacy', '고'), ('core.cone', 'legacy', '노'),
    ('core.path', 'legacy', '모오'), ('core.volley', 'legacy', '소'), ('core.parry', 'legacy', '거너머서어'),
    ('core.summon', 'legacy', '곰놈몸솜옴'), ('ea.ward', 'legacy', '구누무수우'), ('ea.buff', 'legacy', '걱넉먹석억'),
    ('ea.giyeok', 'legacy', '각낙삭악'), ('field.lift', 'legacy', '국'), ('field.bridge', 'legacy', '뭄'),
    # WP-02 numeric / conditional attacks
    ('core.single', 'WP-02', '난'), ('volley.ramp', 'WP-02', '솟'), ('volley.heat', 'WP-02', '손'), ('single.execute', 'WP-02', '갓'),
    # WP-03 two-stage / delayed second judgement
    ('cone.backblast', 'WP-03', '논'), ('single.blast', 'WP-03', '만'), ('path.stalagmite', 'WP-03', '목'),
    ('path.tide', 'WP-03', '옥'), ('single.skip', 'WP-03', '망'),
    # WP-04 derived shots after a confirmed hit
    ('hit.crescent', 'WP-04', '낫'), ('hit.droplets', 'WP-04', '안'), ('volley.ricochet', 'WP-04', '속'),
    ('hit.spread', 'WP-04', '간'), ('hit.cycle', 'WP-04', '앙'),
    # WP-05 enemy control
    ('circle.interrupt', 'WP-05', '곤'), ('single.freeze', 'WP-05', '앗'), ('path.undertow', 'WP-05', '옹'),
    # WP-06 world-fixed zones
    ('zone.slow', 'WP-06', '곡'), ('zone.spikes', 'WP-06', '곳'), ('cone.healzone', 'WP-06', '녹'), ('zone.tree', 'WP-06', '검'),
    # WP-07 enemy modifiers (gated: Q4)
    ('mod.brand', 'WP-07', '산몬'), ('mod.armorbreak', 'WP-07', '맛놋'), ('mod.pierce', 'WP-07', '강낭'),
    ('mod.dull', 'WP-07', '못'), ('mod.weaken', 'WP-07', '옷'),
    # WP-08 self buffs
    ('buff.burstheal', 'WP-08', '건'), ('buff.empower', 'WP-08', '넌'), ('buff.steadfast', 'WP-08', '넘'),
    ('buff.swift', 'WP-08', '선'), ('buff.chain', 'WP-08', '언'), ('buff.reserve', 'WP-08', '엄'),
    ('buff.reflect', 'WP-08', '먼'), ('buff.slowback', 'WP-08', '멈'), ('buff.insight', 'WP-08', '섬'),
    # WP-09 companion shots
    ('buff.companion', 'WP-09', '겅넝멍성엉'),
    # WP-10 install + detonation
    ('install.mark', 'WP-10', '감남맘삼암'),
    # WP-12 field gates (gated: Q5)
    ('field.burn', 'WP-12', '눈'), ('field.cut', 'WP-12', '숫'), ('field.purify', 'WP-12', '웅'),
    # WP-13 attacks that need enemy-side systems first (gated: Q5)
    ('intercept.single', 'WP-13', '상'), ('intercept.barrage', 'WP-13', '송'), ('single.unblockable', 'WP-13', '삿'),
    ('zone.haze', 'WP-13', '농몽'), ('path.purge', 'WP-13', '온'), ('single.cover', 'WP-13', '막'), ('path.cover', 'WP-13', '공'),
    # WP-14 sword forms (gated: Q7)
    ('sword.form', 'WP-14', '것넛멋섯엇'),
]
# Opened by the user answers of 2026-10-04 (DECISIONS D308-13) and built in phase B; the question that gated each is kept for the record.
GATED = {'WP-07': 'Q4', 'WP-12': 'Q5', 'WP-13': 'Q5', 'WP-14': 'Q7'}
# WP-00 fixes and WP-11 replacements touch glyphs that already have a legacy handler.
REWORK = {'WP-00': '곰놈몸솜옴각국고', 'WP-11': '가마아'}
WP11_HANDLER = {'가': 'single.ranged', '마': 'single.ballistic', '아': 'single.homing'}
OPEN_GATE = set('곰놈몸솜옴')            # demo precedent: usable from the start although the final is ㅁ
TEST_GATE = set('막')                    # 2026-10-04: behind a main-game final but enemy / world dependent (Q5): TEST unlock only (Gate test in its rule sheet)
LEGACY_FEATURE = {'core.single': 'book', 'core.circle': 'book', 'core.cone': 'book', 'core.path': 'book', 'core.volley': 'book',
                  'core.parry': 'book', 'core.summon': 'book', 'ea.ward': 'ward', 'ea.buff': 'buff.g', 'ea.giyeok': 'giyeok',
                  'field.lift': 'guk', 'field.bridge': 'mum'}

# Clause checklist. layer: L2 = pure rule runner (runs without the editor), L3 = edit-mode fixture (needs the editor queue),
# 기존 = an existing check already covers it, Play = needs Play, 보류 = the system it needs does not exist / question pending.
# Phase B (2026-10-04): every clause that was held (보류) for Q4 / Q5 / Q7 is now an L2 clause of its package's runner cases;
# what still needs a real enemy, the world or an input binding is named in the clause text and listed in PHASE_B.md.
_PARRY = [('받아침', 'L3·기존', '상극 속성 공격을 창 안에서 받으면 성공 판정(COMBAT-PARRY)'),
          ('완성=잔존 방어막', 'L3·기존', '허공 시전도 방어막이 잔존한다(앞 구간 패링·뒤 구간 경감)')]
_WARD = [('언제든 시전', 'L3·기존', '해금 없이 시전된다(접지 실패만 거부)'), ('잠시 잔존', 'L3·기존', '지속 시간 뒤 사라진다'),
         ('패링 판정·보상 없음', 'L3·기존', '패링 창·먹 환급·그로기를 만들지 않는다')]
_SUMMON = [('소환수를 빚어 세움', 'L3', '리졸버를 거친 시전으로 소환수가 서고 스냅샷 위력 > 0, 적용 피해 > 0(WP-00)'),
           ('그로기 미축적', 'L3·기존', '소환 피해가 그로기·5속 마스크를 바꾸지 않는다'),
           ('어그로 없음', 'L3·기존', '충돌체·NavMeshAgent가 없고 적의 표적이 되지 않는다')]
_INSTALL = [('적 1체에 발사·부착', 'L2', '대상 1체에만 표식이 붙고 설치 자체 피해는 0이다'),
            ('부착 후 공격 진 명중 시 격발', 'L2', 'PlayerDirect 공격 술식 명중에만 격발한다(소환·지속·잔탄은 격발하지 않는다)'),
            ('큰 그로기', 'L2', '격발 1회에 그로기 단계가 데이터 값만큼 오른다(출처=상합)'),
            ('속성 피해', 'L2', '격발 피해의 속성 = 설치 글자의 초성 속성, 1회')]
_SWORD = [('검 공통 스펙', 'L2', '목검과 같은 처리기·같은 수치, 속성=초성, 그 속성이 극하는 원소 하나에만 상극')]
_COMPANION = [('잔탄 동반', 'L2', '버프 지속 중 승인된 시전마다 소투사체가 추가 예약된다(속성=초성, 5속·그로기 미기여)')]
_BLANK = [('의도적 공백', 'L2', '해석 결과 = Blank, 불발(먹 MisfireInkCost 소모·획 증발)')]

CLAUSES = {
    '가': [('단일 대상에', 'L3·기존', '착탄 예약 1건'), ('곧게 뻗는', 'L3·기존', '시전 시점 대상으로 직선 비행'),
           ('근중거리', 'L2', '사거리 밖 대상은 맞지 않는다(WP-11)')],
    '각': [('대상 행동 불가', 'L3·기존', 'Control.BlocksActions 참, 이동 0'), ('보스는 둔화', 'L3', 'IsBoss 프로필이면 차단 없이 감속(WP-00)'),
           ('시간 만료로만 해제', 'L3·기존', '지속 시간 뒤에만 풀린다'), ('묶인 대상 타격 가능, 추가 피해 없음', 'L3·기존', '타격해도 풀리지 않고 증폭이 없다')],
    '간': [('박힌 가시', 'L2', '명중해야 불씨가 붙는다(허공=없음)'), ('대상이 쓰러지거나', 'L2', '대상 사망 시 즉시 전이'),
           ('시간이 지나면', 'L2', '타이머 만료 시 전이'), ('가장 가까운 적에게', 'L2', '전이 대상 = 반경 안 최근접 생존 적'), ('같은 피해', 'L2', '전이 피해 = 원 명중 위력')],
    '감': _INSTALL,
    '갓': [('체력이 낮은 대상에게', 'L2', '착탄 직전 HP 비율이 문턱 이하일 때만'), ('큰 추가 피해', 'L2', '문턱 이하 = 추가 피해 1회, 문턱 위 = 기본 피해만')],
    '강': [('방어력 무시 피해', 'L2', '방어력 0.4 과녁: 일반 타격은 (1-방어력), 이 시전의 타격은 위력 그대로. 자기 공격 번호에만 1회. 방어력 0 과녁은 무받침 글자와 같은 피해')],
    '거': _PARRY,
    '걱': [('오랫동안 천천히 체력 회복', 'L3·기존', '지속 시간 동안 초당 회복')],
    '건': [('짧고', 'L2', '지속 시간 < 걱의 지속 시간'), ('크게 체력 회복', 'L2', '총 회복량 > 걱의 같은 시간 회복량, 사망자 부활 없음')],
    '검': [('나무를 심어', 'L2', '고정 위치에 영역 1개'), ('그 영역 안에서 회복', 'L2', '영역 안 = 회복, 밖 = 0'),
           ('시간 경과', 'L2', '수명 만료로 소멸'), ('피격 시 소멸', 'L2', '줄기에 닿는 타격 tree.hits회로 부러지고 그 순간부터 회복 0. 적이 나무를 직접 노릴 수단은 없어, 플레이어를 친 근접 가해자가 tree.reach 안이면 나무도 맞은 것으로 본다')],
    '것': [('작도 불가', 'L2', '검 변신 중에는 어떤 술식의 먹 비용도 치를 수 없다(패링·방벽 포함, 불발 먹도 없다). 작도 모드 진입 자체를 막는 것은 입력 이음매 뒤 Play'), ('클릭=검격', 'L2', '검격 1회 = 리치·호 안의 적 1체에 변신 위력(행 기본 위력 x 필세) 피해, 간격당 1회. 클릭 입력 연결은 입력 이음매 뒤 Play'), ('상극 검격 그로기 소폭', 'L2', '검의 속성이 적의 속성을 극하면 상극 검격이고, 같은 적에게 groggy.hits회마다 그로기 groggy.steps단계(출처=상극 검격). 속성 없는 적·비상극은 0'), ('먹 소진 해제', 'L2', '유지비(sword.drain/초)로 먹이 0이 되는 틱에 해제. 변신 중 들어온 먹은 즉시 반납된다')],
    '겅': [('모든 술식에', 'L2', '승인된 시전마다(종류 무관, 조준 대상이 있을 때)'), ('소투사체 동반', 'L2', '추가 착탄 예약'), ('잔탄 몸=목', 'L2', '잔탄 속성 = 목')],
    '고': [('지정 영역에서', 'L3·기존', '조준 대상 위치(없으면 전방 고정 거리)의 원'), ('산발적 솟음', 'L3', '가시별 돌출 시각이 다르다(수치=데이터, WP-00)'),
           ('적별 1회 타격', 'L3·기존', '적마다 가장 가까운 가시 완료 시각에 1회')],
    '곡': [('영역에', 'L2', '원형 영역 1개'), ('일정 시간 퍼져 잔존', 'L2', '수명 동안 유지 뒤 소멸'), ('이동 감속장', 'L2', '안의 적 이동 배율 < 1, 나가면 1')],
    '곤': [('영역의 적들을', 'L2', '원 안 생존 적 전원'), ('짧게 쳐올림', 'L2', '짧은 행동 차단(데이터 값), 보스는 감속'),
           ('예열 끊기', 'L3', '예열(텔레그래프) 중인 공격이 취소된다'), ('완화형', 'L2', '지속이 각보다 짧고 위력 계수가 낮다')],
    '곰': _SUMMON,
    '곳': [('솟은 가시가', 'L2', '1차 = 고와 같은 원형 판정'), ('잠시 남아', 'L2', '수명 동안 영역 잔존'), ('밟는 적에게 피해', 'L2', '안의 적에게 지속 피해(5속·그로기 미기여)')],
    '공': [('전진하며 소폭 피해', 'L2', '행 자체의 복도(경로 판정)와 작은 기본 위력(무받침 글자보다 낮다): 전선이 닿는 시각에 적별 1회'), ('은엄폐 제공', 'L2', '전선 = 복도를 가로지르는 벽. 벽 뒤 플레이어와 벽 앞 적을 잇는 선이 벽을 지나면 그 적의 원거리 타격이 관문에서 막힌다. 전선과 함께 움직이고 파도가 끝나면 사라짐. 실제 원거리 적과의 맞물림은 Play')],
    '구': _WARD,
    '국': [('발밑에서 솟아올라 위로 오른다', 'L3·기존', '승강 수치 = 데이터(WP-00)'), ('비전투 전용', 'L3·기존', '전투 중 거부')],
    '나': [('단일 대상에', 'L3·기존', '착탄 예약 1건'), ('표준 탄속', 'L2', '탄속 배율 = 1(기준선)'), ('위력 기준선', 'L2', '표의 기준 위력')],
    '낙': [('적중 대상에', 'L3·기존', '명중한 대상에만 붙는다'), ('지속 피해', 'L3·기존', '지속 시간 동안 초당 피해(5속 미기여)')],
    '난': [('부가 효과 없이', 'L2', '통제·영역·파생 판정이 없다'), ('최대 위력의 한 방', 'L2', '단일 착탄 1건, 기본 위력 = 단발 행 중 최대')],
    '남': _INSTALL,
    '낫': [('명중 후', 'L2', '확정 명중이 있어야 한다'), ('두 줄기', 'L2', '2차 판정 2개'), ('부채꼴로 갈라져 나감', 'L2', '착탄점에서 좌우 대칭 부채꼴'), ('새 몸=화', 'L2', '2차 피해 속성 = 화')],
    '낭': [('방어력 무시 피해', 'L2', '강-1과 같은 규칙(같은 처리기 mod.pierce, 속성만 화)')],
    '너': _PARRY,
    '넉': [('근접한 적에게', 'L3·기존', '반경 안 보이는 적'), ('지속 피해 오라', 'L3·기존', '초당 피해')],
    '넌': [('다음 한 번의 공격', 'L2', '다음 공격 술식 1회에만 적용되고 소모된다'), ('대폭 증폭', 'L2', '위력 × 데이터 배율')],
    '넘': [('피격이 작도를 끊지 못함', 'L2', '지속 중 피격 시 작도 중단 요청이 나가지 않는다'), ('피해는 받음', 'L2', '받는 피해량은 그대로')],
    '넛': _SWORD,
    '넝': _COMPANION,
    '노': [('지정 영역에', 'L3·기존', '전방 부채꼴'), ('즉발', 'L3·기존', '형성 딜레이 뒤 일제')],
    '녹': [('광역 화염', 'L2', '1차 = 노와 같은 부채꼴 판정'), ('적 명중 지점에서 회복존 분출', 'L2', '실제 명중한 적 위치에만 생긴다'),
           ('플레이어 진입 시 HP 회복', 'L2', '안 = 회복, 밖 = 0'), ('허공 시전 시 회복존 없음', 'L2', '명중 0 = 회복존 0')],
    '논': [('방사 끝점에서', 'L2', '2차 중심 = 부채꼴 끝점'), ('크게 터지는', 'L2', '2차 위력 > 1차 위력'), ('이단 구조', 'L2', '1차 뒤 시간차로 2차')],
    '놈': _SUMMON,
    '놋': [('훑인 전원', 'L2', '부채꼴이 훑은 적 전원(착탄 확정된 적)에만 방어 감소, 밖은 0. 시전 시점에는 아무에게도 없음'), ('방어력 감소', 'L2', '방어력 - break.defence(0 미만 없음), break.duration 뒤 복귀, 중첩 없음(가장 센 것). 방어력 0 과녁에는 추가 피해 없음')],
    '농': [('방사 자리에 김 잔존', 'L2', '1차 = 노와 같은 부채꼴. 부채꼴 가운데에 원 1개(haze.radius)가 haze.life 동안 남는다'), ('안의 적 명중률 감소', 'L2', '안에 선 적의 타격 중 haze.miss 몫이 관문에서 빗나간다(주사위가 아닌 누적식, 근접·원거리 모두). 나가면 즉시 복귀, 겹쳐도 센 것 하나')],
    '누': _WARD,
    '눈': [('덩굴을 불로 태워 길을 연다', 'L2', '마주 보고 닿는 가장 가까운 닫힌 덩굴 게이트 1개를 gate.delay 뒤에 연다(다른 종류·열린 것·열리는 중인 것은 제외). 월드 배치는 별도(시험용 게이트)'), ('비전투 전용', 'L2', '추격 중인 적이 있거나 combat.radius 안에 생존 적이 있으면 시전 불성립(먹 0)')],
    '마': [('단일 대상에', 'L3·기존', '착탄 예약 1건'), ('포물선', 'Play', '연출 궤적'), ('최고 단발 위력', 'L2', '무받침 단일 5자 중 기본 위력 최대'),
           ('조준 난도 높음', 'L2', '비유도 — 시전 시점 위치로 낙하(WP-11, Q8)')],
    '막': [('낙하 지점에', 'L2', '1차 = 마와 같은 비유도 낙하 판정(초성의 탄도). 바위는 낙하 지점 = 시전 순간 대상이 선 자리에(대상 없는 시전은 전방 cover.range) 반경 cover.radius로 선다'), ('엄폐물로 잔존', 'L2', 'cover.life 동안 서서, 적과 플레이어를 잇는 선이 바위를 지나면 그 적의 원거리 타격이 관문에서 막힌다(근접은 막지 않음). 상한 cover.max, 오래된 것부터 교체. 충돌체는 월드 작업')],
    '만': [('착탄 시', 'L2', '1차 착탄 시각에'), ('소범위 폭발', 'L2', '착탄점 원형 2차 판정(1차 대상 중복 없음)')],
    '맘': _INSTALL,
    '맛': [('명중 대상', 'L2', '단일: 착탄이 확정된 그 대상에만, 1회. 허공 시전·다른 술식의 타격으로는 붙지 않음'), ('방어력 감소', 'L2', '방어력 - break.defence(0 미만 없음), break.duration 뒤 복귀. 방어력 0 과녁에는 추가 피해 없음(중장갑 카운터)')],
    '망': [('착탄 후', 'L2', '1차 착탄 뒤'), ('한 번 더 앞으로 튀어', 'L2', '시전 방향으로 전진한 두 번째 점'), ('두 번째 지점 타격', 'L2', '두 번째 점 반경 안 1회')],
    '머': _PARRY,
    '먹': [('원거리 공격 피해', 'L3·기존', '원거리 종류에만'), ('약한 경감 지속', 'L3·기존', '지속 시간 동안 경감')],
    '먼': [('근접 공격한 적에게', 'L2', '근접 종류의 피격에서 가해자에게만'), ('반사 피해', 'L2', '가해자에게 피해 1회(플레이어 이득 없음)')],
    '멈': [('근접 공격한 적에게', 'L2', '근접 종류의 피격에서 가해자에게만'), ('둔화 부여', 'L2', '가해자 이동 배율 < 1, 지속 뒤 복귀')],
    '멋': _SWORD,
    '멍': _COMPANION,
    '모': [('직선 경로', 'L3·기존', '전방 복도'), ('즉발', 'L3·기존', '전선 도달 시각에 적별 1회')],
    '목': [('폭풍 경로에', 'L2', '1차 = 모와 같은 복도'), ('시간차로 솟아', 'L2', '1차 뒤 지연'), ('2차 타격', 'L2', '2차 시각에 복도 안 생존 적 1회')],
    '몬': [('훑인 전원에', 'L2', '복도(행 기하)가 훑은 적 전원에만 낙인, 밖은 0. 착탄 전에 죽은 적에는 붙지 않음'), ('받는 피해 증가 낙인', 'L2', '낙인 중 받는 피해 x brand.scale(모든 출처), brand.duration 뒤 원래대로, 중첩 없음. 낙인을 찍는 타격 자체는 증폭 없음')],
    '몸': _SUMMON,
    '못': [('훑인 적들의', 'L2', '복도가 훑은 적 전원에만 무딤 1회분, 밖은 0'), ('다음 공격 1회 무딤', 'L2', '다음 타격 1회만 x dull.scale, 그다음은 원래대로. 재시전은 1회분 갱신(누적 없음), dull.duration 뒤 소멸. 플레이어 피해 관문 연결은 L3')],
    '몽': [('경로에 잔존', 'L2', '1차 = 모와 같은 복도. 복도 가운데에 원 1개(haze.radius)가 haze.life 동안 남는다'), ('안의 적 명중률 감소', 'L2', '농-2와 같은 규칙(같은 처리기 zone.haze)')],
    '무': _WARD,
    '뭄': [('땅을 생성해 다리를 만든다', 'L3·기존', 'MumBridge295Checks'), ('비전투 전용', 'L3·기존', '전투 중 거부'), ('최후 어휘', 'L2', 'ㅁ 종성 해금 증명 없이는 Locked')],
    '사': [('단일 대상에', 'L3·기존', '착탄 예약 1건'), ('최속', 'L2', '탄속 배율이 단일 5자 중 최대'), ('저위력', 'L2', '기본 위력이 단일 5자 중 최소')],
    '삭': [('일자 관통', 'L3·기존', '직선 전선'), ('경로상 뒤의 적까지 피해', 'L3·기존', '경로 위 적 각 1회, 벽에서 멈춤')],
    '산': [('찍힌 대상이', 'L2', '단일: 착탄이 확정된 그 대상에만, 1회'), ('짧게 받는 피해 증가', 'L2', '낙인 중 받는 피해 x brand.scale, brand.duration(광역 낙인보다 짧다) 뒤 원래대로. 단일 행 중 최속 탄속')],
    '삼': _INSTALL,
    '삿': [('가드·방벽·반사 자세를 무시하고 꽂힘', 'L2', '적의 자세(받는 타격의 일부를 막는 가드 이음매)가 이 시전의 타격에만 통하지 않는다. 자기 공격 번호에 1회, 자세 자체는 유지. 가드하는 실제 적은 별도(시험용 과녁)')],
    '상': [('날아오는 적 투사체를 맞혀', 'L2', '부채꼴(intercept.angle·range) 안에서 시전자 쪽으로 날아오는 투사체 중 가장 가까운 것 하나. 없으면 사와 같은 단일 타격'), ('얼려 떨어뜨림', 'L2', '침이 닿는 시각에 아직 공중이면 요격(시험용 투사체는 얼어 떨어짐, 현행 적의 비행 투사체는 공격 중단). 요격된 것은 다시 대상이 되지 않는다')],
    '서': _PARRY,
    '석': [('작도 감쇠 여유 증가 지속', 'L3·기존', '체류 감쇠 유예 증가')],
    '선': [('투사체 속도 증가', 'L2', '공격 술식의 탄속 배율 증가, 위력 불변')],
    '섬': [('적 속성 가독 강화', 'Play', '표현 전용 — 버프 상태와 신호만 L2'), ('적 투사체 궤적이 잘 읽힘', 'Play', '표현 전용(Q8)')],
    '섯': _SWORD,
    '성': _COMPANION,
    '소': [('다연발', 'L3·기존', '발수만큼 예약'), ('즉발', 'L3·기존', '현행 선딜 1.45 s(D191) — 문구와 수치의 관계는 사용자 확인')],
    '속': [('명중한 송곳이', 'L2', '명중한 발만'), ('다른 표적으로', 'L2', '같은 대상 제외'), ('재발사', 'L2', '추가 착탄 예약(도탄 횟수 상한)')],
    '손': [('연속 시전마다', 'L2', '창 안에서 이어 시전할 때마다'), ('다음 속사 위력 증가', 'L2', '단계마다 배율 증가, 상한'), ('자기 스택', 'L2', '창을 넘기면 0으로')],
    '솜': _SUMMON,
    '솟': [('한 시전 안에서', 'L2', '한 계획 안의 발 순서'), ('뒤 탄일수록 위력 증가', 'L2', '발별 위력이 순증가, 합 = 시전 위력')],
    '송': [('적 투사체 여럿을', 'L2', '소의 부채꼴(반각·사거리) 안에서 날아오는 투사체 전부, 가까운 순으로 최대 intercept.max'), ('일제 격추', 'L2', '탄막이 서는 순간(소의 형성 딜레이) 한 번의 판정으로 함께 떨어뜨린다. 적 타격은 소와 같다')],
    '수': _WARD,
    '숫': [('큰 바위를 잘라 길을 연다', 'L2', '눈-1과 같은 규칙, 대상 = 바위 게이트'), ('비전투 전용', 'L2', '눈-2와 같은 규칙')],
    '아': [('단일 대상에', 'L3·기존', '착탄 예약 1건'), ('느린', 'L2', '탄속 배율 < 1'), ('각도 한계 내', 'L2', '누적 선회각이 한계를 넘으면 빗나간다(WP-11)'),
           ('비행 추적', 'L2', '비행 중 움직인 대상을 따라간다(WP-11)')],
    '악': [('명중 후', 'L3·기존', '첫 명중 뒤'), ('되돌아와', 'L3·기존', '되돌이 지연'), ('같은 대상 재타격', 'L3·기존', '같은 대상·같은 생명에 2회째')],
    '안': [('명중 시', 'L2', '확정 명중이 있어야 한다'), ('작은 물방울들로 갈라져', 'L2', '방울 N개'), ('주변 적을 각자 추적', 'L2', '서로 다른 인근 적(1차 대상 제외)'), ('새 몸은 물 유지', 'L2', '2차 속성 = 수')],
    '암': _INSTALL,
    '앗': [('단일 얼림(행동 불가)', 'L2', '명중 대상 행동 차단'), ('첫 피격에 깨지며', 'L2', '다음 피해 1건에 해제'), ('추가 피해 + 해제', 'L2', '추가 피해 1회 뒤 통제 제거'),
           ('보스는 각 선례 준용', 'L2', '보스 = 차단 대신 감속(Q8)')],
    '앙': [('명중 후 되돌아오며', 'L2', '명중해야, 귀환 시간 뒤'), ('먹 소폭 환급', 'L2', '환급 < 시전 비용'), ('체력 소폭 회복', 'L2', '회복 1회')],
    '어': _PARRY,
    '억': [('술식 먹 비용 감소 지속', 'L3·기존', '지속 중 승인 술식 지출 감소')],
    '언': [('직전과 다른 속성의 술식일 때', 'L2', '직전 승인 시전과 속성이 다를 때만'), ('비용 할인', 'L2', '지출 × (1 − 할인)')],
    '엄': [('먹 최대치 일시 확장', 'L2', '지속 중 용량 배율 > 1'), ('종료 시 초과분은', 'L2', '만료 순간 초과분 계산'), ('서서히 줄어드는 비축으로', 'L2', '비축이 시간에 따라 감소')],
    '엇': _SWORD,
    '엉': _COMPANION,
    '오': [('전진하는 느린 파도', 'L3·기존', '전선 속도'), ('경로 타격', 'L3·기존', '전선 도달 시각에 적별 1회')],
    '옥': [('끝까지 갔다가', 'L2', '1차 = 오와 같은 복도 전진'), ('되돌아오며', 'L2', '끝에서 시작점으로 귀환 전선'), ('2회 타격', 'L2', '적별 최대 2회(가는 길 1·오는 길 1)')],
    '온': [('경로상 적 장판·잔존물을 지우며 전진', 'L2', '1차 = 오와 같은 복도. 전선이 닿는 순간 복도에 걸친 적 장판을 지운다(옆·뒤·끝 너머는 그대로). 장판을 남기는 실제 적은 별도(시험용 장판)')],
    '옴': _SUMMON,
    '옷': [('훑인 전원', 'L2', '복도가 훑은 적 전원에만 공격력 감소, 밖은 0'), ('공격력 잠시 감소', 'L2', '지속 중 모든 타격 x weaken.scale(소모 없음), weaken.duration 뒤 원래대로. 플레이어 피해 관문 연결은 L3')],
    '옹': [('훑인 전원 잠시 굼뜨게', 'L2', '복도 안 적 이동 배율 < 1, 지속 뒤 복귀'), ('공중의 적은 끌어내림', 'L2', '전선이 훑을 때 공중인 적은 같은 시간 동안 지상으로(감속은 동일). 공중 이음매 + 시험용 과녁까지, 실제 공중 적은 별도')],
    '우': _WARD,
    '웅': [('오염을 정화한다', 'L2', '눈-1과 같은 규칙, 대상 = 오염 게이트(그 안에 서 있으면 마주 볼 필요 없음)'), ('비전투 전용', 'L2', '눈-2와 같은 규칙')],
}


def load():
    raw = CSV_PATH.read_bytes()
    sha = hashlib.sha256(raw).hexdigest()
    rows = list(csv.reader(io.StringIO(raw.decode('utf-8-sig'))))
    return sha, rows[0], rows[1:]


def grammar(initial, medial, final):
    """SPELL-GRAMMAR grid (Spellcraft Bible ch.3, 12, 13): category from the cell alone."""
    if medial == 'ㅏ': return '상합 설치' if final == 'ㅁ' else '공격'
    if medial == 'ㅓ': return '패링' if final == '무' else '버프'
    if medial == 'ㅗ': return '소환' if final == 'ㅁ' else '공격'
    if final == '무': return '방벽'
    return '필드' if final == initial else '공백'


def build():
    sha, header, rows = load()
    errors = []
    if sha != EXPECTED_SHA: errors.append('CSV sha256 changed: ' + sha)
    if len(rows) != 120: errors.append('rows != 120: %d' % len(rows))
    owner = {}
    for handler, wp, glyphs in HANDLERS:
        for g in glyphs:
            if g in owner: errors.append('glyph listed twice: ' + g)
            owner[g] = (handler, wp)
    counts = {}
    out_map, out_clauses = [], []
    for index, r in enumerate(rows, 1):
        letter, initial, element, medial, frame, final, category, effect, state, note = r
        counts[category] = counts.get(category, 0) + 1
        if grammar(initial, medial, final) != category:
            errors.append('grammar grid mismatch %s: %s vs %s' % (letter, grammar(initial, medial, final), category))
        if category == '공백':
            if letter in owner: errors.append('blank has a handler: ' + letter)
            handler, wp, status, gate, feature = '', '', 'Blank(선언된 불발)', '-', ''
            clauses = _BLANK
        else:
            if letter not in owner: errors.append('assigned glyph without handler: ' + letter); continue
            handler, wp = owner[letter]
            feature = LEGACY_FEATURE.get(handler, '') if wp == 'legacy' else ''
            gate = 'open' if letter in OPEN_GATE else 'test:' + final if letter in TEST_GATE else ('final:' + final if final != '무' else 'none')
            if wp in GATED: status = '예약 해제·2단계 제작(D308-13 %s 답)' % GATED[wp]   # keeps the prefix spell120_base308.py reads: the Base sheet still carries these 24 rows as reserved, the package sheets replace them
            elif wp == 'legacy': status = '기존'
            else: status = '이번 제작'
            clauses = CLAUSES.get(letter)
            if not clauses: errors.append('no clause list: ' + letter); clauses = []
        rework = [w for w, gs in REWORK.items() if letter in gs]
        out_map.append([index, letter, initial, element, medial, final, category, wp or '-', handler,
                        WP11_HANDLER.get(letter, ''), '·'.join(rework), gate, feature, status])
        for n, (fragment, layer, meaning) in enumerate(clauses, 1):
            if fragment not in effect: errors.append('fragment not in CSV text %s-%d: %s' % (letter, n, fragment))
            out_clauses.append([letter, '%s-%d' % (letter, n), fragment, layer, meaning, wp or '-'])
    for k, v in COUNTS.items():
        if counts.get(k) != v: errors.append('category count %s: %s != %d' % (k, counts.get(k), v))
    unknown = set(owner) - {r[0] for r in rows}
    if unknown: errors.append('plan names glyphs outside the CSV: ' + ''.join(sorted(unknown)))
    return sha, out_map, out_clauses, errors


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    sha, out_map, out_clauses, errors = build()
    by_status, by_wp, by_layer = {}, {}, {}
    for row in out_map:
        by_status[row[13]] = by_status.get(row[13], 0) + 1
        by_wp[row[7]] = by_wp.get(row[7], '') + row[1]
    for row in out_clauses: by_layer[row[3]] = by_layer.get(row[3], 0) + 1
    print('csv sha256', sha)
    print('rows', len(out_map), 'clauses', len(out_clauses))
    for k in sorted(by_status): print(' status', k, by_status[k])
    for k in sorted(by_wp): print(' wp', k, len(by_wp[k]), by_wp[k])
    for k in sorted(by_layer): print(' layer', k, by_layer[k])
    ids = sorted({row[8] for row in out_map if row[8]} | set(WP11_HANDLER.values()))
    print('handler ids', len(ids), ' '.join(ids))
    if errors:
        print('FAIL', len(errors))
        for e in errors: print('  ' + e)
        sys.exit(1)
    if '--check' in sys.argv:
        print('OK (check only)'); return
    OUT.mkdir(parents=True, exist_ok=True)
    with open(OUT / 'handler_map308.csv', 'w', encoding='utf-8-sig', newline='') as f:
        w = csv.writer(f, lineterminator='\n')
        w.writerow(['번호', '글자', '초성', '속성', '중성', '종성', '분류', '묶음', '처리기', 'WP-11 교체 처리기', '재작업 묶음', '해금', '구형 기능 키', '상태'])
        w.writerows(out_map)
    with open(OUT / 'clauses308.csv', 'w', encoding='utf-8-sig', newline='') as f:
        w = csv.writer(f, lineterminator='\n')
        w.writerow(['글자', '절 ID', 'CSV 원문 조각', '검사 층', '단언', '묶음'])
        w.writerows(out_clauses)
    print('OK wrote', OUT / 'handler_map308.csv', OUT / 'clauses308.csv')


if __name__ == '__main__':
    main()
