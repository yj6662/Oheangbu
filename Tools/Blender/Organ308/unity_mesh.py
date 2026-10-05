# #308 organ-art: read-only reader for a text-serialized Unity Mesh asset (positions + triangle indices per submesh).
# Supports float32 position channel 0 in stream 0 and 16/32-bit index buffers (enough for Mesh_Tree.asset).
import re
import struct
from pathlib import Path


def read_mesh(path):
    text = Path(path).read_text(encoding="utf-8")
    subs = []
    for m in re.finditer(r"- serializedVersion: 2\s+firstByte: (\d+)\s+indexCount: (\d+)\s+topology: (\d+)\s+"
                         r"baseVertex: (\d+)\s+firstVertex: (\d+)\s+vertexCount: (\d+)", text):
        subs.append(dict(firstByte=int(m.group(1)), indexCount=int(m.group(2)), topology=int(m.group(3)),
                         baseVertex=int(m.group(4)), firstVertex=int(m.group(5)), vertexCount=int(m.group(6))))
    index_format = int(re.search(r"m_IndexFormat: (\d+)", text).group(1))
    ib = bytes.fromhex(re.search(r"m_IndexBuffer: ([0-9a-f]*)", text).group(1))
    vcount = int(re.search(r"m_VertexCount: (\d+)", text).group(1))
    size = int(re.search(r"m_DataSize: (\d+)", text).group(1))
    vb = bytes.fromhex(re.search(r"_typelessdata: ([0-9a-f]*)", text).group(1))
    if len(vb) != size:
        raise ValueError("vertex data size mismatch")
    chans = re.findall(r"- stream: (\d+)\s+offset: (\d+)\s+format: (\d+)\s+dimension: (\d+)", text)
    stride = 0
    for st, off, fmt, dim in chans:
        if int(st) == 0 and int(dim) > 0:
            width = {0: 4, 1: 2}.get(int(fmt), 4)
            stride = max(stride, int(off) + width * int(dim))
    if stride * vcount != size:
        raise ValueError("unsupported vertex layout (stride %d)" % stride)
    st, off, fmt, dim = map(int, chans[0])
    if (st, off, fmt, dim) != (0, 0, 0, 3):
        raise ValueError("position channel is not float3 at offset 0")
    pos = [struct.unpack_from("<3f", vb, i * stride) for i in range(vcount)]
    width = 4 if index_format == 1 else 2
    tris = []
    for s in subs:
        n = s["indexCount"]
        idx = struct.unpack_from("<%d%s" % (n, "I" if width == 4 else "H"), ib, s["firstByte"])
        tris.append([(idx[i] + s["baseVertex"], idx[i + 1] + s["baseVertex"], idx[i + 2] + s["baseVertex"])
                     for i in range(0, n, 3)])
    return dict(positions=pos, submeshes=subs, triangles=tris)
