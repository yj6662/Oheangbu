"""Mark retained diagnostic hypotheses honestly after reference-role review."""
import json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/VerifiedAttachments'
for name,status in [('attachment-role-ledger.json','READ_ONLY_MODEL_CUT_LEDGER_ART_ROLE_HYPOTHESIS_REJECTED'),('candidate-policy-ledger.json','REJECTED_SHARED_CUT_PIN_HYPOTHESIS_DIAGNOSTIC_ONLY')]:
    file=OUT/name;report=json.loads(file.read_text());report['status']=status
    report['artRoleWarning']='Shared Inner/Outer cut is not a sewn cuff. The reference outer hem is free; legacy role labels are geometric hypotheses. No model authored. See ART_ROLE_CORRECTION.md.'
    file.write_text(json.dumps(report,indent=2),encoding='utf-8')
