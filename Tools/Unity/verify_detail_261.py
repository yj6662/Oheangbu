"""Serial background-only candidate art/navigation verification; never enters Play."""
from pathlib import Path
import json,shutil
from playtest_polish import call

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild'
D=OUT/'Detail261'
TYPE='Oheangbu.EditorTools.WorldMacro.CompactRebuildAuthoring'
steps=[('Detail261','audit','audit.txt'),('Detail261','capture:after',None),
       ('Run','open',None),('Run','nav-slice','nav.txt'),('Run','open-test',None),
       ('Run','cave-polish-map','map.txt'),('BranchAudit259','','branch-audit.txt'),
       ('ProgressionAudit251','','progression-audit.txt'),
       ('BranchBackground259','walk',None),('BranchBackground259','reverse',None)]
receipt=[]
for method,arg,report in steps:
    result=call(TYPE,method,arg)
    receipt.append(result)
    (D/'verification-queue.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf-8')
    if report:(D/report).write_text(result['result'],encoding='utf-8')
    if 'FAIL ' in result['result'] or result['result'].startswith('FAIL'):
        raise RuntimeError(f'Check failed: {method} {arg}')
for mode in ['walk','reverse']:
    p=OUT/'Branches259'/f'collision-{mode}.json';d=json.loads(p.read_text())
    assert d['status']=='PASS'
    shutil.copy2(p,D/p.name)
call('editor','status')
print('Background verification completed')
