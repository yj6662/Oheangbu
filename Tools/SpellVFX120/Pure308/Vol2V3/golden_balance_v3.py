"""Freeze additional original-checker calculations; refuse existing output."""
import hashlib, importlib.util, json, pathlib, sys
sys.dont_write_bytecode=True
HERE=pathlib.Path(__file__).resolve().parent
ROOT=HERE.parents[3]
source=ROOT/'Docs/Research/debate25/check_table120_v10.py'
out=HERE/'Golden/balance.json'
if out.exists(): raise SystemExit('Frozen balance golden exists; no automatic refresh.')
sys.argv=[str(source)]
spec=importlib.util.spec_from_file_location('v10',source)
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
header,data=m.load_csv();doc=m.load_doc();result={'checkerHash':hashlib.sha256(source.read_bytes()).hexdigest(),'candidateRankings':{}}
def profile(frame,event,value):
    if event!='return':return
    if frame.f_code.co_name=='check_c4':result['efficiency']=frame.f_locals['eff']
    if frame.f_code.co_name=='ranking' and m.PEAK_OVERRIDE:
        v=frame.f_locals
        if v['g_share']==0.85 and v['eo_second']==0.25:
            result['candidateRankings'][str(int(v['n_targets']))]=[{'letter':r.ch,'score':s} for s,r in value]
sys.setprofile(profile);m.run_checks(header,data,doc);sys.setprofile(None)
assert not m._fails()
m.EO_SECOND=0.25;m.PEAK_OVERRIDE={'ㄱ재료':0.85,'뒤쪽':0.315}
result['candidatePeaks']=[{'letter':r.ch,'total':m.total_of(m.compute_peak_terms(r))} for r in m.make_rows(header,data) if r.kind=='극치']
assert len(result['candidateRankings'])==2
out.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Frozen balance golden: efficiency 1/2, candidate ranks 1/2, candidate peaks 20')
