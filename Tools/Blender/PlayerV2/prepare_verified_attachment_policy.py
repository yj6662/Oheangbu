"""Prepare explicit sewing-role motion envelopes for numerical preflight only."""
import json,hashlib
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/VerifiedAttachments';ledger=json.loads((OUT/'attachment-role-ledger.json').read_text());reports=[]
fixed={'recorded_sewn_seed_currently_aligned','recorded_sewn_seed_needs_current_join_repair','newly_verified_shared_boundary','reinforced_six_mm_sewing_margin'}
for s in ledger['surfaces']:
    name=s['mesh'];data=np.load(OUT/(name+'-source.npz'));old=data['mobility'];new=old.copy();rows=[]
    for row in s['vertices']:
        i=row['vertex'];role=row['role'];d=row['restGraphDistanceToSewingMeters']
        if role in fixed:new[i]=0
        elif role=='unresolved_component_without_verified_sewing':raise RuntimeError('Unattached component is not releasable: '+name+':'+str(i))
        else:
            t=np.clip((d-.006)/(.050-.006),0,1);allowance=.18*t*t*(3-2*t);new[i]=max(old[i],allowance)
        rows.append({'vertex':i,'role':role,'beforeMaxDistanceMeters':float(old[i]),'afterMaxDistanceMeters':float(np.float32(new[i])),
          'restGraphDistanceToSewingMeters':d,'oldAuthoredSeed':row['oldAuthoredSeed'],'nearestOtherBoundary':row['nearestBoundary']})
    new=new.astype(np.float32).astype(np.float64)
    for label,count in [('candidate25',25),('candidate429',len(data['poseNames']))]:
        np.savez_compressed(OUT/(name+'-'+label+'.npz'),rest=data['rest'],edges=data['edges'],mobility=new,posePoints=data['posePoints'][:count],poseNames=data['poseNames'][:count])
    reports.append({'mesh':name,'beforeExactPins':int(np.sum(old==0)),'afterExactPins':int(np.sum(new==0)),
      'releasedAutomaticPinCount':int(np.sum((old==0)&(new>0))),'newFixedSharedSewingVertices':np.flatnonzero((old>0)&(new==0)).tolist(),
      'maximumAllowedDisplacementMeters':float(np.max(new)),'unresolvedCurrentJoinVertices':[r['vertex'] for r in s['vertices'] if r['role']=='recorded_sewn_seed_needs_current_join_repair'],
      'vertices':rows})
report={'status':'REJECTED_SHARED_CUT_PIN_HYPOTHESIS_DIAGNOSTIC_ONLY','artRoleWarning':'This first numerical hypothesis incorrectly assumes an Inner/Outer cut is a fixed cuff. No model was authored from it. Reference outer hem must remain free; see ART_ROLE_CORRECTION.md.','sourceSha256':ledger['sourceSha256'],'ledgerSha256':hashlib.sha256((OUT/'attachment-role-ledger.json').read_bytes()).hexdigest(),
 'policy':'Keep recorded sewing/shared-body/cuff cut seeds (including explicitly unresolved current seam matches), exact new shared boundary, and existing6mm reinforced sewing band fixed. Remove no real attachment. Only automatic restX/height pins lacking sewing evidence become fabric. Continuous free allowance=max(previous,0.18m*smoothstep(6mm,50mm,triangle geodesic to real sewing)); max18cm remains. No detached component allowed. R925 is a newly fixed true BodyCore shared fold.',
 'acceptanceUnchanged':True,'surfaces':reports}
(OUT/'candidate-policy-ledger.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps([{k:v for k,v in r.items() if k!='vertices'} for r in reports],indent=2))
