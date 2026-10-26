#!/usr/bin/env python3
"""D308-150 V10-V11 free showcase media: engine frames (Vol2VfxCapture, out=free_showcase/<round>) -> per-spell mp4 (1x then 0.5x),
a side-by-side still (concept | engine frame at the matching moment), a frame contact sheet, and one combined mp4 of all three.

  python Tools/Art/SpellVFX308/free_showcase_media308.py r1                     # sheets only (to pick the moments)
  python Tools/Art/SpellVFX308/free_showcase_media308.py r1 --video na=0.40 go=0.70 gak=1.40   # + videos and side-by-sides at those ages (s)
Out: Art/SpellVFX308/Vol2Vfx/free_showcase/<round>/media/"""
import csv, sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parents[3]
BASE = REPO / 'Art/SpellVFX308/Vol2Vfx/free_showcase'
CONCEPTS = BASE / 'concepts'
SPELLS = [  # key, template prefix of the job folder, concept(s), label
    ('na', 'P0_', ['na_b.png', 'na_c.png'], '나 · 화 · ㅏ 경로 · 한 번 — 붓끝에서 불화살이 첫 적에게 날아가 한 번 맞고 2초 태운다'),
    ('go', 'F0_', ['go_b.png'], '고 · 목 · ㅗ 면 · 한 번 — 앞 부채꼴(5 m)을 덩굴·죽순이 한 번 쓸고 안의 적을 붙든다'),
    ('gak', 'P1_', ['gak_a.png', 'gak_c.png'], '각 · 목 · ㅏ 경로 · 남긴다 — 맞은 적의 다리를 가지 새장이 1.5초 붙들고, 풀린 자리에 덤불이 남는다'),
]
# batch 1 (2026-10-26, D308-150 V12): --batch b1 -> free_showcase/batch1/<round>/, the concepts in batch1/concepts, keys by the job folder's template + body
B1_SPELLS = [
    ('ga', 'P0_bridge_Wood_', ['ga_a.png'], '가 · 목 · ㅏ 경로 · 한 번 — 가시 덩굴 작살이 첫 적을 꿰어 감고 2초 붙든다, 끝에 잔뿌리로 풀림'),
    ('ma', 'P0_bridge_Earth_', ['ma_a.png'], '마 · 토 · ㅏ 경로 · 한 번 — 바위덩이가 첫 적을 들이받아 민다, 둘레 땅이 판으로 들린다'),
    ('sa', 'P0_bridge_Metal_', ['sa_b.png'], '사 · 금 · ㅏ 경로 · 한 번 — 던진 검이 첫 적을 꿰어 베고 쨍 하고 파편이 옆으로 튄다'),
    ('a', 'P0_bridge_Water_', ['a_b.png'], '아 · 수 · ㅏ 경로 · 한 번 — 물 구슬이 첫 적 위에서 터져 덮어 적신다(4초), 물이 흘러내린다'),
    ('no', 'F0_bridge_Fire_', ['no_a.png'], '노 · 화 · ㅗ 면 · 한 번 — 앞 부채꼴에 불길이 바깥으로 번져 안의 적을 태운다(2초), 가장자리부터 재로 날림'),
    ('mo', 'F0_bridge_Earth_', ['mo_b.png', 'mo_a.png'], '모 · 토 · ㅗ 면 · 한 번 — 부채꼴 땅이 줄줄이 판으로 솟아 안의 적을 민다, 가라앉으며 둘레가 들린다'),
    ('so', 'F0_bridge_Metal_', ['so_a.png'], '소 · 금 · ㅗ 면 · 한 번 — 부챗살 칼날 다발이 부채꼴을 훑어 안의 적을 벤다, 끝에서 쨍 하고 부서진다'),
    ('o', 'F0_bridge_Water_', ['o_a.png'], '오 · 수 · ㅗ 면 · 한 번 — 말려 오르는 물결이 부채꼴을 덮쳐 안의 적을 적신다(4초), 땅이 젖는다'),
]
# batch 2 (2026-10-26, D308-150 V12): --batch b2 -> free_showcase/batch2/<round>/ (A0 ㅓ guard with the lab-scripted enemy blow, R0 ㅜ ring)
B2_SPELLS = [
    ('geo', 'A0_bridge_Wood_', ['geo_b.png'], '거 · 목 · ㅓ 부착 · 한 번 — 붓대에 덩굴과 대나무 십자가 서서 곰의 앞발을 받아치고 덩굴로 붙든다, 끝에 잔뿌리로 풀림'),
    ('neo', 'A0_bridge_Fire_', ['neo_a.png'], '너 · 화 · ㅓ 부착 · 한 번 — 붓대에 불 바퀴가 돌다 받아치는 순간 불꽃 모란이 핀다, 꽃잎이 끝부터 재로 날림'),
    ('meo', 'A0_bridge_Earth_', ['meo_a.png'], '머 · 토 · ㅓ 부착 · 한 번 — 앞에 돌 판벽이 솟아 앞발을 받고 흙먼지 고리, 가라앉으며 둘레 땅이 판으로 들림'),
    ('seo', 'A0_bridge_Metal_', ['seo_a.png'], '서 · 금 · ㅓ 부착 · 한 번 — 붓대에 강철 초승달 날이 서서 받아치면 불꽃이 옆으로 솟고, 쨍 하고 부서져 파편이 옆으로'),
    ('eo', 'A0_bridge_Water_', ['eo_a.png'], '어 · 수 · ㅓ 부착 · 한 번 — 붓 앞에 물 소용돌이 원반이 돌다 받아치면 물보라 왕관, 물이 아래로 흘러내림'),
    ('gu', 'R0_bridge_Wood_', ['gu_a.png'], '구 · 목 · ㅜ 둘레 · 한 번 — 2 m 고리의 빛나는 균열에서 가시 덩굴 관이 솟아 고리 위 적을 붙든다, 잔뿌리로 풀림'),
    ('nu', 'R0_bridge_Fire_', ['nu_a.png'], '누 · 화 · ㅜ 둘레 · 한 번 — 달아오른 고리 선에서 불의 벽이 솟고 먹 연기가 오른다, 고리 위 적이 탄다, 끝부터 재로'),
    ('mu', 'R0_bridge_Earth_', ['mu_a.png'], '무 · 토 · ㅜ 둘레 · 한 번 — 땅이 둥근 물결 둔덕으로 솟아 굴러 나가며 고리 위 적을 민다, 흙먼지 도넛, 둘레 땅이 판으로 들림'),
    ('su', 'R0_bridge_Metal_', ['su_a.png'], '수 · 금 · ㅜ 둘레 · 한 번 — 칼날 고리가 톱처럼 돌며 고리 위 적을 벤다(바닥에 KCISA 톱니 고리), 쨍 하고 부서져 파편이 옆으로'),
    ('u', 'R0_bridge_Water_', ['u_b.png'], '우 · 수 · ㅜ 둘레 · 한 번 — 고리를 따라 물기둥이 솟아 먹 리본이 감기고, 고리 위 적에게 쏟아져 적신다, 땅으로 흘러내림'),
]
# batch 3 (2026-10-26, D308-150 V12): --batch b3 -> free_showcase/batch3/<round>/ (남긴다: the cast, then the residue that stays, the expiry warning and the exit).
# Frames are STREAMED to the encoder (a 남긴다 job is 300 - 600 frames; holding 19 of them in memory would take tens of GB).
# Video per glyph: the cast at 1x (age <= cut) -> the same at 0.5x -> the residue segment at 1x to the end, with the capture's JUMP (a LAB-SIMULATED long wait)
# labelled on the frames after it.  --video key=<side-by-side age>[/...]  --cut key=<cast segment end, s> (default 1.8)
B3_SPELLS = [
    ('nak', 'P1_bridge4_Fire_red', ['nak_a.png'], '낙 · 화 · ㅏ 경로 · 남긴다 — 불먹 화살이 첫 적을 태우고, 맞은 자리에 불타는 먹숯 나무가 선다(잔존 10초), 끝부터 타 재로 날림'),
    ('mak', 'P1_bridge4_Earth_red', ['mak_a.png'], '막 · 토 · ㅏ 경로 · 남긴다 — 돌창이 첫 적을 밀치고, 그 자리에 가지처럼 솟은 바위 벼랑이 선다(10초), 가라앉으며 둘레 땅이 판으로'),
    ('sak', 'P1_bridge4_Metal_red', ['sak_a.png'], '삭 · 금 · ㅏ 경로 · 남긴다 — 강철 별이 첫 적을 베고, 그 자리에 칼날 가시덤불이 선다(10초), 쨍 하고 파편이 옆으로'),
    ('ak', 'P1_bridge4_Water_red', ['ak_a.png'], '악 · 수 · ㅏ 경로 · 남긴다 — 물줄기가 첫 적을 적시고, 그 자리에 산호처럼 갈라지는 분수가 솟는다(10초), 아래로 흘러내림'),
    ('geok', 'A1_bridge4_Wood_red', ['geok_a.png'], '걱 · 목 · ㅓ 부착 · 남긴다 극치 — 자기 회복 HP 30 / 3초: 어린 나무와 덩굴이 몸을 감싸 오르고 생명빛이 가슴으로, 다 차면 잔뿌리로 풀림'),
    ('neok', 'A1_bridge4_Fire_red', ['neok_a.png'], '넉 · 화 · ㅓ 부착 · 남긴다 — 붓대와 소매에 불이 붙어 6초, 휘두르면 불의 호, 닿은 적이 탄다, 끝부터 타 재로'),
    ('meok', 'A1_bridge4_Earth_red', ['meok_a.png'], '먹 · 토 · ㅓ 부착 · 남긴다 — 붓끝이 돌 철퇴, 팔에 돌 건틀릿(6초), 닿은 적을 민다, 가라앉으며 발밑 땅이 판으로'),
    ('seok', 'A1_bridge4_Metal_red', ['seok_a.png'], '석 · 금 · ㅓ 부착 · 남긴다 — 붓대에 칼날 가지가 돋아 언월도(6초), 닿은 적을 벤다, 쨍 하고 파편이 옆으로'),
    ('eok', 'A1_bridge4_Water_red', ['eok_a.png'], '억 · 수 · ㅓ 부착 · 남긴다 — 붓끝에서 물 채찍이 감겨 돈다(6초), 닿은 적을 적신다, 팔을 타고 아래로 흘러내림'),
    ('gok', 'F1_bridge4_Wood_red', ['gok_a.png'], '곡 · 목 · ㅗ 면 · 남긴다 극치 — 앞 부채꼴에 어린 숲이 자라며 1.5배로 넓어지고(10초) 들어온 적을 붙든다, 잔뿌리로 풀림'),
    ('nok', 'F1_bridge4_Fire_red', ['nok_a.png'], '녹 · 화 · ㅗ 면 · 남긴다 — 앞 부채꼴이 불타는 갈대밭으로 남아(10초) 안의 적을 태운다, 끝부터 타 재로'),
    ('mok', 'F1_bridge4_Earth_red', ['mok_b.png'], '목 · 토 · ㅗ 면 · 남긴다 — 앞 부채꼴에 현무암 기둥 고원이 솟아(10초) 안의 적을 민다, 가라앉으며 둘레 땅이 판으로'),
    ('sok', 'F1_bridge4_Metal_red', ['sok_a.png'], '속 · 금 · ㅗ 면 · 남긴다 — 앞 부채꼴에 강철 말뚝이 꽂히고 날선 줄로 이어져(10초) 지나는 적을 벤다, 쨍 하고 파편이 옆으로'),
    ('ok', 'F1_bridge4_Water_red', ['ok_b.png'], '옥 · 수 · ㅗ 면 · 남긴다 — 앞 부채꼴이 꼬인 물기둥이 선 늪으로(10초) 안의 적을 적신다, 땅으로 흘러 빠짐'),
    ('guk', 'R1_bridge4_Wood_red', ['guk_a.png'], '국 · 목 · ㅜ 둘레 · 남긴다 극치 — 2 m 고리의 뿌리가 나무 기둥(발판)으로 자란다(10초), 고리 안 내 잔존물 수명 ×1.5, 잔뿌리로 풀림'),
    ('nuk', 'R1_bridge4_Fire_red', ['nuk_a.png'], '눅 · 화 · ㅜ 둘레 · 남긴다 — 2 m 고리에 불 횃불 기둥이 선다(10초), 고리를 넘는 적을 태운다, 끝부터 타 재로'),
    ('muk', 'R1_bridge4_Earth_red', ['muk_a.png'], '묵 · 토 · ㅜ 둘레 · 남긴다 — 2 m 고리에 선돌이 둘러선다(10초), 넘는 적을 민다, 가라앉으며 둘레 땅이 판으로'),
    ('suk', 'R1_bridge4_Metal_red', ['suk_b.png'], '숙 · 금 · ㅜ 둘레 · 남긴다 — 2 m 고리에 칼이 거꾸로 꽂히고 빛 고리와 날선 줄로 이어진다(10초), 넘는 적을 벤다, 쨍 하고 파편이 옆으로'),
    ('uk', 'R1_bridge4_Water_red', ['uk_b.png'], '욱 · 수 · ㅜ 둘레 · 남긴다 — 2 m 고리를 물이 강처럼 돌며 아치로 솟는다(10초), 넘는 적을 적신다, 땅으로 스며 내려감'),
]


# batch 4 (2026-10-26, D308-150 V12): --batch b4 -> free_showcase/batch4/<round>/ (번진다: the strike, the hops to the neighbours, the 3 s marks to their end).
# Same streamed writer as batch 3 (cast 1x -> 0.5x -> the rest 1x with the JUMP labelled); the capture folders carry the opt-in spread=1 suffix (_b4).
B4_SPELLS = [
    ('gan', 'P2_bridge4_b4_Wood_red', ['gan_a.png'], '간 · 목 · ㅏ 경로 · 번진다 — 덩굴 탄이 첫 적을 맞히고, 덩굴이 이웃 적(3 m)마다 아치로 건너가 묶는다(1.0을 나눠 가짐), 발밑 덩굴 자국 3초, 끝에 잔뿌리로 풀림'),
    ('nan', 'P2_bridge4_b4_Fire_red', ['nan_a.png'], '난 · 화 · ㅏ 경로 · 번진다 극치 — 불먹 탄이 터지고 불새가 이웃마다 같은 크기로 날아가 꽂힌다(묽어지지 않음: 각 0.5, 셋까지), 화 자국 3초, 끝부터 타 재로'),
    ('man', 'P2_bridge4_b4_Earth_red', ['man_a.png'], '만 · 토 · ㅏ 경로 · 번진다 — 흙 탄이 첫 적을 밀고, 땅을 타고 이웃마다 번져 민다(1.0을 나눠 가짐), 토 자국 3초, 가라앉으며 둘레 땅이 판으로'),
    ('san', 'P2_bridge4_b4_Metal_red', ['san_b.png'], '산 · 금 · ㅏ 경로 · 번진다 — 쇠날이 첫 적을 베고 이웃마다 번져 벤다(1.0을 나눠 가짐), 금 자국 3초, 쨍 끊기며 파편이 옆으로'),
    ('an', 'P2_bridge4_b4_Water_red', ['an_a.png'], '안 · 수 · ㅏ 경로 · 번진다 — 물 탄이 첫 적을 적시고 이웃마다 번져 적신다(1.0을 나눠 가짐), 물 자국 3초, 아래로 흘러내림'),
    ('geon', 'A2_bridge4_b4_Wood_red', ['geon_a.png'], '건 · 목 · ㅓ 부착 · 번진다 — 덩굴이 붓에 붙고(6초), 휘두르기에 닿은 적에서 이웃으로 번져 묶는다, 자국 3초, 잔뿌리로 풀림'),
    ('neon', 'A2_bridge4_b4_Fire_red', ['neon_a.png'], '넌 · 화 · ㅓ 부착 · 번진다 극치 — 횃불 붓(6초)이 불 몫 셋(예산 1.5)을 싣고, 첫 두 휘두르기에서 이웃으로 날린다(각 0.5), 자국 3초, 끝부터 타 재로'),
    ('meon', 'A2_bridge4_b4_Earth_red', ['meon_b.png'], '먼 · 토 · ㅓ 부착 · 번진다 — 흙이 붓에 붙고(6초), 휘두르기에 닿은 적에서 이웃으로 번져 민다, 자국 3초, 가라앉으며 땅판'),
    ('seon', 'A2_bridge4_b4_Metal_red', ['seon_b.png'], '선 · 금 · ㅓ 부착 · 번진다 — 쇠날이 붓에 붙고(6초), 휘두르기에 닿은 적에서 이웃으로 번져 벤다, 자국 3초, 쨍 끊기며 파편'),
    ('eon', 'A2_bridge4_b4_Water_red', ['eon_b.png'], '언 · 수 · ㅓ 부착 · 번진다 — 물이 붓에 붙고(6초), 휘두르기에 닿은 적에서 이웃으로 번져 적신다, 자국 3초, 아래로 흘러내림'),
    ('gon', 'F2_bridge4_b4_Wood_red', ['gon_a.png'], '곤 · 목 · ㅗ 면 · 번진다 — 부채꼴 덩굴이 가장자리에서 바깥 적으로 번져 묶는다, 자국 3초, 잔뿌리로 풀림'),
    ('non', 'F2_bridge4_b4_Fire_red', ['non_a.png'], '논 · 화 · ㅗ 면 · 번진다 극치 — 부채꼴 불이 끝까지 번진 뒤 되돌아 번진다(역화: 1.0 + 되번짐 0.5), 자국 3초 두 겹, 끝부터 타 재로'),
    ('mon', 'F2_bridge4_b4_Earth_red', ['mon_a.png'], '몬 · 토 · ㅗ 면 · 번진다 — 부채꼴 흙이 가장자리에서 바깥 적으로 번져 민다, 자국 3초, 가라앉으며 땅판'),
    ('son', 'F2_bridge4_b4_Metal_red', ['son_b.png'], '손 · 금 · ㅗ 면 · 번진다 — 부채꼴 쇠날이 가장자리에서 바깥 적으로 번져 벤다, 자국 3초, 쨍 끊기며 파편'),
    ('on', 'F2_bridge4_b4_Water_red', ['on_a.png'], '온 · 수 · ㅗ 면 · 번진다 — 부채꼴 물이 가장자리에서 바깥 적으로 번져 적신다, 자국 3초, 아래로 흘러내림'),
    ('gun', 'R2_bridge4_b4_Wood_red', ['gun_a.png'], '군 · 목 · ㅜ 둘레 · 번진다 — 2 m 덩굴 고리를 넘는 적에서 바깥으로 번져 묶는다, 자국 3초, 잔뿌리로 풀림'),
    ('nun', 'R2_bridge4_b4_Fire_red', ['nun_a.png'], '눈 · 화 · ㅜ 둘레 · 번진다 극치 — 불 고리가 목 재료(덩굴)를 따라 끝까지 번진다(실험실 연출: 덩굴 재료를 깔아 둠), 덩굴 안의 적이 1.0, 적 장판도 태움'),
    ('mun', 'R2_bridge4_b4_Earth_red', ['mun_a.png'], '문 · 토 · ㅜ 둘레 · 번진다 — 2 m 흙 고리를 넘는 적에서 바깥으로 번져 민다, 자국 3초, 가라앉으며 땅판'),
    ('sun', 'R2_bridge4_b4_Metal_red', ['sun_a.png'], '순 · 금 · ㅜ 둘레 · 번진다 — 2 m 쇠날 고리를 넘는 적에서 바깥으로 번져 벤다, 자국 3초, 쨍 끊기며 파편'),
    ('un', 'R2_bridge4_b4_Water_red', ['un_a.png'], '운 · 수 · ㅜ 둘레 · 번진다 — 2 m 물 고리를 넘는 적에서 바깥으로 번져 적신다, 자국 3초, 아래로 흘러내림'),
]


# batch 5 (2026-10-27, D308-150 V12): --batch b5 -> free_showcase/batch5/<round>/ (심는다: the cast, the seed waiting, the trigger, the exits by end reason).
# Same streamed writer; the trigger (the capture's LAB-SIMULATED step: a follow-up hit / the player hit / a walk-in / a crossing / the caster on the band)
# is replayed at 0.5x from 0.15 s before to 0.9 s after it. Folders carry seed=1 (_b5) or the expiry demo seed=expire (_b5x). Keys ending in 'x' = the demo.
B5_SPELLS = [
    ('gam', 'P3_bridge4_b5_Wood_red', ['gam_a.png'], '감 · 목 · ㅏ 경로 · 심는다 — 지나는 적 셋에 덩굴 씨앗(8초 대기), 후속 직접 명중 · 갈무리에 작동해 붙든다, 아니면 시든다'),
    ('nam', 'P3_bridge4_b5_Fire_red', ['nam_b.png'], '남 · 화 · ㅏ 경로 · 심는다 — 지나는 적 셋에 불 씨앗(8초 대기), 후속 직접 명중 · 갈무리에 작동해 데운다(2초 화상), 아니면 재로'),
    ('mam', 'P3_bridge4_b5_Earth_red', ['mam_c.png'], '맘 · 토 · ㅏ 경로 · 심는다 극치 — 흙 씨앗이 후속 직접 명중 · 갈무리에 작동해 적을 땅에 짓누른다(이동 불가 1.5초 + 대피해)'),
    ('sam', 'P3_bridge4_b5_Metal_red', ['sam_b.png'], '삼 · 금 · ㅏ 경로 · 심는다 — 지나는 적 셋에 쇠날 씨앗(8초 대기), 후속 직접 명중 · 갈무리에 작동해 벤다, 아니면 쨍 끊김'),
    ('am', 'P3_bridge4_b5_Water_red', ['am_b.png'], '암 · 수 · ㅏ 경로 · 심는다 — 지나는 적 셋에 물 씨앗(8초 대기), 후속 직접 명중 · 갈무리에 작동해 적신다(4초), 아니면 흘러내림'),
    ('geom', 'A3_bridge4_b5_Wood_red', ['geom_a.png'], '검 · 목 · ㅓ 부착 · 심는다 — 내 몸에 덩굴 씨앗(8초), 내가 맞으면 작동해 둘레 2 m를 붙든다(피해는 그대로)'),
    ('neom', 'A3_bridge4_b5_Fire_red', ['neom_a.png'], '넘 · 화 · ㅓ 부착 · 심는다 — 내 몸에 불 씨앗(8초), 내가 맞으면 작동해 둘레 2 m를 데운다(피해는 그대로)'),
    ('meom', 'A3_bridge4_b5_Earth_red', ['meom_b.png'], '멈 · 토 · ㅓ 부착 · 심는다 극치 — 내 몸의 흙 씨앗이 내가 맞을 때 작동: 둘레 2 m 흙 솟음 밀침 + 그 피격의 경직 면제'),
    ('seom', 'A3_bridge4_b5_Metal_red', ['seom_b.png'], '섬 · 금 · ㅓ 부착 · 심는다 — 내 몸에 쇠날 씨앗(8초), 내가 맞으면 작동해 둘레 2 m를 벤다(피해는 그대로)'),
    ('eom', 'A3_bridge4_b5_Water_red', ['eom_a.png'], '엄 · 수 · ㅓ 부착 · 심는다 — 내 몸에 물 씨앗(8초), 내가 맞으면 작동해 둘레 2 m를 적신다(피해는 그대로)'),
    ('gom', 'F3_bridge4_b5_Wood_red', ['gom_b.png'], '곰 · 목 · ㅗ 면 · 심는다 — 앞 부채꼴에 덩굴 씨앗(8초), 적이 들어오면 작동해 안의 적을 붙든다'),
    ('nom', 'F3_bridge4_b5_Fire_red', ['nom_b.png'], '놈 · 화 · ㅗ 면 · 심는다 — 앞 부채꼴에 불 씨앗(8초), 적이 들어오면 작동해 안의 적을 데운다'),
    ('mom', 'F3_bridge4_b5_Earth_red', ['mom_a.png'], '몸 · 토 · ㅗ 면 · 심는다 극치 — 흙 거인이 웅크려 기다리다(8초) 적이 오면 한 번 내려찍고 무너져 토 재료 면(10초)이 된다'),
    ('som', 'F3_bridge4_b5_Metal_red', ['som_a.png'], '솜 · 금 · ㅗ 면 · 심는다 — 앞 부채꼴에 쇠날 씨앗(8초), 적이 들어오면 작동해 안의 적을 벤다'),
    ('om', 'F3_bridge4_b5_Water_red', ['om_a.png'], '옴 · 수 · ㅗ 면 · 심는다 — 앞 부채꼴에 물 씨앗(8초), 적이 들어오면 작동해 안의 적을 적신다'),
    ('gum', 'R3_bridge4_b5_Wood_red', ['gum_a.png'], '굼 · 목 · ㅜ 둘레 · 심는다 — 2 m 고리에 덩굴 씨앗(8초), 적이 고리를 넘으면 작동해 붙든다'),
    ('num', 'R3_bridge4_b5_Fire_red', ['num_a.png'], '눔 · 화 · ㅜ 둘레 · 심는다 — 2 m 고리에 불 씨앗(8초), 적이 고리를 넘으면 작동해 데운다'),
    ('mum', 'R3_bridge4_b5_Earth_red', ['mum_b.png'], '뭄 · 토 · ㅜ 둘레 · 심는다 극치 — 고리의 흙 씨앗이 내가 올라서면 땅으로 굳는다(8초): 고리 안 적 돌진 · 도약 차단'),
    ('sum', 'R3_bridge4_b5_Metal_red', ['sum_b.png'], '숨 · 금 · ㅜ 둘레 · 심는다 — 2 m 고리에 쇠날 씨앗(8초), 적이 고리를 넘으면 작동해 벤다'),
    ('um', 'R3_bridge4_b5_Water_red', ['um_a.png'], '움 · 수 · ㅜ 둘레 · 심는다 — 2 m 고리에 물 씨앗(8초), 적이 고리를 넘으면 작동해 적신다'),
]


def b3_main(rnd, ages, cuts, only, batch='b3'):
    import imageio_ffmpeg
    base = REPO / ('Art/SpellVFX308/Vol2Vfx/free_showcase/' + ('batch5' if batch == 'b5' else 'batch4' if batch == 'b4' else 'batch3'))
    spells = B5_SPELLS if batch == 'b5' else B4_SPELLS if batch == 'b4' else B3_SPELLS
    if batch == 'b5' and '--demo' in sys.argv:
        # the expiry demos (seed=expire): the same glyphs, the _b5x folders, keys + 'x'
        spells = [(k + 'x', f.replace('_b5_', '_b5x_'), c, t.split(' — ')[0] + ' — 작동 없이 수명 끝(시연: 조건이 오지 않음)') for k, f, c, t in B5_SPELLS]
        # + the 信 demo (seedhold=1): a letter drawn mid-wait holds the waiting seeds (the hub's own pause; their expiry moves out by it)
        spells += [(k + 'h', f.replace('_b5_', '_b5h_'), c, t.split(' — ')[0] + ' — 信: 글자를 쓰는 동안 대기 시계 멈춤(시연)') for k, f, c, t in B5_SPELLS]
    concepts = base / 'concepts'
    frames_root = base / rnd / 'frames'

    def open_writer(dst):
        g = imageio_ffmpeg.write_frames(str(dst), (W, H), fps=60, codec='libx264', pix_fmt_out='yuv420p', output_params=['-crf', '19', '-preset', 'medium'], macro_block_size=8)
        g.send(None)
        return g

    combined_name = ('free_showcase_batch5_demo_all.mp4' if '--demo' in sys.argv else 'free_showcase_batch5_all.mp4') if batch == 'b5' else 'free_showcase_batch4_all.mp4' if batch == 'b4' else 'free_showcase_batch3_all.mp4'
    combined = None if only else open_writer(base / rnd / combined_name)
    for key, folder, cons, text in spells:
        if only and key not in only:
            continue
        d = frames_root / folder
        if not (d / 'index.csv').exists():
            print('NO FRAMES', key, d); continue
        with open(d / 'index.csv', encoding='utf-8', errors='replace') as f:
            rs = [r for r in csv.DictReader(f) if r.get('video') == '1' and (d / ('f_%05d.jpg' % int(r['frame']))).exists()]
        if not rs:
            print('NO VIDEO FRAMES', key); continue
        cut = cuts.get(key, 1.1 if batch == 'b5' else 1.8)
        name = text.split(' — ')[0]
        # side by side at the asked ages
        for i, age in enumerate(ages.get(key, [1.2])):
            best = min(rs, key=lambda r: abs(float(r['ageSec']) - age))
            eng = label(Image.open(d / ('f_%05d.jpg' % int(best['frame']))).resize((W, H)), 'ENGINE (실제 게임 카메라) · %.2f s' % float(best['ageSec']))
            con = label(Image.open(concepts / cons[0]).resize((W, H)), 'CONCEPT ' + cons[0][:-4])
            sbs = Image.new('RGB', (W * 2 + 16, H), (20, 20, 20)); sbs.paste(con, (0, 0)); sbs.paste(eng, (W + 16, 0))
            dst = base / rnd / f'{key}_side_by_side{"" if i == 0 else "_" + str(i + 1)}.jpg'
            sbs.save(dst, quality=92); print('side', dst)
        one = open_writer(base / rnd / f'{key}_cast_half_then_lasting.mp4')
        writers = [w for w in (one, combined) if w is not None]

        def send(im, times=1):
            b = im.convert('RGB').tobytes()
            for w in writers:
                for _ in range(times):
                    w.send(b)

        if combined is not None:
            t = label(Image.open(concepts / cons[0]).resize((W, H)), 'CONCEPT (자유 시안) · ' + cons[0][:-4], text)
            b = t.convert('RGB').tobytes()
            for _ in range(90):
                combined.send(b)
        cast = [r for r in rs if float(r['ageSec']) <= cut]
        rest = [r for r in rs if float(r['ageSec']) > cut]
        for r in cast:
            send(label(Image.open(d / ('f_%05d.jpg' % int(r['frame']))).resize((W, H)), name + ' · 시전 1×', 't = %s s' % r['ageSec']))
        for r in cast:
            send(label(Image.open(d / ('f_%05d.jpg' % int(r['frame']))).resize((W, H)), name + ' · 시전 0.5×', 't = %s s' % r['ageSec']), 2)
        jumped, jump_note, jump_left = False, '', 0
        # batch 5: the trigger step's age (the first LAB-SIMULATED step that is not the 信 drawing) - replayed at 0.5x around it
        trig = None
        if batch == 'b5':
            for r in rs:
                n = r.get('note', '') or ''
                if 'LAB-SIMULATED' in n and 'drawn' not in n and 'drawing' not in n and 'JUMP' not in n:
                    trig = float(r['ageSec']); break
        fired = False
        for r in rest:
            note = r.get('note', '') or ''
            if 'JUMP' in note:
                jumped, jump_note, jump_left = True, note, 75
            sub = 't = %s s' % r['ageSec']
            if jumped:
                sub += ('   [시간 건너뜀: 실험실 대기 생략 → 자국 만료 1.4 초 전부터]' if batch == 'b4' else '   [시간 건너뜀: 실험실 긴 대기 생략 → 만료 1.4 초 전부터]') if jump_left > 0 else '   [시간 건너뜀 뒤]'
                jump_left -= 1
            if 'LAB-SIMULATED' in note:
                sub += '   [' + note.replace('LAB-SIMULATED: ', '실험실 연출: ')[:60] + ']'
            age = float(r['ageSec'])
            if batch == 'b5':
                if trig is not None and age >= trig - 0.15:
                    fired = True
                slow = trig is not None and trig - 0.15 <= age <= trig + 0.9 and not jumped
                phase = (' · 작동 0.5×' if slow else ' · 작동 뒤 1×') if fired else ' · 씨앗 대기 1×'
                send(label(Image.open(d / ('f_%05d.jpg' % int(r['frame']))).resize((W, H)), name + phase, sub), 2 if slow else 1)
                continue
            send(label(Image.open(d / ('f_%05d.jpg' % int(r['frame']))).resize((W, H)), name + (' · 번짐 · 자국 1×' if batch == 'b4' else ' · 남은 것 1×'), sub))
        one.close()
        print('mp4', key, len(cast), 'cast frames +', len(rest), 'lasting frames')
    if combined is not None:
        combined.close()
        print('mp4 combined', base / rnd / combined_name)


def b3_entry():
    rnd = sys.argv[1]
    ages, cuts = {}, {}
    for flag, dst in (('--video', ages), ('--cut', cuts)):
        if flag in sys.argv:
            for a in sys.argv[sys.argv.index(flag) + 1:]:
                if a.startswith('--'):
                    break
                k, v = a.split('=')
                dst[k] = [float(x) for x in v.split('/')] if flag == '--video' else float(v)
    only = sys.argv[sys.argv.index('--only') + 1].split(',') if '--only' in sys.argv else None
    b3_main(rnd, ages, cuts, only, sys.argv[sys.argv.index('--batch') + 1])


if '--batch' in sys.argv and sys.argv[sys.argv.index('--batch') + 1] == 'b1':
    BASE = BASE / 'batch1'
    CONCEPTS = BASE / 'concepts'
    SPELLS = B1_SPELLS
if '--batch' in sys.argv and sys.argv[sys.argv.index('--batch') + 1] == 'b2':
    BASE = BASE / 'batch2'
    CONCEPTS = BASE / 'concepts'
    SPELLS = B2_SPELLS
W, H = 1280, 720
FONT = 'C:/Windows/Fonts/malgunbd.ttf'


def font(size):
    try:
        return ImageFont.truetype(FONT, size)
    except OSError:
        return ImageFont.load_default()


def job_dir(rnd, prefix):
    frames = BASE / rnd / 'frames'
    c = sorted([d for d in frames.iterdir() if d.is_dir() and d.name.startswith(prefix)]) if frames.exists() else []
    return c[0] if c else None


def rows(d):
    with open(d / 'index.csv', encoding='utf-8', errors='replace') as f:
        return [r for r in csv.DictReader(f) if r.get('video') == '1']


def frame_path(d, r):
    return d / ('f_%05d.jpg' % int(r['frame']))


def label(img, text, sub=None):
    img = img.convert('RGB'); dr = ImageDraw.Draw(img)
    f = font(26); x, y = 18, 14
    bb = dr.textbbox((x, y), text, font=f)
    dr.rectangle((bb[0] - 8, bb[1] - 6, bb[2] + 8, bb[3] + 6), fill=(0, 0, 0))
    dr.text((x, y), text, font=f, fill=(255, 255, 255))
    if sub:
        f2 = font(20); bb = dr.textbbox((x, H - 44), sub, font=f2)
        dr.rectangle((bb[0] - 8, bb[1] - 6, bb[2] + 8, bb[3] + 6), fill=(0, 0, 0))
        dr.text((x, H - 44), sub, font=f2, fill=(255, 230, 200))
    return img


def sheet(rnd, key, d):
    rs = rows(d)
    step = max(1, len(rs) // 40)
    pick = rs[::step][:40]
    tw, th = 320, 180
    out = Image.new('RGB', (tw * 8, th * ((len(pick) + 7) // 8)), (0, 0, 0))
    dr = ImageDraw.Draw(out)
    for i, r in enumerate(pick):
        p = frame_path(d, r)
        if not p.exists():
            continue
        im = Image.open(p).resize((tw, th))
        out.paste(im, ((i % 8) * tw, (i // 8) * th))
        dr.text(((i % 8) * tw + 6, (i // 8) * th + 4), '%s f%s' % (r['ageSec'], r['frame']), font=font(16), fill=(255, 255, 0))
    dst = BASE / rnd / 'media' / f'{key}_sheet.jpg'
    dst.parent.mkdir(parents=True, exist_ok=True)
    out.save(dst, quality=88)
    print('sheet', dst)


def at_age(d, age):
    rs = rows(d)
    best = min(rs, key=lambda r: abs(float(r['ageSec']) - age))
    return frame_path(d, best), best


def side_by_side(rnd, key, d, ages, concepts):
    outs = []
    for i, age in enumerate(ages):
        p, r = at_age(d, age)
        eng = label(Image.open(p).resize((W, H)), 'ENGINE (실제 게임 카메라) · %.2f s' % float(r['ageSec']))
        con = label(Image.open(CONCEPTS / concepts[min(i, len(concepts) - 1)]).resize((W, H)), 'CONCEPT ' + concepts[min(i, len(concepts) - 1)][:-4])
        sbs = Image.new('RGB', (W * 2 + 16, H), (20, 20, 20))
        sbs.paste(con, (0, 0)); sbs.paste(eng, (W + 16, 0))
        dst = BASE / rnd / f'{key}_side_by_side{"" if i == 0 else "_" + str(i + 1)}.jpg'
        sbs.save(dst, quality=92)
        outs.append(dst)
        print('side', dst)
    return outs


def frames_for(d, text):
    rs = rows(d)
    one = [label(Image.open(frame_path(d, r)).resize((W, H)), text + ' · 1×', 't = %s s' % r['ageSec']) for r in rs if frame_path(d, r).exists()]
    half = []
    for r in rs:
        p = frame_path(d, r)
        if p.exists():
            im = label(Image.open(p).resize((W, H)), text + ' · 0.5×', 't = %s s' % r['ageSec'])
            half += [im, im]
    return one, half


def write_mp4(dst, frames, fps=60):
    import imageio_ffmpeg
    gen = imageio_ffmpeg.write_frames(str(dst), (W, H), fps=fps, codec='libx264', pix_fmt_out='yuv420p', output_params=['-crf', '19', '-preset', 'medium'], macro_block_size=8)
    gen.send(None)
    for im in frames:
        gen.send(im.convert('RGB').tobytes())
    gen.close()
    print('mp4', dst, len(frames))


def title(concept, text, seconds=1.5, fps=60):
    im = Image.open(CONCEPTS / concept).resize((W, H))
    im = label(im, 'CONCEPT (자유 시안) · ' + concept[:-4], text)
    return [im] * int(seconds * fps)


def main():
    if '--batch' in sys.argv and sys.argv[sys.argv.index('--batch') + 1] in ('b3', 'b4', 'b5'):
        b3_entry()
        return
    rnd = sys.argv[1]
    ages = {}
    if '--video' in sys.argv:
        for a in sys.argv[sys.argv.index('--video') + 1:]:
            if a.startswith('--'):
                break
            k, v = a.split('='); ages[k] = [float(x) for x in v.split('/')]
    combined = []
    for key, prefix, concepts, text in SPELLS:
        d = job_dir(rnd, prefix)
        if d is None:
            print('NO FRAMES', key); continue
        sheet(rnd, key, d)
        if key not in ages:
            continue
        side_by_side(rnd, key, d, ages[key], concepts)
        one, half = frames_for(d, text.split(' — ')[0])
        write_mp4(BASE / rnd / f'{key}_1x_then_half.mp4', one + half)
        combined += title(concepts[0], text) + one + half
    if combined:
        write_mp4(BASE / rnd / ('free_showcase_batch2_all.mp4' if SPELLS is B2_SPELLS else 'free_showcase_batch1_all.mp4' if SPELLS is B1_SPELLS else 'free_showcase_all.mp4'), combined)


if __name__ == '__main__':
    main()
