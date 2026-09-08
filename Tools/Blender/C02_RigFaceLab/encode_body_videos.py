"""Encode completed real rendered frames; retain frame timing and verify by full decode."""
from pathlib import Path
import subprocess,json,sys
ROOT=Path('C:/Users/yj666/Oheangbu')
LAB=ROOT/'Art/PlayerPhase1/C02_RigFaceLab'
FFMPEG=Path('C:/Users/yj666/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe')
for stage in sys.argv[1:]:
    folder=LAB/'Body'/('Video_'+stage);p=folder/'progress.json'
    data=json.loads(p.read_text(encoding='utf8'))
    if data['status']!='COMPLETE':raise RuntimeError('Render incomplete '+stage)
    results=[]
    for job in data['jobs']:
        if job['rendered_frames']!=job['frame_count']:raise RuntimeError('Missing frames '+job['label'])
        out=Path(job['mp4_path'])
        if out.exists():
            results.append({'label':job['label'],'status':'EXISTING_REVERIFY','file':str(out)});continue
        argv=job['ffmpeg_argv'].copy();argv[0]=str(FFMPEG)
        proc=subprocess.run(argv,capture_output=True,text=True)
        results.append({'label':job['label'],'status':'ENCODED' if proc.returncode==0 else 'FAIL','error':proc.stderr,'file':str(out)})
        if proc.returncode:raise RuntimeError(proc.stderr)
    (folder/'encoding_report.json').write_text(json.dumps(results,indent=2),encoding='utf8')
    subprocess.run([sys.executable,str(ROOT/'Tools/Blender/AutoPlayerV1/verify_videos.py'),str(folder),'--ffmpeg',str(FFMPEG)],check=True)
