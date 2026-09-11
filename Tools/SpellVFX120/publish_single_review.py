"""Publish one authored item from completed evidence; never evaluates its visual quality."""
import argparse
from pathlib import Path
import json
import html
import re
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Art/SpellVFX120"

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("metadata")
    args = parser.parse_args()
    meta = json.loads(Path(args.metadata).read_text(encoding="utf-8"))
    item, version, key = meta["id"], meta["version"], meta["key"]
    audit = json.loads((OUT / meta["audit"]).read_text(encoding="utf-8"))
    row = next(r for r in audit["rows"] if r["glyph"] == meta["glyph"])
    if audit["status"] != "PASS_REAL_UPDATE_LIFETIME_ONLY" or row["status"] != "PASS_REAL_UPDATE_LIFETIME_ONLY" or not audit["restored"]:
        raise RuntimeError("Selected technical audit has not passed and restored")
    media = []
    for kind in ("IntegratedStills", "ExternalStills", "IntegratedFrames", "ExternalFrames"):
        folder = "KTP" + kind + version
        data = json.loads((OUT / folder / (item + "_capture.json")).read_text(encoding="utf-8"))
        same = data["loadedRuntimeAssemblyMvid"] == audit["appMvid"]
        if not same: raise RuntimeError("Capture/runtime DLL mismatch: " + folder)
        media.append(dict(folder=folder, mvid_match=same))
    for kind in ("Integrated", "External"):
        encoded = json.loads((OUT / ("ClipsKTP" + kind + version) / (item + "_encoding.json")).read_text(encoding="utf-8"))
        if encoded["status"] != "VERIFIED_ENCODING_AND_TIMING_ONLY": raise RuntimeError("Encoding incomplete")
    before = meta.get("before", "KTPIntegratedStills21/" + item + "_1.png")
    images = ["KTP" + k + version + "/" + item + suffix for k, suffix in
              [("IntegratedStills", "_1.png"), ("ExternalStills", "_1.png"), ("ExternalStills", "_2.png"), ("ExternalStills", "_4.png")]]
    esc = html.escape
    report_name, page_name = key + "_REPORT.md", key + "_REVIEW.html"
    report = ["# " + meta["glyph"] + " · " + meta["title"], "", "비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.", "", "## 변경", ""]
    report.extend("- " + x for x in meta["changes"])
    report += ["", "## 실제 비용", "", meta["cost"], "", "## 기술 확인", "",
               "단독 C2 실제 Update/Destroy 검사 통과. 오류 " + str(audit["errors"]) + ", 셰이더 오류 " + str(audit["shaderErrors"]) + ".",
               "", "| 항목 | 관측값 |", "|---|---:|"]
    metrics = ["life", "destroyedAt", "castPeak", "impactPeak", "nativeSystemsPeak", "capacityPeak"] + meta.get("metrics", [])
    report.extend("| " + k + " | " + str(row[k]) + " |" for k in metrics if k in row)
    report += ["", "씬·카메라·참조 자산 복귀: " + str(audit["restored"] and audit["sourceUnchanged"] and audit["sceneUnchanged"]) + ". root·children 제거: " + str(row["rootGone"] and row["childrenGone"]) + ".",
               "", "App MVID: " + audit["appMvid"] + ". 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.",
               "", "## 미완료·미검증", ""]
    report.extend("- " + x for x in meta["limits"])
    backend_note = ("이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다." if str(meta.get("before", "21")) == "21" else "위에 명시된 제작 버전의 비교 기록이다. 최신 공통 런타임의 재촬영이나 미술·전체 성능 PASS를 의미하지 않는다.")
    report += ["", meta["captureNote"], "", meta.get("backendNote", backend_note),
               "", "근거: " + meta["audit"] + ", " + meta["build"] + ", " + meta["scope"] + ".", ""]
    (OUT / report_name).write_text("\n".join(report), encoding="utf-8")
    old = (OUT / "GO_013_REVIEW.html").read_text(encoding="utf-8")
    style = old[old.index("<style>"):old.index("</style>") + 8]
    page = '<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>' + esc(meta["title"]) + '</title>' + style
    page += '<main><span class="tag">' + esc(meta["glyph"]) + ' · 사용자 비주얼 검토 대기</span><h1>' + esc(meta["title"]) + '</h1><p>' + esc(meta["intro"]) + '</p><div class="note">' + esc(meta["limits"][0]) + '</div><h2>짧은 재생</h2><div class="grid">'
    for kind, poster, label in [("Integrated", images[0], "C2 기본 카메라"), ("External", images[1], "외부 카메라")]:
        page += '<figure><video controls muted loop playsinline preload="metadata" poster="' + poster + '" src="ClipsKTP' + kind + version + '/' + item + '.mp4"></video><figcaption>' + label + '</figcaption></figure>'
    page += '</div><button id="play">두 영상 함께 재생</button><h2>수정 전후</h2><div class="grid">'
    for path, label in [(before, "이전"), (images[0], "현재")]:
        page += '<figure><a href="' + path + '"><img loading="lazy" src="' + path + '" alt="' + label + '"></a><figcaption>' + label + '</figcaption></figure>'
    page += '</div><h2>외부 시점</h2><div class="grid">'
    for path, label in zip(images[2:], meta.get("detailLabels", ["진행", "소멸"])):
        page += '<figure><a href="' + path + '"><img src="' + path + '" alt="' + esc(label) + '"></a><figcaption>' + esc(label) + '</figcaption></figure>'
    page += '</div><details><summary>구현 기록과 확인 범위</summary>' + ''.join('<p>' + esc(x) + '</p>' for x in meta["changes"])
    page += '<p>' + esc(meta["cost"]) + '</p><p>' + esc(meta["captureNote"]) + '</p><p>'
    links = [(report_name, "작업 보고서"), (meta["audit"], "단독 Play"), (meta["build"], "제작 검사"), (meta["scope"], "변경 범위")]
    page += ' · '.join('<a href="' + x + '">' + y + '</a>' for x,y in links) + '</p></details><p><a href="' + meta["previous"] + '">이전 시안</a> · <a href="KTP_REWORK.html">기존 전체 목록</a></p></main>'
    page += '<script>document.getElementById("play").onclick=()=>document.querySelectorAll("video").forEach(v=>{v.currentTime=0;v.play().catch(()=>{});});</script></html>'
    (OUT / page_name).write_text(page, encoding="utf-8")
    delivery = dict(status="PASS", links=[], media=media)
    for f in sorted(set(re.findall(r'(?:href|src|poster)="([^"]+)"', page))):
        exists=(OUT/f).is_file()
        with urllib.request.urlopen(urllib.request.Request("http://127.0.0.1:8771/"+urllib.parse.quote(f),method="HEAD"),timeout=10) as response: code=response.status
        delivery["links"].append(dict(path=f,exists=exists,http=code))
        if not exists or code!=200: delivery["status"]="FAIL"
    (OUT/(key.lower()+"_delivery_check.json")).write_text(json.dumps(delivery,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(dict(status=delivery["status"],review=page_name,links=len(delivery["links"]),mvid=audit["appMvid"]),ensure_ascii=False))

if __name__ == "__main__": main()
