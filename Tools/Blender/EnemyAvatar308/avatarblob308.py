"""#308 enemy rig revision 2: read the joint frames Unity itself stored in an imported humanoid Avatar (Library artifact, READ ONLY).
The Avatar's human skeleton carries, per human bone, m_PreQ / m_PostQ / m_Sgn / m_Limit / m_Length / m_Type (76 bytes, little endian).
The table is found by its shape (two unit quaternions followed by three signs, 76-byte stride), then the node table in front of it
(parent index, axes index) is read back. Nothing is written into Library; work on a copy."""
import struct
import numpy as np


def read_axes(path):
    b = open(path, 'rb').read(); hits = []
    for off in range(4):
        a = np.frombuffer(b[off: off + (len(b) - off) // 4 * 4], dtype='<f4'); n = len(a) - 12
        with np.errstate(all='ignore'):
            q1 = a[0:n] ** 2 + a[1:n + 1] ** 2 + a[2:n + 2] ** 2 + a[3:n + 3] ** 2; q2 = a[4:n + 4] ** 2 + a[5:n + 5] ** 2 + a[6:n + 6] ** 2 + a[7:n + 7] ** 2
            s = (np.abs(np.abs(a[8:n + 8]) - 1) < 1e-6) & (np.abs(np.abs(a[9:n + 9]) - 1) < 1e-6) & (np.abs(np.abs(a[10:n + 10]) - 1) < 1e-6)
            ok = np.where((np.abs(q1 - 1) < 1e-4) & (np.abs(q2 - 1) < 1e-4) & s)[0]
        hits += [off + 4 * int(k) for k in ok]
    hits.sort(); runs = []; cur = [hits[0]]
    for h in hits[1:]:
        if h - cur[-1] == 76: cur.append(h)
        else: runs.append(cur); cur = [h]
    runs.append(cur); A = max(runs, key=len)[0]
    i32 = lambda o: struct.unpack_from('<i', b, o)[0]
    nA = i32(A - 4); N = None
    for cand in range(nA, 80):
        o_id = A - 4 - 4 * cand - 4
        if i32(o_id) == cand and i32(o_id - 8 * cand - 4) == cand: N = cand; break
    if N is None: raise ValueError('node table not found in ' + path)
    o_node = o_id - 8 * N
    nodes = [(i32(o_node + 8 * k), i32(o_node + 8 * k + 4)) for k in range(N)]
    axes = []
    for k in range(nA):
        o = A + 76 * k; f = struct.unpack_from('<18f', b, o)
        axes.append({'pre': np.array(f[0:4]), 'post': np.array(f[4:8]), 'sgn': np.array(f[8:11]), 'min': np.degrees(f[11:14]), 'max': np.degrees(f[14:17]), 'len': f[17],
                     'type': struct.unpack_from('<I', b, o + 72)[0]})
    # human skeleton pose (T-pose as Unity kept it): count, then 40-byte xforms t(3) q(4) s(3)
    o = A + 76 * nA; nP = i32(o); pose = []
    if nP == N:
        for k in range(N):
            f = struct.unpack_from('<10f', b, o + 4 + 40 * k); pose.append({'t': np.array(f[0:3]), 'q': np.array(f[3:7]), 's': np.array(f[7:10])})
    return {'nodes': nodes, 'axes': axes, 'pose': pose, 'offset': A}
