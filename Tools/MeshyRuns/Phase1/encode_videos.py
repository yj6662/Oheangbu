from pathlib import Path
import subprocess,json
import imageio_ffmpeg
OUT=Path(__file__).resolve().parents[3]/'Art/PlayerPhase1'
ff=imageio_ffmpeg.get_ffmpeg_exe();clips=[]
for label in ['Walk','Run','ArmsUp','Squat','BrushSwing']:
 folder=OUT/'Previews'/label;dest=OUT/'Previews'/(label+'.mp4')
 subprocess.run([ff,'-y','-loglevel','error','-framerate','12','-i',str(folder/'%04d.png'),'-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(dest)],check=True)
 clips.append(dict(motion=label,path=str(dest.relative_to(OUT)),frames=len(list(folder.glob('*.png'))),fps=12))
concat=OUT/'Previews/concat.txt';concat.write_text(''.join("file '"+str((OUT/c['path']).resolve()).replace('\\','/')+"'\n" for c in clips),encoding='utf-8')
subprocess.run([ff,'-y','-loglevel','error','-f','concat','-safe','0','-i',str(concat),'-c','copy','-movflags','+faststart',str(OUT/'Previews/Phase1_motion_validation.mp4')],check=True)
(OUT/'Previews/video_manifest.json').write_text(json.dumps(clips,indent=2))
print('Encoded five clips and combined diagnostic video')
