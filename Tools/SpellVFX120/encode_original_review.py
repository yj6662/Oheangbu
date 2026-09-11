import json,subprocess
from pathlib import Path
o=Path('Art/SpellVFX120/OriginalReview');ff=next((Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries').glob('*.exe'))
for report in o.glob('*/report.json'):
 for frames in sorted(report.parent.glob('role_*')):
  if not frames.is_dir():continue
  output=frames.with_suffix('.mp4')
  if output.exists() and output.stat().st_mtime>max(x.stat().st_mtime for x in frames.glob('*.png')):continue
  subprocess.run([str(ff),'-y','-loglevel','error','-framerate','24','-i',str(frames/'%04d.png'),'-c:v','libx264','-crf','20','-pix_fmt','yuv420p','-movflags','+faststart',str(output)],check=True)
  subprocess.run([str(ff),'-v','error','-i',str(output),'-f','null','-'],check=True)
  print(str(output),flush=True)
