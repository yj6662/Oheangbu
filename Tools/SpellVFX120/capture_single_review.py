"""Capture one explicitly selected spell in two cameras; never evaluates art."""
import argparse
import json
import time
from capture_ktp_rework import call

p=argparse.ArgumentParser()
p.add_argument('index',type=int,help='One-based catalog index')
p.add_argument('version')
p.add_argument('--position',type=float,nargs=3,default=[2.5,2.0,5.5])
p.add_argument('--target',type=float,nargs=3,default=[0,1.1,4])
a=p.parse_args()
if a.index<1 or a.index>120: p.error('Index out of range')
for external,clip in [(False,False),(True,False),(False,True),(True,True)]:
    folder='KTP'+('External' if external else 'Integrated')+('Frames' if clip else 'Stills')+a.version
    req=dict(start=a.index-1,count=1,folder=folder,width=1280,height=720,frames=5,clip=clip,fps=24,duration=0,demonstrationCues=True)
    if external: req.update(externalCamera=True,hideTemporaryTargets=True,cameraPosition=dict(zip('xyz',a.position)),cameraTarget=dict(zip('xyz',a.target)))
    print(call('Capture',req),flush=True)
    deadline=time.monotonic()+240
    while True:
        time.sleep(2)
        state=json.loads(call('CapturePoll'))
        if state['status']=='COMPLETE': break
        if state['status']!='RUNNING' or time.monotonic()>deadline: raise RuntimeError(state)
    print(folder+' COMPLETE',flush=True)
