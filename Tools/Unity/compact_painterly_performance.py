"""Four sequential isolated Play measurements, alternating baseline/final. No prewarm."""
from pathlib import Path
import contextlib,io,json,shutil,time
import world_macro

root=Path(__file__).resolve().parents[2]
out=root/'Art/World/WorldMacro/Compact/InkLandscape/Painterly'
world_macro.OUT=world_macro.OUT/'Playtest'
prefix='chapter3:compact:recovery:'

def call(command):
    with contextlib.redirect_stdout(io.StringIO()):
        result=world_macro.call('Demo',prefix+command)
    return result['result']

def measure(side,number):
    start=time.time()
    print(call('natural:performance:stream:ink'),flush=True)
    time.sleep(52)
    while True:
        result=call('natural:performance:poll')
        if result.startswith('RUNNING'):
            time.sleep(5)
            continue
        data=json.loads(result)
        if data['status']!='MEASURED_DIAGNOSTIC_CAMERA' or data.get('error'):
            raise RuntimeError(result)
        files=list((root/'Art/PlaytestRecovery/Performance').glob('*_stream_1080/measurement.json'))
        path=max(files,key=lambda p:p.stat().st_mtime)
        if path.stat().st_mtime<start:raise RuntimeError('No fresh measurement receipt')
        target=out/f'performance_{side}_{number}.json'
        if target.exists():
            archive=out/'SupersededPerformance';archive.mkdir(exist_ok=True)
            shutil.copy2(target,archive/target.name)
        shutil.copy2(path,target)
        print(side,number,data['utc'],[(p['phase'],round(p['cpuP50'],3),round(p['gpuP50'],3)) for p in data['phases']],flush=True)
        return

baseline=False
try:
    for number in (1,2):
        print(call('ink-paint:performance-before'),flush=True);baseline=True
        measure('before',number)
        print(call('ink-paint:performance-after'),flush=True);baseline=False
        measure('after',number)
finally:
    if baseline:print(call('ink-paint:performance-after'),flush=True)
