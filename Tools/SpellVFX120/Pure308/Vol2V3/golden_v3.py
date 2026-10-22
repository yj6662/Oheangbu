"""One-time extraction from the unmodified v10 checker; never overwrites a golden."""
import csv, difflib, hashlib, importlib.util, io, json, pathlib, sys
sys.dont_write_bytecode = True
HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[3]
BASE = ROOT / 'Docs/Research/debate25'
paths = {'numbers': ROOT/'Docs/Research/e0/numbers_v3.csv', 'table': BASE/'vol2_table120_v10.csv', 'markdown': BASE/'vol2_v10.md', 'checker': BASE/'check_table120_v10.py', 'sheet': BASE/'e2_sheet_v10.md', 'contract': ROOT/'Docs/Research/e3/port_contract_v3.md'}
out = HERE/'Golden/source.json'
if out.exists():
    raise SystemExit('Frozen golden exists; automatic refresh is forbidden.')
spec = importlib.util.spec_from_file_location('v10', paths['checker'])
m = importlib.util.module_from_spec(spec)
sys.argv = [str(paths['checker'])]
spec.loader.exec_module(m)
header, data = m.load_csv()
doc = m.load_doc()
rows = m.make_rows(header, data)
ranks = {}
def profile(frame, event, result):
    if event == 'return' and frame.f_code.co_name == 'ranking':
        v = frame.f_locals
        if m.EO_SECOND == 0.3 and all(v.get(k) is None for k in ('frames','eo_cost','g_share','bare_ab','eo_second')) and v['body_value'] == m.parse_body_value(doc):
            n = int(v['n_targets'])
            ranks.setdefault(str(n), [{'letter': r.ch, 'score': score} for score, r in result])
sys.setprofile(profile)
m.run_checks(header,data,doc)
sys.setprofile(None)
checks = [{'id': c.cid, 'pass': c.count('PASS'), 'fail': c.count('FAIL'), 'warn': c.count('WARN')} for c in m.CHECKS]
assert not m._fails(), m._fails()
per,shared = m.parse_area_split(doc)
eo_per,eo_shared,_ = m.parse_area_split_eo(doc)
caps,eo_caps = m.parse_peak_caps(doc)
records=[]
for r in rows:
    terms = m.compute_peak_terms(r) if r.kind == '극치' else []
    p = sum(v*n for name,v,n in terms if not m.is_shared_term(name,n)) if terms else (0 if r.is_parry else (eo_per if r.jung=='ㅓ' else per)[r.jong])
    s = sum(v*n for name,v,n in terms if m.is_shared_term(name,n)) if terms else (0 if r.is_parry else (eo_shared if r.jung=='ㅓ' else shared)[r.jong])
    records.append({'letter':r.ch,'cost':r.cost or 0,'perTarget':p,'shared':s,'terms':[{'name':name,'amount':v,'count':n,'shared':m.is_shared_term(name,n)} for name,v,n in terms], 'cap':m.cap_for(r,caps,eo_caps) if r.kind=='극치' else None})
negative=[]
original=m.run_checks
def capture(h, dat, dc):
    changes=[{'row':i,'column':j,'value':v} for i,row in enumerate(dat) for j,v in enumerate(row) if v != data[i][j]]
    a,b=doc.splitlines(keepends=True),dc.splitlines(keepends=True)
    patches=[{'old':''.join(a[i:j]),'new':''.join(b[k:l])} for op,i,j,k,l in difflib.SequenceMatcher(None,a,b).get_opcodes() if op!='equal']
    original(h,dat,dc)
    negative.append({'cells':changes,'document':patches,'sourceFailures':len(m._fails())})
m.run_checks=capture
selftests=m.selftests(header,data,doc)
for test,info in zip(negative,selftests):
    test['name']=info[0]
    assert test['sourceFailures']>0
m.run_checks=original
m.EO_SECOND=0.3
numbers=list(csv.DictReader(paths['numbers'].open(encoding='utf-8-sig',newline='')))
def sha(b):return hashlib.sha256(b).hexdigest()
# Freeze the recognized narrative dialect; numeric formulas are independently checked in C#.
lines=['using System;','namespace Oheangbu.Vol2.Rules','{','    internal static class ContractSchemaV3','    {']
def cs(s):return '@"'+s.replace('"','""')+'"'
lines+=['        internal const string Numbers = '+cs('\n'.join('|'.join([r['Key'],r['Unit'],'null' if not r['Value'] else 'value',r['Candidate'],r['CandidateTrigger']]) for r in numbers))+';']
lines+=['        internal const string Narratives = '+cs('\n'.join(r[0]+'|'+ '|'.join(sha(v.encode()) for v in r[5:]) for r in data))+';','    }','}']
(HERE/'src').mkdir(exist_ok=True)
(HERE/'src/ContractSchemaV3.cs').write_text('\n'.join(lines)+'\n',encoding='utf-8')
result={'schema':'vol2-v3/1','hashes':{k:sha(p.read_bytes()) for k,p in paths.items()},'checks':checks,'rows':records,'rankings':ranks,'negative':negative,'reactionCells':[{'acting':a,'existing':b,'text':v} for (a,b),v in m.parse_reaction_table(doc)[0].items()]}
out.parent.mkdir(exist_ok=True)
out.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Frozen source golden:',len(records),'rows;',len(negative),'negative;',checks)
