"""Check delivery links, hashes and recorded media gates without awarding art PASS."""
from pathlib import Path
from html.parser import HTMLParser
from urllib.parse import unquote,urlsplit
import json,hashlib

ROOT=Path('C:/Users/yj666/Oheangbu')
LAB=ROOT/'Art/PlayerPhase1/C02_RigFaceLab'
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))

class Links(HTMLParser):
    def __init__(self):super().__init__();self.urls=[]
    def handle_starttag(self,tag,attrs):
        self.urls.extend(value for key,value in attrs if key in ('src','href') and value)

def run():
    before=read(LAB/'preservation_before.json');checks=[]
    for name,item in before['files'].items():
        checks.append({'check':'protected_source','path':name,'pass':sha(Path(name))==item['sha256']})
    checks.append({'check':'baseline_copy','pass':sha(LAB/'Baseline.blend')==before['baseline_sha256']})
    quality=read(LAB/'Unity/quality_preservation_before.json')
    checks.append({'check':'global_quality_exact','pass':sha(Path(quality['path']))==quality['sha256']})
    final=read(LAB/'final_summary.json')
    for name,item in final['artifacts'].items():
        checks.append({'check':'artifact_hash','artifact':name,'pass':sha(LAB/item['path'])==item['sha256']})
    unity=read(LAB/'Unity/unity_validation.json')
    checks.append({'check':'unity_import_final_fbx','pass':unity['import']['sha256']==sha(LAB/'Final/Integrated_B2_C3.fbx')})
    checks.append({'check':'original_texture','pass':sha(LAB/'Final/Textures/C02_original_texture.png')=='146f6acb6d30310961e862fe1df5eae930bfd6de5e747f386d1cf9811973c032'})
    for folder in ['B2_Final_Blink_Close','B2_Final_Sequence_Full']:
        video=read(LAB/'Unity'/folder/'video_validation.json')
        checks.append({'check':'recorded_full_media_decode','folder':folder,'pass':video['status']=='TECHNICAL_PASS' and all(video['video']['checks'].values())})
    parser=Links();parser.feed((LAB/'REVIEW.html').read_text(encoding='utf8'))
    broken=[]
    for value in parser.urls:
        parsed=urlsplit(value)
        if parsed.scheme or parsed.netloc or not parsed.path:continue
        if not (LAB/unquote(parsed.path)).is_file():broken.append(value)
    checks.append({'check':'review_local_links','count':len(parser.urls),'broken':broken,'pass':not broken})
    checks.append({'check':'additional_cost_zero','pass':read(LAB/'additional_cost_ledger.json')['actual_additional_credits']==0})
    result={'status':'PASS_DELIVERY_CHECKS_ONLY' if all(c['pass'] for c in checks) else 'FAIL','quality':'UNVERIFIED','checks':checks}
    (LAB/'handoff_validation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
    print(json.dumps({'status':result['status'],'checks':len(checks),'broken_links':broken,'failures':[c for c in checks if not c['pass']]}))
    if result['status']=='FAIL':raise SystemExit(1)

if __name__=='__main__':run()
