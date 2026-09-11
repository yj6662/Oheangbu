"""Copy PolyOne water graph and replace global Time with per-effect time."""
import json,copy,uuid
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
src=ROOT/'Oheangbu/Assets/PolyOne/Water URP/Shader/Shader Water.shadergraph'
out=ROOT/'Oheangbu/Assets/_Project/Art/SpellVFX120/AreaFive';out.mkdir(exist_ok=True)
s=src.read_text(encoding='utf-8-sig');decoder=json.JSONDecoder();items=[]
while s.strip():
    s=s.lstrip();obj,end=decoder.raw_decode(s);items.append(obj);s=s[end:]
graph=items[0];time_nodes=[o for o in items if o.get('m_Type','').endswith('.TimeNode')]
prop=copy.deepcopy(next(o for o in items if o.get('m_Type','').endswith('.Vector1ShaderProperty')))
pid=uuid.uuid4().hex;prop.update(m_ObjectId=pid,m_Name='Effect Time',m_DefaultReferenceName='_EffectTime',m_OverrideReferenceName='_EffectTime',m_RefNameGeneratedByDisplayName='Effect Time',m_Value=0.0,m_FloatType=0)
prop['m_Guid']['m_GuidSerialized']=str(uuid.uuid4());items.append(prop);graph['m_Properties'].append({'m_Id':pid})
for node in time_nodes:
    edges=[e for e in graph['m_Edges'] if e['m_OutputSlot']['m_Node']['m_Id']==node['m_ObjectId']]
    assert all(e['m_OutputSlot']['m_SlotId']==0 for e in edges),edges
    slot=next(o for o in items if o.get('m_ObjectId')==node['m_Slots'][0]['m_Id'])
    node['m_Type']='UnityEditor.ShaderGraph.PropertyNode';node['m_Name']='Property';node['m_Property']={'m_Id':pid};node['m_Slots']=[node['m_Slots'][0]]
    slot['m_DisplayName']='Effect Time';slot['m_ShaderOutputName']='Out'
(out/'AreaWater.shadergraph').write_text('\n\n'.join(json.dumps(o,indent=4) for o in items),encoding='utf-8')
print('Copied PolyOne graph; replaced '+str(len(time_nodes))+' global time nodes.')
