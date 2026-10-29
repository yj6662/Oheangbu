"""Independent Python canonical DTO hash; never refreshes an existing manifest."""
import csv, decimal, hashlib, json, pathlib
H=pathlib.Path(__file__).resolve().parent
R=H.parents[3]
out=H/'Golden/import_manifest.json'
if out.exists(): raise SystemExit('Frozen import manifest exists; no automatic refresh.')
g=json.loads((H/'Golden/source.json').read_text(encoding='utf-8'))
nums=list(csv.DictReader((R/'Docs/Research/e0/numbers_v3.csv').open(encoding='utf-8-sig',newline='')))
rows=list(csv.reader((R/'Docs/Research/debate25/vol2_table120_v10.csv').open(encoding='utf-8-sig',newline='')))[1:]
values={r['Key']:r['Value'] for r in nums}
def num(v):
    s=format(decimal.Decimal(str(v)),'f')
    return s.rstrip('0').rstrip('.') if '.' in s else s
def escape(s):return s.replace('\r\n','\n').replace('\r','\n').replace('\\','\\\\').replace('\t','\\t').replace('\n','\\n')
lines=['vol2-v3/1']
for r in sorted(nums,key=lambda r:r['Key']):
    fields=[r[k] for k in ['Key','Value','Unit','Status','Source','Candidate','CandidateTrigger','Note']]
    fields[1]=num(fields[1]) if fields[1] else 'null'
    lines.append('N\t'+'\t'.join(map(escape,fields)))
kinds={'즉시':0,'둘째 접촉':1,'재료':2,'번짐':3,'되번짐':4,'작동':5,'벗김':6,'뒤쪽 가름':7,'귀환':8}
golden={r['letter']:r for r in g['rows']}
for fields in sorted(rows,key=lambda r:r[0]):
    ch=fields[0];b='ㄱㄴㅁㅅㅇ'.index(fields[1].split('=')[0]);f='ㅏㅓㅗㅜ'.index(fields[2].split('=')[0]);v=['없음','ㄱ','ㄴ','ㅁ','ㅅ','ㅇ'].index(fields[3].split('=')[0]);kind=['무받침','일반','극치'].index(fields[4])
    terms=[]
    def add(k,key):terms.append((k,values[key],1))
    if kind==2:terms=[(kinds[t['name']],t['amount'],t['count']) for t in golden[ch]['terms']]
    elif v==0:
        if f!=1:add(0,'action.normal.none.perTarget')
    elif v==3:add(5,'action.seed.activation')
    else:
        add(0,'action.instant')
        if f==1:add(1,'action.eo.second')
        if v==1:add(2,'action.normal.g.shared')
        elif v==2:add(3,'action.spread.total')
        elif v==4:add(6,'action.strip.utility')
        elif f!=1:add(8,'action.return.amount')
    line='S\t'+'\t'.join(map(escape,fields))+'\t'+'\t'.join(map(str,[b,f,v,kind]))+'\t'+num(golden[ch]['cost'])
    for k,a,count in terms:line+='\t'+str(k)+':'+num(a)+':'+str(count)
    lines.append(line)
canonical='\n'.join(lines)+'\n'
result={'schema':'vol2-v3/1','importer':'pure-v3/1','sourceHashes':g['hashes'],'dtoHash':hashlib.sha256(canonical.encode('utf-8')).hexdigest()}
out.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Frozen independent Python DTO hash:',result['dtoHash'])
