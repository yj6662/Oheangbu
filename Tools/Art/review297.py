"""#297 review page builder (gate ①: lighting + first cave + opening area; gate ②: 철옹 산성).

python Tools/Art/review297.py gate1      -> Art/World/Compact/Rebuild/Finish297/Review1/REVIEW.html   (review1.json)
python Tools/Art/review297.py gate2      -> Art/World/Compact/Rebuild/Finish297/Review2/REVIEW.html   (review2.json)
A view whose "before" is null is shown alone (no left figure).
Copies the same-camera Unity captures (Finish297/Views/<stage>/...) into Review1/{before,after}/ and writes a Korean
review page in the #293 style: view switcher (before | after), verification table (links to the raw logs), remaining
work and how to revert. Text lines marked TEST/IMPLEMENTED/VALIDATED follow the spec's reporting rule.
"""
import json, shutil, sys, html, re, base64, io
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
F = ROOT / 'Art/World/Compact/Rebuild/Finish297'
V = F / 'Views'


def copy_pair(dst, name, before, after):
    for stage, src in (('before', before), ('after', after)):
        if src is None or not Path(src).exists(): continue
        d = dst / stage; d.mkdir(parents=True, exist_ok=True)
        im = Image.open(src).convert('RGB'); im.save(d / f'{name}.jpg', quality=90)


def page(title, subtitle, intro, groups, table, remaining, revert, gate='①'):
    views = []
    for g in groups:
        for n, label in g['views']: views.append([g['id'], g['id'] + '-' + n, label, g['pairs'][n][0] is None])
    rows = ''.join(f'<tr><td>{html.escape(a)}</td><td>{b}</td><td>{c}</td></tr>' for a, b, c in table)
    gbuttons = ''.join(f'<section><h2>{html.escape(g["title"])}</h2><p>{g["text"]}</p><div class="controls" data-group="{g["id"]}"></div>'
                       f'<h3 class="view-name" data-group="{g["id"]}"></h3><div class="pair"><figure class="bf" data-group="{g["id"]}"><p>{html.escape(g.get("left","수정 전"))}</p>'
                       f'<a class="bl" data-group="{g["id"]}"><img class="bi" data-group="{g["id"]}" alt="수정 전"></a></figure><figure><p>{html.escape(g.get("right","수정 후"))}</p>'
                       f'<a class="al" data-group="{g["id"]}"><img class="ai" data-group="{g["id"]}" alt="수정 후"></a></figure></div></section>' for g in groups)
    return f'''<!doctype html>
<html lang="ko">
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>{html.escape(re.sub('<[^>]+>', ' ', title))}</title>
<style>
body{{margin:0;background:#eee9de;color:#2d302b;font:17px/1.7 system-ui,"Malgun Gothic",sans-serif}}main{{max-width:1400px;margin:auto;padding:36px 22px 72px}}
h1{{font-size:34px;line-height:1.35}}h2{{margin-top:48px;font-size:25px}}h3{{font-size:19px;margin:14px 0 6px}}
.note{{background:#f8f4ea;border-left:4px solid #8a5a33;padding:12px 16px}}.pair{{display:grid;grid-template-columns:1fr 1fr;gap:14px}}
@media(max-width:900px){{.pair{{grid-template-columns:1fr}}}}figure{{margin:0}}figure p{{margin:0 0 4px;font-size:14px;color:#6b6457}}img{{width:100%;display:block;border-radius:4px;background:#ccc}}
.controls{{display:flex;flex-wrap:wrap;gap:6px}}button{{font:inherit;font-size:14px;padding:5px 11px;border:1px solid #9c937f;background:#faf7f0;border-radius:4px;cursor:pointer}}
button.on{{background:#2d302b;color:#faf7f0}}table{{border-collapse:collapse;width:100%;font-size:15px}}td,th{{border-bottom:1px solid #cfc6b3;padding:8px;text-align:left;vertical-align:top}}
code{{background:#e4dccb;padding:1px 5px;border-radius:3px;font-size:14px}}
</style>
<main>
<p>오행부 · #297 월드 마감 · 검토 게이트 {gate}</p>
<h1>{title}</h1>
<p class="note">{subtitle}</p>
{intro}
{gbuttons}
<h2>검증</h2>
<table><tr><th>항목</th><th>이번 결과</th><th>근거</th></tr>{rows}</table>
<h2>남은 작업</h2>
{remaining}
<h2>되돌리기</h2>
{revert}
</main>
<script>
const views={json.dumps(views, ensure_ascii=False)};
for(const box of document.querySelectorAll('.controls')){{
 const g=box.dataset.group;const mine=views.filter(v=>v[0]===g);
 const show=(i)=>{{const [gid,file,name,single]=mine[i];document.querySelector('.view-name[data-group="'+g+'"]').textContent=name;
  document.querySelector('.bf[data-group="'+g+'"]').style.display=single?'none':'';
  for(const [stage,img,link] of [['before','.bi','.bl'],['after','.ai','.al']]){{const p=stage+'/'+file+'.jpg';document.querySelector(img+'[data-group="'+g+'"]').src=p;document.querySelector(link+'[data-group="'+g+'"]').href=p;}}
  box.querySelectorAll('button').forEach((b,k)=>b.classList.toggle('on',k===i));}};
 mine.forEach((v,i)=>{{const b=document.createElement('button');b.textContent=v[2];b.addEventListener('click',()=>show(i));box.append(b);}});show(0);
}}
</script>
</html>'''


def gate(n, mark):
    dst = F / f'Review{n}'
    if dst.exists(): shutil.rmtree(dst)
    dst.mkdir(parents=True)
    cfg = json.loads((F / f'review{n}.json').read_text(encoding='utf-8'))
    for g in cfg['groups']:
        for n, label in g['views']:
            src = g['pairs'][n]; copy_pair(dst, g['id'] + '-' + n, V / src[0] if src[0] else None, V / src[1])
    for name in cfg.get('evidence', []):
        p = F / name
        if p.exists(): (dst / 'evidence').mkdir(exist_ok=True); shutil.copy(p, dst / 'evidence' / Path(name).name)
    text = page(cfg['title'], cfg['subtitle'], cfg['intro'], cfg['groups'], cfg['table'], cfg['remaining'], cfg['revert'], mark)
    (dst / 'REVIEW.html').write_text(text, encoding='utf-8')
    # single-file copy (images 1280 px embedded) for viewers that cannot resolve relative files
    cache = {}
    def data(stage, name):
        p = dst / stage / f'{name}.jpg'
        if not p.exists(): return ''
        if p not in cache:
            im = Image.open(p).convert('RGB'); im.thumbnail((1280, 720)); b = io.BytesIO(); im.save(b, 'JPEG', quality=80)
            cache[p] = 'data:image/jpeg;base64,' + base64.b64encode(b.getvalue()).decode()
        return cache[p]
    embedded = {f'{s}/{n}.jpg': data(s, n) for g in cfg['groups'] for s in ('before', 'after') for n in [g['id'] + '-' + v for v, _ in g['views']]}
    one = text.replace("const p=stage+'/'+file+'.jpg';", "const p=IMG[stage+'/'+file+'.jpg']||'';").replace('const views=', 'const IMG=' + json.dumps(embedded) + ';' + chr(10) + 'const views=')
    (dst / 'REVIEW-single.html').write_text(one, encoding='utf-8')
    print(dst / 'REVIEW.html', dst / 'REVIEW-single.html')


if __name__ == '__main__':
    {'gate1': lambda: gate(1, '①'), 'gate2': lambda: gate(2, '②')}[sys.argv[1]]()
