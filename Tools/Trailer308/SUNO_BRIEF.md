# SUNO 브리프 — 트레일러 4차 음악 (30초 발표용 · 90초 홍보용)

2026-10-11 · 계획서 §3 · D308-69(TEST). Suno는 사용자가 직접 돌린다(human-in-the-loop, PROD-AIASSET). 나는 브리프와 뒤처리만 한다.
가편집은 지난 ElevenLabs 곡(`music/bgm308_30_1.mp3` · `bgm308_90_1.mp3`, TEST)으로 돌아가고 있다 — **ElevenLabs 곡은 납품 금지**.

## 공통 규칙 (두 곡 모두)

- **Instrumental.** 보컬 · 합창 · 허밍 · 가사 0.
- **국악만.** 신스 패드 · 서양 오케스트라(현악 섹션 · 금관 · 피아노) · 드럼 세트 · 일렉 기타 금지. "cinematic epic" 류 금지.
- ArtAudio 팔레트(`오행부_ArtAudio_Bible_v0_1.md` 국악 항): **거문고 = 공통 바탕** / 청림 = 대금 · 생황 / 적로 = 태평소 · 북 / 철옹 = 나발 · 나각 · 용고 / 현강 = 아쟁 · 해금 / 황경 = 편종.
- 어둡고 절제된 톤, 잔향 깊음, 템포는 느리게 시작해 뒤에서 몰아친다. 멜로디는 한 줄(대금) — 두 선율이 겹치지 않게.
- 정지(stopdown)와 타격은 **곡 안에 있어야 한다** — 편집에서 자르면 꼬리가 끊긴다. 다만 자르기 · 정지 보강 · 타격 보강은 `audio.py`가 엔벌로프로 한 번 더 한다.
- 길이는 **넉넉히** (30초판 ≥ 35 s · 90초판 ≥ 95 s). 끝은 한 음이 울려 사라지는 것으로.

## 30초 (발표용 · 900 f) — 구조

| 초 | 화면 | 음악 |
|---|---|---|
| 0–4 | Cold Open — 먹 번짐, 적이 달려들고 '나'를 써서 발사 · 적중 | **거의 무음.** 북 한 번(0.3 s) + 거문고 저음 한 음이 길게. 4초에 적중하는 impact는 효과음이 낸다 — 음악은 자리를 비운다 |
| 4–17 | 세계(산수 → 자동차 → 잔도) → 뭄 → 전투 | **흐름.** 거문고 오스티나토 위에 대금 한 줄(청림). 10–11초 생황이 잠깐. 14–17초 북 · 장구가 들어와 전투 |
| 17–19 | 사찰 마당 연등, 정적 | **완전 정지.** 소리 0(잔향만 2초). 이 2초가 곡 안에 있어야 한다 |
| 19–26 | 거인 실루엣 급접근 → 획 · 발사 · 적중 · 회피 4컷 가속 | **용고 폭발.** 19.0초에 가장 큰 타격(용고 + 나발), 그 뒤 7초는 용고 · 장구가 1.2/1.0/0.9/0.9 s 간격으로 몰아친다. 멜로디 없음, 리듬만 |
| 26–30 | 암전 0.5 → 로고 3.5 | **잔향.** 26초의 타격 하나가 울려 퍼지고, 28.3초 낙관 자리에 편종 한 번(또는 북 한 번). 30초에 소리 0 |

BPM 제안: 72 → 96(19초부터). 조성: 평조(G 중심 5음계)나 계면조, 하나만.

**Style 프롬프트 (영문, Suno "Style of Music" 칸)**

```
Korean traditional instrumental trailer cue, dark and restrained, no vocals, no choir, no synth pads, no western orchestra.
Instruments only: geomungo (low plucked zither, constant foundation), daegeum (bamboo flute, single melodic line), saenghwang (brief),
buk and janggu drums, yonggo war drum, nabal (long brass horn) for one blast, pyeonjong (bronze bells) once at the end.
Structure 0-4s near silence: one buk hit and one long low geomungo note. 4-17s slow flowing ostinato on geomungo with a sparse daegeum melody,
drums enter at 14s. 17-19s complete stop, only reverb tail. 19s the biggest hit: yonggo and nabal together, then 19-26s accelerating
drum pattern only, no melody. 26-30s one struck note rings out into silence. Tempo 72 then 96 bpm. Deep reverb, ink-painting mood, cinematic but minimal.
```

한글 설명(사용자용): 앞 4초는 거의 비워 두고(적중 소리가 들려야 함), 4–17초는 거문고 바탕에 대금 한 줄로 흐르다가 14초에 북 · 장구가 들어오고, 17–19초는 **아예 멈춘다**, 19초에 용고 · 나발 한 방, 그 뒤 7초는 북만 몰아치고, 26초 타격 하나가 30초까지 울려 사라진다.

## 90초 (홍보용 · 2,700 f) — 구조

강토 순서대로 악기를 바꾼다(청림 → 황경 → 적로 → 철옹 → 현강 → 사찰).

| 초 | 화면 | 음악 |
|---|---|---|
| 0–5 | Cold Open(훅) | 거의 무음. 북 한 번 + 거문고 저음 |
| 5–18 | 산수 급접근 → 벌목마을 → 자동차 | 청림: 거문고 오스티나토 + 대금. 12초 자동차부터 장구가 가볍게 |
| 18–21 | 황경 궁성 | 편종 두세 음이 멜로디를 받는다 |
| 21–31 | 적로 군영 → 전투 ① | 적로: 태평소가 대금을 밀어내고, 북이 전투를 친다(24–31) |
| 31–39 | 뭄(강 건너기, 8초) | 넓게. 아쟁 저음 + 대금, 북 없음 |
| 39–46 | 철옹 병기창 → 벼랑 잔도 | 철옹: 나각(소라) + 용고 느린 박 |
| 46–53 | 전투 ② | 용고 · 장구 전투, 해금이 긴장 |
| 53–57 | 사찰 마당, 정적 | **완전 정지 4초** (잔향만) |
| 57–62 | 거인 실루엣 | **대취타** 5초: 용고 · 나발 · 태평소 전부. 57.0초 최대 타격 |
| 62–81.5 | 획 · 발사 · 적중 9컷 가속(4.0 → 0.8 s) + 거인 앞 3초 | 리듬만, 점점 빠르게. 81.5초 마지막 타격 |
| 84.5–90 | 암전 → 로고 5초 | 잔향, 87.6초 낙관 자리에 편종 한 번, 90초 소리 0 |

BPM 제안: 84 → 108(57초부터).

**Style 프롬프트 (영문)**

```
Korean traditional instrumental trailer cue, 95 seconds, dark and restrained, no vocals, no choir, no synth pads, no western orchestra.
Instruments: geomungo foundation throughout; daegeum and saenghwang (0-18s), pyeonjong bronze bells (18-21s), taepyeongso shawm and buk drum (21-31s),
ajaeng bowed zither wide and slow (31-39s), nagak conch and slow yonggo (39-46s), yonggo, janggu and haegeum for a fight (46-53s),
complete silence with reverb tail (53-57s), then daechwita full military band blast: yonggo, nabal, taepyeongso, biggest hit at 57s (57-62s),
drums only accelerating 62-81s, one final hit at 81s ringing out, bells once around 87s, silence by 90s. Tempo 84 then 108 bpm. Deep reverb, ink-painting mood.
```

한글 설명: 강토를 지날 때마다 악기가 바뀐다(대금 → 편종 → 태평소 → 아쟁 → 나각 → 해금), 53–57초는 **아예 멈추고**, 57초에 대취타가 터진다, 그 뒤는 북만 점점 빠르게, 81.5초 마지막 타격이 90초까지 울린다.

## Suno 조작 절차

1. Create → **Custom** 켬 → **Instrumental** 켬 → Style of Music 칸에 위 영문 프롬프트(길이 한도에 걸리면 Structure 문장부터 줄인다) → Title `Oheangbu Trailer 30` / `… 90`.
2. 모델은 플랜에서 가장 높은 것(상업권이 붙는 것). 한 번에 2곡이 나온다 — **4–6곡**까지 돌려 고른다.
3. 고르는 기준(듣는 순서): ① 17–19(53–57)초 정지가 있는가 ② 19(57)초 타격이 가장 큰가 ③ 보컬 · 신스가 섞이지 않았는가 ④ 앞 4초가 비어 있는가. 정지와 타격이 없는 곡은 버린다(편집으로 못 만든다).
4. 다운로드는 **WAV**가 되면 WAV, 아니면 MP3(320 kbps). 파일명 `suno_30_<n>.wav|mp3` · `suno_90_<n>.wav|mp3` → `Art/Pitch308/Trailer/music/`.
5. 원장 기록(한 곡마다 — 플랜 · 모델판 · 검수자):
   `python Tools/Trailer308/audio.py ledger --file suno_30_1.wav --plan <Pro|Premier> --model <v4.5 등> --reviewed-by 한예준 --prompt "<쓴 프롬프트>"`
6. 내게 알려 줄 것: 파일명 · 플랜 · 모델판 · WAV 여부 · 어느 곡을 골랐는지(한 줄 이유).

## 권리

- Suno 상업 이용은 **유료 플랜(Pro/Premier)에서 생성한 곡**에만 붙는다 — 플랜과 생성 시점을 원장에 남긴다. 무료 플랜 곡은 발표에도 쓰지 않는다.
- 릴리스 음원은 human-in-the-loop 필수(PROD-AIASSET): 사용자가 골랐다는 기록(`reviewed_by`)이 원장에 있어야 한다.
- ElevenLabs 곡(`bgm308_*`)은 가편집 전용 — 상업 조건 미확인(TEST). 제출 · 발표 · 공개본에 넣지 않는다.

## 넘긴 뒤 내가 하는 일

1. `python Tools/Trailer308/audio.py suggest --music suno_30_1.wav --hit 19.0` — 온셋 에너지로 곡의 큰 타격을 트레일러 19.0초(거인 발견)에 맞추는 오프셋 후보 3개. 90초판은 `--hit 57.0 --length 90`.
2. `python Tools/Trailer308/cut.py 30 --remux --music suno_30_1.wav --music-offset <후보>` — 영상 재렌더 없이 WAV만 다시 만들어 입힌다(두 편).
3. `python Tools/Trailer308/check.py 30` · `90` → PASS 뒤 `sheet.py`로 인화 → 사용자 판정 → `--deck`.
4. 안 맞으면 엔벌로프 수치(`audio.py` 상단 DUCK_* · RAMP_*)만 손본다 — 곡을 다시 돌리는 건 정지 · 타격이 없을 때만.
