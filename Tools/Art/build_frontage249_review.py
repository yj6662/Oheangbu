"""Review captured Unity views; no synthetic renders or asset changes."""
from pathlib import Path
import html

root = Path(__file__).resolve().parents[2]
out = root / 'Art/World/Compact/Rebuild'
views = [('arrival', '마을 진입'), ('court', '생활 마당'), ('shop', '상점'),
         ('artisan', '장인 작업장'), ('inn', '객주 문')]
sections = []
for key, title in views:
    sections.append(f'''<section><h2>{html.escape(title)}</h2><div class="pair">
    <figure><a href="Frontage249/before_{key}.png"><img src="Frontage249/before_{key}.png"></a><figcaption>이전</figcaption></figure>
    <figure><a href="Frontage249/after_{key}.png"><img src="Frontage249/after_{key}.png"></a><figcaption>현재 후보</figcaption></figure></div></section>''')
page = '''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>청림 마을 · 생활 공간</title><style>
*{box-sizing:border-box}body{margin:0;background:#e6e1d5;color:#262b28;font-family:system-ui,sans-serif;line-height:1.7}
main{max-width:1600px;margin:auto;padding:44px 28px}header{max-width:860px;margin-bottom:50px}h1{font-size:32px;font-weight:600}h2{font-size:20px;font-weight:500}
.pair{display:grid;grid-template-columns:1fr 1fr;gap:18px}figure{margin:0}img{width:100%;display:block}figcaption{font-size:13px;color:#575e56;margin-top:7px}section{margin:42px 0}a{color:inherit}small{display:block;color:#575e56}
@media(max-width:850px){.pair{grid-template-columns:1fr}main{padding:24px 16px}}
</style><main><header><small>축소맵 후보 / #249</small><h1>물건과 작업 흔적으로 읽히는 마을</h1>
<p>상점에는 천막 아래 상품 진열대, 장인에게는 대패 작업대와 목재 건조대, 객주 문 양옆에는 등과 여행 짐을 더했습니다. 상인과 장인은 마당 쪽을 바라봅니다.</p>
<p>같은 지형·카메라·렌더링 설정의 Unity 전후 캡처입니다. 이미지 클릭 시 1920×1080 원본을 엽니다.</p>
<small>자동 통행·서비스 검사는 아래 기록에서 확인합니다. 처음 방문하는 사람의 길찾기, 이동 중 미술 판단과 성능 실측은 별도 확인이 필요합니다.</small>
<p><a href="Frontage249/audit.txt">배치·접근 검사</a> · <a href="Frontage249/walk.txt">자동 보행</a> · <a href="Frontage249/runtime.txt">실제 Play 상호작용</a> · <a href="Frontage249/delivery.json">전달 기록</a></p></header>'''
(out / 'FRONTAGE_REVIEW.html').write_text(page + ''.join(sections) + '</main></html>', encoding='utf-8')
print(out / 'FRONTAGE_REVIEW.html')
