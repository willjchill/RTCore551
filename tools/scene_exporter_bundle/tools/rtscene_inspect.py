#!/usr/bin/env python3
"""
RTCore551 - Task 1 helper: inspect / validate .rtscene files written by the Unity exporter.

Pure standard library (Python 3.8+). Commands:

  info <file>                 header, counts, camera rays, BVH stats, CRC check
  check <file> [--rays N]     everything in `info` + BVH structure validation + BVH-vs-brute-force ray test
  check-reference <file>      byte-for-byte parity test against the C# RTSelfTest reference scene
  make-reference <out>        write the reference scene from Python (what C# should reproduce)
  selftest                    internal consistency tests of this tool and of the format (no Unity needed)

File format (little-endian) is documented in README.md, section "Binary format".
"""
import math
import struct
import sys
import zlib

MAGIC = b"RTS1"
VERSION = 1
HEADER_BYTES = 32
CAMERA_BYTES = 48
SKY_BYTES = 8
LIGHT_BYTES = 16
MATERIAL_BYTES = 16
TRIANGLE_BYTES = 40
BVH_NODE_BYTES = 32
POS_FRAC = 16
CAM_FRAC = 28


# ----------------------------------------------------------------------------- fixed point

def to_fixed(v, frac=POS_FRAC):
    """Round half away from zero, like Math.Round(x, MidpointRounding.AwayFromZero) in C#."""
    a = v * (1 << frac)
    fl = math.floor(abs(a))
    if abs(a) - fl >= 0.5:
        fl += 1
    r = -fl if a < 0 else fl
    if r > 2**31 - 1 or r < -2**31:
        raise OverflowError("value %r does not fit Q%d.%d" % (v, 32 - frac, frac))
    return int(r)


def to_float(i, frac=POS_FRAC):
    return i / float(1 << frac)


def fx3(x, y, z, frac=POS_FRAC):
    return (to_fixed(x, frac), to_fixed(y, frac), to_fixed(z, frac))


# ----------------------------------------------------------------------------- scene model
# triangle  = (v0, v1, v2, material_id, flags)           v = (x, y, z) ints in Q16.16
# bvh node  = (min_xyz, max_xyz, offset, count)          ints
# light     = (pos_xyz, r, g, b, intensity_q4_4)
# material  = 16 raw bytes
# camera    = (origin, dir00, dirDX, dirDY)              each (x, y, z) ints
# sky       = (hr, hg, hb, zr, zg, zb)

class Scene:
    def __init__(self):
        self.width = 320
        self.height = 240
        self.max_bounces = 3
        self.camera = ((0, 0, 0),) * 4
        self.sky = (0, 0, 0, 0, 0, 0)
        self.lights = []
        self.materials = []
        self.triangles = []
        self.bvh = []


def is_degenerate(t):
    v0, v1, v2 = t[0], t[1], t[2]
    a = [v1[i] - v0[i] for i in range(3)]
    b = [v2[i] - v0[i] for i in range(3)]
    c = (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
    return c == (0, 0, 0)


def build_bvh(tris, leaf_size=4):
    """Exact mirror of BVHBuilder.cs: median split on the widest centroid axis, ties broken by index."""
    n = len(tris)
    if n == 0:
        return [], []
    leaf_size = max(1, min(255, leaf_size))
    tmin = [tuple(min(t[k][a] for k in range(3)) for a in range(3)) for t in tris]
    tmax = [tuple(max(t[k][a] for k in range(3)) for a in range(3)) for t in tris]
    c3 = [[t[0][a] + t[1][a] + t[2][a] for t in tris] for a in range(3)]
    idx = list(range(n))
    nodes = [None]

    def build_node(node_index, start, count):
        sel = idx[start:start + count]
        bmin = tuple(min(tmin[i][a] for i in sel) for a in range(3))
        bmax = tuple(max(tmax[i][a] for i in sel) for a in range(3))
        cmin = [min(c3[a][i] for i in sel) for a in range(3)]
        cmax = [max(c3[a][i] for i in sel) for a in range(3)]

        if count <= leaf_size:
            nodes[node_index] = (bmin, bmax, start, count)
            return

        axis, best = 0, cmax[0] - cmin[0]
        for a in (1, 2):
            ext = cmax[a] - cmin[a]
            if ext > best:
                best, axis = ext, a
        if best > 0:
            key = c3[axis]
            idx[start:start + count] = sorted(sel, key=lambda i: (key[i], i))

        mid = start + count // 2
        left = len(nodes)
        nodes.append(None)
        nodes.append(None)
        nodes[node_index] = (bmin, bmax, left, 0)
        build_node(left, start, mid - start)
        build_node(left + 1, mid, start + count - mid)

    build_node(0, 0, n)
    return [tris[i] for i in idx], nodes


# ----------------------------------------------------------------------------- serialisation

def section_layout(n_lights, n_materials, n_tris, n_nodes):
    """Returns [(name, offset, size)] for every section after the header, plus the CRC."""
    out, off = [("header", 0, HEADER_BYTES)], HEADER_BYTES
    for name, size in (("camera", CAMERA_BYTES), ("sky", SKY_BYTES),
                       ("lights", LIGHT_BYTES * n_lights),
                       ("materials", MATERIAL_BYTES * n_materials),
                       ("triangles", TRIANGLE_BYTES * n_tris),
                       ("bvh", BVH_NODE_BYTES * n_nodes)):
        out.append((name, off, size))
        off += size
    out.append(("crc32", off, 4))
    return out


def pack_scene(s):
    out = bytearray()
    out += struct.pack("<4sHHHHBBBBIHHII", MAGIC, VERSION, HEADER_BYTES, s.width, s.height,
                       POS_FRAC, CAM_FRAC, s.max_bounces, 0,
                       len(s.triangles), len(s.materials), len(s.lights), len(s.bvh), 0)
    out += struct.pack("<12i", *[c for vec in s.camera for c in vec])
    out += struct.pack("<8B", s.sky[0], s.sky[1], s.sky[2], 0, s.sky[3], s.sky[4], s.sky[5], 0)
    for (pos, r, g, b, inten) in s.lights:
        out += struct.pack("<3i4B", pos[0], pos[1], pos[2], r, g, b, inten)
    for m in s.materials:
        assert len(m) == MATERIAL_BYTES
        out += bytes(m)
    for (v0, v1, v2, mat, flags) in s.triangles:
        out += struct.pack("<9iHH", *v0, *v1, *v2, mat, flags)
    for (bmin, bmax, offset, count) in s.bvh:
        out += struct.pack("<6iIHH", *bmin, *bmax, offset, count, 0)
    out += struct.pack("<I", zlib.crc32(bytes(out)) & 0xFFFFFFFF)
    return bytes(out)


class FormatError(Exception):
    pass


def parse_scene(data):
    """Parses and checks a .rtscene. Raises FormatError on any structural problem or CRC mismatch."""
    if len(data) < HEADER_BYTES + 4:
        raise FormatError("file too short (%d bytes)" % len(data))
    (magic, ver, hdr, w, h, pfrac, cfrac, bounces, _res,
     n_tri, n_mat, n_light, n_node, _flags) = struct.unpack_from("<4sHHHHBBBBIHHII", data, 0)
    if magic != MAGIC:
        raise FormatError("bad magic %r" % magic)
    if ver != VERSION:
        raise FormatError("unsupported version %d" % ver)
    if hdr != HEADER_BYTES:
        raise FormatError("unexpected header size %d" % hdr)
    if pfrac != POS_FRAC or cfrac != CAM_FRAC:
        raise FormatError("unexpected fraction bits %d/%d" % (pfrac, cfrac))

    layout = section_layout(n_light, n_mat, n_tri, n_node)
    expected = layout[-1][1] + 4
    if len(data) != expected:
        raise FormatError("size mismatch: file has %d bytes, header implies %d" % (len(data), expected))
    stored = struct.unpack_from("<I", data, expected - 4)[0]
    actual = zlib.crc32(data[:expected - 4]) & 0xFFFFFFFF
    if stored != actual:
        raise FormatError("CRC mismatch: stored %08x, computed %08x" % (stored, actual))

    offs = {name: off for name, off, _ in layout}
    s = Scene()
    s.width, s.height, s.max_bounces = w, h, bounces
    cam = struct.unpack_from("<12i", data, offs["camera"])
    s.camera = tuple(tuple(cam[i:i + 3]) for i in (0, 3, 6, 9))
    sk = struct.unpack_from("<8B", data, offs["sky"])
    s.sky = (sk[0], sk[1], sk[2], sk[4], sk[5], sk[6])
    for i in range(n_light):
        f = struct.unpack_from("<3i4B", data, offs["lights"] + i * LIGHT_BYTES)
        s.lights.append((tuple(f[0:3]), f[3], f[4], f[5], f[6]))
    for i in range(n_mat):
        o = offs["materials"] + i * MATERIAL_BYTES
        s.materials.append(bytes(data[o:o + MATERIAL_BYTES]))
    for i in range(n_tri):
        f = struct.unpack_from("<9iHH", data, offs["triangles"] + i * TRIANGLE_BYTES)
        s.triangles.append((tuple(f[0:3]), tuple(f[3:6]), tuple(f[6:9]), f[9], f[10]))
    for i in range(n_node):
        f = struct.unpack_from("<6iIHH", data, offs["bvh"] + i * BVH_NODE_BYTES)
        s.bvh.append((tuple(f[0:3]), tuple(f[3:6]), f[6], f[7]))
    return s


# ----------------------------------------------------------------------------- validation

def validate_bvh(tris, nodes):
    """Returns a list of problems (empty list = valid). Mirrors BVHBuilder.Validate."""
    if not tris:
        return [] if not nodes else ["non-empty BVH for an empty scene"]
    if not nodes:
        return ["empty BVH for a non-empty scene"]
    problems, covered, seen = [], [0] * len(tris), set()

    def contains(outer, mn, mx):
        return all(outer[0][a] <= mn[a] and outer[1][a] >= mx[a] for a in range(3))

    stack = [0]
    while stack:
        n = stack.pop()
        if not 0 <= n < len(nodes):
            problems.append("node index out of range: %d" % n)
            continue
        if n in seen:
            problems.append("node %d visited twice" % n)
            continue
        seen.add(n)
        node = nodes[n]
        if node[3] > 0:
            if node[2] + node[3] > len(tris):
                problems.append("leaf %d reads past the triangle array" % n)
                continue
            for t in range(node[2], node[2] + node[3]):
                covered[t] += 1
                tmin = tuple(min(tris[t][k][a] for k in range(3)) for a in range(3))
                tmax = tuple(max(tris[t][k][a] for k in range(3)) for a in range(3))
                if not contains(node, tmin, tmax):
                    problems.append("leaf %d box does not contain triangle %d" % (n, t))
        else:
            l, r = node[2], node[2] + 1
            if r >= len(nodes):
                problems.append("internal node %d has children out of range" % n)
                continue
            for c in (l, r):
                if not contains(node, nodes[c][0], nodes[c][1]):
                    problems.append("internal node %d does not contain child %d" % (n, c))
            stack += [r, l]
    problems += ["triangle %d referenced %d times" % (i, c) for i, c in enumerate(covered) if c != 1]
    return problems


# ----------------------------------------------------------------------------- reference traversal (CPU)

def _sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def _dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def _cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def ray_triangle(o, d, a, b, c):
    """Moller-Trumbore, double sided. Returns t > 0 or None."""
    e1, e2 = _sub(b, a), _sub(c, a)
    p = _cross(d, e2)
    det = _dot(e1, p)
    if abs(det) < 1e-12:
        return None
    inv = 1.0 / det
    tv = _sub(o, a)
    u = _dot(tv, p) * inv
    if u < 0.0 or u > 1.0:
        return None
    q = _cross(tv, e1)
    v = _dot(d, q) * inv
    if v < 0.0 or u + v > 1.0:
        return None
    t = _dot(e2, q) * inv
    return t if t > 1e-9 else None


def ray_box(o, d, bmin, bmax, t_limit, eps=1e-6):
    t0, t1 = 0.0, t_limit
    for a in range(3):
        lo, hi = bmin[a] - eps, bmax[a] + eps
        if d[a] == 0.0:
            if o[a] < lo or o[a] > hi:
                return False
        else:
            ta, tb = (lo - o[a]) / d[a], (hi - o[a]) / d[a]
            if ta > tb:
                ta, tb = tb, ta
            t0, t1 = max(t0, ta), min(t1, tb)
            if t0 > t1:
                return False
    return True


def primary_ray(scene, x, y):
    """Ray for pixel (x, y) exactly as the RTL ray generator will compute it: dir = dir00 + x*dirDX + y*dirDY."""
    o = [to_float(c) for c in scene.camera[0]]
    d = [(scene.camera[1][k] + x * scene.camera[2][k] + y * scene.camera[3][k]) / float(1 << CAM_FRAC) for k in range(3)]
    return o, d


class FloatMesh:
    def __init__(self, scene):
        self.tris = [tuple(tuple(to_float(c) for c in v) for v in t[:3]) for t in scene.triangles]
        self.nodes = [(tuple(to_float(c) for c in n[0]), tuple(to_float(c) for c in n[1]), n[2], n[3]) for n in scene.bvh]


def closest_brute(mesh, o, d):
    best = None
    for t in mesh.tris:
        h = ray_triangle(o, d, *t)
        if h is not None and (best is None or h < best):
            best = h
    return best


def closest_bvh(mesh, o, d):
    if not mesh.nodes:
        return None
    best, stack = None, [0]
    while stack:
        n = mesh.nodes[stack.pop()]
        if not ray_box(o, d, n[0], n[1], best if best is not None else float("inf")):
            continue
        if n[3] > 0:
            for i in range(n[2], n[2] + n[3]):
                h = ray_triangle(o, d, *mesh.tris[i])
                if h is not None and (best is None or h < best):
                    best = h
        else:
            stack += [n[2] + 1, n[2]]
    return best


def traversal_check(scene, n_rays):
    """Shoots n_rays deterministic primary rays; BVH traversal must find the same closest t as brute force."""
    mesh = FloatMesh(scene)
    hits, bad = 0, []
    for i in range(n_rays):
        x, y = (i * 37) % scene.width, (i * 91) % scene.height
        o, d = primary_ray(scene, x, y)
        tb, tv = closest_brute(mesh, o, d), closest_bvh(mesh, o, d)
        if (tb is None) != (tv is None) or (tb is not None and abs(tb - tv) > 1e-9):
            bad.append((x, y, tb, tv))
        elif tb is not None:
            hits += 1
    return hits, bad


# ----------------------------------------------------------------------------- reference scene (mirror of RTSelfTest.cs)

def make_reference_scene():
    state = [12345]

    def nxt():
        state[0] = (state[0] * 1664525 + 1013904223) & 0xFFFFFFFF
        return state[0] >> 16

    s = Scene()
    s.width, s.height, s.max_bounces = 64, 48, 2

    # RTCameraMath.PackFromBasis
    origin, fwd, right, up, half_h = [0.0, 1.0, -6.0], [0.0, 0.0, 1.0], [1.0, 0.0, 0.0], [0.0, 1.0, 0.0], 0.5
    half_w = half_h * s.width / s.height
    du = 2.0 * half_w / s.width
    dv = 2.0 * half_h / s.height
    sx = -half_w + du * 0.5
    sy = half_h - dv * 0.5
    d00 = [fwd[k] + sx * right[k] + sy * up[k] for k in range(3)]
    dx = [du * right[k] for k in range(3)]
    dy = [-dv * up[k] for k in range(3)]
    s.camera = (fx3(*origin), fx3(*d00, CAM_FRAC), fx3(*dx, CAM_FRAC), fx3(*dy, CAM_FRAC))

    s.sky = (217, 230, 255, 89, 140, 242)

    def mat(ar, ag, ab, refl=0, transp=0, ior=0, smooth=0):
        return bytes([ar, ag, ab, 0, 0, 0, 0, 0, refl, transp, ior, smooth, 0, 0, 0, 0])

    s.materials = [mat(204, 204, 204),
                   mat(242, 242, 242, refl=230, ior=64, smooth=255),
                   mat(255, 255, 255, refl=26, transp=230, ior=96, smooth=255)]
    s.lights = [(fx3(0.0, 3.75, 0.0), 255, 255, 255, 16),
                (fx3(-3.0, 3.0, -2.0), 255, 242, 204, 8)]

    tris = []
    for i in range(100):
        x = ((nxt() % 129) - 64) / 16.0
        y = (nxt() % 65) / 16.0
        z = ((nxt() % 129) - 64) / 16.0
        ax = ((nxt() % 33) - 16) / 16.0
        ay = ((nxt() % 33) - 16) / 16.0
        az = ((nxt() % 33) - 16) / 16.0
        bx = ((nxt() % 33) - 16) / 16.0
        by = ((nxt() % 33) - 16) / 16.0
        bz = ((nxt() % 33) - 16) / 16.0
        t = (fx3(x, y, z), fx3(x + ax, y + ay, z + az), fx3(x + bx, y + by, z + bz), i % 3, 1)
        if is_degenerate(t):
            continue
        tris.append(t)
    s.triangles, s.bvh = build_bvh(tris, 4)
    return s


def first_difference(a, b, layout):
    for i in range(min(len(a), len(b))):
        if a[i] != b[i]:
            name = next((n for n, off, size in layout if off <= i < off + size), "?")
            return i, name
    return (min(len(a), len(b)), "length") if len(a) != len(b) else (None, None)


# ----------------------------------------------------------------------------- commands

def describe(s, n_rays=0):
    print("resolution   : %d x %d, max bounces %d" % (s.width, s.height, s.max_bounces))
    print("triangles    : %d" % len(s.triangles))
    print("materials    : %d" % len(s.materials))
    print("lights       : %d" % len(s.lights))
    print("bvh nodes    : %d" % len(s.bvh))
    leaves = [n for n in s.bvh if n[3] > 0]
    if leaves:
        print("bvh leaves   : %d (max %d tris/leaf, avg %.2f)" % (len(leaves), max(n[3] for n in leaves),
                                                                 sum(n[3] for n in leaves) / float(len(leaves))))
        root = s.bvh[0]
        print("scene bounds : (%.3f, %.3f, %.3f) .. (%.3f, %.3f, %.3f)" % (
            *[to_float(c) for c in root[0]], *[to_float(c) for c in root[1]]))
    print("camera origin: (%.3f, %.3f, %.3f)" % tuple(to_float(c) for c in s.camera[0]))
    for name, (x, y) in (("top-left", (0, 0)), ("centre", (s.width // 2, s.height // 2)),
                         ("bottom-right", (s.width - 1, s.height - 1))):
        _, d = primary_ray(s, x, y)
        ln = math.sqrt(_dot(d, d))
        print("ray %-12s: dir (%.4f, %.4f, %.4f)" % (name, d[0] / ln, d[1] / ln, d[2] / ln))
    mem = len(s.triangles) * TRIANGLE_BYTES + len(s.bvh) * BVH_NODE_BYTES + len(s.materials) * MATERIAL_BYTES
    print("scene memory : %d bytes (%.1f KiB)" % (mem, mem / 1024.0))


def cmd_info(path):
    s = parse_scene(open(path, "rb").read())
    print("CRC32 OK, format valid")
    describe(s)
    return 0


def cmd_check(path, n_rays):
    s = parse_scene(open(path, "rb").read())
    print("CRC32 OK, format valid")
    describe(s)
    problems = validate_bvh(s.triangles, s.bvh)
    if problems:
        print("BVH INVALID:")
        for p in problems[:20]:
            print("  -", p)
        return 1
    print("BVH structure OK")
    hits, bad = traversal_check(s, n_rays)
    print("traversal check: %d rays, %d hit something, %d mismatches vs brute force" % (n_rays, hits, len(bad)))
    for x, y, tb, tv in bad[:10]:
        print("  pixel (%d,%d): brute=%s bvh=%s" % (x, y, tb, tv))
    return 1 if bad else 0


def cmd_check_reference(path):
    mine = pack_scene(make_reference_scene())
    theirs = open(path, "rb").read()
    if mine == theirs:
        print("IDENTICAL: the C# exporter matches the documented format byte for byte (%d bytes)." % len(theirs))
        return 0
    ref = make_reference_scene()
    layout = section_layout(len(ref.lights), len(ref.materials), len(ref.triangles), len(ref.bvh))
    off, name = first_difference(mine, theirs, layout)
    print("DIFFERENT: first mismatch at byte %s, in section '%s'. (python %d bytes, file %d bytes)" % (off, name, len(mine), len(theirs)))
    return 1


def cmd_make_reference(out):
    open(out, "wb").write(pack_scene(make_reference_scene()))
    print("wrote", out)
    return 0


def _random_scene(seed, n_tris):
    import random
    rnd = random.Random(seed)
    s = Scene()
    s.width, s.height = 64, 48
    s.camera = (fx3(0, 1, -8), fx3(-0.6667, 0.4896, 1.0, CAM_FRAC), fx3(0.0208, 0, 0, CAM_FRAC), fx3(0, -0.0208, 0, CAM_FRAC))
    s.materials = [bytes(16)]
    tris = []
    for _ in range(n_tris):
        c = [rnd.uniform(-6, 6), rnd.uniform(0, 5), rnd.uniform(-3, 6)]
        v = [fx3(*[c[k] + rnd.uniform(-1.3, 1.3) for k in range(3)]) for _ in range(3)]
        t = (v[0], v[1], v[2], 0, 1)
        if not is_degenerate(t):
            tris.append(t)
    s.triangles, s.bvh = build_bvh(tris, 4)
    return s


def selftest():
    failures = []

    def expect(cond, label):
        print(("PASS  " if cond else "FAIL  ") + label)
        if not cond:
            failures.append(label)

    # 1. round trip + CRC
    ref = make_reference_scene()
    blob = pack_scene(ref)
    back = parse_scene(blob)
    expect(back.triangles == ref.triangles and back.bvh == ref.bvh and back.materials == ref.materials,
           "reference scene survives pack -> parse unchanged (%d tris, %d nodes, %d bytes)" % (len(ref.triangles), len(ref.bvh), len(blob)))
    expect(pack_scene(make_reference_scene()) == blob, "scene generation + BVH build is deterministic")

    # 2. corruption must be caught
    bad = bytearray(blob)
    bad[HEADER_BYTES + 60] ^= 0x01
    try:
        parse_scene(bytes(bad))
        expect(False, "flipped bit is detected by CRC")
    except FormatError:
        expect(True, "flipped bit is detected by CRC")
    try:
        parse_scene(blob[:-5])
        expect(False, "truncated file is rejected")
    except FormatError:
        expect(True, "truncated file is rejected")

    # 3. BVH validity and a deliberately broken tree
    expect(validate_bvh(back.triangles, back.bvh) == [], "reference BVH passes structural validation")
    broken = list(back.bvh)
    root = broken[0]
    broken[0] = ((root[0][0] + 1, root[0][1], root[0][2]), root[1], root[2], root[3])   # shrink root box -> must be caught
    expect(validate_bvh(back.triangles, broken) != [], "validator catches a root box that no longer contains its children")

    # 4. field offsets match the README table (independent of pack/parse helpers)
    expect(blob[:4] == b"RTS1" and struct.unpack_from("<H", blob, 8)[0] == 64 and struct.unpack_from("<H", blob, 10)[0] == 48,
           "header: magic at 0, width at 8, height at 10")
    expect(struct.unpack_from("<I", blob, 16)[0] == len(ref.triangles) and struct.unpack_from("<I", blob, 24)[0] == len(ref.bvh),
           "header: triangle count at 16, BVH node count at 24")
    tri0 = HEADER_BYTES + CAMERA_BYTES + SKY_BYTES + LIGHT_BYTES * 2 + MATERIAL_BYTES * 3
    expect(struct.unpack_from("<3i", blob, tri0)[0] == ref.triangles[0][0][0], "first triangle's v0.x is at the documented offset")

    # 5. camera: centre ray points roughly along +z, corners spread as expected
    _, dc = primary_ray(ref, ref.width // 2, ref.height // 2)
    _, d0 = primary_ray(ref, 0, 0)
    expect(abs(dc[2] - 1.0) < 1e-6 and abs(dc[0]) < 0.02 and abs(dc[1]) < 0.02, "centre ray looks along +z")
    expect(d0[0] < 0 < d0[1], "top-left ray points left and up")

    # 6. BVH traversal == brute force on the reference scene and on random scenes
    hits, mism = traversal_check(back, 400)
    expect(not mism and hits > 0, "reference scene: BVH traversal matches brute force (%d/400 rays hit)" % hits)
    for seed, n in ((1, 50), (2, 300), (3, 1)):
        sc = parse_scene(pack_scene(_random_scene(seed, n)))
        hits, mism = traversal_check(sc, 300)
        expect(not mism and validate_bvh(sc.triangles, sc.bvh) == [], "random scene seed=%d (%d tris): valid BVH, traversal matches (%d hits)" % (seed, n, hits))

    # 7. empty scene is representable
    empty = Scene()
    expect(parse_scene(pack_scene(empty)).bvh == [], "empty scene packs and parses")

    # 8. fixed-point helpers
    expect(to_fixed(0.5) == 32768 and to_fixed(-0.5) == -32768 and to_fixed(1 / 65536.0 / 2) == 1,
           "to_fixed: Q16.16 values and half-away-from-zero rounding")
    try:
        to_fixed(40000.0)
        expect(False, "out-of-range coordinate raises")
    except OverflowError:
        expect(True, "out-of-range coordinate raises")

    print()
    print("%d check(s) failed" % len(failures) if failures else "All checks passed")
    return 1 if failures else 0


def main(argv):
    if len(argv) < 2 or argv[1] in ("-h", "--help"):
        print(__doc__)
        return 0
    cmd = argv[1]
    try:
        if cmd == "info" and len(argv) == 3:
            return cmd_info(argv[2])
        if cmd == "check" and len(argv) >= 3:
            n = int(argv[argv.index("--rays") + 1]) if "--rays" in argv else 300
            return cmd_check(argv[2], n)
        if cmd == "check-reference" and len(argv) == 3:
            return cmd_check_reference(argv[2])
        if cmd == "make-reference" and len(argv) == 3:
            return cmd_make_reference(argv[2])
        if cmd == "selftest":
            return selftest()
    except FormatError as e:
        print("FORMAT ERROR:", e)
        return 2
    print(__doc__)
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
