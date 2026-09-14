"""Authored first placement pass; narrative facts are cited, unconfirmed details remain TEST."""
import json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
out=ROOT/'Art/World/WorldMacro/Content';out.mkdir(parents=True,exist_ok=True)
entries=[]
kinds={'Encounter':0,'Npc':1,'Evidence':2,'Event':3,'Dungeon':4,'Boss':5,'Rest':6,'GatePreview':7}
def add(id,label,realm,anchor,kind,x,z,role='',text='',stage='EA',count=1,requires='',source='LDB-REALMS; NARR-STORYLINE'):
    entries.append(dict(Id=id,Label=label,Realm=realm,Anchor=anchor,Kind=kinds[kind],Offset=dict(x=x,y=0,z=z),Role=role,Text=text or '배치 초안입니다. 전투·보상·진행 연결은 별도 구현 대상입니다.',Stage=stage,Count=count,Requires=requires,Source=source,Radius=4,ActivationDistance=500 if kind in ('Dungeon','Boss') else 220,State='TEST',Preserve=True))
# Explicit known early-game places, not a circular scattering template.
add('mine_inquiry','무너진 갱도 조사','Cheongrim','Cave','Evidence',-5,-18,text='잘린 심지와 바깥으로 튄 암편이 남았다. 광맥의 폭주만으로 생긴 흔적은 아닌 듯하다.')
add('mine_beast','폭파로 풀려난 짐승','Cheongrim','Cave','Encounter',-16,-35,'NeutralMelee',count=2)
add('mine_fire','화맥에 물든 짐승','Cheongrim','Mine','Encounter',35,55,'FireRanged',count=1)
add('mine_return','폐광 상부 국 예고','Cheongrim','Cave','GatePreview',35,30,'국',text='절벽 위에 오래된 작업대가 보인다. 지금은 닿지 않는다. 본선 통과 조건은 아니다.')
add('geumpyo_inn','금표 주막','Cheongrim','Inn','Rest',-20,-10,text='약초와 젖은 나무 냄새가 섞인다. 휴식·저장 서비스 연결 예정 지점이다.')
add('herbalist','잔류 약초꾼','Cheongrim','Inn','Npc',-13,-14,text='“잎이 무성하다고 다 살아 있는 숲은 아니오. 물든 심부에선 뿌리부터 보시오.”')
add('logger','돌아온 벌목꾼','Cheongrim','Inn','Npc',-27,-12,text='“쇠가 박힌 자리만 뿌리가 멈췄소. 창고 뒤 철책을 보면 알 거요.”')
add('geumpyo_stone','금표 비석','Cheongrim','Cheongrim','Evidence',32,25,text='봉산 출입을 금하는 관의 글씨 아래, 나무꾼들이 남긴 작은 흠집이 겹쳐 있다.',source='LDB-LENSES; LDB-REALMS')
add('logging_dungeon','버려진 벌목장 창고','Cheongrim','Logging','Dungeon',40,-55,'Warehouse')
add('metal_lesson','덩굴이 멎은 철책','Cheongrim','Logging','Evidence',20,-20,'MetalBlocksGrowth',text='부러진 쇠도끼와 철책에 닿은 덩굴만 검게 마른 채 멈춰 있다.',source='LDB-REALMS: 청룡 금 차단 선행 학습')
add('logging_pack','적재장 짐승 무리','Cheongrim','Logging','Encounter',-38,30,'WoodRanged',count=3)
add('deep_pack','심부 포박 무리','Cheongrim','DeepForest','Encounter',-46,-32,'WoodRanged',count=3)
add('root_dungeon','뿌리 굴','Cheongrim','DeepForest','Dungeon',90,35,'Mine')
add('hunter_inn','사냥꾼 주막','Cheongrim','DeepForest','Rest',-150,-90,text='성역으로 가기 전 마지막 사람의 온기가 남아 있다. 휴식 연결 예정 지점이다.')
add('hunter','숲길 사냥꾼','Cheongrim','DeepForest','Npc',-144,-96,text='“저 큰 나무 앞 금줄은 넘지 마시오. 이야기는 이쪽에서도 들을 수 있소.”')
add('tree_warning','신목 금줄과 장승','Cheongrim','Tree','Evidence',-42,-40,text='가지 말라는 말들이 금줄에 겹겹이 묶여 있다. 영역 진입은 선택이다.',source='LDB-REALMS: 신목 옵션 보스')
add('sacred_tree','신목 영역','Cheongrim','Tree','Boss',0,24,'WoodFieldBoss',text='선택형 필드 보스 영역. 보스 행동·공격은 아직 연결하지 않았다.')
add('azure_dragon','청룡 성역','Cheongrim','Dragon','Boss',18,18,'WoodMajorBoss')
add('dragon_dual','성역의 두 전승','Cheongrim','Dragon','Evidence',-30,-35,text='비석에는 “나라가 세운 수호신수”, 낡은 나무패에는 “숲의 주인”이라 적혀 있다.',source='LDB-LENSES; NARR-LENSES')
add('jeongdam_j1','정담 · 길목 역참','Cheongrim','PostStation','Npc',-18,-10,text='“오랜만이군. 폐광을 다녀왔다지? 상경길도 전 같지 않네. 객주 쪽 사정부터 알아보게.”',source='NARR-CHARACTERS; NARR-STORYLINE J1')
add('wangso_w1','왕소 · 객주 분소','Cheongrim','MerchantBranch','Npc',-18,-14,text='“봉인을 건드리지 않고 본점까지 옮겨 주실 분을 찾고 있어요. 급히 정하실 일은 아니에요.”',source='NARR-CHARACTERS; NARR-STORYLINE W1')
add('cargo_contract','봉인 화물 의뢰 지점','Cheongrim','MerchantBranch','Event',-10,-23,text='봉인 화물 호송 수주와 상경 가도 동행의 배치 지점. 실제 의뢰 수락은 아직 연결하지 않았다.',requires='wangso_w1')
for id,anchor,x,z in [('entrance','Mine',-25,-10),('logging','Logging',-40,-45),('fork','DeepForest',-48,-58),('dragon','Dragon',-45,-75)]:
    add('shrine_c_'+id,'청림 성황당 · '+id,'Cheongrim',anchor,'Rest',x,z,'Shrine',text='조용한 성황당. 재시도·회복 연결 예정 지점.',source='LDB-CHECKPOINT')
# Southern battlefield: witnesses disagree, settlements sparse.
for id,label,kind,x,z,role in [('burned_camp','버려진 화맥 진지','Dungeon',-100,90,'Warehouse'),('war_tomb','전몰자 묘역','Dungeon',150,-100,'Tomb'),('fire_line','불탄 참호 짐승','Encounter',55,105,'FireRanged'),('ash_pack','재밭의 무속성 짐승','Encounter',-75,-55,'NeutralMelee'),('suzaku','주작 화맥 아레나','Boss',220,210,'FireMajorBoss')]:
    add(id,label,'Jeokro','Jeokro',kind,x,z,role,stage='Tour',count=3 if kind=='Encounter' else 1)
add('war_elder','위령객','Jeokro','Jeokro','Npc',-40,-150,text='“이긴 자의 글에 죽은 자의 이름이 다 들어갈 수 있겠소.”',stage='Tour')
add('war_official','회군의 관찬 비문','Jeokro','Jeokro','Evidence',-25,-120,text='비문은 의거라 적는다. 승리의 날짜 아래 이름 없는 돌무덤이 이어진다.',stage='Tour',source='LDB-LENSES: 회군 다중전승')
add('war_song','돌무덤의 구전','Jeokro','Jeokro','Event',110,-130,text='유민의 노래는 같은 날을 배신이라 부른다.',stage='Tour',source='LDB-LENSES: 회군 다중전승')
add('south_inn','재길 주막','Jeokro','SouthBridge','Rest',80,-100,stage='Tour')
add('war_shrine','위령 성황당','Jeokro','Jeokro','Rest',180,150,'Shrine',stage='Tour')
add('ash_revisit','재 위 덩굴 장벽 예고','Jeokro','Jeokro','GatePreview',150,60,'눈',stage='Tour')
# Fortress and exile: preserve the real built fortress footprint.
for id,label,kind,x,z,role in [('armory','버려진 군기고','Dungeon',105,-80,'Warehouse'),('exile_tomb','성외 옛 묘역','Dungeon',-135,95,'Tomb'),('iron_patrol','성외 단일 금 병졸','Encounter',95,70,'MetalRanged'),('gate_beasts','고개 앞 물든 짐승','Encounter',-90,-90,'NeutralMelee'),('white_tiger','백호 성곽 밖 아레나','Boss',-170,195,'MetalMajorBoss')]:
    add(id,label,'Cheolong','Fortress',kind,x,z,role,stage='Tour',count=3 if kind=='Encounter' else 1)
add('smith','철옹 대장장이','Cheolong','Fortress','Npc',-82,-40,text='“성벽을 버티는 건 돌만이 아니지. 금 간 쇠도 이어 쓰는 사람이 있어야 해.”',stage='Tour')
add('exile_scholar','폐서원의 유배 문인','Cheolong','Cheolong','Npc',-180,-120,text='“지워진 줄은 빈 줄이 아니오. 남은 글의 사이를 읽어 보시오.”',stage='Tour',source='LDB-LENSES: 폐서원')
add('exile_manuscript','폐서원 필사본','Cheolong','Cheolong','Evidence',-185,-115,text='거둠의 법 아래 지워진 이름이 여백에 다시 적혀 있다.',stage='Tour',source='LDB-LENSES')
add('fort_inn','성외 주막','Cheolong','WestPass','Rest',-48,-45,stage='Tour')
add('fort_shrine','백호 길목 성황당','Cheolong','Fortress','Rest',-150,140,'Shrine',stage='Tour')
add('rubble_revisit','무너진 성벽 바위','Cheolong','Fortress','GatePreview',-100,105,'숫',stage='Tour')
# Flooded old capital and the late-game temple have separate narrative staging.
for id,label,kind,x,z,role in [('sunken_store','잠긴 저자의 건조 창고','Dungeon',-125,-85,'Warehouse'),('old_tomb','옛 도읍 교외 묘역','Dungeon',160,70,'Tomb'),('river_beasts','물가의 수속성 짐승','Encounter',-70,100,'WaterRanged'),('ruin_guard','폐도읍 골목 짐승','Encounter',80,-130,'NeutralMelee'),('black_tortoise','현무 궁터 아레나','Boss',210,170,'WaterMajorBoss')]:
    add(id,label,'Hyeongang','Hyeongang',kind,x,z,role,stage='Tour',count=3 if kind=='Encounter' else 1)
add('refugee_elder','옛 궁터의 유민 장로','Hyeongang','Hyeongang','Npc',-45,-48,text='“서울을 옮겨도 물길까지 데려갈 수는 없었지. 여기에도 사람은 살았소.”',stage='Tour',source='LDB-LENSES: 고려 유민 앵커')
add('water_inn','수상 마을 물가 주막','Hyeongang','Hyeongang','Rest',-120,-150,stage='Tour')
add('tortoise_shrine','옛 궁터 성황당','Hyeongang','Hyeongang','Rest',160,130,'Shrine',stage='Tour')
add('w2_revelation','현강의 수상한 손길','Hyeongang','Hyeongang','Event',-90,-55,text='왕소 재회와 정체 노출의 후속 서사 지점. 실제 분기·진영 선택은 미연결.',stage='Tour',source='NARR-STORYLINE: 슬롯4 현강')
add('pollution_revisit','오염 수역 정화 예고','Hyeongang','NorthBridge','GatePreview',-55,55,'웅',stage='Tour')
add('gap_revisit','무너진 다리 너머 예고','Hyeongang','OldCapitalCreekBridge','GatePreview',70,40,'뭄',stage='Return')
add('temple_legacy','숨은 사찰 · 레거시 던전','Hyeongang','Temple','Dungeon',70,95,'Temple',stage='Late')
add('monk_scholar','승복 입은 선비','Hyeongang','Temple','Npc',-24,-45,text='“붓을 놓은 사람이 글을 잊은 것은 아니지요.”',stage='Late',source='LDB-LENSES: 불교 앵커')
add('sutra_comment','불경 주석의 빈 자리','Hyeongang','Temple','Evidence',-18,-42,text='봉안 기록의 한 대목이 도려내졌다. 남은 주석은 다른 역사를 가리킨다.',stage='Late')
add('hyeonun_meeting','현운 선사 대면 위치','Hyeongang','Temple','Event',30,55,text='보스전 직전 비전투 대면과 왕소 진행도별 대사 예약.',stage='Late',source='LDB-BOSSPLACEMENT; NARR-STORYLINE')
add('hyeonun','현운 선사 아레나','Hyeongang','Temple','Boss',100,160,'HumanBoss',stage='Late')
add('mechanical_buddha','메카천수관음 아레나','Hyeongang','Temple','Boss',-115,180,'MechanicalBoss',stage='Late')
# Capital: outer gate, city anchors, return and finale do not appear as one simultaneous quest.
for id,label,kind,x,z,role,anchor in [('capital_tomb','성저 교외 묘역','Dungeon',85,-70,'Tomb','SouthPost'),('merchant_cellar','객주 지하 창고 시안','Dungeon',-90,-100,'Warehouse','SouthPost'),('earth_checkpoint','가도 토 속성 검문병','Encounter',55,-50,'EarthRanged','SouthPost'),('outer_road_beasts','가도 무속성 짐승','Encounter',-60,-130,'NeutralMelee','EastPass'),('south_gate_boss','남문의 낯선 장수','Boss',0,-55,'EarthGateBoss','SouthGate')]:
    add(id,label,'Hwanggyeong',anchor,kind,x,z,role,count=3 if kind=='Encounter' else 1)
add('capital_inn','남문 밖 주막','Hwanggyeong','SouthPost','Rest',-36,-35)
add('road_inn','상경 가도 주막','Hwanggyeong','EastPass','Rest',100,-85)
add('merchant_head','객주 본점 인도 담당','Hwanggyeong','SouthPost','Npc',-44,-28,text='“봉인이 그대로군요. 짐은 이쪽에 내려 두시면 됩니다.”',source='NARR-STORYLINE: W1 성저 인도')
add('cargo_delivery','봉인 화물 인도 지점','Hwanggyeong','SouthPost','Event',-39,-22,text='객주 본점의 화물 인도 위치. 캠페인 호송·보상은 미연결.',requires='cargo_contract')
add('capital_notice','국상과 계엄 방문','Hwanggyeong','SouthGate','Evidence',-33,-95,text='국상 중 통행을 제한한다는 방이 성문 앞에 붙어 있다.',source='LDB-HWANGGYEONG; NARR-STORYLINE')
add('south_shrine','남문 앞 성황당','Hwanggyeong','SouthGate','Rest',-45,-120,'Shrine')
add('archive_clerk','사고의 서리','Hwanggyeong','Palace','Npc',-95,-55,text='“기록은 남겨 두는 것이지, 아무에게나 내어 주는 것은 아닙니다.”',stage='City',source='LDB-LENSES: 사고 정사 앵커')
add('city_jeongdam','정담 관아 재회 위치','Hwanggyeong','Hwanggyeong','Event',-85,50,text='정담의 관아 본거지와 후반 목격 장면의 배치 후보. 인물을 동시에 복제하지 않는다.',stage='Return',source='NARR-CHARACTERS; NARR-STORYLINE J2')
add('fallen_dragon','개천의 이무기','Hwanggyeong','CapitalBridge','Boss',90,40,'FallenDragon',stage='Return',source='LDB-HWANGGYEONG; COMBAT-BOSS')
add('sangun','인왕산 산군 영역','Hwanggyeong','Hwanggyeong','Boss',-600,350,'TigerFieldBoss',stage='Return',source='LDB-BOSSPLACEMENT: 산군 인왕산')
add('final_sovereign','궁성 안 최종 대면','Hwanggyeong','Palace','Boss',0,90,'FinalTwoPhase',stage='Finale',source='NARR-FINALE; LDB-BOSSPLACEMENT')
add('muhak','왕십리 태극 문지기 위치','Hwanggyeong','Hwanggyeong','Boss',650,-150,'SecretHumanBoss',stage='Finale',source='LDB-BOSSPLACEMENT: 무학 왕십리')
add('machine_hand','기계의 손 공방 아레나 후보','Cheolong','Fortress','Boss',210,160,'MechanicalBoss',stage='Late',source='COMBAT-BOSS: 장영실, 지역 미정 TEST 제작 해석')
(out/'seed.json').write_text(json.dumps(dict(Version='content-layout-v1',Entries=entries),ensure_ascii=False,indent=2),encoding='utf-8')
print(f'Authored {len(entries)} entries; no Unity mutation in this script.')
