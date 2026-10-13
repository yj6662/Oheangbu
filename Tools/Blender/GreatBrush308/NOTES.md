# GreatBrush308 — 양손 큰 붓(SPEC-SPELL-SWING-308 1단계)

상태: 스틸 판정 대기. Unity에는 아무것도 넣지 않았다(출력은 `Art/Characters308/GreatBrush308/`).

## 다시 만드는 순서 (Blender 5.0 헤드리스, 한 번에 하나)

```
blender -b --factory-startup -P build.py  -- <out>            # 붓 .blend · .fbx · 붓털 그림
blender -b --factory-startup -P verify.py -- <out>/SM_GreatBrush308.fbx   # 다시 들여와 계약 검사
bash run.sh [자세 ...] [--res 1280] [--hands] [--only-metrics] [--refine]   # 자세 스틸(resource_guard --gpu-run 경유)
SCRIPT=close.py bash run.sh                                     # 붓만 가까이
python sheet.py                                                 # sheet.png
```

- `poses.py` = 자세 자료(몸 회전 · 붓 자리 · 방향). `--refine`은 적힌 값 둘레에서 팔 · 손목이 편한 곳을 찾아 출력만 한다(값은 손으로 다시 적는다).
- 손은 소켓에 해석식 두 뼈 IK로 붙는다. 손가락은 자루 굵기에 닿을 때까지 감는다(모든 자세가 같은 쥐기).
- 렌더는 Workbench(편집기가 GPU 메모리 85 %를 쓰는 중이라 EEVEE는 쓰지 않았다).

## 참고 자료와 가져온 것

1. https://www.1stdibs.com/furniture/asian-art-furniture/scholars-objects/monumental-calligraphy-brush-37-marble-rings/id-f_31420612
   큰 글씨용 붓 실물(길이 37인치 ≈ 94 cm, 붓머리 폭 4인치 ≈ 10 cm, 말털, 굵은 목). → 붓털 뿌리 지름 9 cm · 붓머리가 자루보다 훨씬 굵고 목(잔 모양)이 그 사이를 잇는 비례. 우리 붓은 길이만 1.55 m로 늘렸다.
2. https://beyond-calligraphy.com/?p=25619 (붓의 부위 이름)
   붓털 → 목(軸先) → 자루(筆管) → 꼬리 → 걸이 끈(掛紐). 붓털은 먹을 머금는 배(腹)와 탄력을 내는 허리(腰)로 나뉜다. → 꼬리 고리 + 끈 · 술, 붓털 윤곽(배에서 부풀고 허리 뒤로 뾰족), 뼈 8개를 허리 – 끝에 고르게 둔 까닭.
3. https://thewiki.kr/w/붓 — **본문 미확인**(403, 검색 요약만 봤다): 퍼포먼스 · 대형 작품용 "사람 키만 한 초대형 붓"이 따로 있다는 것. → 사람 키에 가까운 길이.
   https://news.knn.co.kr/tv/nft1/vods/165/130096 — **본문 미확인**(검색 요약만): 붓장인이 붓대는 곧아야 하고 오죽을 주로 쓴다고 말한 것. → 검은 대나무 마디 자루.

확인 수준: 1 · 2는 본문을 열어 확인했다(1의 자루는 대리석 고리 장식이고 목은 통나무 — 놋쇠가 아니다. 2는 걸이 끈이 있다고만 하고 붙는 자리는 말하지 않는다). 3은 요약뿐이다.
확인하지 못한 것: 한국 대붓의 목 재질(놋쇠는 Spec 지정이다 — 실물은 뿔 · 나무 · 대나무 목이 많다), 대붓 퍼포먼스의 양손 쥐기 자세 사진.

## 쥐기에 대해 알게 된 것

- 두 손 모두 엄지가 붓끝을 향한다(방망이 쥐기). 팔이 0.51 m로 짧고 손 간격이 0.42 m라, 두 손이 가슴 – 허리 높이에 있고 붓이 위 · 앞으로 향할 때 손목이 가장 편하다.
- 붓끝을 아래로 세워 꽂는 자세는 이 쥐기로 아래팔이 한계 밖으로 돌아간다(엄지가 아래). 그래서 f는 붓끝을 위로 세운 막기 자세다.
- 꼬리에서 오른손까지 0.60 m라 꼬리를 땅에 대려면 무릎을 꿇어야 한다.

## v3 — 1차 비례 + 가닥 붓털 (2026-10-13)

출력 `Art/Characters308/GreatBrush308/v3/`. 1차 · 2차 출력과 스크립트는 건드리지 않는다. Unity 미반영.

```
bash run_v3.sh build_v3.py [--counts 12,18,26,32] [--rings 9] [--seed 3083]   # .blend · .fbx · 붓털 그림 · build-report.json
bash run_v3.sh verify_v3.py                                                   # FBX 다시 들여와 검사 → v3/verify.log
bash run_v3.sh close_v3.py [--only rest,head_side,…] [--sub iterN]            # 스틸 9장(Workbench 1280x720)
python sheet_v3.py [--sub iterN]                                              # v3/sheet.png
```

- 붓털 = 어두운 속심 + 가닥 다발 88개(4겹 12 · 18 · 26 · 32, 그중 7개는 바깥으로 삐친다). 전체 7,718 삼각형.
- 뼈 33개: BrushHandle + Bristle_01..08 + BristleRing_A..F_01..04(60도, `_01` 의 부모 = Bristle_01, 연결 안 함). 꼭짓점당 영향 ≤ 4(가운데 2 + 가까운 고리 사슬 둘의 뼈 하나씩).
- 꼭짓점 색 `Col`(선형 float, 면 모서리): R = 가닥 난수, G = 뿌리→끝, B = 겹(0 = 속심). 내보내기 `colors_type='LINEAR'`.
- BristlePress 는 로컬 Y(Blender)로 납작해지고 로컬 X 로 퍼진다 — 사슬을 로컬 X 축으로 꺾어 쓴다(끌리는 방향이 다르면 붓을 자루 축으로 돌려 맞춰야 한다).
- 함정: Blender 5 의 `shape_key_add` 는 값을 1.0 으로 만든다. v3 는 0 으로 되돌려 내보낸다. **1차 · 2차 FBX 에는 두 키가 1.0 으로 들어 있다**(Unity 임포트 때 기본 가중치 확인 필요).

## v4 — 더 세밀하고 약간만 더 부드럽게 (2026-10-13)

출력 `Art/Characters308/GreatBrush308/v4/`. v3 출력 · 스크립트는 읽기만 한다.

```
python fork_v4.py                      # build_v3.py → build_v4.py · verify_v4.py · probe_v4.py · run_v4.sh (조정 값은 fork_v4.py 에 있다 — build_v4.py 를 손으로 고치면 덮어쓰인다)
bash run_v4.sh build_v4.py             # → verify_v4.py → close_v4.py [--only …] [--sub iterN] ;  python sheet_v4.py
```

- 가닥 204개 = 다섯 겹 14 · 24 · 36 · 50 · 56 + 맨 바깥 잔털 24. 전체 15,644 삼각형(상한 18,000). 뼈 · 소켓 · 키 · `Col` 채널은 v3 와 같다(B 는 6단계: 잔털 = 1).
- 붓끝 전체가 한쪽으로 1.1 cm 기운다(LEAN) — TipSocket 은 축 위 그대로다.
- 퍼짐은 극좌표로 만든다(바깥 겹 세제곱 비례 · 끝으로 갈수록 휨 · 일곱 무리로 당김). 무리 당김을 .5 넘게 주면 가닥이 서로 엇갈려 헝클어진 머리처럼 보인다.

## v5 — 조금 더 부드럽게 + 먹물에 젖은 붓 (2026-10-13)

출력 `Art/Characters308/GreatBrush308/v5/`. v3 · v4 는 읽기만 한다. 먹방울 메시 · `M_GreatBrush_InkDrop` 은 사용자가 뺐다(만들지 않는다).

```
python fork_v5.py            # build_v4.py → build_v5.py · verify_v5.py · probe_v5.py · close_v5.py · run_v5.sh (조정 값은 fork_v5.py)
bash run_v5.sh build_v5.py   # → verify_v5.py → close_v5.py [--only …] [--sub iterN] [--rough .26] ;  python sheet_v5.py
```

- 젖은 다발 17개(바깥 두 겹 12 + 셋째 겹 5, 3–6가닥씩 71가닥) + 가운데 다발(안쪽 두 겹 38가닥 = 붓끝). 다발 안 가닥은 가중치 · 퍼짐 · 눌림 방향을 같이 쓴다.
- `Col.A` = 젖음 0–1(가닥 · 다발마다 먹이 오른 높이가 다르다). UV v 는 이제 "먹 양"이다(뿌리→끝은 `Col.G`). 붓털이 아닌 곳의 색은 (0,0,0,0).
- `TipSocket` = 실제 털끝: 축에서 1.5 cm 벗어난다(v4 까지는 축 위).
- 스틸의 윤기는 렌더 전용이다 — close_v5.py 가 `Col.A > 0.03` 인 면을 윤나는 재질 사본으로 나눈다. FBX 의 붓털 재질은 하나 그대로이고, 게임에서는 셰이더가 A 로 윤기를 내야 한다.
- 함정: 속심이 길면 가닥이 가운데로 모이는 붓끝에서 속심이 뭉툭한 통으로 튀어나온다(속심은 0.70 에서 끝낸다). Workbench 는 roughness 가 너무 낮으면(0.12) 반사가 실선이 되어 오히려 덜 젖어 보인다(0.26).
