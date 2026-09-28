"""Package fresh escort evidence. No Unity control or game/save modification."""
from datetime import datetime, timezone
from pathlib import Path
import hashlib
import html
import json
import shutil

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild/Escort252'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    lines = (OUT / 'runtime.txt').read_text(encoding='utf-8-sig').splitlines()
    failures = [s for s in lines if s.startswith('FAIL ')]
    # Retain this observed, correctly rejected early request in the raw record.
    # Only its specifically recorded successful retry resolves it.
    permitted = {'FAIL existing front summon accepts departure: 자동차가 완전히 멈춘 뒤 부를 수 있다.',
                 'FAIL actual roadside rest road_rest_2',
                 'FAIL departure has safe entry/exit ground',
                 'FAIL front probe reaches authored parking: 앞에 자동차를 놓을 공간이 부족하다.'}
    assert all(s in permitted for s in failures), failures
    assert any(s.startswith('PASS call gesture actually materialized existing car:') for s in lines)
    required = ['PASS actual cargo_delivery commits after lock release',
                'PASS restart retains measured stage, delivery and currency',
                'PASS death retains actual inspections',
                'PASS NPC and carried cargo restored at same checkpoint',
                'PASS actual roadside rest road_rest_2',
                'PASS F conversation follows live travelling NPC']
    assert all(s in lines for s in required), [s for s in required if s not in lines]
    journeys = {}
    for name in ['checkpoint_1', 'checkpoint_2', 'cargo_delivery']:
        drive = json.loads((OUT / f'drive-{name}.json').read_text())
        walk = json.loads((OUT / f'approach-{name}.json').read_text())
        assert drive['status'] == walk['status'] == 'PASS', name
        journeys[name] = {k: v for k, v in drive.items() if k != 'trail'}
        source = OUT.parent / 'Village245' / f'escort252_{name}.png'
        shutil.copy2(source, OUT / f'{name}.png')
    canonical = ROOT / 'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Compact.unity'
    assert sha(canonical) == '4c092a039bd257c724f55473222ccc7ff22dea606187c597471913cb6a9c92ca'
    baseline = json.loads((OUT.parent / 'Frontage249/backup.json').read_text())
    preserved = {name: sha(Path(name)) == digest for name, digest in baseline.items() if 'AppData' in name}
    assert preserved and all(preserved.values())
    receipt = json.loads((OUT.parent / 'migration_slice.json').read_text(encoding='utf-8-sig'))
    candidate = ROOT / 'Oheangbu' / receipt['scene']
    sources = [candidate, candidate.parent / 'Content.asset', candidate.parent / 'Map.asset',
               candidate.parent / 'Navigation.asset', candidate.parent / 'Geography.asset',
               candidate.parent / 'Village245/Campaign.asset']
    scripts = ROOT / 'Oheangbu/Assets/_Project/Scripts'
    sources += list((scripts / 'Editor/WorldMacro').glob('CompactRebuildEscort252*.cs'))
    sources += list((scripts / 'App/World').glob('WorldMacroPlaytestSession*.cs'))
    sources += list((scripts / 'App/Demo').glob('DemoEscort*.cs'))
    sources += list((scripts / 'App/World/Vehicle').glob('WorldMacroPalanquin*.cs'))
    sources += [scripts / 'Editor/WorldMacro/CompactRebuildVehicle250.cs']
    checks = {}
    for name in ['runtime', 'audit', 'navigation', 'vehicle-placement']:
        rows = (OUT / f'{name}.txt').read_text(encoding='utf-8-sig').splitlines()
        checks[name] = {'passed_lines': sum(s.startswith('PASS ') for s in rows),
                        'failed_attempts': [s for s in rows if s.startswith('FAIL ')]}
    report = dict(revision=252, utc=datetime.now(timezone.utc).isoformat(), scene=receipt['scene'],
                  slot='world-demo-compact-cave-v4', checks=checks, journeys=journeys,
                  total_driving_metres=sum(x['metres'] for x in journeys.values()),
                  total_driving_seconds=sum(x['seconds'] for x in journeys.values()),
                  resolved_attempts=['Early summon while initial car was still settling was correctly refused; retry after stopping passed.',
                                     'NPC collided with guesthouse foundation; east-side staging route passed live pickup and walking.',
                                     'Entry setup and boarding in same frame was rejected before locomotion grounded; separated fixture passed.',
                                     'Actual downhill disembark used stale walking height and triggered fall recovery; shared traversal transition fixed and route rerun.',
                                     'Rest approach fixture stopped just outside its 2 m range; radius-aware approach passed.',
                                     'Large-coordinate diagonal 6 m summon rounded to 5.999987 m and was rejected. A 5 mm numeric tolerance fixed it; hull, wheel and exit safety rules remain.'],
                  manual_play='not performed', performance='not measured', art_approval='pending; functional route and standing character proxies',
                  remaining=['South gate actor and physical door not yet connected; final two stages remain disabled.',
                             'Roadside landscape and architecture detail, seated pose, native-input driving/combat and CPU/GPU review remain.'],
                  canonical_sha256=sha(canonical), normal_saves_preserved=preserved,
                  sources={p.relative_to(ROOT).as_posix(): sha(p) for p in sources})
    (OUT / 'delivery.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    page = '''<!doctype html><html lang="ko"><meta charset="utf-8"><title>상경 호송 연결</title>
<style>body{max-width:1160px;margin:48px auto;padding:0 24px;background:#e5dfd1;color:#252621;font:17px/1.7 sans-serif}h1{font-size:32px}figure{margin:36px 0}img{width:100%}a{color:#415447}td,th{padding:9px 24px 9px 0;text-align:left;border-bottom:1px solid #b4ae9f}table{border-collapse:collapse}</style>
<h1>왕소와 상경길</h1><p>객주에서 화물 집기 → 동승 → 두 검문 → 성저 객주 인도를 실제 후보에 연결했습니다. 선택 보고와 학습을 먼저 끝내지 않아도 계약·청룡 해금 뒤 출발할 수 있습니다.</p>
<p><strong>기능 검토 화면입니다.</strong> 길 주변 풍경과 검문소 외형, 동승 자세는 후속 미술 작업이 남아 있습니다. 남문 장수와 개문은 다음 제작 단계입니다.</p><table><tr><th>새 검증</th><th>결과</th></tr>'''
    for name, result in checks.items():
        page += f'<tr><td><a href="Escort252/{name}.txt">{html.escape(name)}</a></td><td>{result["passed_lines"]} PASS 행'
        if result['failed_attempts']:
            page += ' · 실패 회차·수정 후 재시도 기록 포함'
        page += '</td></tr>'
    page += f'</table><p>실제 바퀴 물리 자동 주행 {report["total_driving_metres"]:.1f}m / {report["total_driving_seconds"]:.1f}초. 하차 후 검문까지는 충돌을 적용한 캐릭터 보행입니다. 탑승 준비에는 플레이어 위치 배치가 포함됩니다.</p>'
    page += '<p>왕소/화물 누락 거부, 저장 실패와 재시도, 중복 보상 방지, 휴식·사망 복구·저장 재실행, 이동한 왕소의 후속 대화를 확인했습니다. 정상 하차가 추락사로 처리되던 공통 높이 판정도 수정했습니다. 수동 운전·전투, CPU/GPU 실측, 사용자 미술 판단은 별도입니다.</p>'
    for name, label in [('checkpoint_1', '첫 검문'), ('checkpoint_2', '둘째 검문'), ('cargo_delivery', '성저 객주 인도')]:
        page += f'<figure><img src="Escort252/{name}.png"><figcaption>{label} — 기능 배치, 풍경과 인물 외형 보완 예정</figcaption></figure>'
    page += '<p>테스트: Open latest test scene → W_Demo_Compact_MigrationCheck → Play.</p><p><a href="Escort252/delivery.json">상세 증거와 보존 해시</a></p></html>'
    (OUT.parent / 'ESCORT_REVIEW.html').write_text(page, encoding='utf-8')
    print(json.dumps({'checks': checks, 'metres': report['total_driving_metres']}, ensure_ascii=False))


if __name__ == '__main__':
    main()
