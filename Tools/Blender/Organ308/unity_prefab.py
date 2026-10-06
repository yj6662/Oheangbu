# #308 organ-art: minimal read-only Unity prefab YAML reader (Transform hierarchy -> prefab-root-space matrices).
# Pure Python (no Blender, no Unity). Unity is left-handed, Y-up; quaternion/matrix formulas are the usual ones.
import re
from pathlib import Path

DOC = re.compile(r"^--- !u!(\d+) &(-?\d+)(?: stripped)?\s*$", re.M)


def _vec(text, key, n):
    m = re.search(r"\b" + key + r": \{([^}]*)\}", text)
    if not m:
        return None
    vals = dict((k.strip(), float(v)) for k, v in (p.split(":") for p in m.group(1).split(",")))
    return [vals[c] for c in "xyzw"[:n]]


def _ref(text, key):
    m = re.search(r"\b" + key + r": \{fileID: (-?\d+)", text)
    return m.group(1) if m else None


def quat_to_mat(q):
    x, y, z, w = q
    return [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]


def mat_to_quat(m):
    t = m[0][0] + m[1][1] + m[2][2]
    if t > 0:
        s = (t + 1.0) ** 0.5 * 2
        w = 0.25 * s; x = (m[2][1] - m[1][2]) / s; y = (m[0][2] - m[2][0]) / s; z = (m[1][0] - m[0][1]) / s
    elif m[0][0] > m[1][1] and m[0][0] > m[2][2]:
        s = (1.0 + m[0][0] - m[1][1] - m[2][2]) ** 0.5 * 2
        w = (m[2][1] - m[1][2]) / s; x = 0.25 * s; y = (m[0][1] + m[1][0]) / s; z = (m[0][2] + m[2][0]) / s
    elif m[1][1] > m[2][2]:
        s = (1.0 + m[1][1] - m[0][0] - m[2][2]) ** 0.5 * 2
        w = (m[0][2] - m[2][0]) / s; x = (m[0][1] + m[1][0]) / s; y = 0.25 * s; z = (m[1][2] + m[2][1]) / s
    else:
        s = (1.0 + m[2][2] - m[0][0] - m[1][1]) ** 0.5 * 2
        w = (m[1][0] - m[0][1]) / s; x = (m[0][2] + m[2][0]) / s; y = (m[1][2] + m[2][1]) / s; z = 0.25 * s
    if w < 0:
        x, y, z, w = -x, -y, -z, -w
    return [x, y, z, w]


def trs(p, q, s):
    R = quat_to_mat(q)
    return [[R[i][0] * s[0], R[i][1] * s[1], R[i][2] * s[2], p[i]] for i in range(3)] + [[0, 0, 0, 1]]


def mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def inv_affine(m):
    # general 3x3 inverse (bones may carry ~1 non-uniform scale)
    a = [row[:3] for row in m[:3]]
    det = (a[0][0] * (a[1][1] * a[2][2] - a[1][2] * a[2][1]) - a[0][1] * (a[1][0] * a[2][2] - a[1][2] * a[2][0])
           + a[0][2] * (a[1][0] * a[2][1] - a[1][1] * a[2][0]))
    inv = [[(a[1][1] * a[2][2] - a[1][2] * a[2][1]) / det, (a[0][2] * a[2][1] - a[0][1] * a[2][2]) / det, (a[0][1] * a[1][2] - a[0][2] * a[1][1]) / det],
           [(a[1][2] * a[2][0] - a[1][0] * a[2][2]) / det, (a[0][0] * a[2][2] - a[0][2] * a[2][0]) / det, (a[0][2] * a[1][0] - a[0][0] * a[1][2]) / det],
           [(a[1][0] * a[2][1] - a[1][1] * a[2][0]) / det, (a[0][1] * a[2][0] - a[0][0] * a[2][1]) / det, (a[0][0] * a[1][1] - a[0][1] * a[1][0]) / det]]
    t = [-sum(inv[i][k] * m[k][3] for k in range(3)) for i in range(3)]
    return [inv[i] + [t[i]] for i in range(3)] + [[0, 0, 0, 1]]


def apply(m, v, w=1.0):
    return [m[i][0] * v[0] + m[i][1] * v[1] + m[i][2] * v[2] + m[i][3] * w for i in range(3)]


class Prefab:
    def __init__(self, path):
        text = Path(path).read_text(encoding="utf-8")
        heads = list(DOC.finditer(text))
        self.names, self.tf = {}, {}
        for i, h in enumerate(heads):
            body = text[h.end(): heads[i + 1].start() if i + 1 < len(heads) else len(text)]
            cls, fid = h.group(1), h.group(2)
            if cls == "1":
                m = re.search(r"m_Name: ?(.*)", body)
                self.names[fid] = (m.group(1).strip() if m else "")
            elif cls in ("4", "224"):
                self.tf[fid] = dict(go=_ref(body, "m_GameObject"), father=_ref(body, "m_Father"),
                                    p=_vec(body, "m_LocalPosition", 3), q=_vec(body, "m_LocalRotation", 4),
                                    s=_vec(body, "m_LocalScale", 3))
        self.instances = len(re.findall(r"^--- !u!1001 ", text, re.M))
        self.root = next(f for f, t in self.tf.items() if t["father"] in (None, "0"))
        self._world = {}

    def name(self, fid):
        return self.names.get(self.tf[fid]["go"], "")

    def world(self, fid):
        """prefab-root-space matrix (root's own TRS excluded, i.e. root = identity)."""
        if fid in self._world:
            return self._world[fid]
        t = self.tf[fid]
        if fid == self.root:
            m = [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]]
        else:
            m = mul(self.world(t["father"]), trs(t["p"], t["q"], t["s"]))
        self._world[fid] = m
        return m

    def path(self, fid):
        parts = []
        while fid != self.root:
            parts.append(self.name(fid))
            fid = self.tf[fid]["father"]
        return "/".join(reversed(parts))

    def by_name(self, name):
        hits = [f for f in self.tf if self.name(f) == name]
        return hits
