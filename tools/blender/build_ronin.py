"""
Генератор ИГРОКА «Путь клинка» из Meshy-модели (GLB → low-poly → скелет под Unity → веса → клипы Idle/WalkF/Jump/Hit → FBX).
Запуск: python3 build_ronin.py  (нужен модуль bpy; вход RONIN_LOW=…/ronin_low.blend — меш после Decimate, см. decimate.py).
Конвенции как у остальных моделей: Z вверх, лицо в -Y, левая сторона персонажа = +X. Кости называются так же, как у героя/врага,
поэтому HeroRigDriver, Animator и клипы работают без изменений. Катана из модели (остров меша) жёстко привязана к Hips.
"""
import bpy, bmesh, math, os, sys
import numpy as np
from mathutils import Vector, Matrix, Quaternion, Euler

OUT_DIR = os.environ.get('HERO_OUT', '/tmp/hero')
LOW = os.environ['RONIN_LOW']
os.makedirs(OUT_DIR, exist_ok=True)
FBX_PATH = os.path.join(OUT_DIR, 'Ronin_Player.fbx')
BLEND_PATH = os.path.join(OUT_DIR, 'Ronin_Player.blend')
WEIGHT_POWER = float(os.environ.get('W_POWER', '2.5'))

bpy.ops.wm.open_mainfile(filepath=LOW)
sc = bpy.context.scene
sc.render.fps = 30
body = [o for o in bpy.data.objects if o.type == 'MESH'][0]
body.name = 'Ronin_Body'; body.data.name = 'Ronin_Body_Mesh'
body.data.materials[0].name = 'Ronin'
bpy.context.view_layer.objects.active = body
for v in body.data.vertices:          # тело по центру: торс лежал на y≈-0.12
    v.co.y += 0.115
body.data.update()
P = np.array([tuple(v.co) for v in body.data.vertices])

# ------------------------------------------------------------------ скелет по измерениям меша
bpy.ops.object.armature_add(enter_editmode=True, location=(0, 0, 0))
rig = bpy.context.object
rig.name = 'Ronin_Rig'; rig.data.name = 'Ronin_Armature'
for b in list(rig.data.edit_bones): rig.data.edit_bones.remove(b)
BONES = {}
def bone(name, head, tail, parent=None, deform=True):
    b = rig.data.edit_bones.new(name)
    b.head = head; b.tail = tail
    if parent: b.parent = rig.data.edit_bones[parent]
    b.use_deform = deform
    BONES[name] = (Vector(head), Vector(tail))

bone('Root', (0, 0, 0.05), (0, 0, 0.25), None, False)
bone('Hips', (0, 0, 0.93), (0, 0, 1.12), 'Root')
bone('Spine', (0, 0, 1.12), (0, 0, 1.30), 'Hips')
bone('Chest', (0, 0, 1.30), (0, 0, 1.55), 'Spine')
bone('Neck', (0, 0, 1.55), (0, 0, 1.64), 'Chest')
bone('Head', (0, 0, 1.64), (0, 0, 1.90), 'Neck')
for side, s in (('L', 1), ('R', -1)):
    bone('Shoulder.' + side, (s * 0.06, 0, 1.50), (s * 0.24, 0, 1.52), 'Chest')
    bone('UpperArm.' + side, (s * 0.24, 0, 1.52), (s * 0.38, 0.0, 1.27), 'Shoulder.' + side)
    bone('Forearm.' + side, (s * 0.38, 0.0, 1.27), (s * 0.50, -0.09, 1.04), 'UpperArm.' + side)
    bone('Hand.' + side, (s * 0.50, -0.09, 1.04), (s * 0.51, -0.10, 0.90), 'Forearm.' + side)
    bone('UpperLeg.' + side, (s * 0.17, 0.0, 0.93), (s * 0.24, 0.0, 0.50), 'Hips')
    bone('LowerLeg.' + side, (s * 0.24, 0.0, 0.50), (s * 0.32, 0.05, 0.10), 'UpperLeg.' + side)
    bone('Foot.' + side, (s * 0.32, 0.05, 0.10), (s * 0.34, -0.14, 0.04), 'LowerLeg.' + side)
bpy.ops.object.mode_set(mode='OBJECT')
DEFORM = [n for n in BONES if n != 'Root']

# ------------------------------------------------------------------ веса: по расстоянию до костей + поправки
def seg_dist_np(p, a, b):
    ab = b - a
    t = np.clip(((p - a) @ ab) / (ab @ ab), 0.0, 1.0)
    return np.linalg.norm(p - (a + np.outer(t, ab)), axis=1)

names = DEFORM
D = np.stack([seg_dist_np(P, np.array(BONES[n][0]), np.array(BONES[n][1])) for n in names], axis=1)   # [V, B]
W = 1.0 / np.maximum(D, 0.02) ** WEIGHT_POWER
idx = {n: i for i, n in enumerate(names)}
# ноги не должны тянуть центральную юбку/пояс (выше колена, ближе к оси)
for side in 'LR':
    for leg in ('UpperLeg.', 'LowerLeg.'):
        k = idx[leg + side]
        near_axis = np.clip((np.abs(P[:, 0]) - 0.02) / 0.15, 0.2, 1.0)
        above_knee = P[:, 2] > 0.52
        W[:, k] *= np.where(above_knee, near_axis, 1.0)
# руки не должны цеплять торс и наоборот: плечо/рука работают только снаружи от оси (|x| > 0.2) или выше плеча
for side in 'LR':
    for arm in ('Shoulder.', 'UpperArm.', 'Forearm.', 'Hand.'):
        k = idx[arm + side]
        outside = np.clip((np.abs(P[:, 0]) - 0.16) / 0.10, 0.0, 1.0)
        W[:, k] *= np.maximum(outside, np.where(P[:, 2] > 1.58, 1.0, 0.0))
# катана: меш после Decimate рассыпан на мелкие куски по UV-швам, поэтому ищем её геометрически —
# прямая по вершинам, торчащим назад (y > 0.30: это точно клинок в ножнах), и всё в трубке радиуса 0.045 вдоль неё → только Hips
tip_pts = P[P[:, 1] > 0.30]
c0 = tip_pts.mean(axis=0)
_, _, vt = np.linalg.svd(tip_pts - c0, full_matrices=False)
ax = vt[0] / np.linalg.norm(vt[0])
if ax[1] < 0: ax = -ax                       # направление к кончику (назад)
rel = P - c0
tpar = rel @ ax
dist_line = np.linalg.norm(rel - np.outer(tpar, ax), axis=1)
t_tip = tpar[P[:, 1] > 0.30].max()
sword_mask = (dist_line < 0.045) & (tpar < t_tip + 0.02) & (tpar > t_tip - 1.15) & (P[:, 1] > -0.32)
sword_set = set(np.nonzero(sword_mask)[0].tolist())
print('КАТАНА: ось', ax.round(2), 'вершин', len(sword_set), 'из них y>0.30:', int((P[:, 1] > 0.30).sum()))
N = W / W.sum(axis=1, keepdims=True)
# оставляем 4 сильнейших влияния
top = np.argsort(-N, axis=1)[:, :4]
groups = {n: body.vertex_groups.new(name=n) for n in names}
for vi in range(len(P)):
    if vi in sword_set:
        groups['Hips'].add([vi], 1.0, 'REPLACE'); continue
    ws = [(N[vi, k], names[k]) for k in top[vi]]
    tot = sum(w for w, _ in ws)
    for w, n in ws:
        if w / tot > 0.03: groups[n].add([vi], w / tot, 'REPLACE')
mod = body.modifiers.new('Armature', 'ARMATURE'); mod.object = rig
body.parent = rig
print('ВЕРШИН:', len(body.data.vertices), 'ТРЕУГОЛЬНИКОВ≈', len(body.data.polygons), 'ГРУПП ВЕСОВ:', len(body.vertex_groups))

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
        pb.location = local_vec(pb, (0, 0, d['dz'])) if d.get('dz') else Vector((0, 0, 0))
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
    spec = {'Hips': {'dz': 0.006 * br}, 'Spine': {'rx': -0.6 * br}, 'Chest': {'rx': -1.2 * br}, 'Head': {'rx': 1.0 * br}}
    for sd in 'LR': leg(spec, sd, STANCE_THIGH, STANCE_KNEE)
    return spec
new_action('Idle', range(1, 62), idle_fn)

# WalkF: 36 кадров, петля — шаг вперёд (назад — проигрыванием с отрицательной скоростью)
def walk_fn(f):
    ph = (f - 1) / 36.0 * 2 * math.pi
    spec = {'Hips': {'dz': 0.020 * math.cos(2 * ph), 'rz': 4.0 * math.sin(ph)},
            'Spine': {'rx': 3.0, 'rz': -5.0 * math.sin(ph)},
            'Chest': {'rz': -2.0 * math.sin(ph)},
            'Head': {'rz': 2.0 * math.sin(ph)}}
    for sd, off in (('L', 0.0), ('R', math.pi)):
        p = ph + off
        swing = 26.0 * math.sin(p)
        flex = 8.0 + 42.0 * (max(0.0, math.cos(p)) ** 1.5)
        leg(spec, sd, 6.0 + swing, flex)
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
    spec = {'Hips': {'dz': -0.03 * h}, 'Spine': {'rx': -8.0 * h}, 'Chest': {'rx': -14.0 * h}, 'Neck': {'rx': -4.0 * h}, 'Head': {'rx': -10.0 * h}}
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
