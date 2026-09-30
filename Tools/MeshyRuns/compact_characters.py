"""Compact #234 authored character generation; durable paid reservations, no POST retries."""
import sys,json,importlib.util
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location("base",ROOT/"Tools/MeshyRuns/SpellVFX120/run_stone_dokkaebi.py")
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
m.OUT=ROOT/"Art/World/Compact/Rebuild/Characters"
ledger=m.OUT/"ledger.json"
base="Full body realistic Korean historical game character. Natural proportions, restrained materials, clear silhouette. Standing neutral relaxed pose, arms slightly apart, empty hands, feet separate. No base, no scene, no lettering, no weapons, no giant accessories. "
prompts={
"logger":base+"A weathered middle aged male Joseon mountain woodcutter, square face, short stubble, tied hair under a faded cloth headband. Coarse hemp jeogori jacket, sleeves tied at forearms, loose baji trousers bound below the knees, worn straw shoes, small cloth waist sash. Broad working shoulders, tired but alert expression.",
"herbalist":base+"An elderly Korean male mountain herbalist, lean face, thoughtful eyes and short grey beard. Soft cloth cap, layered modest jeogori, knee length open sleeveless vest, loose bound trousers, cloth shoes, small herb pouch tied to waist. Slight stoop but natural balanced posture. Distinct narrow silhouette.",
"innkeeper":base+"A mature Korean female rural innkeeper around fifty, hair in a plain low bun with a wooden pin, kind firm face. Practical modest Joseon jeogori with narrow tied collar, ankle length chima skirt and worn waist apron, cloth shoes. Sturdy grounded silhouette. Simple working clothes, no court dress or jewelry.",
"mine_beast":"One realistic Korean mountain wild boar for a dark historical game. Full quadruped animal, muscular low barrel body, heavy shoulders, coarse bristled fur, wedge head, small dark eyes, short curved tusks, four separate short legs and cloven hooves, short tail. Dusty hide from an abandoned mine. Neutral natural standing pose, readable articulated limbs. Charcoal brown grey fur, matte earth and stone dust. No armor, spikes, glowing eyes, lava, saddle, pedestal, ground, scenery or lettering.",
"mine_fire":"One realistic corrupted Korean mountain badger like quadruped beast, full body with four separate legs. Low powerful torso, heavy forepaws with digging claws, tapered blunt muzzle, small ears, thick neck, short tail. Hide clumped by soot and mineral dust, broken crusts of dark rust red mineral on shoulders and throat, scorched coarse dark fur. Natural standing pose with distinct articulated legs. No flames, glowing cracks, emissive eyes, armor, weapons, giant spikes, pedestal, ground, scene or letters. Historical fantasy game creature."}
texture="Restrained realistic matte textures for a Korean ink wash world. Worn natural hemp and faded indigo or muted brown for clothing, natural skin and hair. For animals use charcoal umber fur and mineral dirt, rust stains only on the corrupted creature. Broad readable material variation, no painted outlines, metallic sheen, text or emission."
def run(op):
 data=json.loads(ledger.read_text()) if ledger.exists() else {"model":"meshy-7.1","self_imposed_cap":175,"pricing_source":"https://docs.meshy.ai/en/api/pricing","requests":[]}
 if op in ("preview","refine"):
  for ident,prompt in prompts.items():
   name=ident+"_"+op
   if any(r["name"]==name for r in data["requests"]):continue
   cfg={"mode":op,"ai_model":"meshy-7.1","target_formats":["fbx","glb"]}
   if op=="preview":cfg.update(prompt=prompt,geometry_resolution="2k",should_remesh=True,target_polycount=25000,topology="triangle")
   else:
    pre=next(r for r in data["requests"] if r["name"]==ident+"_preview")
    if pre["status"]!="SUCCEEDED":continue
    cfg.update(preview_task_id=pre["task_id"],texture_prompt=texture,enable_pbr=True,texture_resolution="2k")
   cost=25 if op=="preview" else 10
   assert len(prompt)<=800
   if sum(r["reserved_credits"] for r in data["requests"])+cost>175:raise RuntimeError("Run cap")
   if m.api("GET","/openapi/v1/balance").get("balance",0)<cost:raise RuntimeError("Insufficient existing credit")
   row={"id":ident,"name":name,"mode":op,"config":cfg,"reserved_credits":cost,"status":"SUBMISSION_RESERVED"}
   data["requests"].append(row);m.save(ledger,data)
   try: response=m.api("POST",m.ENDPOINT,cfg);row.update(task_id=response["result"],status="SUBMITTED")
   except Exception:
    row["status"]="SUBMISSION_UNCERTAIN_NO_RETRY";m.save(ledger,data);raise
   m.save(ledger,data)
 elif op=="poll":
  for row in data["requests"]:
   if not row.get("task_id"):continue
   if row.get("status")=="SUCCEEDED" and row.get("files"):continue
   response=m.api("GET",m.ENDPOINT+"/"+row["task_id"]);m.save(m.OUT/".private"/(row["name"]+".json"),response)
   for k in ("status","progress","consumed_credits"):row[k]=response.get(k)
   m.save(ledger,data)
   if row["status"]=="SUCCEEDED":row["files"]=m.download_files(response,row);m.save(ledger,data)
 print(json.dumps([{k:r.get(k) for k in ("name","status","progress","consumed_credits")} for r in data["requests"]]))
if __name__=="__main__":run(sys.argv[1])
