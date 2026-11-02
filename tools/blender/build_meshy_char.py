"""
Персонаж «Путь клинка» из лёгкой Meshy-модели (GLB ~10 тыс. треугольников, A-поза, без скелета):
GLB → масштаб и центр → наш скелет из 20 костей по замерам меша → веса (тепловой метод на воксельном прокси)
→ поправки весов (руки не тянут полы, ножны и полы доспеха на тазу) → клипы Idle/WalkF/Jump/Hit → FBX + превью.

Запуск: blender -b --factory-startup --python build_meshy_char.py
env:
  MESHY_GLB   путь к GLB из Meshy
  MESHY_NAME  имя результата (Akiko_Player, Oni_Enemy, Shogun_Enemy) — так же называются FBX и материал «MZ_<имя>»
  MESHY_OUT   папка результата
  BODY_H      рост до макушки без рогов/гребня/шпилек, м (по умолчанию 1.72)
  TOP_FRAC    доля роста до макушки от полной высоты меша (рога, гребень шлема выше макушки)
  ARM_PAD     добавка к радиусу «капсулы» руки, м (широкие рукава)
  SHOULDER_X  плечевой сустав от оси, в долях фигуры 1,72 м (0.17)
Конвенции как у остальных моделей: Z вверх, лицо в −Y, левая сторона персонажа = +X. Кости и клипы называются так же,
как у героя/врага, поэтому HeroRigDriver, Animator и MocapBaker работают без изменений.
"""
import bpy, bmesh, math, os, time
import numpy as np
from mathutils import Vector, Quaternion, Matrix

SRC = os.environ['MESHY_GLB']
NAME = os.environ['MESHY_NAME']
OUT = os.environ.get('MESHY_OUT', '/tmp/meshy'); os.makedirs(OUT, exist_ok=True)
BODY_H = float(os.environ.get('BODY_H', '1.72'))
TOP_FRAC = float(os.environ.get('TOP_FRAC', '1.0'))
ARM_PAD = float(os.environ.get('ARM_PAD', '0.0'))
SHOULDER_X = float(os.environ.get('SHOULDER_X', '0.17'))
VOXEL = float(os.environ.get('VOXEL', '0.012'))
FBX_PATH = os.path.join(OUT, NAME + '.fbx'); BLEND_PATH = os.path.join(OUT, NAME + '.blend')
k = BODY_H / 1.72          # все типовые размеры ниже — для фигуры с макушкой 1,72 м
def log(*a): print('MZ', NAME, *a, flush=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene; sc.render.fps = 30
bpy.ops.import_scene.gltf(filepath=SRC)
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
for o in meshes:   # всё в мировые координаты, без родителей
    mw = o.matrix_world.copy(); o.parent = None; o.data.transform(mw); o.matrix_world = Matrix.Identity(4)
if len(meshes) > 1:
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]; bpy.ops.object.join()
body = meshes[0]
for o in list(bpy.data.objects):
    if o != body: bpy.data.objects.remove(o, do_unlink=True)
body.name = NAME + '_Body'; body.data.name = NAME + '_Mesh'

# ------------------------------------------------------------------ 1. масштаб, центр, лицом в −Y
def get_co():
    P = np.empty(len(body.data.vertices) * 3); body.data.vertices.foreach_get('co', P); return P.reshape(-1, 3)
P = get_co()
zmin, zmax = P[:, 2].min(), P[:, 2].max()
s = BODY_H / (TOP_FRAC * (zmax - zmin))
P = np.stack([P[:, 0] * s, P[:, 1] * s, (P[:, 2] - zmin) * s], 1)
band = (P[:, 2] > 0.95 * k) & (P[:, 2] < 1.35 * k)
P[:, 0] -= np.median(P[band, 0]); P[:, 1] -= np.median(P[band, 1])
# носки стоп смотрят вперёд: у стопы вылет вперёд от голени больше, чем назад
shin = (P[:, 2] > 0.25 * k) & (P[:, 2] < 0.35 * k)
foot = P[:, 2] < 0.04 * k
shin_y = np.median(P[shin, 1])
ahead_neg, ahead_pos = shin_y - P[foot, 1].min(), P[foot, 1].max() - shin_y
if ahead_pos > 1.2 * ahead_neg:
    P[:, 0] *= -1; P[:, 1] *= -1
    log('развёрнута на 180°: носки смотрели в +Y')
log('масштаб', round(s, 4), 'вершин', len(P), 'полная высота', round(P[:, 2].max(), 3), 'стопы вперёд/назад', round(ahead_neg, 3), round(ahead_pos, 3))
body.data.vertices.foreach_set('co', P.ravel()); body.data.update()

# ------------------------------------------------------------------ 2. суставы по замерам меша
def mid_y(z0, z1, xmax, side=0):
    m = (P[:, 2] > z0) & (P[:, 2] < z1) & (np.abs(P[:, 0]) < xmax)
    if side: m &= side * P[:, 0] > 0
    return float(np.median(P[m, 1])) if m.sum() > 5 else 0.0
J = {}
for nm, z in (('hips', 0.92), ('spine', 1.08), ('chest', 1.26), ('neck', 1.47), ('head', 1.56)):
    J[nm] = (0.0, mid_y((z - 0.03) * k, (z + 0.03) * k, 0.12 * k), z * k)
J['head_end'] = (0.0, J['head'][1], 1.76 * k)
for sd, sg in (('L', 1), ('R', -1)):
    # стопа: центр по X, пятка и носок по Y
    F = P[(P[:, 2] < 0.06 * k) & (sg * P[:, 0] > 0.01)]
    fx = float(np.median(F[:, 0])); fy0, fy1 = np.percentile(F[:, 1], 2), np.percentile(F[:, 1], 98)
    J['ank' + sd] = (fx, float(fy1 - 0.30 * (fy1 - fy0)), 0.10 * k)
    J['toe' + sd] = (fx, float(fy0 + 0.03 * k), 0.03 * k)
    J['hip' + sd] = (sg * 0.10 * k, mid_y(0.86 * k, 0.96 * k, 0.20 * k, sg), 0.91 * k)
    J['knee' + sd] = (0.5 * (J['hip' + sd][0] + fx), mid_y(0.45 * k, 0.51 * k, 0.40 * k, sg) - 0.01 * k, 0.48 * k)
    # рука в A-позе: плечо — по пропорции, кончик кисти — дальняя вершина вдоль направления «вниз-наружу»
    yc = J['chest'][1]
    S = np.array([sg * SHOULDER_X * k, yc, 1.40 * k])
    cand = (sg * P[:, 0] > 0.24 * k) & (P[:, 2] > 0.45 * k) & (P[:, 2] < 1.35 * k)
    d0 = np.array([sg * 0.7071, 0.0, -0.7071])
    T = P[cand][np.argmax((P[cand] - S) @ d0)]
    A = T - S
    def on_arm(f):
        q = S + f * A
        m = (sg * P[:, 0] > 0.2 * k) & (np.abs(P[:, 0] - q[0]) < 0.05 * k) & (np.abs(P[:, 2] - q[2]) < 0.05 * k)
        if m.sum() > 3: q[1] = float(np.median(P[m, 1]))
        return tuple(float(v) for v in q)
    J['sh' + sd] = (sg * 0.04 * k, yc, 1.40 * k)
    J['arm' + sd] = tuple(float(v) for v in S)
    J['elb' + sd] = on_arm(0.42); J['wri' + sd] = on_arm(0.77); J['hnd' + sd] = tuple(float(v) for v in T)
    log('рука', sd, 'кончик', np.round(T, 3), 'угол к вертикали', round(math.degrees(math.atan2(abs(A[0]), -A[2])), 1),
        '| стопа x', round(fx, 3), 'пятка/носок', round(fy1, 3), round(fy0, 3))

bpy.ops.object.armature_add(enter_editmode=True, location=(0, 0, 0))
rig = bpy.context.object; rig.name = NAME + '_Rig'; rig.data.name = NAME + '_Armature'
for b in list(rig.data.edit_bones): rig.data.edit_bones.remove(b)
def bone(name, h, tl, parent=None, deform=True):
    b = rig.data.edit_bones.new(name); b.head = J[h] if isinstance(h, str) else h; b.tail = J[tl] if isinstance(tl, str) else tl
    if parent: b.parent = rig.data.edit_bones[parent]
    b.use_deform = deform
bone('Root', (0, 0, 0.05), (0, 0, 0.25), None, False)
bone('Hips', 'hips', 'spine', 'Root'); bone('Spine', 'spine', 'chest', 'Hips'); bone('Chest', 'chest', 'neck', 'Spine')
bone('Neck', 'neck', 'head', 'Chest'); bone('Head', 'head', 'head_end', 'Neck')
for sd in 'LR':
    bone('Shoulder.' + sd, 'sh' + sd, 'arm' + sd, 'Chest'); bone('UpperArm.' + sd, 'arm' + sd, 'elb' + sd, 'Shoulder.' + sd)
    bone('Forearm.' + sd, 'elb' + sd, 'wri' + sd, 'UpperArm.' + sd); bone('Hand.' + sd, 'wri' + sd, 'hnd' + sd, 'Forearm.' + sd)
    bone('UpperLeg.' + sd, 'hip' + sd, 'knee' + sd, 'Hips'); bone('LowerLeg.' + sd, 'knee' + sd, 'ank' + sd, 'UpperLeg.' + sd)
    bone('Foot.' + sd, 'ank' + sd, 'toe' + sd, 'LowerLeg.' + sd)
bpy.ops.object.mode_set(mode='OBJECT')

# ------------------------------------------------------------------ 3. веса: тепловой метод на воксельном прокси
t = time.time()
proxy = body.copy(); proxy.data = body.data.copy(); proxy.name = 'Proxy'; sc.collection.objects.link(proxy)
rm = proxy.modifiers.new('rm', 'REMESH'); rm.mode = 'VOXEL'; rm.voxel_size = VOXEL
bpy.ops.object.select_all(action='DESELECT'); proxy.select_set(True); bpy.context.view_layer.objects.active = proxy
bpy.ops.object.modifier_apply(modifier='rm')
bm = bmesh.new(); bm.from_mesh(proxy.data); bm.verts.ensure_lookup_table()
comp = [-1] * len(bm.verts); sizes = []
for v in bm.verts:
    if comp[v.index] >= 0: continue
    cid = len(sizes); comp[v.index] = cid; st = [v]; cnt = 0
    while st:
        x = st.pop(); cnt += 1
        for e in x.link_edges:
            o = e.other_vert(x)
            if comp[o.index] < 0: comp[o.index] = cid; st.append(o)
    sizes.append(cnt)
big = max(range(len(sizes)), key=lambda i: sizes[i])
bmesh.ops.delete(bm, geom=[v for v in bm.verts if comp[v.index] != big], context='VERTS')
bm.to_mesh(proxy.data); bm.free()
bpy.ops.object.select_all(action='DESELECT'); proxy.select_set(True); rig.select_set(True); bpy.context.view_layer.objects.active = rig
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
for mm in list(proxy.modifiers): proxy.modifiers.remove(mm)
proxy.parent = None; proxy.matrix_world = Matrix.Identity(4)
for g in proxy.vertex_groups: body.vertex_groups.new(name=g.name)
dt = body.modifiers.new('dt', 'DATA_TRANSFER'); dt.object = proxy; dt.use_vert_data = True
dt.data_types_verts = {'VGROUP_WEIGHTS'}; dt.vert_mapping = 'POLYINTERP_NEAREST'
dt.layers_vgroup_select_src = 'ALL'; dt.layers_vgroup_select_dst = 'NAME'
bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); bpy.context.view_layer.objects.active = body
bpy.ops.object.modifier_apply(modifier='dt')
bpy.data.objects.remove(proxy, do_unlink=True)
log('веса (тепловой метод)', round(time.time() - t, 1), 'с, островов прокси', len(sizes), 'вершин прокси в главном', sizes[big])

# ------------------------------------------------------------------ 4. поправки весов
V = get_co(); nv = len(V)
names = [g.name for g in body.vertex_groups]
for need in ('Hips', 'Spine', 'Chest', 'Neck', 'Head'):
    if need not in names: body.vertex_groups.new(name=need); names.append(need)
gix = {nm: i for i, nm in enumerate(names)}
W = np.zeros((nv, len(names)))
for v in body.data.vertices:
    for ge in v.groups: W[v.index, ge.group] = ge.weight
W[W.sum(1) <= 1e-6, gix['Hips']] = 1.0
W /= W.sum(1, keepdims=True)
def seg_d(a, b):
    a = np.array(a); b = np.array(b); ab = b - a
    tt = np.clip(((V - a) @ ab) / (ab @ ab), 0, 1)
    return np.linalg.norm(V - (a + np.outer(tt, ab)), axis=1)
# руки двигают только то, что в «капсуле» руки: иначе полы, ножны и наплечники улетают за кистью, когда она поднимает катану
for sd in 'LR':
    dist = np.minimum.reduce([seg_d(J['sh' + sd], J['arm' + sd]) - (0.05 * k + ARM_PAD), seg_d(J['arm' + sd], J['elb' + sd]) - (0.10 * k + ARM_PAD),
                              seg_d(J['elb' + sd], J['wri' + sd]) - (0.09 * k + ARM_PAD), seg_d(J['wri' + sd], J['hnd' + sd]) - (0.08 * k + 0.5 * ARM_PAD)])
    outside = (dist > 0.0) & (V[:, 2] < 1.32 * k)
    moved = np.zeros(nv)
    for bn in ('Shoulder.', 'UpperArm.', 'Forearm.', 'Hand.'):
        if bn + sd in gix:
            c = gix[bn + sd]; moved += W[:, c] * outside; W[:, c] *= ~outside
    W[:, gix['Hips']] += moved * (V[:, 2] < 1.05 * k); W[:, gix['Chest']] += moved * (V[:, 2] >= 1.05 * k)
    log('рука', sd, 'снято влияний с', int((moved > 0.01).sum()), 'вершин вне капсулы')
# полы доспеха, юбка, ножны: дальше ~0,13 м от оси ноги — в основном таз (доля ноги 0,25…1 по близости)
region = (V[:, 2] > 0.36 * k) & (V[:, 2] < 1.05 * k)
for sd in 'LR':
    dl = np.minimum(seg_d(J['hip' + sd], J['knee' + sd]), seg_d(J['knee' + sd], J['ank' + sd]))
    aff = np.clip((0.19 * k - dl) / (0.06 * k), 0, 1); aff = 0.25 + 0.75 * aff * aff * (3 - 2 * aff)
    for bn in ('UpperLeg.' + sd, 'LowerLeg.' + sd):
        if bn in gix:
            c = gix[bn]; mv = W[:, c] * (1 - aff) * region
            W[:, c] -= mv; W[:, gix['Hips']] += mv
W /= W.sum(1, keepdims=True)
# сглаживание весов низа тела по рёбрам меша: переход тянется, а не рвётся
E = np.empty(len(body.data.edges) * 2, dtype=np.int64); body.data.edges.foreach_get('vertices', E); E = E.reshape(-1, 2)
deg = np.bincount(E.ravel(), minlength=nv).astype(float)
low = (V[:, 2] > 0.30 * k) & (V[:, 2] < 1.08 * k)
for _ in range(10):
    acc = np.zeros_like(W); np.add.at(acc, E[:, 0], W[E[:, 1]]); np.add.at(acc, E[:, 1], W[E[:, 0]])
    avg = acc / np.maximum(deg, 1)[:, None]
    W[low] = 0.5 * W[low] + 0.5 * avg[low]
W /= W.sum(1, keepdims=True)
for g in list(body.vertex_groups): body.vertex_groups.remove(g)
grp = [body.vertex_groups.new(name=nm) for nm in names]
for vi in range(nv):
    row = W[vi]; top = np.argsort(-row)[:4]; tot = row[top].sum() or 1.0
    for c in top:
        w = row[c] / tot
        if w > 0.01: grp[c].add([vi], float(w), 'REPLACE')
body.parent = rig
am = body.modifiers.new('Armature', 'ARMATURE'); am.object = rig
marker = bpy.data.objects.new('FrontMarker', None); marker.location = (0, -0.5, 0.92 * k)
sc.collection.objects.link(marker); marker.parent = rig
for mt in body.data.materials:
    if mt: mt.name = 'MZ_' + NAME
log('групп весов', len(body.vertex_groups), 'материалов', len(body.data.materials))

# ------------------------------------------------------------------ 5. клипы (общий код из build_ronin.py) и экспорт
for act in list(bpy.data.actions): bpy.data.actions.remove(act)
here = os.path.dirname(os.path.abspath(__file__))
src_ronin = open(os.path.join(here, 'build_ronin.py'), encoding='utf-8').read()
anim = src_ronin[src_ronin.index('# ------------------------------------------------------------------ анимации'):src_ronin.index('# ------------------------------------------------------------------ сохранение')]
exec(anim, {'bpy': bpy, 'math': math, 'Vector': Vector, 'Quaternion': Quaternion, 'Matrix': Matrix, 'rig': rig, 'sc': sc})
bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
bpy.ops.object.select_all(action='DESELECT')
for o in (rig, marker, body): o.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=FBX_PATH, use_selection=True, object_types={'ARMATURE', 'MESH', 'EMPTY'},
    apply_unit_scale=True, bake_space_transform=False, add_leaf_bones=False, armature_nodetype='NULL',
    use_armature_deform_only=False, mesh_smooth_type='FACE', path_mode='STRIP', embed_textures=False,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0)
log('ЭКСПОРТ', FBX_PATH, os.path.getsize(FBX_PATH) // 1024, 'КБ')

# ------------------------------------------------------------------ 6. превью (Cycles на CPU): покой спереди, руки к рукояти, шаг сбоку
sc.render.engine = 'CYCLES'; sc.cycles.samples = 12; sc.cycles.device = 'CPU'
try: sc.cycles.use_denoising = False
except Exception: pass
sc.render.resolution_x = 420; sc.render.resolution_y = 640
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs[0].default_value = (0.18, 0.18, 0.2, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 0.8
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.0
sun.rotation_euler = (math.radians(50), 0, math.radians(-30)); sc.collection.objects.link(sun)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.3 * max(1.0, P[:, 2].max() / 2.0)
target = Vector((0, 0, P[:, 2].max() * 0.5))
def shoot(tag, pos):
    cam.location = pos; cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(OUT, NAME + '_' + tag + '.png'); bpy.ops.render.render(write_still=True)
PB = rig.pose.bones
rig.animation_data.action = bpy.data.actions['Idle']; sc.frame_set(1)
shoot('1rest', (0, -6, target.z))
# руки вперёд-вверх к рукояти (в игре их так ставит IK): проверка, что полы и наплечники не рвутся
for sd, sg in (('L', 1), ('R', -1)):
    for bn, ang, ax in (('UpperArm.', -75, (1, 0, 0)), ('Forearm.', -35, (1, 0, 0))):
        pb = PB[bn + sd]; pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = Quaternion(pb.bone.matrix_local.to_3x3().inverted() @ Vector(ax), math.radians(ang))
    pb = PB['UpperArm.' + sd]
    pb.rotation_quaternion = pb.rotation_quaternion @ Quaternion(pb.bone.matrix_local.to_3x3().inverted() @ Vector((0, 0, 1)), math.radians(-sg * 25))
rig.animation_data.action = None
shoot('2grip', (4.2, -4.2, target.z))
for pb in PB: pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0)
rig.animation_data.action = bpy.data.actions['WalkF']; sc.frame_set(10)
shoot('3walk', (6, 0, target.z))
log('ГОТОВО')
