# #308 organ-art: dump FBX GlobalSettings axes/unit and Model node Lcl transforms (read-only, raw FBX parse).
# Headless: blender -b --factory-startup --python fbx_header.py -- <file.fbx> [...]
import sys
from io_scene_fbx import parse_fbx


def props(node):
    out = {}
    for p70 in node.elems:
        if p70.id == b"Properties70":
            for p in p70.elems:
                name = p.props[0].decode("utf-8", "replace")
                out[name] = p.props[4:]
    return out


for path in sys.argv[sys.argv.index("--") + 1:]:
    root, version = parse_fbx.parse(path)
    gs = next(e for e in root.elems if e.id == b"GlobalSettings")
    g = props(gs)
    keys = ("UpAxis", "UpAxisSign", "FrontAxis", "FrontAxisSign", "CoordAxis", "CoordAxisSign", "UnitScaleFactor",
            "OriginalUnitScaleFactor")
    print("ORGAN308_FBX", path.split("/")[-1], "v%d" % version, {k: g.get(k) for k in keys})
    objs = next(e for e in root.elems if e.id == b"Objects")
    for e in objs.elems:
        if e.id == b"Model":
            p = props(e)
            print("ORGAN308_FBX   Model", e.props[1].split(b"\x00")[0].decode(), {k: p.get(k) for k in
                  ("Lcl Translation", "Lcl Rotation", "Lcl Scaling", "PreRotation", "GeometricRotation")})
