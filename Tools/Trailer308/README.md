# Tools/Trailer308 — 트레일러 4차 촬영 도구 (2026-10-11)

계획서 `~/.claude/plans/atomic-tumbling-rainbow.md` §2. 테이크 = `Art/Presentation297/Films/<take>.mp4` (1920×1080 · 30 fps),
명세 = `Art/Presentation297/Films/specs/<take>.json`, 회차표 = `manifest.json`, 원장 = `ledger.jsonl`.
전제: `PlaytestPolishQueue.cs` 복원 + `Presentation297` 의 glyph · hud · settle · hitLog 가 라이브(없으면 글자 테이크는 verify FAIL — 안전).

## 밤 회차 (사용자 부재 15분 뒤 자동)

```
python Tools/Trailer308/takes.py --check                      # 명세 22개 + manifest, TBD 0 · 편집표 길이(1.5배) 확인
nohup bash Tools/Trailer308/watch.sh > /dev/null 2>&1 &       # 유휴 ≥ 900 s 되면 R1 play → R2 mum → R3 world → R4 car (회차마다 편집기 재시작)
tail -f Tools/Trailer308/logs/watch/watch_*.log               # 진행 보기 / 끝나면 Tools/Trailer308/logs/watch.txt 요약
touch Tools/Trailer308/logs/watch/STOP                        # 지금 회차 끝나면 멈춤
bash Tools/Trailer308/shoot.sh r1_play play                   # 한 회차만 손으로 (사용자 자리 비움 + 전경 창 Unity 일 때만 Play 를 연다)
```

## 재촬영 · 한 테이크

```
grep FAIL Tools/Trailer308/ledger.jsonl                                   # 무엇이 왜 FAIL 인지(reason)
bash Tools/Trailer308/shoot.sh r5_fix fix:p_hook,p_fight                  # FAIL 테이크만 (R5) — manifest 항목을 그대로 쓴다(명세 재생성 없음)
python Tools/Trailer308/takes.py --take p_hook --letter 가                # 글자 바꿔 명세만 다시 → 그 다음 fix: 회차 (p_fight* 둘째 글자: --letter2)
python Tools/Trailer308/takes.py --take w_giant,p_burst_giant --giant-site alt   # 청룡이 추격하면 거인을 황경 광장으로
python Tools/Trailer308/takes.py --take p_hook --forget                   # 기억된 글자 · 거인 값을 버리고 sites308.json 기본값으로
python Tools/Trailer308/verify.py p_hook --no-ledger                      # 지금 film-state.json 을 다시 판정만
python Tools/Trailer308/encode.py p_hook --keep                           # PNG → mp4 (PNG 보존)
python Tools/Trailer308/filmwait308.py --selftest                         # 대기 루프 자체 검사(편집기 없이)
```

`--letter` · `--letter2` · `--giant-head` · `--giant-scale` · `--giant-site` 는 manifest 항목 `overrides` 에 남아 같은 테이크를 다시 만들
때(`fix:` 회차, `--round`, shoot.sh 의 `--giant-head` 갱신) 그대로 쓰인다. `TAKES="p_hook p_fight" bash shoot.sh r1b play` 로 회차 안의
테이크를 고를 수 있다. `NOLIFE=1` 은 이 수명에서 Play 를 아직 안 열었고 Unity 가 전경 창일 때만. `DRY=1` 은 편집기를 건드리지 않고 계획만 찍는다.

## 종료 코드 (shoot.sh → watch.sh)

0 완료 · 1 테이크 FAIL 있음 · 2 메모리 선 · 3 hold 실패 · 6 error CS · 7 재시작 실패(life.ps1 exit 2 = 안 닫힘 · 3 = 안 올라옴) ·
8 큐 파일 · GPU 잠금 · 씬 dirty · 편집기 무응답 · scene:open/close-all 거부 · 큐 충돌 · **9 재시작 전 사용자 있음(아무것도 안 건드림)** ·
**10 재시작 뒤 사용자 복귀 / 전경 창이 Unity 아님**. watch.sh 는 9 → POLL 뒤 다시, 10 → 연속 `FG_FAILS`(2)회면 감시 종료, 6·7·8 → 종료.

## 로그 위치

- `Tools/Trailer308/logs/<label>/` — `summary.txt`(한눈), `round.txt`(회차 전체), `hold.txt`, `<take>_a<n>.txt`(filmwait308), `pre_*`/`post_*`(전제 명령 응답), `verify.txt`, `encode.txt`, `slot.txt`, `release.txt`
- `Tools/Trailer308/ledger.jsonl` — 테이크 판정 한 줄씩(`take, round, status, frames, hits, commits, reason[, teleported]`) + 회차 이벤트(start · end · pre-refused · restart-failed …)
- `Tools/Trailer308/logs/watch.txt` — 밤 감시 요약, `logs/watch/watch_<date>.log` 상세
- `Art/Presentation297/Films/film-state.json` — 편집기의 마지막 필름 상태(status: settling → running → done, commits: `OK 나 …`, `HIT <id> dmg=… hp=… frame=N`, `glyph 나 strokes=N`, `settle quiet after …`, `board True`)

## 판정 규칙 (verify.py)

done · frames==total · PNG 수 == frames(인코딩 뒤엔 생략) · 글자 테이크는 글자마다 `OK <글자>` · 전투(p_hook* · p_fight* · p_burst_1..4)는 `HIT` ≥ 1 이고
첫 OK 뒤 · 뭄은 `OK 뭄` + (mum-status 있으면) active ≥ 1 · 자동차는 `board True` · 세계 컷(w_*)은 frames 만.
`--pre` 로 받은 전제 명령 응답에 `refused` · `no held Play` · `no ground` 가 있으면 FAIL, 마지막 `teleported to (x, y, z)` 는 manifest 의 feet 와
수평 5 m · 높이 8 m 안이어야 한다(갱도 위 산에 놓인 스냅 텔레포트를 잡는다 — p_hook 의 y 161–168 확인이 여기). PASS 면 encode.py 가 PNG 를 mp4 로 바꾸고 지운다.

## 자리 · 조향 · 알려진 가정

- 1인칭 테이크 자리는 `Map307Capture hold-teleport:x,z,yaw`(지면 + NavMesh 스냅) — `Session.Teleport` 는 스냅이 없어 spec 의 playerFeet 는 쓰지 않는다.
  잔도 · 강둑 · **땅속 갱도의 적(mine_beast_0 → p_burst_4)** 은 `exact: true` 로 `hold-teleport:x,y,z,yaw`(y = 적의 y 168.35; 스냅은 y 2000 레이캐스트라 갱도 위 산에 놓인다).
- 적 앞 자리는 **가장 가까운 적**까지 stand_m(승냥이 무리 23 m · 군졸 무리 22 m · 단독 19 m, 감지 16/13 바깥)에 서서 W 로 걸어 들어간다. 감지 + 2 m 미만이면 takes.py 가 경고.
- 테이크 길이는 편집표(timeline.py TL30/TL90)의 가장 긴 컷 × 1.5 이상이고 컷 끝(src + dur)을 덮는다 — `--check` 가 어긴 것을 적고 exit 2.
- 자동차: spec `board:true` 가 탑승을 맡고, A/D 일정은 `WorldLayout_Main.asset` Routes `mine_overlook__geumpyo_inn` 폴리라인을 주막 쪽부터 자전거 모형으로 따라간 것.
  D = 우회전(yaw 증가) 가정 — `p_car_probe` 를 먼저 보고 `sites308.json` 의 `car_route` 수치를 고친다.
- 거인: `guardian:3220,89,3530:180:9:Idle@0:target` 의 **응답 `guardian at (…) head N`** 에서 머리 높이를 읽어 w_giant · p_burst_giant 명세를 그 자리에서 재생성한다
  (`guardian-info` 는 root/feet/ground/clock 만 돌려주고 head 가 없다). 응답에 head 가 없으면 summary 에 경고를 남기고 추정치(2.2 × scale)로 찍는다.
- 필름 대기는 `filmwait308.py`(settling · running 둘 다 대기, 남은 필름은 film-stop 뒤 시작, 상한이면 film-stop). `Stage308_ops/filmwait.py` 는 settling 에서 끊겨 쓰지 않는다.
  **실제 film-state 로는 아직 검증 못 했다**(편집기 금지 세션) — 밤 회차 전 R1 첫 테이크의 `<take>_a1.txt` 에 `settle quiet/cap …` 와 done 이 찍히는지 본다.
