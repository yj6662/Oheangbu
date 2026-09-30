import sys,json,base64,importlib.util
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location("base",ROOT/"Tools/MeshyRuns/pine_rest_trees.py");b=importlib.util.module_from_spec(spec);spec.loader.exec_module(b);m=b.m
path=m.OUT/"image_ledger.json";endpoint="/openapi/v1/image-to-3d"
if sys.argv[1]=="submit":
 if path.exists():raise RuntimeError("Existing reservation retained")
 config={"ai_model":"meshy-7.1","geometry_resolution":"4k","should_remesh":True,"target_polycount":100000,"should_texture":True,"texture_resolution":"4k","enable_pbr":True,"image_enhancement":False,"target_formats":["fbx","glb"]}
 row={"name":"PhotorealPine_image","id":"PhotorealPine","mode":"image","config":config,"reserved_credits":35,"status":"SUBMISSION_RESERVED","reference":"pine_reference.png"}
 if m.api("GET","/openapi/v1/balance").get("balance",0)<35:raise RuntimeError("Insufficient balance")
 m.save(path,row)
 request=dict(config,image_url="data:image/png;base64,"+base64.b64encode((m.OUT/"pine_reference.png").read_bytes()).decode())
 response=m.api("POST",endpoint,request);row.update(task_id=response["result"],status="SUBMITTED");m.save(path,row)
else:
 row=json.loads(path.read_text(encoding="utf-8"));response=m.api("GET",endpoint+"/"+row["task_id"]);m.save(m.OUT/".private"/"PhotorealPine_image.json",response)
 for k in ("status","progress","consumed_credits"):row[k]=response.get(k)
 m.save(path,row)
 if row["status"]=="SUCCEEDED" and not row.get("files"):row["files"]=m.download_files(response,row);m.save(path,row)
print(json.dumps({k:row.get(k) for k in ("task_id","status","progress","consumed_credits")}))
