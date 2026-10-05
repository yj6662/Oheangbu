#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""spell120_sweep308.py - drive the editor tool Spell120Sweep308 through the VFX120 queue and wait until it is done.

The editor tool runs over many editor updates, so a plain queue submit returns at once. This driver stays alive until
the sweep has written its report, which is what lets `resource_guard.py --gpu-run` hold the GPU lock for the whole capture.

  python Tools/SpellVFX120/spell120_sweep308.py describe --set catalog          # read-only: renderer, quality level, render scale
  python Tools/SpellVFX120/spell120_sweep308.py trial    --set catalog          # 5 glyphs, both camera modes
  python Tools/SpellVFX120/spell120_sweep308.py run      --set catalog          # all glyphs of the set (needs the trial)
  python Tools/SpellVFX120/spell120_sweep308.py run      --set live --view first
  python Tools/SpellVFX120/spell120_sweep308.py run      --set catalog --retries 3   # resume by itself after an INTERRUPTED stop
  python Tools/SpellVFX120/spell120_sweep308.py cancel
  add --dry-run to print the queue command without sending it

An assembly reload, a Play request, a quality-level switch or a scene change by another session stops the sweep with
INTERRUPTED; finished glyphs are kept and the same request resumes. --retries does that resubmission. It cannot help
when the runtime assembly changed in between: the trial gate then asks for a new trial, and the driver stops and says so.

It never overwrites another session's pending command (Art/SpellVFX120/command.json) and never starts Play.
"""
import argparse
import json
import os
import sys
import time
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
QUEUE = os.path.join(REPO, 'Art', 'SpellVFX120')
SWEEP = os.path.join(QUEUE, 'Spell120_308', 'Sweep')


def read_json(path):
    for _ in range(5):
        try:
            with open(path, 'r', encoding='utf-8-sig') as fh:
                return json.load(fh)
        except (OSError, ValueError):
            time.sleep(.2)   # the editor may be writing the file right now
    return None


def submit(method, request, wait):
    command = os.path.join(QUEUE, 'command.json')
    if os.path.exists(command):
        raise SystemExit('refused: an unconsumed command.json of another session exists; try again when it is gone')
    token = uuid.uuid4().hex
    pending = os.path.join(QUEUE, 'command_' + token + '.pending')
    with open(pending, 'w', encoding='utf-8', newline='\n') as fh:
        json.dump({'id': token, 'method': method, 'request': request}, fh)
    os.replace(pending, command)
    response = os.path.join(QUEUE, 'response_' + token + '.json')
    end = time.monotonic() + wait
    while time.monotonic() < end:
        if os.path.exists(response):
            data = read_json(response)
            if data is not None:
                return data
        time.sleep(.25)
    raise SystemExit('the editor did not answer within %ds (is it compiling, in Play, or is the queue script not deployed?) '
                     'response file: %s' % (wait, response))


def wait_for(path, started, timeout, label):
    """Block until `path` is newer than `started`; print progress.json while waiting."""
    progress_path = os.path.join(SWEEP, 'progress.json')
    end = time.monotonic() + timeout
    last = None
    while time.monotonic() < end:
        if os.path.exists(path) and os.path.getmtime(path) >= started:
            return read_json(path)
        progress = read_json(progress_path) if os.path.exists(progress_path) else None
        if progress:
            line = '%s %s/%s %s %s' % (progress.get('status'), progress.get('index'), progress.get('total'),
                                       progress.get('cameraMode'), progress.get('view'))
            if line != last:
                print('  ' + line, flush=True)
                last = line
            if progress.get('status') in ('FAILED', 'FAILED_TO_START', 'INTERRUPTED', 'CANCELLED') \
                    and os.path.getmtime(progress_path) >= started and label == 'run':
                print('  stopped: ' + str(progress.get('error'))[:600])
                return None
        time.sleep(1.0)
    print('timed out after %ds; the sweep may still be running in the editor (send: cancel)' % timeout)
    return None


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('action', choices=['describe', 'trial', 'run', 'cancel', 'poll'])
    ap.add_argument('--set', default='catalog', choices=['catalog', 'live', 'original', 'bolt300'])
    ap.add_argument('--camera', default='game', choices=['game', 'preview'])
    ap.add_argument('--view', default='review', choices=['review', 'first'])
    ap.add_argument('--glyphs', default='')
    ap.add_argument('--start', type=int, default=0)
    ap.add_argument('--count', type=int, default=120)
    ap.add_argument('--width', type=int, default=1280)
    ap.add_argument('--height', type=int, default=720)
    ap.add_argument('--renderer-index', type=int, default=-1)
    ap.add_argument('--stage-height', type=float, default=4000.0)
    ap.add_argument('--no-resume', action='store_true')
    ap.add_argument('--no-targets', action='store_true')
    ap.add_argument('--no-floor', action='store_true')
    ap.add_argument('--force', action='store_true', help='skip the trial gate and the stale-assembly check')
    ap.add_argument('--timeout', type=int, default=900)
    ap.add_argument('--retries', type=int, default=0, help='run only: resubmit this many times after an INTERRUPTED stop')
    ap.add_argument('--retry-wait', type=int, default=20, help='seconds to wait before a resubmission')
    ap.add_argument('--dry-run', action='store_true')
    a = ap.parse_args()
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')

    request = {'set': a.set, 'cameraMode': a.camera, 'view': a.view, 'glyphs': a.glyphs, 'start': a.start, 'count': a.count,
               'width': a.width, 'height': a.height, 'rendererIndex': a.renderer_index, 'stageHeight': a.stage_height,
               'resume': not a.no_resume, 'targets': not a.no_targets, 'floor': not a.no_floor, 'force': a.force}
    method = {'describe': 'Sweep308Describe', 'trial': 'Sweep308Trial', 'run': 'Sweep308', 'cancel': 'Sweep308Cancel',
              'poll': 'Sweep308Poll'}[a.action]
    payload = json.dumps(request, ensure_ascii=False)
    if a.dry_run:
        print(json.dumps({'method': method, 'request': payload}, ensure_ascii=False, indent=1))
        return 0
    attempt = 0
    while True:
        started = time.time()
        if attempt > 0:
            # a resubmission continues the interrupted run even when the first attempt asked for a fresh capture
            payload = json.dumps(dict(request, resume=True), ensure_ascii=False)
        answer = submit(method, payload if a.action in ('describe', 'trial', 'run') else None, 60)
        if answer.get('status') != 'COMPLETE':
            error = str(answer.get('error') or '')
            # the editor is still compiling or importing after the interruption: that passes, anything else does not
            if a.action == 'run' and attempt > 0 and attempt <= a.retries and 'Wait for edit mode' in error:
                print('  editor not ready yet; trying again in %ds' % a.retry_wait, flush=True)
                time.sleep(a.retry_wait)
                attempt += 1
                continue
            print(json.dumps(answer, ensure_ascii=False, indent=1)[:4000])
            return 2
        print(str(answer.get('result'))[:8000])
        if a.action != 'run':
            break
        code = finish_run(a, started)
        if code != 5 or attempt >= a.retries:
            return 4 if code == 5 else code
        attempt += 1
        print('  INTERRUPTED; resubmitting the same request (%d/%d) in %ds' % (attempt, a.retries, a.retry_wait), flush=True)
        time.sleep(a.retry_wait)
    if a.action == 'trial':
        summary = wait_for(os.path.join(SWEEP, 'trial_%s.json' % a.set), started, a.timeout, 'trial')
        if summary is None:
            return 3
        print(summary.get('status'))
        for line in summary.get('lines', []):
            print(line)
        return 0 if str(summary.get('status', '')).startswith('TRIAL_CAPTURED') else 4
    return 0


def finish_run(a, started):
    """Wait for the report of one run. 0 captured, 3 no report, 4 incomplete, 5 interrupted (resumable)."""
    folder = '%s_%s_%s' % (a.set, a.camera, a.view)
    report = wait_for(os.path.join(SWEEP, folder, 'sweep.json'), started, a.timeout, 'run')
    if report is None:
        return 3
    env = report.get('environment') or {}
    print('%s: requested %s captured %s reused %s exceptions %s beatsWithoutPixels %s glyphsWithoutAnyPixels %s in %.1fs'
          % (report.get('status'), report.get('requested'), report.get('captured'), report.get('reused'),
             report.get('exceptions'), report.get('beatsWithoutPixels'), report.get('glyphsWithoutAnyPixels'),
             report.get('elapsedSeconds') or 0))
    print('renderer %s | quality %s | render scale %s | lighting %s' % (env.get('rendererName'), env.get('qualityLevel'),
                                                                       env.get('pipelineRenderScale'), env.get('lightingOverride')))
    print('empty-stage render %.1f ms | render callbacks: %s' % (env.get('baselineRenderMilliseconds') or 0,
                                                              env.get('renderCallbackSubscribers')))
    print('physics floor: %s' % env.get('physicsFloor'))
    print('not isolated: %s' % env.get('notIsolated'))
    print('open scene preserved: hierarchy %s, environment %s, dirty flags %s; leaked roots adopted %s; camera touches %s'
          % (report.get('hierarchyPreserved'), report.get('environmentPreserved'), report.get('sceneDirtyFlagsPreserved'),
             report.get('leakedRootsAdopted'), report.get('openSceneCameraTouches')))
    rows = report.get('rows') or []
    hidden = [r.get('glyph') for r in rows if r.get('unattributedHiddenRoots')]
    if hidden:
        print('new hidden roots appeared in an open scene during: %s (left alone; see the per-glyph records)' % ''.join(hidden))
    deploy = [r.get('glyph') for r in rows if r.get('deploy308Active')]
    if deploy:
        print('deploy layer (Deploy308) drew in: %s' % ''.join(deploy))
    if report.get('reason'):
        print('reason: ' + str(report['reason'])[:1500])
    if report.get('status') == 'INTERRUPTED':
        return 5
    return 0 if report.get('status') == 'CAPTURED_ART_UNVERIFIED' else 4


if __name__ == '__main__':
    sys.exit(main())
