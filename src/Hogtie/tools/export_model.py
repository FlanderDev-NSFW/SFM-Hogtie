"""Export the hogtie device (Hogtie.blend, object "hogtie-device") into the mod's runtime model (assets/hogtie.htm).

Usage (Blender 5.x, headless):
    blender -b --disable-autoexec Hogtie.blend --python src/Hogtie/tools/export_model.py -- <absolute path>/src/Hogtie/assets/hogtie.htm

The device is one mesh of 24 loose parts. They are sorted into groups the mod moves separately: the base (floor plate, post
and the pins of its height adjuster), the bar (bar, neck holder, the small upright rod), the collar, the upper-arm and wrist
cuffs with their mounts on the bar, the ankle bar and the ankle cuffs. Model coordinates are Blender's: x = sideways
(0 = middle), y = along the device (the collar at -y, the post at +y), z = up from the floor; metres.

Every cuff has a size shape key ("Neck", "Upper Arm", "Lower Arm", "Ankle"). It scales the ring about the point where it is
held (the anchor): centre(v) = anchor + (centre - anchor) * (1 + v), radius(v) = radius * (1 + v). The key's deltas are
exported per vertex, so the runtime can set any size: position = basis + v * delta.

Format (little endian): "HGTD" u32 version=1
  collar:       f32x3 centre, f32x3 anchor, f32x3 axis (towards the head), f32 innerRadius, f32 width
  2 x upper cuff, 2 x wrist cuff, 2 x ankle cuff (side 0 = x < 0, 1 = x > 0), each:
                f32x3 centre, f32x3 anchor, f32x3 axis, f32 innerRadius, f32 width
                (arm cuffs: axis +y along the bar; ankle cuffs: axis from the knee up towards the foot)
  bar:          f32 centreZ, f32 radius, f32 frontY, f32 backY (its straight part)
  post:         f32 y, f32 radius, f32 top
  ankle bar:    f32 centreZ
  plate:        f32 halfWidth, f32 frontY, f32 backY
  u32 partCount, per part: u8 group, u8 side (0, 1, 2 = middle), u8 hasKey, u32 vertexCount, f32x3 positions[],
      f32x3 normals[], (hasKey) f32x3 keyDeltas[], u32 indexCount, u32 indices[]
  groups: 0 base, 1 bar, 2 collar, 3 upper cuff, 4 wrist cuff, 5 upper mount, 6 wrist mount, 7 ankle bar, 8 ankle cuff
"""
import struct
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

out_path = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "hogtie.htm"
obj = bpy.data.objects["hogtie-device"]
me = obj.data
keys = me.shape_keys.key_blocks
if obj.modifiers:
    raise SystemExit("unexpected modifiers on hogtie-device")
mw = obj.matrix_world
if any(abs(mw[i][j] - (1 if i == j else 0)) > 1e-6 for i in range(4) for j in range(4)):
    raise SystemExit("hogtie-device is not at the origin, unrotated and unscaled")

basis = [v.co.copy() for v in me.vertices]

# --- loose parts ----------------------------------------------------------------------------------
bm = bmesh.new()
bm.from_mesh(me)
bm.verts.ensure_lookup_table()
comp_of = [-1] * len(bm.verts)
comps = []
for v in bm.verts:
    if comp_of[v.index] >= 0:
        continue
    stack, members = [v], []
    comp_of[v.index] = len(comps)
    while stack:
        x = stack.pop()
        members.append(x.index)
        for e in x.link_edges:
            y = e.other_vert(x)
            if comp_of[y.index] < 0:
                comp_of[y.index] = len(comps)
                stack.append(y)
    comps.append(members)
bm.free()


def bbox(idx):
    pts = [basis[i] for i in idx]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


KEY_GROUP = {"Neck": 2, "Upper Arm": 3, "Lower Arm": 4, "Ankle": 8}
info = []  # per loose part: (group, side, key name or None)
for idx in comps:
    lo, hi = bbox(idx)
    size, mid = hi - lo, (lo + hi) / 2
    key = None
    for k in keys[1:]:
        if max((k.data[i].co - basis[i]).length for i in idx) > 1e-5:
            key = k.name
    side = 2 if abs(mid.x) < 0.01 else (0 if mid.x < 0 else 1)
    if key:
        group = KEY_GROUP[key]
    elif size.x > 0.5 or size.z > 0.5:
        group = 0                                   # floor plate, post
    elif hi.x < 0 and abs(mid.y - 0.145) < 0.02:
        group, side = 0, 2                          # pins of the post's height adjuster
    elif size.y > 0.3 or (size.y > 0.1 and mid.y < -0.3) or (size.z > 0.1 and size.x < 0.03):
        group = 1                                   # bar, neck holder, upright rod
    elif size.z < 0.01 and abs(mid.y - 0.077) < 0.01:
        group = 6                                   # the wrist cuffs' mount
    elif size.z < 0.01 and abs(mid.y + 0.134) < 0.01:
        group = 5                                   # the upper cuffs' mount
    elif size.x > 0.15 and size.y < 0.05:
        group = 7                                   # ankle bar
    else:
        raise SystemExit("unclassified part at %s size %s" % (tuple(mid), tuple(size)))
    info.append((group, side, key))
    print("HTM-CHECK part %2d: %4d verts, group %d side %d key %s, centre (%.3f, %.3f, %.3f)" % (len(info) - 1, len(idx), group, side, key, *mid))

counts = {}
for g, s, k in info:
    counts[(g, s)] = counts.get((g, s), 0) + 1
expected = {(2, 2): 1, (3, 0): 1, (3, 1): 1, (4, 0): 1, (4, 1): 1, (8, 0): 1, (8, 1): 1, (5, 2): 1, (6, 2): 1, (7, 2): 1}
for k, n in expected.items():
    if counts.get(k) != n:
        raise SystemExit("expected %d part(s) of group/side %s, found %s" % (n, k, counts.get(k)))


# --- cuff anchors ---------------------------------------------------------------------------------
def ring(idx, key):
    """Centre, anchor (the fixed point of the size key), axis, inner radius and width of a cuff ring."""
    pts = np.array([[basis[i].x, basis[i].y, basis[i].z] for i in idx])
    c = pts.mean(axis=0)
    _, vecs = np.linalg.eigh(np.cov((pts - c).T))
    axis = vecs[:, 0]
    d = pts - c
    along = d @ axis
    r = np.linalg.norm(d - np.outer(along, axis), axis=1).min()
    big = np.array([[keys[key].data[i].co[k] for k in range(3)] for i in idx])
    cb = big.mean(axis=0)
    rb = np.linalg.norm((big - cb) - np.outer((big - cb) @ axis, axis), axis=1).min()
    if abs(rb / r - 2) > 0.01:
        raise SystemExit("size key %s does not double the ring (%.4f -> %.4f)" % (key, r, rb))
    anchor = 2 * c - cb  # centre(1) = anchor + 2 (centre - anchor)
    return Vector(c), Vector(anchor), Vector(axis), float(r), float(along.max() - along.min())


def find(group, side):
    for i, (g, s, k) in enumerate(info):
        if g == group and s == side:
            return comps[i], k
    raise SystemExit("missing group %d side %d" % (group, side))


collar = list(ring(*find(2, 2)))
if collar[2].y > 0:
    collar[2] = -collar[2]                          # towards the head
if collar[2].z <= 0:
    raise SystemExit("the collar's axis should point up towards the head")
cuffs = []
for group in (3, 4, 8):
    for side in (0, 1):
        c, a, ax, r, w = ring(*find(group, side))
        if group in (3, 4):
            if abs(ax.y) < 0.99:
                raise SystemExit("arm cuff %d/%d is not along the bar" % (group, side))
            ax = Vector((0, 1, 0))
        elif ax.z < 0:
            ax = -ax                                # ankle cuffs: from the knee (behind, low) up towards the foot
        cuffs.append((c, a, ax, r, w))

bar_idx = max((comps[i] for i, (g, s, k) in enumerate(info) if g == 1), key=lambda idx: bbox(idx)[1].y - bbox(idx)[0].y)
lo, hi = bbox(bar_idx)
bar = ((lo.z + hi.z) / 2, (hi.z - lo.z) / 2, lo.y, hi.y)
post_idx = [comps[i] for i, (g, s, k) in enumerate(info) if g == 0 and bbox(comps[i])[1].z - bbox(comps[i])[0].z > 0.5][0]
lo, hi = bbox(post_idx)
post = ((lo.y + hi.y) / 2, (hi.x - lo.x) / 2, hi.z)
lo, hi = bbox(find(7, 2)[0])
ankle_bar_z = (lo.z + hi.z) / 2
plate_idx = [comps[i] for i, (g, s, k) in enumerate(info) if g == 0 and bbox(comps[i])[1].x - bbox(comps[i])[0].x > 0.5][0]
lo, hi = bbox(plate_idx)
plate = (hi.x, lo.y, hi.y)
if abs(lo.z) > 1e-3:
    raise SystemExit("the plate is not on the floor (z = %.4f)" % lo.z)


def t(v, n=4):
    return tuple(round(x, n) for x in v)


print("HTM-CHECK collar centre %s anchor %s axis %s r %.4f width %.4f" % (t(collar[0]), t(collar[1]), t(collar[2], 3), collar[3], collar[4]))
for n, (c, a, ax, r, w) in zip(["upper 0", "upper 1", "wrist 0", "wrist 1", "ankle 0", "ankle 1"], cuffs):
    print("HTM-CHECK %s centre %s anchor %s axis %s r %.4f width %.4f" % (n, t(c), t(a), t(ax, 3), r, w))
print("HTM-CHECK bar z %.4f r %.4f y %.4f..%.4f; post y %.4f r %.4f top %.4f; ankle bar z %.4f; plate half %.4f y %.4f..%.4f"
      % (*bar, *post, ankle_bar_z, *plate))

# --- parts with split normals ---------------------------------------------------------------------
me.calc_loop_triangles()
normals = me.corner_normals
parts = {}  # (group, side) -> (positions, normals, deltas, indices, index_of, key)
for tri in me.loop_triangles:
    group, side, key = info[comp_of[tri.vertices[0]]]
    if (group, side) not in parts:
        parts[(group, side)] = ([], [], [], [], {}, key)
    pos, nrm, dlt, idx, index_of, _ = parts[(group, side)]
    for li, v in zip(tri.loops, tri.vertices):
        n = normals[li].vector.normalized()
        k = (v, round(n.x, 4), round(n.y, 4), round(n.z, 4))
        if k not in index_of:
            index_of[k] = len(pos)
            pos.append(basis[v])
            nrm.append(n)
            dlt.append(keys[key].data[v].co - basis[v] if key else Vector())
        idx.append(index_of[k])


def v3(f, v):
    f.write(struct.pack("<3f", v[0], v[1], v[2]))


with open(out_path, "wb") as f:
    f.write(b"HGTD")
    f.write(struct.pack("<I", 1))
    for c, a, ax, r, w in [collar] + cuffs:
        v3(f, c)
        v3(f, a)
        v3(f, ax)
        f.write(struct.pack("<2f", r, w))
    f.write(struct.pack("<4f", *bar))
    f.write(struct.pack("<3f", *post))
    f.write(struct.pack("<f", ankle_bar_z))
    f.write(struct.pack("<3f", *plate))
    f.write(struct.pack("<I", len(parts)))
    total = 0
    for (group, side), (pos, nrm, dlt, idx, _, key) in sorted(parts.items()):
        f.write(struct.pack("<3B", group, side, 1 if key else 0))
        f.write(struct.pack("<I", len(pos)))
        for p in pos:
            v3(f, p)
        for n in nrm:
            v3(f, n)
        if key:
            for d in dlt:
                v3(f, d)
        f.write(struct.pack("<I", len(idx)))
        f.write(struct.pack("<%dI" % len(idx), *idx))
        total += len(idx) // 3
        print("HTM-CHECK group %d side %d: %d vertices, %d triangles, key %s" % (group, side, len(pos), len(idx) // 3, key))
print("HTM-CHECK wrote %s: %d parts, %d triangles" % (out_path, len(parts), total))
