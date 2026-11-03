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
# Два входа: GLB без скелета (наш скелет + тепловые веса) или FBX с автоматическим ригом Meshy (Unity Humanoid):
# тогда суставы берутся из костей Meshy, а веса переносятся с них на наши кости по именам — привязка ткани
# у Meshy намного аккуратнее (рукава, полы, наплечники не растягиваются «крыльями»).
RIG = SRC.lower().endswith('.fbx')
MB = {}; MPAR = {}
if RIG:
    bpy.ops.import_scene.fbx(filepath=SRC, automatic_bone_orientation=False, ignore_leaf_bones=False)
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    arm.data.pose_position = 'REST'; arm.animation_data_clear(); bpy.context.view_layer.update()
    # новые выгрузки Meshy идут со скелетом Mixamo («mixamorig:LeftArm», пальцы по 4 фаланги) — приводим к именам
    # Unity Humanoid, которые понимает разбор ниже; группы вершин переименовываем вместе с костями
    MIX = {'Arm': 'UpperArm', 'ForeArm': 'LowerArm', 'UpLeg': 'UpperLeg', 'Leg': 'LowerLeg', 'ToeBase': 'Toes',
           'Toe_End': 'ToesEnd', 'HeadTop_End': 'HeadEnd'}
    def mix_name(nm):
        if ':' not in nm: return nm
        s = nm.split(':', 1)[1]
        for side in ('Left', 'Right'):
            if s.startswith(side):
                rest = s[len(side):]
                if rest in MIX: return side + MIX[rest]
                if rest.startswith('Hand') and len(rest) > 4:   # LeftHandMiddle4 → LeftMiddleEnd, LeftHandIndex2 → LeftIndex2
                    f = rest[4:]
                    return side + (f[:-1] + 'End' if f.endswith('4') else f)
                return s
        return MIX.get(s, s)
    ren = {b.name: mix_name(b.name) for b in arm.data.bones if mix_name(b.name) != b.name}
    if ren:
        for b in arm.data.bones:
            if b.name in ren: b.name = ren[b.name]
        for o in bpy.data.objects:
            if o.type == 'MESH':
                for g in o.vertex_groups:
                    if g.name in ren: g.name = ren[g.name]
        log('скелет Mixamo → имена Humanoid:', len(ren), 'костей')
    # T-поза → A-поза: весь конвейер (кулак, позы хвата, клипы, мокап в Unity) рассчитан на руки, опущенные ~60° от
    # горизонтали, как у остальных моделей. Поворачиваем плечевую кость вокруг оси «вперёд» и делаем позу покоем.
    def apose():
        mw = arm.matrix_world
        ax = (mw.inverted().to_3x3() @ Vector((0, 1, 0))).normalized()
        bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active = arm; arm.select_set(True)
        arm.data.pose_position = 'POSE'
        bpy.ops.object.mode_set(mode='POSE'); bpy.ops.pose.select_all(action='SELECT'); bpy.ops.pose.transforms_clear()
        target = math.radians(-60); done = False
        for side in ('Left', 'Right'):
            ua = arm.pose.bones.get(side + 'UpperArm'); hb = arm.pose.bones.get(side + 'Hand')
            if ua is None or hb is None: continue
            def ang():
                bpy.context.view_layer.update(); d = mw @ hb.head - mw @ ua.head
                return math.atan2(d.z, abs(d.x))
            phi = ang()
            if phi < math.radians(-35): continue   # уже A-поза
            h = ua.head.copy()
            for sgn in (1, -1):
                old = ua.matrix.copy()
                ua.matrix = Matrix.Translation(h) @ Matrix.Rotation(sgn * (phi - target), 4, ax) @ Matrix.Translation(-h) @ old
                if abs(ang() - target) < math.radians(5): break
                ua.matrix = old
            log('A-поза', side, round(math.degrees(phi)), '→', round(math.degrees(ang())))
            done = True
        bpy.ops.object.mode_set(mode='OBJECT')
        if not done: arm.data.pose_position = 'REST'; bpy.context.view_layer.update(); return
        for o in bpy.data.objects:
            if o.type != 'MESH': continue
            for m in list(o.modifiers):
                if m.type == 'ARMATURE':
                    bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active = o; o.select_set(True)
                    try: bpy.ops.object.modifier_apply(modifier=m.name)
                    except Exception as e: log('A-поза: не применился модификатор', o.name, e)
        bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active = arm; arm.select_set(True)
        bpy.ops.object.mode_set(mode='POSE'); bpy.ops.pose.select_all(action='SELECT'); bpy.ops.pose.armature_apply(selected=False)
        bpy.ops.object.mode_set(mode='OBJECT')
        arm.data.pose_position = 'REST'; bpy.context.view_layer.update()
    apose()
    for b in arm.data.bones:
        MB[b.name] = (arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local)
        MPAR[b.name] = b.parent.name if b.parent else None
    for o in bpy.data.objects:
        if o.type == 'MESH':
            for mm in list(o.modifiers): o.modifiers.remove(mm)
    log('риг Meshy: костей', len(MB), sorted(MB))
else:
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
for act in list(bpy.data.actions): bpy.data.actions.remove(act)
body.name = NAME + '_Body'; body.data.name = NAME + '_Mesh'

# ------------------------------------------------------------------ 1. масштаб, центр, лицом в −Y
def get_co():
    P = np.empty(len(body.data.vertices) * 3); body.data.vertices.foreach_get('co', P); return P.reshape(-1, 3)
P = get_co()
zmin, zmax = P[:, 2].min(), P[:, 2].max()
s = BODY_H / (TOP_FRAC * (zmax - zmin))
P = np.stack([P[:, 0] * s, P[:, 1] * s, (P[:, 2] - zmin) * s], 1)
band = (P[:, 2] > 0.95 * k) & (P[:, 2] < 1.35 * k)
cx, cy = np.median(P[band, 0]), np.median(P[band, 1]); FLIP = 1.0
P[:, 0] -= cx; P[:, 1] -= cy
# носки стоп смотрят вперёд: у стопы вылет вперёд от голени больше, чем назад
shin = (P[:, 2] > 0.25 * k) & (P[:, 2] < 0.35 * k)
foot = P[:, 2] < 0.04 * k
shin_y = np.median(P[shin, 1])
ahead_neg, ahead_pos = shin_y - P[foot, 1].min(), P[foot, 1].max() - shin_y
if ahead_pos > 1.2 * ahead_neg:
    P[:, 0] *= -1; P[:, 1] *= -1; FLIP = -1.0
    log('развёрнута на 180°: носки смотрели в +Y')
def tf(v):   # точка исходной модели → в систему меша после масштаба, центровки и разворота
    return np.array([FLIP * (v[0] * s - cx), FLIP * (v[1] * s - cy), (v[2] - zmin) * s])
log('масштаб', round(s, 4), 'вершин', len(P), 'полная высота', round(P[:, 2].max(), 3), 'стопы вперёд/назад', round(ahead_neg, 3), round(ahead_pos, 3))
body.data.vertices.foreach_set('co', P.ravel()); body.data.update()

# ------------------------------------------------------------------ 2. суставы по замерам меша
def mid_y(z0, z1, xmax, side=0):
    m = (P[:, 2] > z0) & (P[:, 2] < z1) & (np.abs(P[:, 0]) < xmax)
    if side: m &= side * P[:, 0] > 0
    return float(np.median(P[m, 1])) if m.sum() > 5 else 0.0
J = {}
SPINE0 = None
if not RIG:   # суставы по замерам меша (у рига Meshy они свои)
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
        # кончик кисти — самая дальняя от оси точка на уровне бёдер и выше (подол и ножны ниже и ближе к телу);
        # среди точек у этого края берём самую нижнюю — это кончики пальцев
        cand = (P[:, 2] > 0.62 * k) & (P[:, 2] < 1.30 * k)
        xm = (sg * P[cand, 0]).max()
        edge = cand & (sg * P[:, 0] > xm - 0.04 * k)
        T = P[edge][np.argmin(P[edge, 2])]
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

if RIG:
    # суставы — из костей Meshy (Unity Humanoid: Hips, Spine…, Neck, Head, Left/Right Shoulder, UpperArm, LowerArm, Hand,
    # UpperLeg, LowerLeg, Foot, Toes). Позвоночник — цепочка от шеи вниз до таза.
    def H(nm): return tuple(float(x) for x in tf(MB[nm][0]))
    def find(*keys):
        for nm in MB:
            low = nm.lower().replace('_', '')
            if all(kk in low for kk in keys): return nm
        return None
    neck = find('neck'); head = find('head') if find('head') != find('headend') else None
    head = next((nm for nm in MB if nm.lower() == 'head'), head)
    chain = []; cur = MPAR.get(neck)
    while cur: chain.append(cur); cur = MPAR.get(cur)
    chain.reverse()   # Hips, Spine, Spine1…
    hipsB = chain[0]; spines = chain[1:]
    J['hips'] = H(hipsB); J['spine'] = H(spines[0]) if spines else J['hips']
    J['chest'] = H(spines[-1]) if len(spines) >= 2 else tuple((np.array(J['spine']) + np.array(H(neck))) * 0.5)
    J['neck'] = H(neck); J['head'] = H(head)
    hend = find('headend') or find('headtop')
    J['head_end'] = H(hend) if hend else (J['head'][0], J['head'][1], max(J['head'][2] + 0.18 * k, 1.74 * k))
    for sd, side in (('L', 'left'), ('R', 'right')):
        J['sh' + sd] = H(find(side, 'shoulder')); J['arm' + sd] = H(find(side, 'upperarm'))
        J['elb' + sd] = H(find(side, 'lowerarm') or find(side, 'forearm')); J['wri' + sd] = H(find(side, 'hand'))
        # кончик кисти: конец среднего пальца рига, иначе — самая дальняя от запястья вершина кисти
        Wv = np.array(J['wri' + sd]); dv = Wv - np.array(J['elb' + sd]); dv /= np.linalg.norm(dv)
        mid_end = find(side, 'middle', 'end') or find(side, 'middle', 'distal')
        if mid_end:
            Tv = np.array(H(mid_end))
        else:
            rel = P - Wv; along = rel @ dv; dist = np.linalg.norm(rel, axis=1)
            cand = (along > 0.02 * k) & (dist < 0.22 * k) & (np.linalg.norm(rel - np.outer(along, dv), axis=1) < 0.07 * k)
            Tv = P[cand][np.argmax(along[cand])] if cand.sum() > 5 else Wv + dv * 0.17 * k
        J['hnd' + sd] = tuple(float(x) for x in Tv)
        J['hip' + sd] = H(find(side, 'upperleg') or find(side, 'upleg')); J['knee' + sd] = H(find(side, 'lowerleg') or find(side, 'leg', 'low'))
        J['ank' + sd] = H(find(side, 'foot')); toe = find(side, 'toes') or find(side, 'toe')
        J['toe' + sd] = H(toe) if toe else (J['ank' + sd][0], J['ank' + sd][1] - 0.14 * k, 0.03 * k)
    SPINE0 = spines[0] if spines else None
    for sd in 'LR': log('кисть', sd, 'запястье', np.round(J['wri' + sd], 3), 'кончик', np.round(J['hnd' + sd], 3))
    log('суставы из рига', {kk: tuple(round(x, 3) for x in v) for kk, v in J.items()})
    if J['armL'][0] < 0: log('ВНИМАНИЕ: левая рука Meshy оказалась на −X')

# ------------------------------------------------------------------ 2б. пальцы в кулак (у Meshy ладони раскрыты)
# Пальцы сгибаются в трёх суставах вокруг поперечной оси кисти к ладони (ладонь в A-позе — к бедру),
# в кулаке остаётся отверстие под рукоять. Центр хвата пишется в пустышку GripPoint.* на кости кисти:
# HeroRigDriver ставит запястье так, чтобы рукоять прошла ровно через неё.
CURL = [float(v) for v in os.environ.get('CURL', '68,88,55').split(',')]
GRIP = {}
def rot(v, axis, a):
    axis = axis / np.linalg.norm(axis)
    ca, sa = np.cos(a)[:, None], np.sin(a)[:, None]
    return v * ca + np.cross(axis, v) * sa + np.outer(v @ axis, axis) * (1 - ca)
for sd, sg in (('L', 1), ('R', -1)):
    Wp = np.array(J['wri' + sd]); Tp = np.array(J['hnd' + sd])
    L = np.linalg.norm(Tp - Wp); D = (Tp - Wp) / L
    N = np.array([-sg, 0.0, 0.0]); N -= D * (N @ D); N /= np.linalg.norm(N)
    Lt = np.cross(D, N)
    rel = P - Wp; s_ = rel @ D; radial = np.linalg.norm(rel - np.outer(s_, D), axis=1)
    hand = (s_ > -0.02 * k) & (s_ < L + 0.03 * k) & (radial < 0.075 * k) & (sg * P[:, 0] > 0.2 * k)
    F = P[hand].copy(); s0 = (F - Wp) @ D
    for frac, ang in reversed(list(zip((0.42, 0.66, 0.84), CURL))):
        sj = frac * L
        kk = np.clip((s0 - sj + 0.006 * k) / (0.012 * k), 0, 1); kk = kk * kk * (3 - 2 * kk)
        J0 = Wp + D * sj + N * 0.012 * k
        F = J0 + rot(F - J0, Lt, np.radians(ang) * kk)
    P[hand] = F
    GRIP[sd] = Wp + D * (0.46 * L) + N * (0.035 * k)
    log('кулак', sd, 'вершин', int(hand.sum()), 'длина кисти', round(L, 3), 'центр хвата', np.round(GRIP[sd], 3))
body.data.vertices.foreach_set('co', P.ravel()); body.data.update()

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
def our_bone(g):
    low = g.lower().replace('_', '')
    sd = '.L' if low.startswith('left') else '.R' if low.startswith('right') else ''
    if sd:
        if 'shoulder' in low: return 'Shoulder' + sd
        if 'upperarm' in low: return 'UpperArm' + sd
        if 'lowerarm' in low or 'forearm' in low: return 'Forearm' + sd
        if 'hand' in low or any(f in low for f in ('thumb', 'index', 'middle', 'ring', 'pinky', 'finger')): return 'Hand' + sd
        if 'upperleg' in low or 'upleg' in low or 'thigh' in low: return 'UpperLeg' + sd
        if 'lowerleg' in low or 'calf' in low or low.endswith('leg'): return 'LowerLeg' + sd
        if 'foot' in low or 'toe' in low: return 'Foot' + sd
    if 'neck' in low: return 'Neck'
    if 'head' in low: return 'Head'
    if g == SPINE0: return 'Spine'
    if 'spine' in low or 'chest' in low: return 'Chest'
    # служебные кости Meshy (Bone_0xx: пальцы, полы, пряди) — за ближайшим именованным предком
    if MPAR.get(g): return our_bone(MPAR[g])
    return 'Hips'
if RIG:
    t = time.time()
    src_names = [g.name for g in body.vertex_groups]
    tgt = {i: our_bone(nm) for i, nm in enumerate(src_names)}
    acc = {}
    for v in body.data.vertices:
        for ge in v.groups:
            key = (v.index, tgt[ge.group]); acc[key] = acc.get(key, 0.0) + ge.weight
    for g in list(body.vertex_groups): body.vertex_groups.remove(g)
    made = {}
    for (vi, bn), w in acc.items():
        if bn not in made: made[bn] = body.vertex_groups.new(name=bn)
        made[bn].add([vi], float(w), 'ADD')
    log('веса из рига Meshy:', {nm: tgt[i] for i, nm in enumerate(src_names)}, round(time.time() - t, 1), 'с')
    log('служебные кости → предок:', {nm: MPAR.get(nm) for nm in MB if nm.startswith('Bone')})
t = time.time()
if RIG: pass
else:
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
SEGS = {'Hips': ('hips', 'spine'), 'Spine': ('spine', 'chest'), 'Chest': ('chest', 'neck'), 'Neck': ('neck', 'head'), 'Head': ('head', 'head_end')}
for sd in 'LR':
    SEGS.update({'Shoulder.' + sd: ('sh' + sd, 'arm' + sd), 'UpperArm.' + sd: ('arm' + sd, 'elb' + sd), 'Forearm.' + sd: ('elb' + sd, 'wri' + sd),
                 'Hand.' + sd: ('wri' + sd, 'hnd' + sd), 'UpperLeg.' + sd: ('hip' + sd, 'knee' + sd), 'LowerLeg.' + sd: ('knee' + sd, 'ank' + sd),
                 'Foot.' + sd: ('ank' + sd, 'toe' + sd)})
names = [g.name for g in body.vertex_groups]
for need in SEGS:
    if need not in names: body.vertex_groups.new(name=need); names.append(need)
gix = {nm: i for i, nm in enumerate(names)}
W = np.zeros((nv, len(names)))
for v in body.data.vertices:
    for ge in v.groups: W[v.index, ge.group] = ge.weight
weak = [nm for nm in SEGS if W[:, gix[nm]].sum() < 3.0]
if weak and not RIG:
    # тепловой метод не справился (кость вне объёма меша): веса по расстоянию до костей, как в build_ronin.py
    log('тепловой метод не дал весов костям', weak, '→ веса по расстоянию')
    W = np.zeros((nv, len(names)))
    for nm, (h, tl) in SEGS.items():
        a = np.array(J[h]); b = np.array(J[tl]); ab = b - a
        tt = np.clip(((V - a) @ ab) / (ab @ ab), 0, 1)
        W[:, gix[nm]] = 1.0 / np.maximum(np.linalg.norm(V - (a + np.outer(tt, ab)), axis=1), 0.02) ** 2.5
W[W.sum(1) <= 1e-6, gix['Hips']] = 1.0
W /= W.sum(1, keepdims=True)
def seg_d(a, b):
    a = np.array(a); b = np.array(b); ab = b - a
    tt = np.clip(((V - a) @ ab) / (ab @ ab), 0, 1)
    return np.linalg.norm(V - (a + np.outer(tt, ab)), axis=1)
# руки двигают только то, что в «капсуле» руки: иначе полы, ножны и наплечники улетают за кистью, когда она поднимает катану
# (у рига Meshy привязка рукавов своя и аккуратная — не трогаем)
for sd in ('' if RIG else 'LR'):
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
# шляпа, её поля, причёска выше HEAD_Z — целиком на голове: поля широкой шляпы иначе тянулись бы за плечами
HEAD_Z = float(os.environ.get('HEAD_Z', '0'))
if HEAD_Z > 0:
    hat = V[:, 2] > HEAD_Z * k
    W[hat] = 0.0; W[hat, gix['Head']] = 1.0
    log('выше', HEAD_Z, '— только голова:', int(hat.sum()), 'вершин')
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
bpy.context.view_layer.update()
grips = []
for sd in 'LR':
    e = bpy.data.objects.new('GripPoint.' + sd, None); sc.collection.objects.link(e)
    e.parent = rig; e.parent_type = 'BONE'; e.parent_bone = 'Hand.' + sd
    e.matrix_world = Matrix.Translation(Vector(tuple(GRIP[sd])))
    grips.append(e)
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
for o in [rig, marker, body] + grips: o.select_set(True)
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
