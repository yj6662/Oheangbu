"""Package the menu candidate only after its actual standalone smoke passes."""
from pathlib import Path
import datetime, hashlib, json, shutil, sys, zipfile

ROOT = Path(__file__).resolve().parents[2]
BUILDS = ROOT / 'Builds/Playtest-20260915'
candidate = Path(sys.argv[1]).resolve()
if candidate.parent != BUILDS.resolve():
    raise SystemExit('Expected an existing dated candidate directory')
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
report = read(candidate/'release_build_report.json')
smoke = read(candidate/'ui_smoke.json')
if report['buildResult'] != 'Succeeded' or report['totalErrors'] or smoke['failed']:
    raise SystemExit('Build or standalone smoke has failures')
if not any(c['id'] == 'saved-state-restored' and c['status'] == 'PASS' for c in smoke['checks']):
    raise SystemExit('Real title/play/continue round trip has not passed')
licenses = candidate/'Licenses'
shutil.copytree(ROOT/'Art/World/WorldMacro/Playtest/VisualCorridor/Licenses', licenses, dirs_exist_ok=True)
shutil.copy2(ROOT/'Oheangbu/Assets/_Project/Art/UI/PlaytestMenus/Fonts/NotoSansCJKkr-LICENSE.txt', licenses/'NotoSansCJKkr-LICENSE.txt')
(licenses/'UIAudio_Source.txt').write_text(
    'Noto Sans CJK KR: https://github.com/notofonts/noto-cjk\n'
    'Font file bundled in player data; SIL Open Font License included separately.\n'
    'Reused project Recraft graphics and ElevenLabs effects from PlaytestFeedback.\n'
    'Menu hanji uses the existing HwaseongHaenggung Korean paper texture.\n'
    'Twice-folded map uses an ImageGen-generated worn hanji texture; source record included.\n'
    'Fragment icon and pickups use existing SeyeonjeongPavilion stone geometry.\n'
    'Provider originals are preserved in the project.\n', encoding='utf-8')
paper_source=ROOT/'Art/UIAudio/PaperMapReview/SOURCE.md'
if paper_source.exists(): shutil.copy2(paper_source,licenses/'PaperMap_Source.md')
(candidate/'Play_1080p.bat').write_text('@echo off\ncd /d "%~dp0"\nstart "" "Oheangbu_Playtest.exe" -screen-width 1920 -screen-height 1080 -screen-fullscreen 0\n',encoding='ascii')
(candidate/'TEST_GUIDE.txt').write_text('''오행부 · UI 확장 플레이테스트 후보

압축을 모두 풀고 Play_1080p.bat 또는 Oheangbu_Playtest.exe를 실행하세요.
시작 로비의 이어하기 / 새 게임 / 옵션 / 조작 안내를 사용할 수 있습니다.
기존 진행은 새 게임 확인 후 백업됩니다. 저장 오류는 화면에 표시됩니다.

Esc: 일시정지·뒤로 / I: 소지품 / M: 지도 / F: 조사·대화·석경 획득
메뉴를 열면 미완성 작도가 취소되고 게임이 멈춥니다.
지도: 드래그·휠 / 발견한 야외 영역 좌클릭: 표식 / 현재 위치 / 전체 보기 / 범례
석경 위치: 폐광 시작·출구, 금표 주막, 산길 쉼터, 황경 성문 앞.
파편은 도감 설명만 공개합니다. 기존 시전 가능 여부는 같습니다.

소환수 5종은 등장·소멸 연출이며 이동·공격·AI는 아직 없습니다.
황경 외형과 산길은 검토 구간이며 모든 퀘스트가 완성된 것은 아닙니다.
플레이어 파지·전신 작도·천 물리는 미완료입니다.

이번 후보는 자동 보행 완주나 사람의 손글씨 검증을 대신하지 않습니다.
120fps를 보장하지 않습니다. 실제 PC의 플레이 감각과 화면을 확인해 주세요.
UI·소리의 최종 미술/청취 판단은 사용자 검토 대상입니다.
오류 제보 시 장소·입력·화면과 Player.log를 남겨 주세요.
''',encoding='utf-8-sig')
allowed_dirs={'D3D12','MonoBleedingEdge','Oheangbu_Playtest_Data','Licenses'}
allowed_files={'Oheangbu_Playtest.exe','UnityPlayer.dll','UnityCrashHandler64.exe','DirectML.dll','README_플레이테스트.txt','TEST_GUIDE.txt','Play_1080p.bat'}
files=sorted(p for p in candidate.rglob('*') if p.is_file() and (p.relative_to(candidate).parts[0] in allowed_dirs or p.parent==candidate and p.name in allowed_files))
def digest(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream,'sha256').hexdigest()
info={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'candidate':candidate.name,'qaComplete':False,
      'scene':report['scene'],'buildResult':report['buildResult'],'errors':report['totalErrors'],'warnings':report['totalWarnings'],
      'smoke':smoke['outcome'],'smokeUnverified':[c for c in smoke['checks'] if c['status']=='UNVERIFIED'],
      'files':[{'path':p.relative_to(candidate).as_posix(),'bytes':p.stat().st_size,'sha256':digest(p)} for p in files]}
(candidate/'BUILD_INFO.json').write_text(json.dumps(info,ensure_ascii=False,indent=2),encoding='utf-8')
files.append(candidate/'BUILD_INFO.json')
archive=BUILDS/f'Oheangbu_UI_Playtest_{candidate.name}.zip'
if archive.exists(): raise SystemExit('Refusing to overwrite '+str(archive))
with zipfile.ZipFile(archive,'x',compression=zipfile.ZIP_DEFLATED,compresslevel=3,allowZip64=True) as bundle:
    for p in files: bundle.write(p,'Oheangbu_Playtest/'+p.relative_to(candidate).as_posix())
with zipfile.ZipFile(archive) as bundle:
    if len(bundle.infolist())!=len(files) or bundle.testzip(): raise RuntimeError('ZIP integrity failed')
result={'archive':str(archive),'sha256':digest(archive),'bytes':archive.stat().st_size,'files':len(files),'crcVerified':True,'qaComplete':False}
(candidate/'package_report.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(result,ensure_ascii=False))
