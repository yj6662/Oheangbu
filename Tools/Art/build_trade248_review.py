"""Publish actual dedicated trade UI captures."""
from pathlib import Path
root = Path(__file__).resolve().parents[2]
out = root / "Art/World/Compact/Rebuild"
body = """<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>청림 전용 거래 창</title><style>
body{max-width:1440px;margin:40px auto;padding:0 24px;background:#e8e2d5;color:#292b25;font:17px/1.75 system-ui,sans-serif}
h1{font-size:30px}h2{font-size:23px;margin-top:40px}p{max-width:1040px}img{width:100%;display:block}figure{margin:24px 0}
figcaption{font-size:14px;color:#606257;margin-top:8px}a{color:#395c50}
</style><h1>상인 앞에서 여는 거래 창</h1>
<p>상점과 강화 화면을 전용 창으로 분리했습니다. 왼쪽에는 장비 목록과 짧은 대사, 오른쪽에는 선택한 장비의 큰 그림·효과·거래 버튼이 있습니다. 보유 통보는 상단에 표시하고 × 또는 Esc로 거래를 마칩니다.</p>
<p>상인은 판매 장비를, 장인은 소유 장비를 보여 줍니다. 처음 열 때 장비 하나를 선택해 바로 살펴볼 수 있습니다. 장비창은 기존 소지품 패널을 사용합니다.</p>"""
for name, caption in [("shop","상점 · 미구매 장비와 가격"),("forge","강화 · 선택 장비의 현재 효과와 다음 단계")]:
    body += f'<h2>{caption}</h2><figure><a href="Trade248/{name}.png"><img src="Trade248/{name}.png" alt="{caption}"></a><figcaption>실제 Unity Play 캡처 · 검사 전용 저장</figcaption></figure>'
body += """<details><summary>이전 메뉴형 상점</summary><img src="UILayout247/shop.png" alt="이전 상점"></details>
<p>이번 검사에서는 전용 창 분리, 현장 접근 조건, 구매 부족금·중복 요청, 강화 단계와 비용, 닫기·뒤로 복귀, 기존 인벤토리 패널을 확인했습니다. 자동 UI 이벤트와 실제 화면 검토이며 수동 플레이·성능 실측·사용자 미술 승인은 별도입니다.</p>
<p><a href="Trade248/runtime.txt">거래 UI 검사</a> · <a href="Trade248/delivery.json">변경·보존 기록</a></p></html>"""
(out / "TRADE_REVIEW.html").write_text(body, encoding="utf-8")
print("Published TRADE_REVIEW.html")
