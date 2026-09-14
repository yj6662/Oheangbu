"""Build a local still-image review from verified Unity captures; no media generation."""
from pathlib import Path
import html
import json
import shutil

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Art/World/WorldMacro/Playtest/AssetReuse"
VIEWS = {
    "inn_entry": "금표 주막 · 입구",
    "inn_side": "금표 주막 · 측면",
    "inn_inside": "금표 주막 · 내부",
    "mine_inside": "폐광 · 내부",
    "branch": "지선 · 작업자 소지품",
    "ramp_side": "폐광 · 연결 경사로",
}
cards = []
for key, label in VIEWS.items():
    before = OUT.parent / "Polish" / f"after_{key}.png"
    after = OUT / f"after_{key}.png"
    if not before.is_file() or not after.is_file():
        raise FileNotFoundError(key)
    shutil.copy2(before, OUT / f"before_{key}.png")
    cards.append(f'''<section id="{key}"><h2>{label}</h2><div class="pair">
    <figure><figcaption>이전 작업 완료판</figcaption><a href="before_{key}.png"><img loading="lazy" src="before_{key}.png" alt="{label} 수정 전"></a></figure>
    <figure><figcaption>보유 에셋 교체판</figcaption><a href="after_{key}.png"><img loading="lazy" src="after_{key}.png" alt="{label} 수정 후"></a></figure></div></section>''')
validation = html.escape((OUT / "validation.txt").read_text(encoding="utf-8-sig"))
nav = " ".join(f'<a href="#{key}">{label}</a>' for key, label in VIEWS.items())
page = f'''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>첫 플레이 구간 · 보유 에셋 교체 검토</title><style>
*{{box-sizing:border-box}}body{{margin:0;background:#171b19;color:#e9e6dc;font:16px/1.7 system-ui,sans-serif}}main{{max-width:1600px;margin:auto;padding:42px 28px}}h1{{font-size:34px;margin:0 0 14px}}h2{{font-size:22px;margin:0 0 18px}}p{{max-width:1060px;color:#c7cdc7}}a{{color:#cbd9b2}}nav{{display:flex;gap:16px;flex-wrap:wrap;padding:22px 0;border-bottom:1px solid #41463f}}section{{padding:30px 0;border-bottom:1px solid #41463f}}.pair{{display:grid;grid-template-columns:1fr 1fr;gap:16px}}figure{{margin:0}}figcaption{{color:#bbc5b6;padding:0 0 8px}}img{{width:100%;aspect-ratio:16/9;object-fit:contain;background:#000;display:block}}pre{{white-space:pre-wrap;background:#202821;padding:20px;font:14px/1.7 monospace}}.tag{{color:#c7d9a6;letter-spacing:.12em;font-size:13px}}@media(max-width:850px){{.pair{{grid-template-columns:1fr}}main{{padding:24px 16px}}}}
</style><main><div class="tag">2026.09.13 · UNITY EDITOR · 1920 × 1080 STILLS</div>
<h1>폐광과 금표 주막 — 보유 에셋으로 교체</h1>
<p>상자형 주막과 소품을 초가·마루·생활용품 에셋으로 바꾸고, 폐광은 원래 UV를 유지한 동굴 암반 스캔으로 조립했습니다. 현재 실제 플레이로 연결된 첫 구간의 수정입니다. 인물 캡슐과 후속 지역의 예약 배치는 유지합니다.</p>
<p>휴식 표적이 체크포인트 범위 밖에 있던 문제도 고쳤습니다. 경로 16,426개 표본, Play 서비스 검사 15개, 새 Play 세션 저장 복원을 통과했습니다. 실제 키 조작·보행 완주·최종 미술과 120fps 성능은 미검증입니다.</p>
<nav>{nav}<a href="REPORT.md">기술 보고서</a><a href="asset_reuse.tsv">사용 에셋 원장</a></nav>
{''.join(cards)}<section><h2>최종 기술 검사</h2><pre>{validation}</pre><p>사진 속 텍스트는 넣지 않았습니다. 설명은 페이지에만 표시합니다. 경사로 측면과 동굴 경계를 막는 구조용 뒷면은 보조 형상으로 남겨 두었습니다. 영상과 자동 보행 완주는 제작하지 않았습니다.</p></section></main></html>'''
(OUT / "REVIEW.html").write_text(page, encoding="utf-8")
print(OUT / "REVIEW.html")
