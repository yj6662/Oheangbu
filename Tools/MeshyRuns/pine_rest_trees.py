import sys,json,importlib.util
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location("meshy_base",ROOT/"Tools/MeshyRuns/SpellVFX120/run_stone_dokkaebi.py")
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
m.OUT=ROOT/"Art/World/PineRest/MeshyTrees"
ledger=m.OUT/"ledger.json"
prompts={
 "RedPine":"One photorealistic mature Korean red pine tree, Pinus densiflora, full tree including roots at ground level. Tall naturally crooked reddish scaly bark trunk, branching trunk visible through irregular open crown, slender tapering limbs, fine dense clusters of long green pine needles at branch tips. Botanically plausible branching hierarchy with several airy asymmetrical needle clusters and clear gaps between boughs. Natural full sized forest tree, 9 meters tall, not bonsai, not a flat illustration, no stylization, no platforms, no terrain, no pot, no rocks, no scenery. Realistic 3D game vegetation asset.",
 "MountainPine":"Single photorealistic mountain Korean red pine tree, naturally windswept, upright lower trunk with gently leaning upper trunk and asymmetrical spreading branches. Reddish brown cracked bark, fine irregular twigs and tufts of dark green long pine needles, airy crown with open spaces between branches. Full grown 7 meter tree with exposed root flare at ground level. Botanically realistic uneven silhouette. No bonsai pot, no pedestal, no ground patch, no background scenery, no cartoon style, no giant solid leaf blobs, no repeated flat umbrella discs. Freestanding 3D vegetation asset."
}
def run(op):
 data=json.loads(ledger.read_text(encoding="utf-8")) if ledger.exists() else {"model":"meshy-7.1","pricing_source":"https://docs.meshy.ai/en/api/pricing","self_imposed_cap":70,"requests":[]}
 if op in ("preview","refine"):
  for name,prompt in prompts.items():
   key=name+"_"+op
   if any(r["name"]==key for r in data["requests"]):continue
   config={"mode":op,"ai_model":"meshy-7.1","target_formats":["fbx","glb"]}
   if op=="preview":config.update(prompt=prompt,geometry_resolution="2k",should_remesh=True,target_polycount=35000,topology="triangle")
   else:
    pre=next(r for r in data["requests"] if r["name"]==name+"_preview")
    if pre["status"]!="SUCCEEDED":raise RuntimeError("Preview not ready")
    config.update(preview_task_id=pre["task_id"],enable_pbr=True,texture_resolution="4k",texture_prompt="Photorealistic natural Korean red pine. Reddish brown scaly bark, grey weathered lower bark, fine dark olive green pine needles. Matte nonmetallic bark and needles. Natural subtle variation. No snow, no gold, no paint, no stylization, no glow.")
   cost=25 if op=="preview" else 10
   if sum(r["reserved_credits"] for r in data["requests"])+cost>70:raise RuntimeError("Run cap")
   if m.api("GET","/openapi/v1/balance").get("balance",0)<cost:raise RuntimeError("Insufficient balance")
   row={"name":key,"id":name,"mode":op,"config":config,"reserved_credits":cost,"status":"SUBMISSION_RESERVED"};data["requests"].append(row);m.save(ledger,data)
   response=m.api("POST",m.ENDPOINT,config);row.update(task_id=response["result"],status="SUBMITTED");m.save(ledger,data)
 elif op=="poll":
  for row in data["requests"]:
   if not row.get("task_id"):continue
   response=m.api("GET",m.ENDPOINT+"/"+row["task_id"]);m.save(m.OUT/".private"/(row["name"]+".json"),response)
   for k in ("status","progress","consumed_credits"):row[k]=response.get(k)
   m.save(ledger,data)
   if row["status"]=="SUCCEEDED" and not row.get("files"):row["files"]=m.download_files(response,row);m.save(ledger,data)
 print(json.dumps([{k:r.get(k) for k in ("name","task_id","status","progress","consumed_credits")} for r in data["requests"]]))
if __name__=="__main__":run(sys.argv[1])
