from pathlib import Path
import json,hashlib,copy,uuid
ROOT=Path(__file__).resolve().parents[2];ED=ROOT/'Oheangbu/Assets/_Project/Scripts/Editor/SpellVFX120';OUT=ROOT/'Art/SpellVFX120/FixedWards';OUT.mkdir(exist_ok=True)
old=json.loads((OUT.parent/'AreaNatural/before_hashes.json').read_text(encoding='utf-8'))
if not (OUT/'before_hashes.json').exists():(OUT/'before_hashes.json').write_text(json.dumps({p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in old},indent=2),encoding='utf-8')
folder=ROOT/'Oheangbu/Assets/_Project/Art/SpellVFX120/FixedWards';folder.mkdir(exist_ok=True)
dest=folder/'WardWater.shadergraph'
if not dest.exists():
 s=(folder.parent/'AreaFive/AreaWater.shadergraph').read_text();dec=json.JSONDecoder();objects=[]
 while s.strip():
  s=s.lstrip();o,i=dec.raw_decode(s);objects.append(o);s=s[i:]
 lookup={o['m_ObjectId']:o for o in objects};graph=objects[0]
 prop=copy.deepcopy(next(o for o in objects if o.get('m_DefaultReferenceName')=='_EffectTime'))
 old_id=prop['m_ObjectId'];prop['m_ObjectId']=uuid.uuid4().hex;prop['m_Name']='Ward Opacity';prop['m_DefaultReferenceName']=prop['m_OverrideReferenceName']='_WardOpacity';prop['m_Value']=.35;prop['m_Guid']['m_GuidSerialized']=str(uuid.uuid4())
 template=next(o for o in objects if o.get('m_Type','').endswith('PropertyNode') and o.get('m_Property',{}).get('m_Id')==old_id)
 node=copy.deepcopy(template);node['m_ObjectId']=uuid.uuid4().hex;node['m_Property']['m_Id']=prop['m_ObjectId']
 slots=[]
 for ref in node['m_Slots']:
  slot=copy.deepcopy(lookup[ref['m_Id']]);slot['m_ObjectId']=uuid.uuid4().hex;ref['m_Id']=slot['m_ObjectId'];slots.append(slot)
 alpha=next(o for o in objects if o.get('m_Name')=='SurfaceDescription.Alpha')
 graph['m_Edges']=[e for e in graph['m_Edges'] if e['m_InputSlot']['m_Node']['m_Id']!=alpha['m_ObjectId']]
 graph['m_Edges'].append({'m_OutputSlot':{'m_Node':{'m_Id':node['m_ObjectId']},'m_SlotId':0},'m_InputSlot':{'m_Node':{'m_Id':alpha['m_ObjectId']},'m_SlotId':0}})
 graph['m_Properties'].append({'m_Id':prop['m_ObjectId']});graph['m_Nodes'].append({'m_Id':node['m_ObjectId']});objects.extend([prop,node]+slots)
 for o in objects:
  if 'm_RenderFace' in o:o['m_RenderFace']=0
 dest.write_text('\n\n'.join(json.dumps(o,indent=4) for o in objects),encoding='utf-8')
p=ED/'Vfx120Queue.cs';s=p.read_text(encoding='utf-8')
if 'case "FixedWardBuild"' not in s:s=s.replace('                    case "AreaFiveBuild":','''                    case "FixedWardBuild": response.result=FixedWardBuild.Build(command.request);break;
                    case "FixedWardAudit": response.result=FixedWardReview.Audit(command.request);break;
                    case "FixedWardCapture": response.result=FixedWardReview.Start(command.request);break;
                    case "AreaFiveBuild":''');p.write_text(s,encoding='utf-8')
print('Ward backups, isolated water graph and queue prepared')
