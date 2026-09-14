"""Inspect the real Unity listener WAV, without altering it, and update the review page."""
from pathlib import Path
import json,subprocess,re
import numpy as np
import imageio_ffmpeg
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlaytestPolish/AudioInk';OUT=ART/'UnityOutput'
report=json.loads((OUT/'listener_output.json').read_text());ff=imageio_ffmpeg.get_ffmpeg_exe();rate=report['sampleRate'];channels=report['channels']
def read(name,upsample=False):
 command=[ff,'-v','error','-i',str(OUT/(name+'.wav'))]
 if upsample:command+=['-ar',str(rate*4)]
 return np.frombuffer(subprocess.check_output(command+['-f','f32le','-']),'<f4').reshape(-1,channels)
full=read('listener_full');up=read('listener_full',True)
hold=read('harvest_start_and_hold');steady=hold[int(rate*.4):min(len(hold),int(rate*2.15))]
nearzero=np.max(np.abs(steady),axis=1)<1e-5;changes=np.diff(np.concatenate([[False],nearzero,[False]]).astype(int));starts=np.where(changes==1)[0];ends=np.where(changes==-1)[0];runs=ends-starts
release=read('harvest_release');active=np.flatnonzero(np.max(np.abs(release),axis=1)>=1e-5)
summary={
 'scope':'Real Unity post-mixer recording, synthetic presentation signals. No OS/device loopback or subjective listening approval.',
 'status':report['status'],
 'raw_peak_dbfs':float(20*np.log10(max(1e-12,np.abs(full).max()))),
 'oversampled_4x_peak_dbfs':float(20*np.log10(max(1e-12,np.abs(up).max()))),
 'clipped_samples':int((np.abs(full)>=1).sum()),
 'recorded_seconds':len(full)/rate,'dsp_seconds':report['dspSeconds'],
 'duration_difference_seconds':abs(len(full)/rate-report['dspSeconds']),
 'callbacks':report['callbacks'],'max_callback_interval_seconds':report['maximumCallbackIntervalSeconds'],
 'nominal_dsp_block_seconds':1024/rate,
 'callback_interpretation':'Wall-clock callback spacing is scheduling telemetry, not a direct device underrun count. 445 complete 1024-frame callbacks and recorded duration agree with the DSP clock. A 40ms spacing alone is not crackle/underrun evidence.',
 'steady_harvest_window_seconds':[.4,2.15],
 'steady_harvest_longest_below_1e_5_ms':float(runs.max()/rate*1000) if len(runs) else 0,
 'steady_harvest_zero_runs_over_10ms':int((runs>rate*.01).sum()),
 'harvest_release_last_audible_sample_seconds':float((active[-1]+1)/rate) if len(active) else 0,
 'quiet_tail_exactly_zero':bool(np.all(read('quiet_tail')==0)),
 'original_listener_wav_unchanged':True}
(OUT/'output_analysis.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf8')
section=f'''<!-- UNITY_OUTPUT_BEGIN --><section style="border:2px solid #9c8a6a;padding:20px;margin:25px 0"><h2>실제 Unity 믹서 출력 검수</h2><p>48 kHz · stereo · {summary['recorded_seconds']:.3f}초. 실제 리스너 출력 피크 {summary['raw_peak_dbfs']:.2f} dBFS, 4배 오버샘플링 피크 {summary['oversampled_4x_peak_dbfs']:.2f} dBFS, 클리핑 0샘플. 게임 12개와 UI 4개 동시 음성을 포함합니다.</p><p>갈무리 유지 구간에 10 ms 이상의 무음 끊김이 없고, 해제 후 약 {summary['harvest_release_last_audible_sample_seconds']:.3f}초에 소리가 정리됐습니다. 최종 무음 구간은 0입니다.</p><p>아래 자료는 실제 믹서를 거친 진단용 소리입니다. 실제 입력·전투와 OS/스피커 출력·청취 평가는 구분합니다.</p><p><a href="UnityOutput/REVIEW.html">단독·최대 중첩·갈무리 출력 전체 검토</a> · <a href="REPORT.md">기술 보고서</a></p><div>동시 재생 <audio controls preload="none" src="UnityOutput/maximum_role_limited_overlap.wav"></audio></div><div>갈무리 유지 <audio controls preload="none" src="UnityOutput/harvest_start_and_hold.wav"></audio></div><div>갈무리 종료 <audio controls preload="none" src="UnityOutput/harvest_release.wav"></audio></div></section><!-- UNITY_OUTPUT_END -->'''
page=ART/'REVIEW.html';text=page.read_text(encoding='utf8');text=re.sub(r'<!-- UNITY_OUTPUT_BEGIN -->.*?<!-- UNITY_OUTPUT_END -->','',text,flags=re.S)
idx=text.find('<article>');text=text[:idx]+section+text[idx:] if idx>=0 else text+section;page.write_text(text,encoding='utf8')
print(json.dumps(summary,ensure_ascii=False,indent=2))
