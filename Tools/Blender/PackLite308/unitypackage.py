"""#308 PackLite: read a cached .unitypackage (tar.gz of <guid>/{asset,asset.meta,pathname}) without the editor.

    python -I Tools/Blender/PackLite308/unitypackage.py list <package> <out.json>
    python -I Tools/Blender/PackLite308/unitypackage.py extract <package> <dest dir> <regex on pathname>

extract writes <dest>/<pathname> and <pathname>.meta (dest must be outside Assets and outside tracked paths:
Art/World/PackLite308/_src/<pack>/ — Asset Store EULA: pack files never go under Tools/ or any tracked folder).
"""
import json, os, re, sys, tarfile


def scan(pkg, want=None, dest=None):
    names, sizes, pending = {}, {}, {}
    with tarfile.open(pkg, 'r:gz') as tf:
        for m in tf:
            if not m.isfile(): continue
            parts = m.name.replace('\\', '/').lstrip('./').split('/')
            if len(parts) != 2: continue
            guid, kind = parts
            if kind == 'pathname':
                p = tf.extractfile(m).read().decode('utf-8', 'ignore').split('\n')[0].strip()
                names[guid] = p
                if dest and guid in pending:
                    for k, data in pending.pop(guid).items(): _write(dest, p, k, data, want)
            elif kind in ('asset', 'asset.meta'):
                if kind == 'asset': sizes[guid] = m.size
                if dest:
                    if guid in names:
                        if want.search(names[guid]): _write(dest, names[guid], kind, tf.extractfile(m).read(), want)
                    elif m.size < 600 * 2**20:
                        pending.setdefault(guid, {})[kind] = tf.extractfile(m).read()   # pathname comes after the asset in some packages
            if dest and len(pending) > 40:
                # keep memory bounded: drop the oldest undecided entries that turned out to have a non-matching name
                for g in [g for g in pending if g in names]: pending.pop(g)
    return names, sizes


def _write(dest, path, kind, data, want):
    if not want.search(path): return
    out = os.path.join(dest, path) + ('.meta' if kind == 'asset.meta' else '')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, 'wb') as f: f.write(data)


if __name__ == '__main__':
    if sys.argv[1] == 'list':
        names, sizes = scan(sys.argv[2])
        json.dump(sorted([[p, sizes.get(g, 0), g] for g, p in names.items()]), open(sys.argv[3], 'w', encoding='utf-8'), ensure_ascii=False)
        print(len(names), 'entries')
    else:
        names, sizes = scan(sys.argv[2], re.compile(sys.argv[4]), sys.argv[3])
        print(len(names), 'entries scanned')
