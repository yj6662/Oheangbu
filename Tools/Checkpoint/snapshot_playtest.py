"""Export a source checkpoint without redistributing local asset packs or credentials."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess


def run(source, target):
    source, target = Path(source).resolve(), Path(target).resolve()
    if source == target or not (target / '.git').exists():
        raise RuntimeError('Use a separate Git worktree')
    def git(*args, cwd=source):
        return subprocess.check_output(['git', *args], cwd=cwd)
    tracked = set(git('ls-files', '-z').decode().split('\0'))
    dirty = set(git('diff', 'HEAD', '--name-only', '-z').decode().split('\0'))
    remote = set(git('ls-files', '-z', cwd=target).decode().split('\0'))
    skip_dirs = {'__pycache__', '.private', '.git', 'bin', 'obj', 'node_modules', '_local', '_out', '_smoke'}
    text_ext = {'.cs', '.shader', '.hlsl', '.cginc', '.compute', '.asmdef', '.asmref', '.meta', '.asset', '.mat', '.prefab', '.unity', '.anim', '.shadergraph', '.shadersubgraph', '.inputactions', '.json', '.csv', '.xml', '.md', '.txt', '.html', '.css', '.js', '.py', '.ps1', '.cmd', '.bat', '.sh', '.yml', '.yaml', '.uxml', '.uss', '.rsp', '.csproj', '.controller', '.overridecontroller', '.mixer', '.preset', '.terrainlayer', '.rendertexture', '.shadervariants'}
    roots = ['Docs', 'Tools', 'Oheangbu/Assets/_Project', 'Oheangbu/Assets/Settings', 'Oheangbu/Packages', 'Oheangbu/ProjectSettings']
    reports = ['Art/PlaytestRecovery', 'Art/PlaytestPolish', 'Art/UIAudio', 'Art/World', 'Art/PlayerPhase1/PlaytestReRig']
    candidates = [p for p in source.iterdir() if p.is_file() and p.name in {'.gitignore', 'CLAUDE.md', 'README.md'}]
    candidates += [source / 'Oheangbu/.gitignore', source / 'Oheangbu/Assets/InputSystem_Actions.inputactions']
    for rel in roots + reports:
        start = source / rel
        if not start.exists():
            continue
        for folder, dirs, files in os.walk(start):
            dirs[:] = [d for d in dirs if d not in skip_dirs and not d.lower().startswith(('backup', 'before', 'intermediate')) and not d.startswith('com.occasoftware.buto')]
            candidates.extend(Path(folder) / f for f in files)
    # Preserve public exclusions added by earlier merged checkpoints.
    for rel in ['.gitignore', 'Oheangbu/.gitignore']:
        existing = (target / rel).read_text(encoding='utf-8-sig')
        incoming = (source / rel).read_text(encoding='utf-8-sig')
        extras = [line for line in incoming.splitlines() if line not in existing.splitlines()]
        (target / rel).write_text(existing.rstrip() + '\n' + '\n'.join(extras) + '\n', encoding='utf-8')
    source_only = []
    copied, sanitized, deleted = [], [], []
    private_patterns = [
        re.compile(r'(?i)([?&](?:X-Amz-[\w-]+|Signature|Expires|AWSAccessKeyId|token)=)[^&\s"<>]+'),
        re.compile(r'\b(?:gh[pousr]_[A-Za-z0-9]{30,}|sk-[A-Za-z0-9_-]{24,}|msy_[A-Za-z0-9_-]{20,})\b'),
    ]
    for path in candidates:
        rel = path.relative_to(source).as_posix()
        if rel in {'.gitignore', 'Oheangbu/.gitignore'} or not path.is_file():
            continue
        if rel in tracked and rel not in dirty:
            continue  # Do not revert newer main changes with unchanged old-checkout files.
        if rel.startswith('Tools/Checkpoint/'):
            continue
        if path.suffix.lower() not in text_ext:
            if rel.startswith('Oheangbu/Assets/_Project/'):
                source_only.append((rel, path, 'local binary asset'))
            continue
        if rel.startswith('Art/') and (path.suffix.lower() not in {'.md', '.html', '.json', '.csv'} or path.stat().st_size > 2_000_000 or re.search(r'(?:^|[-_])(command|response|request)(?:[-_.]|$)', path.name, re.I) or 'requests' in path.parts):
            continue
        if any(part in {'Source', 'Sources', 'Originals'} for part in path.relative_to(source).parts) and rel.startswith('Tools/MeshyRuns/'):
            continue
        if path.stat().st_size > 45_000_000:
            source_only.append((rel, path, 'large local generated data'))
            continue
        data = path.read_bytes()
        try:
            text = data.decode('utf-8-sig')
        except UnicodeDecodeError:
            source_only.append((rel, path, 'binary serialized asset'))
            continue
        excluded = (path.suffix.lower() in {'.shadergraph', '.shadersubgraph', '.anim'}
                    or '\n--- !u!43 ' in text or '\n--- !u!28 ' in text or text.startswith('Mesh:')
                    or path.name == 'AuthoredBoneMotion.json'
                    or '/KtpOriginal/Original_' in rel)
        if excluded:
            source_only.append((rel, path, 'local source/derived asset; restore before import'))
            continue
        if rel in remote and (target / rel).read_bytes() == data:
            continue
        clean = text
        for regex in private_patterns:
            clean = regex.sub('[REDACTED_LOCAL_CREDENTIAL]', clean)
        if clean != text:
            sanitized.append(rel)
            data = clean.encode('utf-8')
        dest = target / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(data)
        copied.append(rel)
    # Only actual deletions tracked by the original checkout are propagated.
    missing = git('diff', 'HEAD', '--diff-filter=D', '--name-only', '-z').decode().split('\0')
    for rel in missing:
        dest = target / rel
        if rel and dest.is_file() and rel in remote:
            if not dest.resolve().is_relative_to(target):
                raise RuntimeError(rel)
            dest.unlink()
            deleted.append(rel)
    manifest = []
    for rel, path, reason in source_only:
        if rel in remote:
            continue
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        manifest.append({'path': rel, 'bytes': path.stat().st_size, 'sha256': digest, 'reason': reason})
    out = target / 'Docs/Checkpoints/Playtest-20260915'
    out.mkdir(parents=True, exist_ok=True)
    (out / 'LOCAL_ASSETS.json').write_text(json.dumps({'restore': 'Restore from the licensed local workspace at the same relative path; this is not a fresh-clone playable build.', 'files': manifest}, ensure_ascii=False, indent=2), encoding='utf-8')
    (out / 'SNAPSHOT.json').write_text(json.dumps({'sourceHead': git('rev-parse', 'HEAD').decode().strip(), 'baseHead': git('rev-parse', 'HEAD', cwd=target).decode().strip(), 'copied': len(copied), 'sourceDeletions': deleted, 'credentialRedactedFiles': sanitized, 'newLocalAssetCount': len(manifest), 'demoImplementationIncluded': False}, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'copied':len(copied),'deleted':len(deleted),'localAssets':len(manifest),'sanitizedFiles':len(sanitized)}, indent=2))


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('source')
    p.add_argument('target')
    a = p.parse_args()
    run(a.source, a.target)
