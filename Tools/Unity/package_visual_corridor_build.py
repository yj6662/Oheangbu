"""Package an already built and startup-checked candidate; never invokes Unity."""
from pathlib import Path
import datetime
import hashlib
import json
import shutil
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]
BUILD_ROOT = ROOT / 'Builds/Playtest-20260915'
directory = Path(sys.argv[1]).resolve()
if directory.parent != BUILD_ROOT.resolve():
    raise SystemExit('Expected one candidate folder under Builds/Playtest-20260915')
report = json.loads((directory/'release_build_report.json').read_text(encoding='utf-8-sig'))
if report['buildResult'] != 'Succeeded' or report['totalErrors'] != 0:
    raise SystemExit('Packaging requires a successful build with no reported errors')
smokes = [json.loads((directory/name).read_text(encoding='utf-8-sig')) for name in ('smoke_start.json','smoke_restart.json')]
if not all(s['status'].startswith('PASS_') for s in smokes) or not smokes[1]['semantic']['restartObserved']:
    raise SystemExit('Startup and restart checks must pass before packaging')
review = ROOT/'Art/World/WorldMacro/Playtest/VisualCorridor'
shutil.copytree(review/'Licenses', directory/'Licenses', dirs_exist_ok=True)
feedback_included = any(check['name'] == 'saved scene ElevenLabs SFX' and check['pass']
                        for check in report.get('checks', []))
if feedback_included:
    (directory/'Licenses'/'Generated_UI_SFX.txt').write_text(
        'Playtest HUD: Recraft API, recraftv4_vector, four original SVG designs.\n'
        'Playtest SFX: ElevenLabs API, eleven_text_to_sound_v2, fourteen final clips.\n'
        'Generated for Oheangbu on 2026-09-14 KST.\n'
        'Game assets are locally prepared transparent PNG and mono PCM WAV versions.\n'
        'Source assets, request provenance and hashes are preserved in the project\n'
        'at Art/UIAudio/PlaytestFeedback. Human visual/listening approval is pending.\n', encoding='utf-8')
(directory/'Play_1080p.bat').write_text('@echo off\ncd /d "%~dp0"\nstart "" "Oheangbu_Playtest.exe" -screen-width 1920 -screen-height 1080 -screen-fullscreen 0\n', encoding='ascii')
(directory/'TEST_GUIDE.txt').write_text('''오행부 / 2026-09-15 플레이테스트 후보

압축을 모두 푼 뒤 Play_1080p.bat 또는 Oheangbu_Playtest.exe를 실행합니다.
조작법은 README_플레이테스트.txt에 있습니다.

이번 확인 구간: 폐광 조사 → 첫 교전 → 산길 탐험 → 금표 주막 대화·휴식.
황경 방향은 보유 에셋으로 경관과 길을 보완한 외형 검토 구간입니다.
황경까지 모든 퀘스트와 도시 내부 콘텐츠가 연결된 완성본은 아닙니다.

소환 글자 곰·놈·몸·솜·옴은 먹을 소비하고 약4.6초 동안 등장·소멸합니다.
현재 소환수의 이동·공격·방어 기능은 없습니다.
플레이어 모델의 손가락 파지·전신 작도 IK·천 물리는 미완료입니다.
실제 손글씨 인식과 보행·전투 감각은 이번 사용자 테스트에서 확인합니다.

120fps는 보장되지 않습니다. 고정 지점 Editor 측정은 약12.4ms였으며,
숨김 실행의 시작·저장 검사는 렌더 성능 측정으로 취급하지 않았습니다.
이 버전은 최종 미술·전 구간 QA 합격본이 아닙니다.

문제 제보: 어느 장소에서 어떤 입력을 했는지와 화면을 남겨 주세요.
실행 로그: %USERPROFILE%\\AppData\\LocalLow\\DefaultCompany\\Oheangbu\\Player.log
기존 저장 파일은 자동으로 지우지 않습니다.
''',encoding='utf-8-sig')
if feedback_included:
    with (directory/'TEST_GUIDE.txt').open('a', encoding='utf-8') as guide:
        guide.write('\n이번 후보에는 Recraft HUD 4종과 ElevenLabs 효과음 14종이 포함됩니다.\n'
                    '체력·먹병·락온 원·상호작용 안내와 시전·피격·받아치기·소환 소리를 확인해 주세요.\n'
                    '모든 음원의 체감 음량과 최종 미술 평가는 사용자 검토를 기다립니다.\n')

def digest(path):
    with path.open('rb') as file:
        return hashlib.file_digest(file, 'sha256').hexdigest()

allowed_dirs={'D3D12','MonoBleedingEdge','Oheangbu_Playtest_Data','Licenses'}
allowed_files={'Oheangbu_Playtest.exe','UnityPlayer.dll','UnityCrashHandler64.exe','DirectML.dll',
               'README_플레이테스트.txt','TEST_GUIDE.txt','Play_1080p.bat'}
files=sorted(p for p in directory.rglob('*') if p.is_file() and
             (p.relative_to(directory).parts[0] in allowed_dirs or p.name in allowed_files and p.parent==directory))
info={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'candidate':directory.name,
      'gitHead':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),
      'workingTreeDirty':bool(subprocess.check_output(['git','status','--porcelain','--untracked-files=no'],cwd=ROOT,text=True)),
      'scene':report['scene'],'sceneSha256':digest(ROOT/'Oheangbu'/report['scene']),
      'buildResult':report['buildResult'],'errors':report['totalErrors'],'warnings':report['totalWarnings'],
      'startup':smokes[0]['status'],'restart':smokes[1]['status'],'qaComplete':False,
      'recraftElevenLabsFeedbackIncluded':feedback_included,
      'files':[{'path':p.relative_to(directory).as_posix(),'bytes':p.stat().st_size,'sha256':digest(p)} for p in files]}
(directory/'BUILD_INFO.json').write_text(json.dumps(info,ensure_ascii=False,indent=2),encoding='utf-8')
files.append(directory/'BUILD_INFO.json')
archive=BUILD_ROOT/f'Oheangbu_Playtest_{directory.name}.zip'
if archive.exists(): raise SystemExit('Refusing to overwrite archive: '+str(archive))
with zipfile.ZipFile(archive,'x',compression=zipfile.ZIP_DEFLATED,compresslevel=3,allowZip64=True) as zip_out:
    for file in files: zip_out.write(file, 'Oheangbu_Playtest/'+file.relative_to(directory).as_posix())
with zipfile.ZipFile(archive) as zip_in:
    if len(zip_in.infolist()) != len(files): raise RuntimeError('Archive file count mismatch')
    corrupt = zip_in.testzip()
    if corrupt: raise RuntimeError('Archive CRC failure: '+corrupt)
result={'archive':str(archive),'sha256':digest(archive),'bytes':archive.stat().st_size,'files':len(files),
        'unpackedBytes':sum(p.stat().st_size for p in files),'crcVerified':True}
(directory/'package_report.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(result,ensure_ascii=False))
