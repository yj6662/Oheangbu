"""Publish actual equipment layout captures without editing the images."""
from pathlib import Path

root = Path(__file__).resolve().parents[2]
out = root / "Art/World/Compact/Rebuild"
body = """<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>장비 UI 배치 정리</title><style>
body{max-width:1440px;margin:40px auto;padding:0 24px;background:#e8e2d5;color:#292b25;font:17px/1.75 system-ui,sans-serif}
h1{font-size:30px}h2{font-size:23px;margin-top:40px}p{max-width:1020px}img{display:block;width:100%}
figure{margin:24px 0}figcaption{font-size:14px;color:#606257;margin-top:8px}a{color:#395c50}
</style><h1>장비창의 간격과 정렬</h1>
<p>선택 테두리를 사방 같은 두께로 맞추고 그림을 가운데에 배치했습니다. 인물 그림과 오른쪽 장착 칸 사이의 간격을 확보하고, 소지품·상점·강화 목록의 칸 크기를 통일했습니다. 목록과 상세 정보 사이의 공백도 줄였습니다.</p>
<p>생성 원화와 장비 규칙은 그대로입니다. 최신 후보 씬에서 I로 확인할 수 있습니다. 아래는 실제 Play의 2560×1440 캡처이며 검사 전용 저장을 사용했습니다.</p>"""
for name, label in [("inventory", "장비창"), ("shop", "상점"), ("forge", "강화")]:
    body += f'<h2>{label}</h2><figure><a href="UILayout247/{name}.png"><img src="UILayout247/{name}.png" alt="{label} 배치 조정 후"></a><figcaption>조정 후</figcaption></figure>'
    body += f'<details><summary>조정 전</summary><figure><img src="UIArt246/{name}.png" alt="{label} 조정 전"></figure></details>'
body += '<p>UI 클릭·잘못된 부위 드롭·장착·해제와 원화 연결을 자동 검사했습니다. 수동 조작·성능 실측·사용자 미술 판정과는 별개입니다. <a href="UILayout247/delivery.json">변경과 검증 기록</a></p></html>'
(out / "UI_LAYOUT_REVIEW.html").write_text(body, encoding="utf-8")
print("Published UI_LAYOUT_REVIEW.html")
