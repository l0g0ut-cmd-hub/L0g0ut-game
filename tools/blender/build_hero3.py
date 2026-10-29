"""
Игрок «Путь клинка» из Meshy-модели «Crimson Shadow Samurai» (FBX 1,2 млн треугольников, PBR 2K, без скелета, A-поза).
Запуск: HERO3_FBX=…/texture.fbx HERO3_TEX=…/папка_с_png HERO_OUT=…/Models TEX_OUT=…/Art/Hero3 python3 build_hero3.py

 1. масштаб: рост с пучком волос 1,80 м, стопы на z=0, торс по центру; лицо в −Y, левая сторона = +X (как у всех наших моделей);
 2. пальцы обеих кистей сгибаются в хват рукояти и запекаются в меш (на полном разрешении, до упрощения):
    изгиб по дуге вокруг линии костяшек, большой палец ложится поверх — в кулаке остаётся отверстие под цуку;
 3. Decimate до TARGET треугольников (UV сохраняются, детали остаются в карте нормалей);
 4. наш скелет из 20 костей по замерам меша (сечения ног, рук, торса);
 5. веса — тепловой метод Blender на водонепроницаемом воксельном прокси (самый большой остров), перенос на меш
    (POLYINTERP_NEAREST); катана в ножнах на левом бедре — жёстко на Hips (не гнётся с ногой);
 6. текстуры: base color и нормали — JPG, металл (R) + гладкость = 1 − шероховатость (A) — PNG для Standard;
 7. клипы Idle/WalkF/Jump/Hit — общий код из build_ronin.py; экспорт FBX.
"""
import bpy, bmesh, math, os, time
import numpy as np
from mathutils import Vector, Matrix, Quaternion

SRC = os.environ['HERO3_FBX']
TEX_IN = os.environ['HERO3_TEX']
OUT_DIR = os.environ.get('HERO_OUT', '/tmp/hero'); os.makedirs(OUT_DIR, exist_ok=True)
TEX_OUT = os.environ.get('TEX_OUT', os.path.join(OUT_DIR, 'Hero3')); os.makedirs(TEX_OUT, exist_ok=True)
FBX_PATH = os.path.join(OUT_DIR, 'Hero3_Player.fbx'); BLEND_PATH = os.path.join(OUT_DIR, 'Hero3_Player.blend')
HEIGHT = float(os.environ.get('HEIGHT', '1.80'))
TARGET = int(os.environ.get('TARGET', '50000'))
JA = [float(x) for x in os.environ.get('JOINTS', '72,95,58').split(',')]   # сгиб суставов пальцев, °
KNUCKLE_Z = float(os.environ.get('KNUCKLE_Z', '0.872'))
THUMB_DEG = float(os.environ.get('THUMB_DEG', '38'))
VOXEL = float(os.environ.get('VOXEL', '0.012'))

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene; sc.render.fps = 30
bpy.ops.import_scene.fbx(filepath=SRC)
body = [o for o in bpy.data.objects if o.type == 'MESH'][0]
for o in list(bpy.data.objects):
    if o != body: bpy.data.objects.remove(o, do_unlink=True)
body.name = 'Hero3_Body'; body.data.name = 'Hero3_Body_Mesh'
body.data.transform(body.matrix_world); body.matrix_world = Matrix.Identity(4)

# ------------------------------------------------------------------ 1. масштаб и центр
n = len(body.data.vertices)
P = np.empty(n * 3); body.data.vertices.foreach_get('co', P); P = P.reshape(-1, 3)
zmin, zmax = P[:, 2].min(), P[:, 2].max(); s = HEIGHT / (zmax - zmin)
Z = (P[:, 2] - zmin) * s; band = (Z > 1.0) & (Z < 1.4)
cx = (P[band, 0].min() + P[band, 0].max()) / 2; cy = (P[band, 1].min() + P[band, 1].max()) / 2
P = np.stack([(P[:, 0] - cx) * s, (P[:, 1] - cy) * s, Z], 1)
print('МАСШТАБ', round(s, 4), 'вершин', n)

# ------------------------------------------------------------------ 2. пальцы в хват
def curl_hand(P, sg):
    """sg=+1 левая кисть (+X), −1 правая. Ладонь к оси тела (−sg·X), пальцы вниз, большой палец спереди-изнутри."""
    wri = np.array([sg * 0.345, 0.0, 0.955])
    region = (sg * P[:, 0] > 0.27) & (P[:, 2] > 0.70) & (P[:, 2] < 0.93) & (P[:, 1] > -0.12) & (P[:, 1] < 0.08)
    D = np.array([sg * -0.007, 0.0, -1.0]); D /= np.linalg.norm(D)                 # вдоль пальцев (вниз, чуть внутрь)
    N = np.array([-sg, 0.0, 0.0]); N -= D * (N @ D); N /= np.linalg.norm(N)          # к ладони
    Lt = np.cross(D, N)                                                               # поперёк кисти
    K0 = np.array([sg * 0.352, 0.0, KNUCKLE_Z])
    # большой палец: изнутри (ближе к оси, чем пальцы) и спереди, ниже запястья
    thumb = region & (sg * P[:, 0] < 0.322) & (P[:, 1] < -0.028) & (P[:, 2] < 0.915)
    fingers = region & ~thumb & (sg * P[:, 0] > 0.318) & (P[:, 2] < KNUCKLE_Z + 0.004)
    # сгиб по суставам (пястно-фаланговый, средний, ногтевой) — от дальнего к ближнему, каждый с плавным переходом ±4 мм:
    # кулак «квадратный», как у настоящей руки на рукояти, а не спираль
    joints = [(0.0, JA[0]), (0.042, JA[1]), (0.068, JA[2])]
    def rot(v, axis, a):
        axis = axis / np.linalg.norm(axis)
        ca, sa = np.cos(a), np.sin(a)
        if np.ndim(a) == 0: ca, sa = math.cos(a), math.sin(a)
        else: ca, sa = ca[:, None], sa[:, None]
        return v * ca + np.cross(axis, v) * sa + np.outer(v @ axis, axis) * (1 - ca)
    F = P[fingers].copy()
    sd0 = (F - K0) @ D
    for sj, aj in reversed(joints):
        k = np.clip((sd0 - sj + 0.004) / 0.008, 0, 1); k = k * k * (3 - 2 * k)
        J0 = K0 + D * sj
        F = J0 + rot(F - J0, Lt, np.radians(aj) * k)
    P[fingers] = F
    C = K0 + N * 0.024 + D * 0.03     # центр хвата: внутри согнутых пальцев
    # большой палец: поворот вокруг оси у основания — ложится поверх согнутых пальцев (к внешней стороне, вниз)
    tb = np.array([sg * 0.305, -0.05, 0.905])
    ax = np.array([0.0, 1.0, 0.0]) * sg
    ang = math.radians(THUMB_DEG)
    Qt = P[thumb] - tb
    k = np.clip((tb[2] - P[thumb][:, 2]) / 0.02, 0, 1)[:, None]                      # основание почти не двигается
    P[thumb] = tb + Qt * (1 - k) + rot(Qt, ax, -ang) * k
    print('ХВАТ', 'L' if sg > 0 else 'R', 'пальцы', int(fingers.sum()), 'большой', int(thumb.sum()), 'центр хвата', (C + np.array([0, 0, 0])).round(3))
    return C
gripL = curl_hand(P, 1); gripR = curl_hand(P, -1)
body.data.vertices.foreach_set('co', P.ravel()); body.data.update()

# ------------------------------------------------------------------ 3. упрощение
t = time.time()
bpy.context.view_layer.objects.active = body; body.select_set(True)
dm = body.modifiers.new('dec', 'DECIMATE'); dm.ratio = min(1.0, TARGET / len(body.data.polygons)); dm.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier='dec')
print('DECIMATE', round(time.time() - t, 1), 'с, треугольников', len(body.data.polygons), 'вершин', len(body.data.vertices))
for mt in body.data.materials:
    if mt: mt.name = 'H3_Body'

# ------------------------------------------------------------------ 4. скелет
J = {'hips': (0, 0.03, 0.92), 'spine': (0, 0.02, 1.08), 'chest': (0, 0.04, 1.26), 'neck': (0, 0.05, 1.47), 'head': (0, 0.03, 1.56), 'head_end': (0, 0.02, 1.76)}
for sd_, sg in (('L', 1), ('R', -1)):
    J['sh' + sd_] = (sg * 0.04, 0.05, 1.40); J['arm' + sd_] = (sg * 0.17, 0.07, 1.40); J['elb' + sd_] = (sg * 0.27, 0.077, 1.16)
    J['wri' + sd_] = (sg * 0.345, 0.0, 0.955); J['hnd' + sd_] = (sg * 0.352, -0.02, 0.82)
    J['hip' + sd_] = (sg * 0.10, 0.04, 0.91); J['knee' + sd_] = (sg * 0.165, 0.06, 0.48); J['ank' + sd_] = (sg * 0.195, 0.095, 0.10); J['toe' + sd_] = (sg * 0.21, -0.09, 0.03)
bpy.ops.object.armature_add(enter_editmode=True, location=(0, 0, 0))
rig = bpy.context.object; rig.name = 'Samurai_Rig'; rig.data.name = 'Samurai_Armature'
for b in list(rig.data.edit_bones): rig.data.edit_bones.remove(b)
def bone(name, h, tl, parent=None, deform=True):
    b = rig.data.edit_bones.new(name); b.head = J[h] if isinstance(h, str) else h; b.tail = J[tl] if isinstance(tl, str) else tl
    if parent: b.parent = rig.data.edit_bones[parent]
    b.use_deform = deform
bone('Root', (0, 0, 0.05), (0, 0, 0.25), None, False)
bone('Hips', 'hips', 'spine', 'Root'); bone('Spine', 'spine', 'chest', 'Hips'); bone('Chest', 'chest', 'neck', 'Spine')
bone('Neck', 'neck', 'head', 'Chest'); bone('Head', 'head', 'head_end', 'Neck')
for sd_ in 'LR':
    bone('Shoulder.' + sd_, 'sh' + sd_, 'arm' + sd_, 'Chest'); bone('UpperArm.' + sd_, 'arm' + sd_, 'elb' + sd_, 'Shoulder.' + sd_)
    bone('Forearm.' + sd_, 'elb' + sd_, 'wri' + sd_, 'UpperArm.' + sd_); bone('Hand.' + sd_, 'wri' + sd_, 'hnd' + sd_, 'Forearm.' + sd_)
    bone('UpperLeg.' + sd_, 'hip' + sd_, 'knee' + sd_, 'Hips'); bone('LowerLeg.' + sd_, 'knee' + sd_, 'ank' + sd_, 'UpperLeg.' + sd_); bone('Foot.' + sd_, 'ank' + sd_, 'toe' + sd_, 'LowerLeg.' + sd_)
bpy.ops.object.mode_set(mode='OBJECT')

# ------------------------------------------------------------------ 5. веса: тепловой метод на воксельном прокси
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
print('ВЕСА (тепловой метод)', round(time.time() - t, 1), 'с, прокси-островов было', len(sizes))

# катана в ножнах (левое бедро, ножны уходят назад-вниз) — жёстко на таз
V = np.empty(len(body.data.vertices) * 3); body.data.vertices.foreach_get('co', V); V = V.reshape(-1, 3)
# ножны видны в сечениях как тонкая линия за левым бедром: y ≈ 0,29 − 0,47·(z − 0,56), x 0,19…0,27
seed = (V[:, 0] > 0.19) & (V[:, 0] < 0.27) & (V[:, 2] > 0.52) & (V[:, 2] < 0.90) & (np.abs(V[:, 1] - (0.29 - 0.47 * (V[:, 2] - 0.56))) < 0.025)
sw_idx = []
if seed.sum() > 20:
    c0 = V[seed].mean(0); _, _, vt = np.linalg.svd(V[seed] - c0, full_matrices=False); ax = vt[0] / np.linalg.norm(vt[0])
    if ax[1] < 0: ax = -ax   # к концу ножен (назад-вниз)
    rel = V - c0; tpar = rel @ ax; dl = np.linalg.norm(rel - np.outer(tpar, ax), axis=1)
    t_tip = tpar[seed].max()
    sw = (dl < 0.032) & (tpar < t_tip + 0.12) & (tpar > t_tip - 1.05) & (V[:, 0] > 0.15) & (V[:, 0] < 0.30)
    sw_idx = np.nonzero(sw)[0].tolist()
    print('НОЖНЫ: ось', ax.round(2), 'вершин', len(sw_idx))
if sw_idx:
    for g in body.vertex_groups:
        if g.name != 'Hips': g.remove(sw_idx)
    body.vertex_groups['Hips'].add(sw_idx, 1.0, 'REPLACE')
# руки влияют только на то, что в «капсуле» руки: шнуры и полы у бедра (рядом с висящей в A-позе кистью)
# иначе улетают за рукой, когда она поднимает катану
def seg_d(Pv, a, b):
    a = np.array(a); b = np.array(b); ab = b - a
    t_ = np.clip(((Pv - a) @ ab) / (ab @ ab), 0, 1)
    return np.linalg.norm(Pv - (a + np.outer(t_, ab)), axis=1)
V = np.empty(len(body.data.vertices) * 3); body.data.vertices.foreach_get('co', V); V = V.reshape(-1, 3)
for sd_ in 'LR':
    dist = np.minimum.reduce([seg_d(V, J['sh' + sd_], J['arm' + sd_]) - 0.03, seg_d(V, J['arm' + sd_], J['elb' + sd_]) - 0.095,
                              seg_d(V, J['elb' + sd_], J['wri' + sd_]) - 0.085, seg_d(V, J['wri' + sd_], J['hnd' + sd_]) - 0.075])
    outside = np.nonzero(dist > 0.0)[0].tolist()
    arm_groups = [body.vertex_groups[n] for n in ('Shoulder.' + sd_, 'UpperArm.' + sd_, 'Forearm.' + sd_, 'Hand.' + sd_)]
    # плечевая кость тянет и наплечник — его не трогаем: только ниже подмышки
    low = set(np.nonzero(V[:, 2] < 1.30)[0].tolist())
    rm_ = [i for i in outside if i in low]
    for g in arm_groups: g.remove(rm_)
    print('РУКА', sd_, 'снято влияний руки с', len(rm_), 'вершин вне капсулы')
# полы плаща: тепловой метод делит их между левой и правой ногой со ступенькой — на широкой стойке полотно рвётся.
# Плащ (дальше ~0,15 м от оси бедра) ведёт в основном таз, ноги — с долей 0,3…1 по близости; затем веса сглаживаются
# по поверхности меша (лапласиан) — переход растягивается, а не рвётся. Штанины (ближе к оси) идут за ногой как были.
names_all = [g.name for g in body.vertex_groups]
gix = {nm: i for i, nm in enumerate(names_all)}
nv = len(body.data.vertices)
Wm = np.zeros((nv, len(names_all)))
for v in body.data.vertices:
    for ge in v.groups: Wm[v.index, ge.group] = ge.weight
Wm[Wm.sum(1) <= 1e-6, gix['Hips']] = 1.0
Wm /= Wm.sum(1, keepdims=True)
region = (V[:, 2] > 0.38) & (V[:, 2] < 1.02)
# красные шнуры и кисти, свисающие с пояса, — по цвету текстуры: им ноги не указ (иначе вытягиваются полосами за коленом)
from PIL import Image as PI2
alb_img = np.asarray(PI2.open([os.path.join(TEX_IN, f) for f in os.listdir(TEX_IN) if f.endswith('_texture.png')][0]).convert('RGB')).astype(np.float32)
uvl = body.data.uv_layers.active.data
Hh, Ww = alb_img.shape[:2]
col_sum = np.zeros((nv, 3)); col_n = np.zeros(nv)
for poly in body.data.polygons:
    for li in poly.loop_indices:
        uv = uvl[li].uv; vi = body.data.loops[li].vertex_index
        px_ = alb_img[int(np.clip((1 - uv[1]) * (Hh - 1), 0, Hh - 1)), int(np.clip(uv[0] * (Ww - 1), 0, Ww - 1))]
        col_sum[vi] += px_; col_n[vi] += 1
col = col_sum / np.maximum(col_n, 1)[:, None]
red = (col[:, 0] > 45) & (col[:, 0] > 1.8 * col[:, 1]) & (col[:, 0] > 1.6 * col[:, 2])
print('КРАСНЫЕ вершины (шнуры/кант):', int(red.sum()))
for sd_ in 'LR':
    dthigh = seg_d(V, J['hip' + sd_], J['knee' + sd_]); dshin = seg_d(V, J['knee' + sd_], J['ank' + sd_])
    dl = np.minimum(dthigh, dshin)
    aff = np.clip((0.20 - dl) / (0.20 - 0.135), 0, 1); aff = 0.3 + 0.7 * aff * aff * (3 - 2 * aff)
    aff = np.where(red & (dl > 0.12), 0.0, aff)
    for bn in ('UpperLeg.' + sd_, 'LowerLeg.' + sd_):
        k = gix[bn]
        moved = Wm[:, k] * (1 - aff) * region
        Wm[:, k] -= moved; Wm[:, gix['Hips']] += moved
Wm /= Wm.sum(1, keepdims=True)
# шнуры, завязанные под коленом и свисающие по голени, целиком на голени (иначе тянутся полосой между бедром и голенью)
for sd_ in 'LR':
    dsh = seg_d(V, J['knee' + sd_], J['ank' + sd_])
    tie = red & (V[:, 2] < 0.45) & (dsh < 0.17) & (np.sign(V[:, 0]) == (1 if sd_ == 'L' else -1))
    Wm[tie] = 0.0; Wm[tie, gix['LowerLeg.' + sd_]] = 1.0
    print('ШНУРЫ ГОЛЕНИ', sd_, int(tie.sum()))
# полы плаща на своих костях (Coat.*: от пояса к подолу) — в игре их качает пружина CoatSpring (инерция, бёдра раздвигают)
COAT = {'Coat.FL': ((0.09, -0.12, 0.90), (0.10, -0.15, 0.48)), 'Coat.FR': ((-0.09, -0.12, 0.90), (-0.10, -0.15, 0.48)),
        'Coat.BL': ((0.08, 0.15, 0.90), (0.09, 0.20, 0.46)), 'Coat.BR': ((-0.08, 0.15, 0.90), (-0.09, 0.20, 0.46)),
        'Coat.SL': ((0.20, 0.0, 0.92), (0.24, 0.02, 0.50)), 'Coat.SR': ((-0.20, 0.0, 0.92), (-0.24, 0.02, 0.50))}
bpy.ops.object.select_all(action='DESELECT'); rig.select_set(True); bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
for nm_, (h_, t_) in COAT.items():
    eb = rig.data.edit_bones.new(nm_); eb.head = h_; eb.tail = t_; eb.parent = rig.data.edit_bones['Hips']; eb.use_deform = True
bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); bpy.context.view_layer.objects.active = body
coat_names = list(COAT)
for nm_ in coat_names:
    names_all.append(nm_); gix[nm_] = len(names_all) - 1
Wm = np.concatenate([Wm, np.zeros((nv, len(coat_names)))], 1)
dleg = np.minimum.reduce([seg_d(V, J['hip' + q], J['knee' + q]) for q in 'LR'] + [seg_d(V, J['knee' + q], J['ank' + q]) for q in 'LR'])
coatness = np.clip((dleg - 0.115) / 0.045, 0, 1)
hf = np.clip((0.88 - V[:, 2]) / 0.28, 0, 1); hf = hf * hf * (3 - 2 * hf)
frac = coatness * hf * ((V[:, 2] > 0.40) & (V[:, 2] < 0.95)) * (np.array([i not in set(sw_idx) for i in range(nv)]) if sw_idx else 1.0)
ang_v = np.arctan2(V[:, 0], -(V[:, 1] - 0.02))            # 0 — спереди, +π/2 — слева (+X)
ang_b = {nm_: math.atan2(0.5 * (h_[0] + t_[0]), -(0.5 * (h_[1] + t_[1]) - 0.02)) for nm_, (h_, t_) in COAT.items()}
aff_c = np.stack([np.maximum(0, np.cos(ang_v - ang_b[nm_])) ** 4 for nm_ in coat_names], 1)
aff_c /= np.maximum(aff_c.sum(1, keepdims=True), 1e-6)
mv = Wm[:, gix['Hips']] * frac
Wm[:, gix['Hips']] -= mv
for j_, nm_ in enumerate(coat_names): Wm[:, gix[nm_]] += mv * aff_c[:, j_]
print('ПОЛЫ ПЛАЩА: вершин с долей костей Coat', int((mv > 0.05).sum()))
E = np.empty(len(body.data.edges) * 2, dtype=np.int64); body.data.edges.foreach_get('vertices', E); E = E.reshape(-1, 2)
deg = np.bincount(E.ravel(), minlength=nv).astype(float)
lowbody = (V[:, 2] > 0.30) & (V[:, 2] < 1.08)
for it in range(int(os.environ.get('SMOOTH_IT', '12'))):
    acc = np.zeros_like(Wm)
    np.add.at(acc, E[:, 0], Wm[E[:, 1]]); np.add.at(acc, E[:, 1], Wm[E[:, 0]])
    avg = acc / np.maximum(deg, 1)[:, None]
    Wm[lowbody] = 0.5 * Wm[lowbody] + 0.5 * avg[lowbody]
Wm /= Wm.sum(1, keepdims=True)
for g in list(body.vertex_groups): body.vertex_groups.remove(g)
grp = [body.vertex_groups.new(name=nm) for nm in names_all]
for vi in range(nv):
    for k in np.nonzero(Wm[vi] > 0.005)[0]: grp[k].add([vi], float(Wm[vi, k]), 'REPLACE')
if sw_idx:   # ножны после сглаживания снова только на тазу
    for g in body.vertex_groups:
        if g.name != 'Hips': g.remove(sw_idx)
    body.vertex_groups['Hips'].add(sw_idx, 1.0, 'REPLACE')
print('ПЛАЩ: сглаживание весов низа тела,', int(lowbody.sum()), 'вершин')
# не больше 4 влияний на вершину, нормировка
gi = {g.index: g for g in body.vertex_groups}
for v in body.data.vertices:
    ws = sorted([(ge.weight, ge.group) for ge in v.groups], reverse=True)
    keep = ws[:4]; tot = sum(w for w, _ in keep) or 1.0
    for w, g in ws[4:]: gi[g].remove([v.index])
    for w, g in keep:
        if w / tot < 0.01: gi[g].remove([v.index])
        else: gi[g].add([v.index], w / tot, 'REPLACE')
body.parent = rig
am = body.modifiers.new('Armature', 'ARMATURE'); am.object = rig
marker = bpy.data.objects.new('FrontMarker', None); marker.location = (0, -0.5, 0.92)
sc.collection.objects.link(marker); marker.parent = rig

# ------------------------------------------------------------------ 6. текстуры
from PIL import Image as PI
def find(sfx):
    for f in os.listdir(TEX_IN):
        if f.endswith(sfx): return os.path.join(TEX_IN, f)
    return None
alb = find('_texture.png'); nrm = find('_normal.png'); met = find('_metallic.png'); rgh = find('_roughness.png')
PI.open(alb).convert('RGB').save(os.path.join(TEX_OUT, 'hero3_albedo.jpg'), quality=92)
PI.open(nrm).convert('RGB').save(os.path.join(TEX_OUT, 'hero3_normal.jpg'), quality=95)
M = np.asarray(PI.open(met).convert('L').resize((1024, 1024), PI.LANCZOS)).astype(np.float32)
R = np.asarray(PI.open(rgh).convert('L').resize((1024, 1024), PI.LANCZOS)).astype(np.float32)
ms = np.stack([M, M, M, 255.0 - R], -1).astype(np.uint8)
PI.fromarray(ms, 'RGBA').save(os.path.join(TEX_OUT, 'hero3_metal_smooth.png'))
print('ТЕКСТУРЫ: металл', round(float(M.mean()) / 255, 3), 'гладкость', round(1 - float(R.mean()) / 255, 3))

# ------------------------------------------------------------------ 7. клипы и экспорт
for act in list(bpy.data.actions): bpy.data.actions.remove(act)
src_ronin = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'build_ronin.py'), encoding='utf-8').read()
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
print('ЭКСПОРТ:', FBX_PATH, os.path.getsize(FBX_PATH) // 1024, 'КБ, вершин', len(body.data.vertices), 'хват L', np.round(gripL, 3), 'R', np.round(gripR, 3))
