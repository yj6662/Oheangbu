"""Build a review from unmodified Unity captures and original generated assets."""
import html
import json
import os
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Art/World/Compact/Rebuild"
ART = OUT / "UIArt246"

def figure(path, caption):
    return f'<figure><a href="{path}"><img src="{path}" alt="{html.escape(caption)}"></a><figcaption>{caption}</figcaption></figure>'

body = """<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>장비 UI 원화 · #246</title><style>
body{max-width:1440px;margin:40px auto;padding:0 24px;background:#e8e2d5;color:#292b25;font:17px/1.75 system-ui,sans-serif}
h1{font-size:32px}h2{font-size:23px;margin-top:44px}p{max-width:1080px}img{width:100%;display:block}
figure{margin:20px 0}figcaption{font-size:14px;color:#606257;margin-top:8px}a{color:#395c50}
.pair{display:grid;grid-template-columns:1fr 1fr;gap:20px}.icons{display:grid;grid-template-columns:repeat(6,1fr);gap:20px}
.icons img{height:160px;object-fit:contain}.icons figcaption{text-align:center}
details{margin:24px 0} @media(max-width:800px){.pair{grid-template-columns:1fr}.icons{grid-template-columns:repeat(3,1fr)}}
</style><h1>종이 위의 인물과 장비</h1>
<p>인물 전신 한 장과 붓·머리띠·의복·손싸개·짚신·목구슬 여섯 장을 이미지 생성으로 제작했습니다. 기존 패널 구성에 먹담채 그림을 적용했고, 소지품·상점·강화·드래그에서 같은 원화를 사용합니다. 인물은 장착 위치를 보여 주는 고정 그림입니다.</p>
<p>아래 화면은 실제 Unity Play에서 촬영한 2560×1440 캡처입니다. 후보 씬 <b>W_Demo_Compact_MigrationCheck</b>에서 I로 소지품을 열면 확인할 수 있습니다. 검사 화면의 통보와 장비는 별도 진단 저장의 값입니다.</p>"""
body += figure("UIArt246/inventory.png", "장비를 착용한 상태 · 선택 장비는 종이 바탕과 붉은 가장자리로 구분")
body += '<div class="icons">'
for name, label in [("brush", "붓"), ("head", "머리"), ("body", "몸"), ("hands", "손"), ("feet", "발"), ("accessory", "장신구")]:
    src = ROOT / f"Oheangbu/Assets/_Project/Art/UI/Equipment246/{name}-v1.png"
    body += figure(os.path.relpath(src, OUT).replace("\\", "/"), label)
body += "</div><h2>빈 장착 칸과 상점</h2>"
body += figure("UIArt246/fresh.png", "새 게임 · 기본 붓과 의복, 비어 있는 네 부위는 옅은 그림")
body += '<div class="pair">' + figure("UIArt246/shop.png", "장비 상인 · 구매 화면") + figure("UIArt246/forge.png", "목공 장인 · 소유 장비 강화") + "</div>"
body += '<details><summary>이전 장비창과 비교</summary>'
body += figure("Village245/inventory.png", "#245 이전 벡터 표시 · UI 비교용이며 월드 카메라 위치는 다름")
body += "</details><h2>적용과 확인</h2>"
body += """<p>생성 원본의 알파를 보존했습니다. 후보 Content만 별도 시각 자료에 연결했고, 기존 정본과 후보 씬의 지형·배치·지도·NavMesh는 바꾸지 않았습니다. 장비 거래와 저장 규칙도 그대로입니다.</p>
<p>이번 Play에서 이미지 연결·드래그·선택 7항목, 장비 UI 이벤트 5항목, 거래와 기존 서사 연결 34항목을 확인했습니다. 이는 자동 fixture이며 수동 조작·CPU/GPU 성능 실측·사용자 미술 승인과 구분합니다. 이번 변경의 미술 상태는 검토 대기입니다.</p>
<p><a href="UIArt246/prompts.json">생성 프롬프트와 원본 경로</a> ·
<a href="UIArt246/assets.json">원본 PNG 크기·투명도·해시</a> ·
<a href="UIArt246/runtime-art.txt">새 UI 원화 검사</a> ·
<a href="UIArt246/ui-events.json">클릭·장착·잘못된 드롭 검사</a> ·
<a href="UIArt246/delivery.json">보존·검증 기록</a></p></html>"""
(OUT / "UI_ART_REVIEW.html").write_text(body, encoding="utf-8")
print("Published UI_ART_REVIEW.html")
