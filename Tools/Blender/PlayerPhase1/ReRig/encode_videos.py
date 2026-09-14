from pathlib import Path
import subprocess,json
import imageio_ffmpeg
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/ReRig');P=OUT/'Previews';OLD=OUT.parent/'Previews'
ff=imageio_ffmpeg.get_ffmpeg_exe();clips=[];comparisons=[]
for label in ['Walk','Run','ArmsUp','Squat','BrushSwing','HandFlex']:
 dest=P/(label+'.mp4');folder=P/label
 subprocess.run([ff,'-y','-loglevel','error','-framerate','12','-i',str(folder/'%04d.png'),'-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(dest)],check=True)
 clips.append({'motion':label,'path':str(dest),'frames':len(list(folder.glob('*.png'))),'fps':12})
 if label=='HandFlex':continue
 comp=P/(label+'_comparison.mp4')
 font="fontfile='C\\:/Windows/Fonts/arial.ttf'"
 vf=f"[0:v][1:v]hstack=inputs=2,drawtext={font}:text='BEFORE - {label}':x=24:y=20:fontsize=28:fontcolor=white,drawtext={font}:text='RERIG - {label}':x=984:y=20:fontsize=28:fontcolor=white[v]"
 subprocess.run([ff,'-y','-loglevel','error','-i',str(OLD/(label+'.mp4')),'-i',str(dest),'-filter_complex',vf,'-map','[v]','-c:v','libx264','-crf','18','-pix_fmt','yuv420p',str(comp)],check=True)
 comparisons.append(comp)
def join(paths,dest):
 listing=P/(dest+'.txt');listing.write_text(''.join("file '"+str(p).replace('\\','/')+"'\n" for p in paths),encoding='utf-8')
 subprocess.run([ff,'-y','-loglevel','error','-f','concat','-safe','0','-i',str(listing),'-c','copy','-movflags','+faststart',str(P/(dest+'.mp4'))],check=True)
join(comparisons,'Rerig_before_after');join([Path(c['path']) for c in clips[:5]],'Rerig_motion_validation')
(P/'video_manifest.json').write_text(json.dumps(clips,indent=2));print('Encoded six clips, full-body video, and matched before/after comparison.')
