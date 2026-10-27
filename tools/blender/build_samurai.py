"""
Генератор героя «Путь клинка» для Blender 3.6+/4.x (запуск: blender -b --python build_samurai.py).
Строит ЦЕЛЬНУЮ стилизованную модель самурая (тело через Skin-модификатор, поверх — слоистая броня),
скелет с именами под Unity, веса, UV (проекция «кубом» в метрах → единая плотность текстуры),
анимации Idle / WalkF / Jump / Hit и экспортирует FBX + .blend.

Конвенции: Z вверх, персонаж смотрит в -Y (передний вид Blender), его ПРАВАЯ сторона = -X, ЛЕВАЯ = +X.
Катаны в модели нет: в игре клинок отдельный объект, им управляет WeaponController. Есть только ножны на поясе.
Руки в риге в A-позе; в игре их поведение задаёт IK к рукояти катаны, поэтому в клипах руки не анимируются.
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Quaternion, Euler

OUT_DIR = os.environ.get('HERO_OUT', '/tmp/hero')
os.makedirs(OUT_DIR, exist_ok=True)
FBX_PATH = os.path.join(OUT_DIR, 'Samurai_Hero.fbx')
BLEND_PATH = os.path.join(OUT_DIR, 'Samurai_Hero.blend')

# ------------------------------------------------------------------ сцена, материалы
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.fps = 30

def mat(name, color, metallic=0.0, rough=0.6):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    bs = m.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value = (*color, 1)
    bs.inputs['Metallic'].default_value = metallic
    bs.inputs['Roughness'].default_value = rough
    return m

M = {
    'skin': mat('Skin', (0.55, 0.38, 0.30)),
    'black': mat('Armor_Black', (0.045, 0.045, 0.055), 0.5, 0.45),
    'red': mat('Lacquer_Red', (0.30, 0.03, 0.04), 0.3, 0.4),
    'brass': mat('Brass', (0.62, 0.42, 0.12), 0.8, 0.4),
    'cloth': mat('Dark_Cloth', (0.07, 0.06, 0.07)),
    'hair': mat('Beard', (0.03, 0.025, 0.02)),
}

# ------------------------------------------------------------------ геометрические помощники (bmesh)
def new_bm(): return bmesh.new()

def uv_box(bm, scale):
    """UV проекцией «кубом» в метрах: scale метров = 1 единица UV (одинаковая плотность текстуры на всех деталях)."""
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            c = l.vert.co
            if ax == 0: u, v = c.y, c.z
            elif ax == 1: u, v = c.x, c.z
            else: u, v = c.x, c.y
            l[uv].uv = (u / scale, v / scale)

def to_obj(name, bm, material, smooth=False, uv_scale=0.4, bone=None):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.normal_update()
    if smooth:
        for f in bm.faces: f.smooth = True
    if uv_scale: uv_box(bm, uv_scale)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free(); me.update()
    o = bpy.data.objects.new(name, me)
    sc.collection.objects.link(o)
    o.data.materials.append(material)
    if bone:
        vg = o.vertex_groups.new(name=bone)
        vg.add(list(range(len(me.vertices))), 1.0, 'REPLACE')
    return o

def xform(bm, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    m = Matrix.Translation(loc) @ Euler([math.radians(a) for a in rot], 'XYZ').to_matrix().to_4x4() @ \
        Matrix.Diagonal((*scale, 1.0))
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)

def box(name, center, size, mat_, bone, rot=(0, 0, 0), bevel=0.006, uv_scale=0.3):
    bm = new_bm()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts: v.co.x *= size[0]; v.co.y *= size[1]; v.co.z *= size[2]
    if bevel:
        bmesh.ops.bevel(bm, geom=bm.edges[:], offset=bevel, segments=2, affect='EDGES')
    xform(bm, center, rot)
    return to_obj(name, bm, mat_, smooth=False, uv_scale=uv_scale, bone=bone)

def ellipsoid(name, center, radii, mat_, bone, rot=(0, 0, 0), u=16, v=10, smooth=True, uv_scale=0.3, cut_z=None):
    bm = new_bm()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=1.0)
    xform(bm, (0, 0, 0), (0, 0, 0), radii)
    xform(bm, center, rot)
    if cut_z is not None:      # срезать всё ниже плоскости z=cut_z (мировая)
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, cut_z), plane_no=(0, 0, 1), clear_inner=True)
    return to_obj(name, bm, mat_, smooth=smooth, uv_scale=uv_scale, bone=bone)

def frustum(name, p1, p2, r1, r2, mat_, bone, segs=12, rx_scale=1.0, uv_scale=0.3, smooth=True, caps=True):
    """Усечённый конус между двумя точками (радиус r1 у p1 и r2 у p2); rx_scale растягивает сечение по поперечной оси."""
    p1, p2 = Vector(p1), Vector(p2)
    axis = (p2 - p1)
    length = axis.length
    z = axis.normalized()
    ref = Vector((1, 0, 0)) if abs(z.x) < 0.9 else Vector((0, 1, 0))
    x = z.cross(ref).normalized(); y = z.cross(x).normalized()
    bm = new_bm()
    rings = []
    for pt, r in ((p1, r1), (p2, r2)):
        ring = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            ring.append(bm.verts.new(pt + x * (math.cos(a) * r * rx_scale) + y * (math.sin(a) * r)))
        rings.append(ring)
    for i in range(segs):
        j = (i + 1) % segs
        bm.faces.new((rings[0][i], rings[0][j], rings[1][j], rings[1][i]))
    if caps:
        bm.faces.new(rings[0][::-1]); bm.faces.new(rings[1])
    return to_obj(name, bm, mat_, smooth=smooth, uv_scale=uv_scale, bone=bone)

def ring(name, center, rx, ry, height, thick, mat_, bone, segs=28, a0=0.0, a1=360.0, taper=1.0, uv_scale=0.3, tilt=0.0):
    """Эллиптический броневой пояс. rx, ry — внутренние радиусы; thick — толщина стенки; a0..a1 — дуга (градусы, 0 = вперёд = -Y);
    taper — масштаб верхнего края относительно нижнего (расширение/сужение)."""
    bm = new_bm()
    full = (a1 - a0) >= 359.9
    n = segs if full else segs + 1
    cols = []
    for i in range(n):
        a = math.radians(a0 + (a1 - a0) * i / segs)
        dx, dy = math.sin(a), -math.cos(a)
        pts = []
        for kk, z in ((0.0, 0.0), (1.0, 0.0), (0.0, height), (1.0, height)):
            t = 1.0 if z == 0.0 else taper
            pts.append(bm.verts.new(((rx + thick * kk) * dx * t, (ry + thick * kk) * dy * t, z)))
        cols.append(pts)
    count = segs if full else n - 1
    for i in range(count):
        c0, c1 = cols[i], cols[(i + 1) % n]
        bm.faces.new((c0[1], c1[1], c1[3], c0[3]))   # наружная
        bm.faces.new((c1[0], c0[0], c0[2], c1[2]))   # внутренняя
        bm.faces.new((c0[2], c1[2], c1[3], c0[3]))   # верх
        bm.faces.new((c0[0], c0[1], c1[1], c1[0]))   # низ
    if not full:
        bm.faces.new((cols[0][0], cols[0][2], cols[0][3], cols[0][1]))
        bm.faces.new((cols[-1][1], cols[-1][3], cols[-1][2], cols[-1][0]))
    xform(bm, center, (tilt, 0, 0))
    return to_obj(name, bm, mat_, smooth=False, uv_scale=uv_scale, bone=bone)


# ------------------------------------------------------------------ скелет
bpy.ops.object.armature_add(enter_editmode=True, location=(0, 0, 0))
rig = bpy.context.object
rig.name = 'Samurai_Rig'
rig.data.name = 'Samurai_Armature'
for b in list(rig.data.edit_bones):
    rig.data.edit_bones.remove(b)

BONES = {}   # имя -> (head, tail)

def bone(name, head, tail, parent=None, deform=True):
    b = rig.data.edit_bones.new(name)
    b.head = head; b.tail = tail
    if parent: b.parent = rig.data.edit_bones[parent]
    b.use_deform = deform
    BONES[name] = (Vector(head), Vector(tail))

bone('Root', (0, 0, 0.05), (0, 0, 0.25), None, False)
bone('Hips', (0, 0, 0.93), (0, 0, 1.08), 'Root')
bone('Spine', (0, 0, 1.08), (0, 0, 1.28), 'Hips')
bone('Chest', (0, 0, 1.28), (0, 0, 1.52), 'Spine')
bone('Neck', (0, 0, 1.52), (0, 0, 1.60), 'Chest')
bone('Head', (0, 0, 1.60), (0, 0, 1.82), 'Neck')
for side, s in (('L', 1), ('R', -1)):
    bone('Shoulder.' + side, (s * 0.05, 0, 1.48), (s * 0.19, 0, 1.50), 'Chest')
    bone('UpperArm.' + side, (s * 0.19, 0, 1.50), (s * 0.30, 0, 1.24), 'Shoulder.' + side)
    bone('Forearm.' + side, (s * 0.30, 0, 1.24), (s * 0.345, -0.04, 1.00), 'UpperArm.' + side)
    bone('Hand.' + side, (s * 0.345, -0.04, 1.00), (s * 0.355, -0.09, 0.90), 'Forearm.' + side)
    bone('UpperLeg.' + side, (s * 0.10, 0, 0.93), (s * 0.11, 0.01, 0.50), 'Hips')
    bone('LowerLeg.' + side, (s * 0.11, 0.01, 0.50), (s * 0.11, 0, 0.10), 'UpperLeg.' + side)
    bone('Foot.' + side, (s * 0.11, 0, 0.10), (s * 0.11, -0.17, 0.04), 'LowerLeg.' + side)
bpy.ops.object.mode_set(mode='OBJECT')

DEFORM = [n for n in BONES if n != 'Root']

def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length

def auto_weights(obj, allowed=None, power=3.0, max_inf=4):
    """Веса по расстоянию до костей (детерминированно, без bpy.ops): чем ближе кость, тем больше вес."""
    names = allowed or DEFORM
    groups = {n: obj.vertex_groups.new(name=n) for n in names}
    for v in obj.data.vertices:
        ds = sorted((seg_dist(v.co, *BONES[n]), n) for n in names)[:max_inf]
        ws = [(1.0 / (max(d, 0.012) ** power), n) for d, n in ds]
        tot = sum(w for w, _ in ws)
        for w, n in ws:
            if w / tot > 0.02:
                groups[n].add([v.index], w / tot, 'REPLACE')

# ------------------------------------------------------------------ тело: Skin-модификатор + сглаживание
def build_skin():
    verts, edges, radii = [], [], []
    def V(p, rx, ry):
        verts.append(Vector(p)); radii.append((rx, ry)); return len(verts) - 1
    pelvis = V((0, 0, 0.98), 0.165, 0.115)
    waist = V((0, 0, 1.14), 0.145, 0.105)
    chest = V((0, 0, 1.34), 0.17, 0.12)
    uchest = V((0, 0, 1.50), 0.13, 0.09)
    neck = V((0, 0, 1.58), 0.05, 0.05)
    edges += [(pelvis, waist), (waist, chest), (chest, uchest), (uchest, neck)]
    for s in (1, -1):
        sh = V((s * 0.17, 0, 1.49), 0.05, 0.05); edges.append((uchest, sh))
        el = V((s * 0.30, 0, 1.24), 0.034, 0.034)
        wr = V((s * 0.345, -0.04, 1.00), 0.028, 0.028)
        hd = V((s * 0.352, -0.075, 0.93), 0.03, 0.026)
        edges += [(sh, el), (el, wr), (wr, hd)]
        hip = V((s * 0.10, 0, 0.93), 0.072, 0.072); edges.append((pelvis, hip))
        kn = V((s * 0.11, 0.01, 0.50), 0.052, 0.052)
        an = V((s * 0.11, 0, 0.10), 0.038, 0.038)
        to = V((s * 0.11, -0.15, 0.045), 0.034, 0.028)
        edges += [(hip, kn), (kn, an), (an, to)]
    me = bpy.data.meshes.new('SkinMesh')
    me.from_pydata([tuple(v) for v in verts], edges, [])
    o = bpy.data.objects.new('Samurai_Skin', me)
    sc.collection.objects.link(o)
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.mesh.customdata_skin_add()
    for i, (rx, ry) in enumerate(radii):
        me.skin_vertices[0].data[i].radius = (rx, ry)
    me.skin_vertices[0].data[pelvis].use_root = True
    sk = o.modifiers.new('Skin', 'SKIN'); sk.use_smooth_shade = True; sk.branch_smoothing = 0.6
    sub = o.modifiers.new('Sub', 'SUBSURF'); sub.levels = 1
    bpy.ops.object.modifier_apply(modifier='Skin')
    bpy.ops.object.modifier_apply(modifier='Sub')
    for p in o.data.polygons: p.use_smooth = True
    o.data.materials.append(M['skin'])
    auto_weights(o)
    o.select_set(False)
    return o

skin_obj = build_skin()
parts = [skin_obj]

def torso_r(z):
    """Полуоси сечения тела на высоте z (по радиусам Skin) — для посадки брони."""
    pts = [(0.98, 0.165, 0.115), (1.14, 0.145, 0.105), (1.34, 0.17, 0.12), (1.50, 0.13, 0.09)]
    z = max(pts[0][0], min(pts[-1][0], z))
    for (z0, a0, b0), (z1, a1, b1) in zip(pts, pts[1:]):
        if z <= z1:
            t = (z - z0) / (z1 - z0)
            return a0 + (a1 - a0) * t, b0 + (b1 - b0) * t
    return pts[-1][1], pts[-1][2]

# ------------------------------------------------------------------ торс: подкладка и лента пластин
rx, ry = torso_r(1.06)
under = ring('Torso_Under', (0, 0, 1.06), rx * 0.99, ry * 0.99, 0.50, 0.016, M['cloth'], None, segs=32)
auto_weights(under, ['Hips', 'Spine', 'Chest'])
parts.append(under)

for i, z0 in enumerate([1.15, 1.21, 1.27, 1.33, 1.39, 1.45]):
    rx, ry = torso_r(z0 + 0.027)
    bone_n = 'Spine' if z0 < 1.28 else 'Chest'
    parts.append(ring('Do_Band%d' % i, (0, 0, z0), rx + 0.004, ry + 0.004, 0.050, 0.020, M['black'], bone_n, segs=32, taper=1.015))
    parts.append(ring('Do_Lace%d' % i, (0, 0, z0 + 0.049), rx + 0.007, ry + 0.007, 0.012, 0.014, M['red'], bone_n, segs=32))

ryc = torso_r(1.385)[1]
parts.append(box('Chest_Plate', (0, -(ryc + 0.056), 1.385), (0.27, 0.036, 0.26), M['black'], 'Chest', bevel=0.012))
for z in (1.50, 1.38, 1.26):
    parts.append(box('Chest_Cord', (0, -(ryc + 0.078), z), (0.27, 0.010, 0.014), M['red'], 'Chest', bevel=0.003))
for x in (-0.07, 0.07):
    parts.append(box('Chest_Cord_V', (x, -(ryc + 0.078), 1.38), (0.014, 0.010, 0.25), M['red'], 'Chest', bevel=0.003))
parts.append(box('Back_Plate', (0, ryc + 0.052, 1.385), (0.27, 0.030, 0.26), M['black'], 'Chest', bevel=0.012))
parts.append(box('Back_Cord', (0, ryc + 0.070, 1.385), (0.27, 0.010, 0.014), M['red'], 'Chest', bevel=0.003))
parts.append(frustum('Collar', (0, 0, 1.52), (0, 0, 1.61), 0.066, 0.054, M['cloth'], 'Neck', segs=14))

# пояс (оби), узел и концы
rx, ry = torso_r(1.04)
parts.append(ring('Obi', (0, 0, 1.02), rx + 0.004, ry + 0.004, 0.075, 0.016, M['red'], 'Hips', segs=32))
parts.append(box('Obi_Knot', (0, -(ry + 0.032), 1.055), (0.065, 0.032, 0.058), M['red'], 'Hips', bevel=0.008))
for x, rz in ((-0.028, 6), (0.028, -6)):
    parts.append(box('Obi_Tail', (x, -(ry + 0.036), 0.945), (0.032, 0.012, 0.17), M['red'], 'Hips', rot=(0, 0, rz), bevel=0.004))

# юбка из пластин (кусадзури)
for k in range(10):
    a = k * 36.0
    cx, cy = 0.195 * math.sin(math.radians(a)), -0.145 * math.cos(math.radians(a))
    parts.append(box('Kusazuri%d' % k, (cx, cy, 0.905), (0.108, 0.022, 0.21), M['black'], 'Hips', rot=(-10, 0, a), bevel=0.006))
    cx2, cy2 = 0.2 * math.sin(math.radians(a)), -0.15 * math.cos(math.radians(a))
    parts.append(box('KusazuriLace%d' % k, (cx2, cy2, 0.805), (0.108, 0.024, 0.024), M['red'], 'Hips', rot=(-10, 0, a), bevel=0.004))

# ножны на левом бедре (клинок в руке — отдельный объект игры)
sheath = frustum('Saya', (0.205, -0.09, 1.04), (0.235, 0.62, 0.78), 0.025, 0.020, M['black'], 'Hips', segs=8)
parts.append(sheath)
parts.append(ellipsoid('Saya_Tip', (0.235, 0.63, 0.775), (0.024, 0.03, 0.024), M['brass'], 'Hips'))
parts.append(frustum('Saya_Cord', (0.207, -0.07, 1.035), (0.212, 0.0, 1.02), 0.030, 0.030, M['red'], 'Hips', segs=8))

# ------------------------------------------------------------------ руки
for side, s in (('L', 1), ('R', -1)):
    parts.append(frustum('Sleeve.' + side, (s * 0.195, 0, 1.485), (s * 0.30, 0, 1.25), 0.058, 0.050, M['cloth'], 'UpperArm.' + side, segs=12))
    for i in range(4):
        parts.append(box('Sode%s%d' % (side, i), (s * (0.232 + 0.006 * i), 0.004, 1.545 - 0.052 * i), (0.165 - 0.010 * i, 0.13, 0.042),
                         M['black'], 'UpperArm.' + side, rot=(0, s * 14, 0), bevel=0.007))
        parts.append(box('SodeLace%s%d' % (side, i), (s * (0.258 + 0.006 * i), 0.004, 1.523 - 0.052 * i), (0.165 - 0.010 * i, 0.13, 0.010),
                         M['red'], 'UpperArm.' + side, rot=(0, s * 14, 0), bevel=0.002))
    parts.append(frustum('Kote.' + side, (s * 0.302, -0.002, 1.235), (s * 0.343, -0.038, 1.015), 0.054, 0.047, M['black'], 'Forearm.' + side, segs=12))
    for t in (0.25, 0.78):
        p = Vector((s * 0.302, -0.002, 1.235)).lerp(Vector((s * 0.343, -0.038, 1.015)), t)
        parts.append(frustum('KoteBand%s%d' % (side, int(t * 100)), p - Vector((0, 0, 0.012)), p + Vector((0, 0, 0.012)), 0.056 - 0.005 * t, 0.056 - 0.005 * t,
                             M['red'], 'Forearm.' + side, segs=12, caps=True))
    parts.append(ellipsoid('Glove.' + side, (s * 0.352, -0.078, 0.935), (0.043, 0.052, 0.060), M['black'], 'Hand.' + side))

# ------------------------------------------------------------------ ноги
for side, s in (('L', 1), ('R', -1)):
    parts.append(frustum('Haidate.' + side, (s * 0.10, 0, 0.95), (s * 0.11, 0.01, 0.52), 0.104, 0.080, M['cloth'], 'UpperLeg.' + side, segs=14))
    parts.append(ellipsoid('Knee.' + side, (s * 0.11, -0.040, 0.505), (0.062, 0.052, 0.078), M['black'], 'LowerLeg.' + side))
    parts.append(frustum('Shin.' + side, (s * 0.11, 0.0, 0.46), (s * 0.11, 0.0, 0.15), 0.070, 0.056, M['black'], 'LowerLeg.' + side, segs=12))
    for z in (0.42, 0.32, 0.22):
        parts.append(frustum('ShinBand%s%d' % (side, int(z * 100)), (s * 0.11, 0.0, z - 0.011), (s * 0.11, 0.0, z + 0.011), 0.066 - 0.04 * (0.46 - z) / 0.31 * 0.3 + 0.0, 0.066 - 0.04 * (0.46 - z) / 0.31 * 0.3 + 0.0,
                             M['red'], 'LowerLeg.' + side, segs=12))
    parts.append(box('Boot.' + side, (s * 0.11, -0.062, 0.047), (0.115, 0.28, 0.078), M['black'], 'Foot.' + side, bevel=0.018))
    parts.append(frustum('Cuff.' + side, (s * 0.11, 0, 0.17), (s * 0.11, 0, 0.11), 0.062, 0.060, M['cloth'], 'LowerLeg.' + side, segs=12))

# ------------------------------------------------------------------ голова, лицо, шлем
HZ = 1.675
parts.append(ellipsoid('Head', (0, 0.005, HZ), (0.097, 0.106, 0.120), M['skin'], 'Head', u=20, v=14))
parts.append(ellipsoid('Nose', (0, -0.108, HZ - 0.015), (0.015, 0.022, 0.024), M['skin'], 'Head', u=10, v=8))
for s in (1, -1):
    parts.append(box('Brow%d' % s, (s * 0.040, -0.097, HZ + 0.020), (0.040, 0.011, 0.010), M['hair'], 'Head', rot=(0, -s * 14, 0), bevel=0.002))
    parts.append(ellipsoid('Eye%d' % s, (s * 0.036, -0.096, HZ), (0.011, 0.007, 0.007), M['hair'], 'Head', u=8, v=6))
parts.append(ellipsoid('Beard', (0, -0.050, HZ - 0.092), (0.088, 0.074, 0.078), M['hair'], 'Head', u=16, v=10))
parts.append(ellipsoid('Moustache', (0, -0.100, HZ - 0.050), (0.044, 0.016, 0.013), M['hair'], 'Head', u=10, v=6))
parts.append(ellipsoid('HairBack', (0, 0.035, HZ + 0.020), (0.104, 0.100, 0.092), M['hair'], 'Head'))

parts.append(ellipsoid('Kabuto', (0, 0.012, HZ + 0.043), (0.130, 0.142, 0.108), M['black'], 'Head', u=20, v=12, cut_z=HZ + 0.020))
parts.append(ring('Kabuto_Brim', (0, 0.012, HZ + 0.010), 0.124, 0.138, 0.014, 0.040, M['black'], 'Head', segs=28))
parts.append(box('Mabisashi', (0, -0.172, HZ + 0.030), (0.20, 0.050, 0.012), M['black'], 'Head', rot=(-12, 0, 0), bevel=0.004))
parts.append(box('MabisashiTrim', (0, -0.196, HZ + 0.037), (0.20, 0.010, 0.012), M['brass'], 'Head', rot=(-12, 0, 0), bevel=0.003))
parts.append(box('KabutoRidge', (0, 0.012, HZ + 0.146), (0.014, 0.23, 0.012), M['brass'], 'Head', bevel=0.003))
for i in range(4):
    r = 1.0 + 0.09 * i
    parts.append(ring('Shikoro%d' % i, (0, 0.012, HZ - 0.020 - 0.036 * i), 0.128 * r, 0.142 * r, 0.036, 0.012, M['black'], 'Head', segs=20, a0=95, a1=265))
    parts.append(ring('ShikoroLace%d' % i, (0, 0.012, HZ - 0.020 - 0.036 * i - 0.004), 0.130 * r, 0.144 * r, 0.007, 0.014, M['red'], 'Head', segs=20, a0=95, a1=265))
for s in (1, -1):
    parts.append(frustum('Horn%d' % s, (s * 0.035, -0.118, HZ + 0.092), (s * 0.115, -0.105, HZ + 0.250), 0.020, 0.004, M['brass'], 'Head', segs=8))
parts.append(ellipsoid('Maedate', (0, -0.130, HZ + 0.072), (0.030, 0.010, 0.030), M['brass'], 'Head', u=12, v=8))

# ------------------------------------------------------------------ сборка: один меш, привязка к скелету
bpy.ops.object.select_all(action='DESELECT')
for o in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = skin_obj
bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
body.name = 'Samurai_Body'
body.data.name = 'Samurai_Body_Mesh'
mod = body.modifiers.new('Armature', 'ARMATURE')
mod.object = rig
body.parent = rig
print('ВЕРШИН:', len(body.data.vertices), 'ТРЕУГОЛЬНИКОВ≈', sum(len(p.vertices) - 2 for p in body.data.polygons),
      'МАТЕРИАЛОВ:', len(body.data.materials), 'ГРУПП ВЕСОВ:', len(body.vertex_groups))

marker = bpy.data.objects.new('FrontMarker', None)
marker.empty_display_type = 'ARROWS'
marker.location = (0, -0.5, 0.98)
sc.collection.objects.link(marker)
marker.parent = rig


# ------------------------------------------------------------------ анимации (руки не анимируем: в игре их ведёт IK к рукояти катаны)
rig.animation_data_create()
PB = rig.pose.bones
for pb in PB:
    pb.rotation_mode = 'QUATERNION'
ANIM_BONES = ['Hips', 'Spine', 'Chest', 'Neck', 'Head',
              'UpperLeg.L', 'LowerLeg.L', 'Foot.L', 'UpperLeg.R', 'LowerLeg.R', 'Foot.R']
AX = {'x': (1, 0, 0), 'y': (0, 1, 0), 'z': (0, 0, 1)}

def local_vec(pb, v):
    """Мировой вектор (в позе покоя) → в локальную систему кости: вращение/сдвиг в позе задаются в ней."""
    return pb.bone.matrix_local.to_3x3().inverted() @ Vector(v)

def key_pose(spec, frame):
    """spec: {кость: {'rx','ry','rz' в градусах вокруг МИРОВЫХ осей, 'dz' — подъём в метрах}}.
    Мировая ось X — поперёк персонажа: rx>0 наклоняет верхнюю часть тела ВПЕРЁД (-Y), а ногу (она смотрит вниз) уводит НАЗАД."""
    for name in ANIM_BONES:
        pb = PB[name]
        d = spec.get(name, {})
        q = Quaternion((1, 0, 0, 0))
        for k in ('rx', 'ry', 'rz'):
            if d.get(k):
                q = q @ Quaternion(local_vec(pb, AX[k[1]]).normalized(), math.radians(d[k]))
        pb.rotation_quaternion = q
        loc = (d.get('dx', 0.0), d.get('dy', 0.0), d.get('dz', 0.0))
        pb.location = local_vec(pb, loc) if any(loc) else Vector((0, 0, 0))
        pb.keyframe_insert('rotation_quaternion', frame=frame)
        pb.keyframe_insert('location', frame=frame)

def leg(spec, side, thigh_fwd, knee_flex):
    """Нога: бедро вперёд на thigh_fwd°, колено согнуто на knee_flex°, стопа остаётся горизонтальной."""
    spec['UpperLeg.' + side] = {'rx': -thigh_fwd}
    spec['LowerLeg.' + side] = {'rx': knee_flex}
    spec['Foot.' + side] = {'rx': thigh_fwd - knee_flex}

def new_action(name, frames, fn):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data.action = act
    for f in frames:
        key_pose(fn(f), f)
    return act

def smooth(t):
    t = max(0.0, min(1.0, t)); return t * t * (3 - 2 * t)

def interp(keys, f):
    """Кусочно-гладкая интерполяция по ключам [(кадр, значение), ...]."""
    if f <= keys[0][0]: return keys[0][1]
    for (f0, v0), (f1, v1) in zip(keys, keys[1:]):
        if f <= f1:
            return v0 + (v1 - v0) * smooth((f - f0) / (f1 - f0))
    return keys[-1][1]

STANCE_THIGH, STANCE_KNEE = 7.0, 14.0

# Idle: 60 кадров, петля — дыхание и лёгкая боевая стойка
def idle_fn(f):
    br = math.sin((f - 1) / 60.0 * 2 * math.pi)
    ph = (f - 1) / 60.0 * 2 * math.pi
    shift = math.sin(ph)                      # вес переходит с ноги на ногу за один цикл
    spec = {'Hips': {'dz': 0.006 * br - 0.004 * abs(shift), 'dx': 0.018 * shift, 'ry': 1.6 * shift},
            'Spine': {'rx': -0.6 * br, 'ry': -1.0 * shift, 'rz': 1.2 * math.sin(ph + 0.8)},
            'Chest': {'rx': -1.2 * br, 'ry': -0.8 * shift, 'rz': -1.5 * math.sin(ph + 0.8)},
            'Neck': {'rz': 0.8 * math.sin(ph + 1.6)},
            'Head': {'rx': 1.0 * br, 'rz': 1.5 * math.sin(ph + 2.0), 'ry': 0.8 * shift}}
    for sd, w in (('L', shift), ('R', -shift)):   # нагруженная нога чуть прямее, разгруженная чуть согнута
        leg(spec, sd, STANCE_THIGH + 1.5 * (-w), STANCE_KNEE + 3.0 * (-w))
    return spec
new_action('Idle', range(1, 62), idle_fn)

# WalkF: 36 кадров, петля — шаг вперёд (назад — проигрыванием с отрицательной скоростью)
def walk_fn(f):
    ph = (f - 1) / 36.0 * 2 * math.pi
    spec = {'Hips': {'dz': 0.026 * math.cos(2 * ph), 'dx': -0.032 * math.cos(ph), 'rz': 5.0 * math.sin(ph), 'ry': 3.5 * math.cos(ph), 'rx': 1.5},
            'Spine': {'rx': 3.5, 'rz': -6.0 * math.sin(ph), 'ry': -2.0 * math.cos(ph)},
            'Chest': {'rz': -3.0 * math.sin(ph), 'ry': -1.5 * math.cos(ph)},
            'Neck': {'rz': 1.5 * math.sin(ph)},
            'Head': {'rz': 3.0 * math.sin(ph), 'rx': -1.0 * math.cos(2 * ph)}}
    for sd, off in (('L', 0.0), ('R', math.pi)):
        p = ph + off
        swing = 26.0 * math.sin(p)
        flex = 8.0 + 42.0 * (max(0.0, math.cos(p)) ** 1.5)
        leg(spec, sd, 6.0 + swing, flex)
        toe = 14.0 * max(0.0, -math.sin(p)) * max(0.0, math.cos(p + 0.6))   # на толчке пятка отрывается от земли
        spec['Foot.' + sd]['rx'] = spec['Foot.' + sd]['rx'] + toe
    return spec
new_action('WalkF', range(1, 38), walk_fn)

# Jump: 24 кадра, без петли — приседание, толчок, группировка в воздухе, подготовка к приземлению
J_THIGH = [(1, STANCE_THIGH), (5, 38), (9, 0), (14, 26), (20, 12), (24, 30)]
J_KNEE = [(1, STANCE_KNEE), (5, 70), (9, 4), (14, 56), (20, 20), (24, 55)]
J_DZ = [(1, 0.0), (5, -0.14), (9, 0.0), (14, 0.0), (20, 0.0), (24, -0.08)]
J_CHEST = [(1, 0.0), (5, 10), (9, -2), (14, 6), (24, 8)]
def jump_fn(f):
    th, kn = interp(J_THIGH, f), interp(J_KNEE, f)
    spec = {'Hips': {'dz': interp(J_DZ, f)}, 'Chest': {'rx': interp(J_CHEST, f)}, 'Spine': {'rx': interp(J_CHEST, f) * 0.5}}
    for sd in 'LR': leg(spec, sd, th, kn)
    return spec
new_action('Jump', range(1, 25), jump_fn)

# Hit: 16 кадров, без петли — откидывание корпуса и голова назад, колени подгибаются
H_ENV = [(1, 0.0), (4, 1.0), (16, 0.0)]
def hit_fn(f):
    h = interp(H_ENV, f)
    spec = {'Hips': {'dz': -0.045 * h, 'dy': 0.05 * h, 'rz': 6.0 * h}, 'Spine': {'rx': -10.0 * h, 'rz': -4.0 * h},
            'Chest': {'rx': -18.0 * h, 'rz': -5.0 * h, 'ry': 3.0 * h}, 'Neck': {'rx': -6.0 * h}, 'Head': {'rx': -14.0 * h, 'rz': 4.0 * h}}
    for sd in 'LR': leg(spec, sd, STANCE_THIGH + 10.0 * h, STANCE_KNEE + 14.0 * h)
    return spec
new_action('Hit', range(1, 17), hit_fn)

rig.animation_data.action = bpy.data.actions['Idle']
sc.frame_start, sc.frame_end = 1, 61

# ------------------------------------------------------------------ сохранение и экспорт
bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
bpy.ops.object.select_all(action='DESELECT')
for o in (rig, body, marker):
    o.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=FBX_PATH, use_selection=True, object_types={'ARMATURE', 'MESH', 'EMPTY'},
    apply_unit_scale=True, bake_space_transform=False, add_leaf_bones=False, armature_nodetype='NULL',
    use_armature_deform_only=False, mesh_smooth_type='FACE', path_mode='AUTO',
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0)
print('ЭКСПОРТ:', FBX_PATH, os.path.getsize(FBX_PATH) // 1024, 'КБ')
