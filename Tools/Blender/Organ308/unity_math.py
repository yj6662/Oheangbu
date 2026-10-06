# #308 organ-art: Unity quaternion / Euler helpers (pure Python, no bpy). Quaternion = (x, y, z, w);
# Euler order like Quaternion.Euler / Transform.eulerAngles (Unity applies Z, then X, then Y: M = Ry * Rx * Rz).
# Taken over from the retired v1 organ308_deploy.py (D308-4b) so the shard pipeline does not import retired code.
import math


def q_mul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz)


def q_matrix(q):
    x, y, z, w = q
    return [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]


def q_rot(q, v):
    m = q_matrix(q)
    return [m[i][0] * v[0] + m[i][1] * v[1] + m[i][2] * v[2] for i in range(3)]


def euler_to_q(e):
    """Quaternion.Euler(x, y, z) = qY * qX * qZ (z applied first)."""
    hx, hy, hz = (math.radians(c) * 0.5 for c in e)
    qx = (math.sin(hx), 0.0, 0.0, math.cos(hx))
    qy = (0.0, math.sin(hy), 0.0, math.cos(hy))
    qz = (0.0, 0.0, math.sin(hz), math.cos(hz))
    return q_mul(q_mul(qy, qx), qz)


def q_to_euler(q):
    """Inverse of euler_to_q for M = Ry Rx Rz, degrees in [0, 360) like Quaternion.eulerAngles."""
    n = math.sqrt(sum(c * c for c in q))
    q = tuple(c / n for c in q)
    m = q_matrix(q)
    sx = max(-1.0, min(1.0, -m[1][2]))
    x = math.asin(sx)
    if abs(math.cos(x)) > 1e-6:
        y = math.atan2(m[0][2], m[2][2])
        z = math.atan2(m[1][0], m[1][1])
    else:  # gimbal: fold z into y
        y = math.atan2(-m[2][0], m[0][0])
        z = 0.0
    return [round(math.degrees(a) % 360.0, 5) for a in (x, y, z)]


def q_angle_deg(a, b):
    d = abs(sum(x * y for x, y in zip(a, b))) / (math.sqrt(sum(x * x for x in a)) * math.sqrt(sum(x * x for x in b)))
    return math.degrees(2 * math.acos(min(1.0, d)))
