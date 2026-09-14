# SPEC-EA-PROLOGUE — 폐광에서 금표 주막까지

> **2026-09-11 적용 순서 변경 — DECISIONS #205 / SPEC-WORLD-MACRO.** 기존 프롤로그 씬과 조사·전투·보상·저장 기능은 보존한다. 후속 공간 제작은 전체 산계·수계·분지를 먼저 설계한 뒤 폐광과 금표 주막의 위치를 정한다. 본문의 첫 야외 보행120–180초, 고정 좌표·경로 knots와 단거리 산맥 우회는 새 전체 지형의 강제 조건이 아니다. 거리·방위·배치 수량은 자연스러운 지형을 위한 지침으로 사용한다. 새 전체 지도는 약8×12km 참고 규모의 불규칙한 외곽으로 만들며, 가도14m/s TEST·도보4.5m/s로 공간을 검토한다. 이번 전체 지도 전달물은 조감도·플레이 시점 스크린샷이며 영상은 제작하지 않는다. 아래 최초 제작 계약과 검증 기록은 과거 프롤로그 결과의 해석을 위해 유지한다.

State: TEST. 2026-09-11. Source: user-approved implementation plan; CONST-SLOTS/RULES, LDB-CHARTER/ATTRACTION/GATING/CHECKPOINT/EA, ART-SKY, COMBAT-ECONOMY, NARR-STORYLINE.

구현 상태와 실제 증거: `Art/World/Prologue/REPORT.md`. 실행 코드가 있다는 사실과 검증 통과는 구분한다. 실제 완주 영상, 앱 재실행, 작도 회귀, 미술 판정이 남으면 본 구간 전체 완료로 올리지 않는다.

## Contract
Preserve C2_CodexWorld as the visual reference. Produce a separate W_Cheongrim_Prologue scene with pale ink clouds, neutral tonal adjustment, very subtle bloom and antialiasing. Existing scenes keep their solid background. Mountain atmospheric scattering remains in its existing material; no second fog pass. No day/night simulation. Navigation uses motivated ore, inhabited lanterns, daylight openings, silhouettes and terrain; no route markers or evenly spaced light breadcrumb trails.

Playable scope: safe mine start → explosion evidence → neutral melee and single-fire ranged encounters → daylight exit → mountain detour + reward loop → thatched inn rest and two temporary NPC conversations. Player and enemies/NPCs use temporary bodies. Existing spell inputs/rules remain authoritative. Educational examples 아/어 do not lock other initial vocabulary or grant finals. The high reward shelf previews 국 only. No shop/upgrade economy is invented.

## World route reservation
1. Mine → Geumpyo inn (this implementation).
2. Geumpyo road → relay station J1 / merchant branch W1 → logging village/yard → deep forest.
3. Deep forest → Cheongryong → 국 revisits.
4. Capital road → checkpoints/escort → Hwanggyeong outer market/cargo delivery.
5. Outer capital → south gate battle → gate opens (EA endpoint). Interior city deferred.

## Runtime policies [TEST]
Data owns paths, interactions, checkpoint IDs and reward amounts. One save slot per production scene, version 1, atomic replacement and backup; transient test runs use a separate namespace. Save durable events immediately and safe grounded location periodically/on exit. On restart restore progress, currency, drop and safe location; invalid location falls back to checkpoint. On death replace the previous drop with all carried currency at last grounded location, reset combat, respawn at checkpoint and refill HP/ink. Rest refills HP/ink and resets ordinary enemies. Evidence, conversation history and one-off pickups persist; rewards cannot duplicate. No experience, potion, fast travel, jumping, climbing or swimming.

Use the existing VContainer combat scope for injected ink/parry/groggy services. Separate progression persistence, interaction presentation, encounters, sky and authoring. UI remains HP/ink/lock-on/interaction prompt only. Production interaction actions use a dedicated InputAction, with draw mode blocking interaction.

## Acceptance
Actual-input mine→inn traversal, reward/rejoin, death→retrieval→second death, rest, disk reload, terrain/steps, chase/leash/occlusion, scene integrity. Compare sky/post on/off using identical cameras. Record CPU/GPU/frame/memory measurements honestly; do not infer 150 FPS. Capture sequentially; do not start capture above 85% system commit. Visual approval is the user's. Any unexecuted check stays UNVERIFIED. First outdoor pure walking target 120–180 seconds is TEST, not artificial padding.
