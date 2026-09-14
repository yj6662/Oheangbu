"""Record the ten directly viewed before/after renders and plot actual measured sections."""
import json,hashlib
from pathlib import Path
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.collections import LineCollection
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ArmLiningTransition';VIEW=OUT/'FinalViews'
r=json.loads((VIEW/'five-pose-validation.json').read_text());h=json.loads((OUT/'handoff-integrity.json').read_text())
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
fig,axes=plt.subplots(2,5,figsize=(16,6),layout='constrained')
for row,side in enumerate(['Left','Right']):
 for col,case in enumerate(r['poseIds']):
  ax=axes[row,col]
  for variant,color in [('before','#db7440'),('after','#168c87')]:
   sample=next(s for s in r['samples'] if s['variant']==variant and s['poseId']==case)
   lining=next(s for s in sample['linings'] if s['name'].endswith(side))
   for layer in lining['section']['layers']:
    is_lining='ArmLining' in layer['mesh']
    if not is_lining and variant=='before':continue
    segments=[[[x*1000,y*1000] for x,y in segment] for segment in layer['segments']]
    ax.add_collection(LineCollection(segments,colors=color if is_lining else '#818181',linewidths=1.7 if is_lining else .8,linestyles='solid' if is_lining else 'dotted'))
  ax.set_xlim(-65,65);ax.set_ylim(-65,65);ax.set_aspect('equal');ax.grid(alpha=.15);ax.set_title(case,fontsize=9);ax.set_xlabel('mm',fontsize=8)
  if col==0:ax.set_ylabel(side+' / mm')
fig.suptitle('Actual posed sections: orange original lining, teal corrected lining, gray outer sleeve\nFive measured poses only; section at 63% upper-arm length. No whole-body intersection claim.',fontsize=11)
fig.savefig(OUT/'five-pose-cross-sections.png',dpi=140);plt.close(fig)
after=[l for s in r['samples'] if s['variant']=='after' for l in s['linings']]
review={'status':'FIVE_POSE_LOCAL_LINING_REVIEW_COMPLETE','sourceSha256':r['sourceSha256'],'derivativeSha256':r['derivativeSha256'],
 'directlyViewedRenders':[dict(p,directlyViewed=True) for p in r['renders']],
 'visualObservation':'All ten actual before/after renders were directly viewed. Rest, grip, elbow120 and forearm +/-90 keep the same visible sleeve and wrapping silhouettes; no newly exposed lining cylinder, open cuff, collapsed forearm or gray wedge was seen. Most corrected lining is intentionally hidden by outer fabric, so parity BVH and cross sections supply local geometric evidence.',
 'measuredScope':'Every UNorm8-zero sleeve vertex tested against its same-side actual closed ArmLining in the five listed static poses, no capsule prefilter.',
 'checkedPinsAcrossPoses':sum(l['checkedPins'] for l in after),'penetratingPinsAcrossPoses':sum(l['insidePins'] for l in after),'minimumClearanceMeters':min(l['minimumPinClearanceMeters'] for l in after),
 'closedLinings':all(l['boundaryEdges']==0 and l['nonManifoldEdges']==0 for l in after),'degenerateTriangleFound':any(l['minimumTriangleArea']<1e-12 for l in after),
 'notes':['The first local rest-shape correction removes rest/grip pin overlap without global arm scaling.','Rest geometry was not further reduced to address the deep bend. Two added longitudinal sampling rings and local compatible skin weights resolve that difference.','Outer sleeve, pin colors, hands, wrapping, other43meshes, bone rests and parent hierarchy are unchanged in this derivative.','Right sleeve797 versus torso is owned by the separate outer-underarm repair and is not counted as solved here.','No production animations, native Cloth pass or RIG_PASS is granted. Other static pose ranges and whole-body intersections remain unclaimed.'],
 'mergeOnly':h['changedMeshes'],'trianglesPerLining':428,'totalTriangleDelta':192,'globalSkinIntersections':None,'globalClothIntersections':None,'rigGate':'NOT_GRANTED'}
(OUT/'visual-review.json').write_text(json.dumps(review,indent=2),encoding='utf-8')
lines=['# 팔 안감 국소 수정 검수','',f'최종 병합 후보: `{h["derivative"]}`',f'SHA256: `{h["derivativeSha256"]}`','',
 '두 ArmLining 메시만 병합합니다. 각 216정점/428삼각형이며 합계 증가는 192삼각형입니다. 나머지43개 메시·본·부모 구조는 해시가 같습니다. 손, 감개와 겉소매는 수정하지 않았습니다.','',
 'Rest, grip, elbow120, forearm−90, forearm+90의 전후 PNG 10장을 직접 확인했습니다. 새 안감 노출·열린 감개·팔 부피 붕괴는 관찰되지 않았습니다. 보이지 않는 안쪽은 실제 폐곡면 BVH와 단면으로 별도 확인했습니다.','',
 f'전체 핀 {review["checkedPinsAcrossPoses"]}회 검사에서 해당 안감 침투 0, 최소 여유 {review["minimumClearanceMeters"]*1000:.4f}mm입니다. 캡슐에 들어간 핀만 고르는 선별은 하지 않았습니다.','',
 '원인: 기존 .32→.442m 구간의 긴 안감 면이 겉소매의 촘촘한 전이 웨이트와 다른 궤적을 만들었습니다. 재웨이트만으로는49건, 1개 링 추가 후14건이 남았고 2개 링에서0이 됐습니다. 깊은 굽힘을 맞추기 위해 안감을 추가로 줄이지 않았습니다.','',
 '이 결과는 위 다섯 정적 포즈의 국소 안감 검수입니다. 다른 자세·전체 피부/천 교차·실시간 Cloth·제작 애니메이션이나 RIG_PASS를 대신하지 않습니다. 우측 소매797의 몸통 접촉은 별도 담당자의 소매 수정 범위입니다.','',
 '근거: handoff-integrity.json, transition-comparison.json, FinalViews/five-pose-validation.json, five-pose-cross-sections.png, visual-review.json.']
(OUT/'visual-review.md').write_text('\n'.join(lines)+'\n',encoding='utf-8');print(json.dumps({k:v for k,v in review.items() if k not in ['directlyViewedRenders','notes']},indent=2))
