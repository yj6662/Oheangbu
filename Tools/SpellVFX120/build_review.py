"""Build the offline 120-spell review page from current source and captured files.

No server, deployment, network dependency, or Unity mutation. Run again after capture:
    python Tools/SpellVFX120/build_review.py --stills-folder SecondPass --stage "2차 캡처"
The selected folder is never silently replaced by another capture version.
An existing image is evidence of capture only, not an artistic or gameplay PASS.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import html
import json
import struct
from pathlib import Path
from urllib.parse import quote

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUTPUT = ROOT / 'Art/SpellVFX120'


def capture_metadata(path: Path, glyph: str) -> dict:
    """Read recorded capture facts only; never infer source revision from file dates."""
    if not path.is_file():
        return {'state': 'NOT_RECORDED'}
    try:
        data = json.loads(path.read_text(encoding='utf-8-sig'))
        if data.get('glyph') != glyph:
            return {'state': 'GLYPH_MISMATCH'}
        return {'state': 'RECORDED', 'sampledSeconds': data.get('sampledSeconds', []),
                'status': data.get('status'), 'demonstrationCues': data.get('demonstrationCues'),
                'gameplayConnectionVerified': data.get('gameplayConnectionVerified'),
                'eventSource': data.get('eventSource')}
    except (OSError, ValueError, AttributeError):
        return {'state': 'UNREADABLE_CAPTURE_METADATA'}


def image_state(path: Path) -> tuple[bool, dict]:
    """Check PNG header/dimensions without requiring optional image libraries."""
    if not path.is_file():
        return False, {'state': 'NOT_CAPTURED'}
    try:
        with path.open('rb') as stream:
            head = stream.read(32)
        if len(head) < 24 or head[:8] != b'\x89PNG\r\n\x1a\n' or head[12:16] != b'IHDR':
            return False, {'state': 'INVALID_PNG_HEADER'}
        width, height = struct.unpack('>II', head[16:24])
        if width < 1 or height < 1 or path.stat().st_size < 64:
            return False, {'state': 'EMPTY_OR_INVALID_PNG'}
        return True, {'state': 'PNG_HEADER_PRESENT', 'width': width, 'height': height,
                      'aspect16x9': abs(width / height - 16 / 9) < .015,
                      'atLeast720p': width >= 1280 and height >= 720}
    except OSError:
        return False, {'state': 'FILE_NOT_READABLE'}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument('--output-dir', type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument('--stills-folder', type=Path, default=Path('Stills'),
                        help='Capture folder inside output-dir, e.g. SecondPass or ThirdPass. No fallback or mixing.')
    parser.add_argument('--stage', help='Captured version label; defaults to the selected folder name.')
    parser.add_argument('--title', default='오행부 · 120 술식 VFX 검수', help='Review page title.')
    args = parser.parse_args()
    output = args.output_dir.resolve()
    stills = (output / args.stills_folder).resolve()
    if not stills.is_relative_to(output) or stills == output:
        parser.error('--stills-folder must select a folder inside --output-dir')
    if stills.exists() and not stills.is_dir():
        parser.error('--stills-folder must be a directory')
    stills_relative = stills.relative_to(output).as_posix()
    stage = args.stage or stills.name
    output.mkdir(parents=True, exist_ok=True)
    direction_path = ROOT / 'Docs/Art/SpellVFX120/visual_direction_draft.json'
    manifest_path = output / 'build_manifest.json'
    direction = json.loads(direction_path.read_text(encoding='utf-8-sig'))
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    definitions = {item['glyph']: item for item in manifest['spells']}
    records, media_checks = [], []
    captured_mtimes = []
    for source in direction['spells']:
        item = definitions[source['glyph']]
        ident = f"{source['index']:03d}_{ord(source['glyph']):04X}"
        evidence_relative = f'{stills_relative}/{ident}_capture.json'
        evidence = capture_metadata(output / evidence_relative, source['glyph'])
        media = []
        for phase in range(5):
            relative = f'{stills_relative}/{ident}_{phase}.png'
            present, check = image_state(output / relative)
            if present:
                captured_mtimes.append((output / relative).stat().st_mtime)
            media.append({'phase': phase, 'path': quote(relative) if present else None,
                          'expected': relative, 'inspection': check})
            media_checks.append({'id': ident, 'phase': phase, 'path': relative, **check})
        clip_relative = f'Clips/{ident}.mp4'
        clip = output / clip_relative
        # Browser-decoding and original-speed validation are separate from this listing.
        clip_present = clip.is_file() and clip.stat().st_size >= 128
        status = 'blank' if not item['assigned'] else ('connected' if item['connected'] else 'asset')
        records.append({
            'index': source['index'], 'id': ident, 'glyph': source['glyph'],
            'title': source['visualTitle'], 'element': source['element'],
            'category': source['classification'], 'frame': source['frame'],
            'status': status, 'intent': source['gameplayEffectVerbatim'],
            'shape': source['shape'], 'motion': source['motion'],
            'cast': source['cast'], 'travel': source['travel'],
            'impact': source['impact'], 'resolve': source['resolve'],
            'accentName': source['accent'], 'readability': source['readability'],
            'palette': source['recipe']['palette'],
            'pattern': item['pattern'],
            'patternName': next((x['observed_motif'] for x in source['patternSources'] if x['unity_asset_path'] == item['pattern']), '검수 목록 참조'),
            'sourceNote': source['sourceNote'], 'media': media,
            'capture': evidence,
            'captureMetadataPath': quote(evidence_relative) if evidence['state'] == 'RECORDED' else None,
            'clip': clip_relative if clip_present else None,
            'clipExpected': clip_relative, 'previewDuration': item['duration'],
            'capturedCount': sum(x['path'] is not None for x in media)
        })
    if len(records) != 120 or len({r['glyph'] for r in records}) != 120:
        raise ValueError('Review requires exactly 120 unique glyphs')
    census = {
        'status': 'OFFLINE_GALLERY_SELECTED_CAPTURE_FILES_ONLY_NOT_RUNTIME_PASS',
        'generatedAtUtc': datetime.now(timezone.utc).isoformat(),
        'count': len(records), 'connected': sum(r['status'] == 'connected' for r in records),
        'assetReview': sum(r['status'] == 'asset' for r in records),
        'intentionalBlanks': sum(r['status'] == 'blank' for r in records),
        'capturedStills': sum(r['capturedCount'] for r in records),
        'expectedStills': 600, 'presentVideos': sum(r['clip'] is not None for r in records),
        'sourceSha256': hashlib.sha256(direction_path.read_bytes()).hexdigest(),
        'manifestSha256': hashlib.sha256(manifest_path.read_bytes()).hexdigest(),
        'pageTitle': args.title,
        'captureVersion': {
            'stage': stage, 'stillsFolder': stills_relative,
            'metadataFiles': sum(r['capture']['state'] == 'RECORDED' for r in records),
            'sourceMatch': 'UNVERIFIED_CAPTURE_SOURCE_REVISION_NOT_RECORDED',
            'fileModifiedRangeUtc': [datetime.fromtimestamp(t, timezone.utc).isoformat()
                                     for t in (min(captured_mtimes), max(captured_mtimes))] if captured_mtimes else [],
            'fileDatesAreCaptureProof': False,
            'note': '선택한 폴더의 촬영본입니다. 파일 시각은 촬영 당시 소스 버전을 증명하지 않습니다.'
        },
        'sourceDescription': '설명·색상·배정은 페이지 생성 시점의 설계 및 build_manifest입니다. 촬영 당시 소스와 일치는 미검증입니다.',
        'videoSource': 'Clips/ 공용 영상 폴더. 선택한 이미지 버전과 영상 버전의 일치는 미검증입니다.',
        'warnings': [m for m in media_checks if m['state'] != 'NOT_CAPTURED' and
                     (m['state'] != 'PNG_HEADER_PRESENT' or not m.get('aspect16x9', False) or not m.get('atLeast720p', False))],
        'media': media_checks
    }
    payload = json.dumps({'spells': records, 'census': {k:v for k,v in census.items() if k != 'media'}},
                         ensure_ascii=False, separators=(',', ':')).replace('</', '<\\/')
    template = HTML_TEMPLATE.replace('__PAGE_TITLE__', html.escape(args.title)).replace('__DATA__', payload)
    (output / 'REVIEW.html').write_text(template, encoding='utf-8')
    (output / 'review_file_validation.json').write_text(json.dumps(census, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({k:v for k,v in census.items() if k not in ('media', 'warnings')}, ensure_ascii=False))


HTML_TEMPLATE = r'''<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="color-scheme" content="light">
<title>__PAGE_TITLE__</title>
<style>
:root{--paper:#f0ecdf;--card:#fbf8ef;--ink:#292922;--muted:#6d7066;--line:#d5d0bf;--accent:#456b5c;--shadow:0 6px 22px #27281f10}
*{box-sizing:border-box}body{margin:0;background:var(--paper);color:var(--ink);font:15px/1.55 "Malgun Gothic","Apple SD Gothic Neo",sans-serif}button,input,select{font:inherit}button,select{cursor:pointer}button:focus-visible,input:focus-visible,select:focus-visible{outline:3px solid #618975;outline-offset:3px}a{color:var(--accent)}header,main,footer{max-width:1540px;margin:auto;padding:24px 32px}header{padding-top:30px;padding-bottom:16px}h1{font-size:30px;line-height:1.3;font-weight:700;margin:0 0 8px;letter-spacing:-.05em}h2,h3,p{margin-top:0}.eyebrow{font-size:12px;letter-spacing:.15em;color:var(--muted);margin-bottom:8px}.intro{max-width:1050px;color:#555b50;margin-bottom:12px}.statistics{display:flex;gap:10px;flex-wrap:wrap}.stat{background:#e5e4d5;border:1px solid var(--line);padding:7px 12px;border-radius:6px;font-size:13px}.stat b{font-variant-numeric:tabular-nums;font-size:17px;margin-right:5px}.notice{border-left:3px solid #9a805d;padding:7px 12px;font-size:13px;color:#625d50;max-width:1100px;margin:14px 0 0}.toolbar{position:sticky;top:0;z-index:5;background:#f0ecdfef;backdrop-filter:blur(12px);border-block:1px solid var(--line);padding:13px 32px}.toolbar-inner{max-width:1476px;margin:auto;display:flex;align-items:center;gap:10px;flex-wrap:wrap}.element-tabs{display:flex;gap:4px;flex-wrap:wrap}.element-tabs button{border:1px solid var(--line);background:var(--card);border-radius:5px;padding:6px 12px;color:var(--muted)}.element-tabs button[aria-pressed=true]{background:var(--ink);color:var(--card);border-color:var(--ink)}.search{display:flex;align-items:center;gap:8px;margin-left:auto}.search input,select{height:36px;border:1px solid var(--line);border-radius:5px;background:var(--card);color:var(--ink);padding:5px 10px}.search input{width:230px}.result-line{font-size:13px;color:var(--muted);margin:0 0 14px;display:flex;justify-content:space-between;gap:15px;flex-wrap:wrap}.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(245px,1fr));gap:17px}.card{border:1px solid var(--line);background:var(--card);border-radius:8px;overflow:hidden;box-shadow:var(--shadow);display:flex;flex-direction:column}.open-card{padding:0;border:0;background:#3c4039;text-align:inherit;color:inherit;width:100%;position:relative;display:block}.thumb{width:100%;aspect-ratio:16/9;display:block;object-fit:contain;background:#3c4039}.placeholder{aspect-ratio:16/9;display:flex;flex-direction:column;align-items:center;justify-content:center;background:linear-gradient(135deg,#dddccf,#e7e4d8);color:#7c8073;gap:4px}.placeholder strong{font-size:31px;font-weight:400;opacity:.42}.placeholder small{font-size:12px}.card-body{padding:13px 15px;flex:1}.card-title{display:flex;gap:10px;align-items:center;margin-bottom:5px}.glyph{font-size:29px;line-height:1.2;font-weight:700}.card-title h2{font-size:15px;margin:0;line-height:1.4}.meta{font-size:12px;color:var(--muted);margin-bottom:8px}.intent{font-size:12px;line-height:1.55;color:#5a5d53;display:-webkit-box;-webkit-box-orient:vertical;-webkit-line-clamp:2;overflow:hidden;margin-bottom:10px}.card-footer{display:flex;align-items:center;justify-content:space-between;gap:5px;flex-wrap:wrap}.pill{font-size:11px;border:1px solid #bacbbb;background:#e6eee3;color:#3f644e;border-radius:4px;padding:2px 5px}.pill.asset{border-color:#c7c8b8;background:#ececdc;color:#656746}.pill.blank{border-color:#d8c8b1;background:#f2e8d8;color:#806645}.capture-count{font-size:11px;color:var(--muted)}.empty{padding:60px;text-align:center;color:var(--muted)}footer{color:var(--muted);font-size:12px;border-top:1px solid var(--line);margin-top:20px;padding-top:15px;display:flex;justify-content:space-between;gap:10px;flex-wrap:wrap}.sr-only{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0,0,0,0)}
dialog{border:1px solid #605f52;padding:0;width:min(1330px,96vw);max-height:95vh;border-radius:10px;background:var(--card);color:var(--ink);box-shadow:0 20px 90px #0006}dialog::backdrop{background:#181b17d4;backdrop-filter:blur(3px)}.dialog-head{display:flex;align-items:center;justify-content:space-between;gap:18px;padding:15px 22px;border-bottom:1px solid var(--line);position:sticky;top:0;background:var(--card);z-index:3}.dialog-title{margin:0;font-size:21px;line-height:1.3}.dialog-head button,.nav-actions button,.phase-tabs button,.media-tabs button{background:var(--paper);border:1px solid var(--line);border-radius:4px;padding:5px 11px;color:var(--ink)}.dialog-scroll{overflow-y:auto;max-height:calc(95vh - 72px);padding:18px 22px 22px}.media-layout{display:grid;grid-template-columns:minmax(0,2fr) minmax(260px,.8fr);gap:20px}.media-stage{aspect-ratio:16/9;background:#343a34;border-radius:6px;overflow:hidden;display:flex;align-items:center;justify-content:center}.media-stage img,.media-stage video{width:100%;height:100%;object-fit:contain;display:block}.media-stage .placeholder{width:100%;height:100%;background:#343a34;color:#c1c8bb}.media-stage .placeholder strong{font-size:52px}.media-tabs,.phase-tabs,.nav-actions{display:flex;gap:7px;flex-wrap:wrap;margin:10px 0}.phase-tabs button[aria-selected=true],.media-tabs button[aria-selected=true]{background:var(--ink);color:var(--card)}.phase-tabs button:disabled{opacity:.36;cursor:default}.media-caption{font-size:12px;color:var(--muted);margin:7px 0 14px;overflow-wrap:anywhere}.source-box{padding:13px;border:1px solid var(--line);border-radius:6px;background:#f2eee1;font-size:12px}.source-box p{margin:0 0 8px}.source-box p:last-child{margin:0}.source-path{overflow-wrap:anywhere;font-size:11px}.swatches{display:flex;gap:8px;margin:10px 0}.swatch{width:25px;height:25px;border:1px solid #0002;border-radius:50%;display:block}.detail-aside h3{font-size:15px;margin:17px 0 7px}.detail-aside p{font-size:13px;color:#54594e;margin-bottom:9px}.phases{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin-top:18px}.phase{border-top:2px solid #bdbb9f;padding:10px 12px;background:#f0eddf;border-radius:0 0 5px 5px}.phase b{display:block;font-size:12px;margin-bottom:5px;color:#456652}.phase p{font-size:12px;margin:0;color:#555a4e}.nav-actions{justify-content:space-between;margin-top:18px}.data-note{font-size:12px;color:#7b6e55;border-top:1px solid var(--line);margin-top:15px;padding-top:10px}
@media(max-width:900px){header,main,footer{padding-inline:18px}.toolbar{padding-inline:18px}.search{margin-left:0;flex:1}.search input{flex:1;width:140px}.media-layout{grid-template-columns:1fr}.detail-aside{display:grid;grid-template-columns:1fr 1fr;gap:15px}.detail-aside h3{margin-top:0}.phases{grid-template-columns:1fr 1fr}.dialog-scroll{padding:13px}.dialog-head{padding:12px 14px}h1{font-size:26px}}
@media(max-width:530px){.grid{grid-template-columns:1fr 1fr;gap:9px}.card-body{padding:9px}.card-title{gap:6px;align-items:flex-start}.card-title h2{font-size:12px}.glyph{font-size:24px}.intent{display:none}.card-footer{gap:4px}.capture-count{font-size:10px}.pill{font-size:9px}.meta{font-size:10px}.detail-aside{grid-template-columns:1fr}.dialog-title{font-size:18px}.phases{grid-template-columns:1fr}.statistics{gap:5px}.stat{padding:5px 8px}.element-tabs button{padding:5px 9px}select{max-width:100%}}
@media(prefers-reduced-motion:reduce){*{scroll-behavior:auto!important}}
</style>
</head>
<body>
<header>
<p class="eyebrow">오행부 / 신규 술식 시각 검수</p>
<h1>__PAGE_TITLE__</h1>
<p class="intro">카드를 열면 같은 술식의 다섯 시간 샘플과 의도한 동작을 함께 볼 수 있습니다. 영상은 파일이 있는 항목에서 재생할 수 있습니다.</p>
<div class="statistics" id="statistics"></div>
<p class="notice" id="captureVersion"></p>
<p class="notice">기존 술식 15자는 카탈로그상 대응 항목입니다. 실제 플레이어 연결·판정·미술 완성은 별도 검수합니다. 나머지 85자는 자산 검수 대상이며, 미배정 20자는 시각 후보입니다.</p>
</header>
<section class="toolbar" aria-label="술식 검색과 필터"><div class="toolbar-inner">
<div class="element-tabs" id="elements" aria-label="오행 필터"></div>
<label class="sr-only" for="statusFilter">배정 상태</label><select id="statusFilter"><option value="all">모든 상태</option><option value="connected">기존 술식 대응 · 15</option><option value="asset">자산 검수 대상 · 85</option><option value="blank">미배정 시각 후보 · 20</option><option value="captured">촬영본 있음</option><option value="missing">미촬영</option></select>
<label class="search" for="searchInput"><span class="sr-only">글자·이름·효과 검색</span><input id="searchInput" type="search" placeholder="글자·이름·효과 검색" autocomplete="off"></label>
</div></section>
<main><div class="result-line"><span id="resultCount" aria-live="polite"></span><span>원본 어휘 순서 · 목 → 화 → 토 → 금 → 수</span></div><div class="grid" id="grid"></div><p class="empty" id="empty" hidden>조건에 맞는 술식이 없습니다.</p></main>
<footer><span id="generated"></span><span>오프라인 검수 페이지 · <a href="build_manifest_validation.json">데이터 검사</a> · <a href="review_file_validation.json">선택한 촬영본·소스 정보</a></span></footer>
<dialog id="detail" aria-labelledby="dialogTitle">
<div class="dialog-head"><h2 class="dialog-title" id="dialogTitle"></h2><button id="closeDialog" aria-label="큰 미리보기 닫기">닫기 ×</button></div>
<div class="dialog-scroll"><div class="media-layout"><div>
<div class="media-tabs" role="tablist" aria-label="검수 미디어"><button id="stillTab" role="tab" aria-selected="true">이미지 5장</button><button id="clipTab" role="tab" aria-selected="false">동작 영상</button></div>
<div class="media-stage" id="mediaStage"></div><div class="phase-tabs" id="phaseTabs" role="tablist" aria-label="촬영 샘플"></div><p class="media-caption" id="mediaCaption"></p>
<div class="source-box" id="sourceBox"></div>
</div><aside class="detail-aside"><div><div id="dialogStatus"></div><h3>술식의 효과</h3><p id="intentText"></p><h3>먼저 볼 부분</h3><p id="readabilityText"></p></div><div><h3>형태와 움직임</h3><p id="shapeText"></p><p id="motionText"></p><div class="swatches" id="swatches"></div><p id="accentText"></p></div></aside></div>
<div class="phases" id="phaseDescriptions"></div><p class="data-note" id="detailNote"></p><div class="nav-actions"><button id="previousSpell">← 이전 술식</button><button id="nextSpell">다음 술식 →</button></div>
</div></dialog>
<script id="review-data" type="application/json">__DATA__</script>
<script>
'use strict';
const DATA=JSON.parse(document.getElementById('review-data').textContent);
const spells=DATA.spells, census=DATA.census;
const $=id=>document.getElementById(id);
const labels={connected:'기존 술식 대응',asset:'자산 검수 대상',blank:'미배정 · 시각 후보'};
let element='all',filtered=spells,selected=null,phase=2,mediaMode='still';
function node(tag,cls,text){const n=document.createElement(tag);if(cls)n.className=cls;if(text!==undefined)n.textContent=text;return n;}
function pill(s){return node('span','pill '+s.status,labels[s.status]);}
function placeholder(s,detail=false){const n=node('div','placeholder');n.append(node('strong','',s.glyph),node('small','',detail?'아직 촬영하지 않은 샘플입니다.':'미촬영'));return n;}
function stat(n,t){const s=node('span','stat');s.append(node('b','',String(n)),document.createTextNode(t));return s;}
$('statistics').append(stat(census.count,'술식'),stat(census.capturedStills+'/'+census.expectedStills,'이미지'),stat(census.presentVideos+'/120','영상'),stat(census.connected,'기존 술식 대응'),stat(census.intentionalBlanks,'미배정'));
$('captureVersion').textContent='촬영본: '+census.captureVersion.stage+' ('+census.captureVersion.stillsFolder+'/). '+census.sourceDescription;
$('generated').textContent='파일 목록 갱신: '+new Date(census.generatedAtUtc).toLocaleString('ko-KR');
for(const [value,label] of [['all','전체'],['목','목 · 나무'],['화','화 · 불'],['토','토 · 흙'],['금','금 · 쇠'],['수','수 · 물']]){
 const b=node('button','',label);b.type='button';b.dataset.element=value;b.setAttribute('aria-pressed',String(value===element));
 b.addEventListener('click',()=>{element=value;for(const x of $('elements').children)x.setAttribute('aria-pressed',String(x.dataset.element===element));render();});$('elements').append(b);
}
function chosenStill(s){return s.media[2].path?s.media[2]:s.media.find(m=>m.path);}
function render(){
 const status=$('statusFilter').value,q=$('searchInput').value.trim().toLocaleLowerCase();
 filtered=spells.filter(s=>(element==='all'||s.element===element)&&(status==='all'||s.status===status||(status==='captured'&&(s.capturedCount>0||s.clip))||(status==='missing'&&s.capturedCount===0&&!s.clip))&&(!q||[s.glyph,s.title,s.intent,s.category,s.shape].join(' ').toLocaleLowerCase().includes(q)));
 $('grid').replaceChildren();
 for(const s of filtered){const card=node('article','card'),button=node('button','open-card');button.type='button';button.setAttribute('aria-label',s.glyph+' · '+s.title+' 크게 보기');
  const shot=chosenStill(s);if(shot){const img=node('img','thumb');img.loading='lazy';img.decoding='async';img.alt=s.glyph+' '+s.title+' 실제 촬영';img.src=shot.path;img.addEventListener('error',()=>{button.replaceChildren(placeholder(s));},{once:true});button.append(img);}else button.append(placeholder(s));
  button.addEventListener('click',()=>openSpell(s));const body=node('div','card-body'),title=node('div','card-title');title.append(node('span','glyph',s.glyph),node('h2','',s.title));
  const foot=node('div','card-footer');foot.append(pill(s),node('span','capture-count',s.capturedCount+'/5장'+(s.clip?' · 영상':'')));
  body.append(title,node('p','meta',String(s.index).padStart(3,'0')+' · '+s.element+' · '+s.category),node('p','intent',s.intent),foot);card.append(button,body);$('grid').append(card);
 }
 $('resultCount').textContent=filtered.length+'개 표시 / 전체 120개';$('empty').hidden=filtered.length!==0;
}
function showMedia(){
 const s=selected,stage=$('mediaStage');stage.replaceChildren();$('phaseTabs').replaceChildren();
 $('stillTab').setAttribute('aria-selected',String(mediaMode==='still'));$('clipTab').setAttribute('aria-selected',String(mediaMode==='clip'));
 if(mediaMode==='clip'){
  if(s.clip){const v=node('video');v.controls=true;v.preload='metadata';v.playsInline=true;v.src=s.clip;v.setAttribute('aria-label',s.glyph+' 실제 동작 검수 영상');v.addEventListener('error',()=>{stage.replaceChildren(placeholder(s,true));$('mediaCaption').textContent='영상 파일을 재생할 수 없습니다. 파일 검사 보고서를 확인하세요.';},{once:true});stage.append(v);$('mediaCaption').textContent=s.clip+' · 원래 재생 속도로 확인하세요. '+census.videoSource;}
  else{stage.append(placeholder(s,true));$('mediaCaption').textContent='동작 영상 미촬영 · 예정 파일: '+s.clipExpected;}
 }else{
  const sample=s.media[phase];if(sample.path){const img=node('img');img.src=sample.path;img.alt=s.glyph+' 검수 샘플 '+(phase+1);img.addEventListener('error',()=>{stage.replaceChildren(placeholder(s,true));$('mediaCaption').textContent='촬영 파일을 읽을 수 없습니다. 갤러리를 다시 생성해 주세요.';},{once:true});stage.append(img);
   const info=sample.inspection,seconds=s.capture.sampledSeconds?.[phase];$('mediaCaption').textContent=census.captureVersion.stage+' · '+sample.expected+' · '+info.width+' × '+info.height+(info.aspect16x9?' · 16:9':' · 화면 비율 확인 필요')+(Number.isFinite(seconds)?' · 시작 후 '+seconds.toFixed(3)+'초':' · 촬영 시점 미기록');
  }else{stage.append(placeholder(s,true));$('mediaCaption').textContent='샘플 '+(phase+1)+' 미촬영 · 예정 파일: '+sample.expected;}
  for(let i=0;i<5;i++){const b=node('button','',String(i+1)+(s.media[i].path?'':' · 미촬영'));b.role='tab';b.setAttribute('aria-selected',String(i===phase));b.addEventListener('click',()=>{phase=i;showMedia();});$('phaseTabs').append(b);}
 }
}
function openSpell(s){
 selected=s;phase=s.media[2].path?2:Math.max(0,s.media.findIndex(m=>m.path));mediaMode='still';$('dialogTitle').textContent=s.glyph+' · '+s.title;$('dialogStatus').replaceChildren(pill(s));
 $('intentText').textContent=s.intent;$('readabilityText').textContent=s.readability;$('shapeText').textContent=s.shape;$('motionText').textContent=s.motion;$('accentText').textContent='보조색 · '+s.accentName;
 $('swatches').replaceChildren();for(const [key,label] of [['primary','기본 안료'],['secondary','보조 안료'],['ink','먹'],['paper','소지']]){const sw=node('span','swatch');sw.style.backgroundColor=s.palette[key];sw.title=label+' '+s.palette[key];sw.setAttribute('aria-label',sw.title);$('swatches').append(sw);}
 const source=$('sourceBox');source.replaceChildren(node('p','', '현재 설계의 문양 원료 · '+s.patternName),node('p','source-path',s.pattern),node('p','', 'KoreanTraditionalPattern_Effect의 검수한 알파 문양입니다. 촬영본과 현재 설계의 일치는 미검증입니다.'));
 if(s.captureMetadataPath){const p=node('p'),a=node('a','','촬영 기록');a.href=s.captureMetadataPath;a.target='_blank';a.rel='noopener';p.append(a,document.createTextNode(s.capture.demonstrationCues?' · 연출 검수용 시연 이벤트 포함':''));source.append(p);}
 $('phaseDescriptions').replaceChildren();for(const [key,label] of [['cast','시전'],['travel','이동·전개'],['impact','명중·작용'],['resolve','소멸·복귀']]){const block=node('div','phase');block.append(node('b','',label),node('p','',s[key]));$('phaseDescriptions').append(block);}
 $('detailNote').textContent=(s.status==='blank'?'이 글자는 의도적 공백입니다. 연출은 시각 후보이며 전투 효과나 해금을 배정하지 않았습니다. ':s.status==='asset'?'현재 게임 이벤트 연결이 확인되지 않은 자산 검수 대상입니다. ':'카탈로그상 기존 술식 대응 항목입니다. 실제 플레이어 연결·시전·판정은 별도 검수합니다. ')+(s.sourceNote?'원본 비고: '+s.sourceNote+' ':'')+'4단계 설명과 색상은 현재 설계입니다. 촬영본의 완성·동작 통과를 뜻하지 않습니다.';
 showMedia();if(!$('detail').open)$('detail').showModal();$('detail').querySelector('.dialog-scroll').scrollTop=0;
}
function navigate(step){if(!selected)return;const list=filtered.length?filtered:spells;let i=list.findIndex(s=>s.glyph===selected.glyph);if(i<0)i=0;openSpell(list[(i+step+list.length)%list.length]);}
$('searchInput').addEventListener('input',render);$('statusFilter').addEventListener('change',render);
$('closeDialog').addEventListener('click',()=>{$('detail').close();});$('detail').addEventListener('close',()=>{$('mediaStage').replaceChildren();});
$('detail').addEventListener('click',e=>{if(e.target===$('detail')){const r=$('detail').getBoundingClientRect();if(e.clientX<r.left||e.clientX>r.right||e.clientY<r.top||e.clientY>r.bottom)$('detail').close();}});
$('stillTab').addEventListener('click',()=>{mediaMode='still';showMedia();});$('clipTab').addEventListener('click',()=>{mediaMode='clip';showMedia();});$('previousSpell').addEventListener('click',()=>navigate(-1));$('nextSpell').addEventListener('click',()=>navigate(1));
document.addEventListener('keydown',e=>{if(!$('detail').open)return;if(e.key==='ArrowLeft'){e.preventDefault();phase=(phase+4)%5;mediaMode='still';showMedia();}if(e.key==='ArrowRight'){e.preventDefault();phase=(phase+1)%5;mediaMode='still';showMedia();}});
render();
</script></body></html>'''


if __name__ == '__main__':
    main()
