"""
Игрок «Путь клинка» из художественной модели самурая (CGTrader, AdrigonStudios; риг Rigify, 274 DEF-кости, веса художника).
Запуск: SAMURAI_FBX=…/Samurai.fbx HERO_OUT=… python3 build_samurai_cg.py   (нужен модуль bpy)

Что делает:
 1. убирает служебное (камера, свет, виджеты Rigify, metarig) и катану из модели — в руке будет игровая катана;
 2. сгибает пальцы в хват рукояти и запекает это в меш (веса художника сохраняются);
 3. сливает веса 274 DEF-костей в наш скелет из 20 костей (те же имена, что у остальных моделей:
    HeroRigDriver, IK, Animator и клипы работают без изменений); на вершину — не больше 4 влияний;
 4. масштаб под зоны поражения (макушка ≈ 1.72 м), лицо в -Y, левая сторона = +X;
 5. руки из T-позы опускаются в A-позу и это становится позой покоя (меньше перекручивания плеч);
 6. клипы Idle/WalkF/Jump/Hit — тот же код, что у Ronin (build_ronin.py);
 7. материалы называются SAM_<папка текстур> — Unity подставляет текстуры из Assets/_Project/Art/Samurai/<папка>/.
"""
import bpy, math, os, re
import numpy as np
from mathutils import Vector, Matrix, Quaternion

SRC = os.environ['SAMURAI_FBX']
OUT_DIR = os.environ.get('HERO_OUT', '/tmp/hero')
os.makedirs(OUT_DIR, exist_ok=True)
FBX_PATH = os.path.join(OUT_DIR, 'Samurai_Player.fbx')
BLEND_PATH = os.path.join(OUT_DIR, 'Samurai_Player.blend')
HEAD_TOP = float(os.environ.get('HEAD_TOP', '1.72'))       # макушка (верх кости головы) в метрах
A_POSE = float(os.environ.get('A_POSE', '50'))             # на сколько опустить руки из T-позы, град.
CURL = [float(x) for x in os.environ.get('CURL', '62,78,52').split(',')]   # сгиб фаланг пальцев в хвате, град.
THUMB = [float(x) for x in os.environ.get('THUMB', '18,30,30').split(',')]
CURL_SIGN = float(os.environ.get('CURL_SIGN', '1'))

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.fps = 30
bpy.ops.import_scene.fbx(filepath=SRC)
src = bpy.data.objects['rig']
for o in list(bpy.data.objects):
    if o.type in ('CAMERA', 'LIGHT') or o.name.startswith('WGT') or o.name in ('Cube', 'metarig', 'KatanaLP') or (o.type == 'EMPTY' and o.name.startswith('WGTS')):
        bpy.data.objects.remove(o, do_unlink=True)
meshes = [o for o in bpy.data.objects if o.type == 'MESH' and o.parent == src]
print('МЕШЕЙ:', len(meshes), [o.name for o in meshes])

# ------------------------------------------------------------------ поза покоя исходного рига, без shape keys
if src.animation_data: src.animation_data.action = None
for pb in src.pose.bones:
    pb.matrix_basis = Matrix.Identity(4)
for o in meshes:
    if o.data.shape_keys:
        o.shape_key_clear()
bpy.context.view_layer.update()

# ------------------------------------------------------------------ 2) пальцы в хват: каждая фаланга поворачивается к ладони (ладони в T-позе смотрят вниз)
# вокруг оси «палец × нормаль ладони» в мировых координатах — у фаланг Rigify крен костей разный, локальная X не годится
PALM_N = Vector((0, 0, -1))
def curl(name, deg):
    pb = src.pose.bones.get(name)
    if pb is None: return
    hd = src.matrix_world @ pb.head; tl = src.matrix_world @ pb.tail
    ax = (tl - hd).normalized().cross(PALM_N)
    if ax.length < 1e-4: return
    R = Matrix.Rotation(math.radians(deg * CURL_SIGN), 4, ax.normalized())
    pb.matrix = src.matrix_world.inverted() @ Matrix.Translation(hd) @ R @ Matrix.Translation(-hd) @ src.matrix_world @ pb.matrix
    bpy.context.view_layer.update()
for s_ in ('L', 'R'):
    for f in ('f_index', 'f_middle', 'f_ring', 'f_pinky'):
        for i, d in enumerate(CURL):
            curl(f'DEF-{f}.0{i + 1}.{s_}', d)
    for i, d in enumerate(THUMB):
        curl(f'DEF-thumb.0{i + 1}.{s_}', d)
for o in meshes:
    bpy.context.view_layer.objects.active = o
    for m in list(o.modifiers):
        if m.type == 'ARMATURE':
            bpy.ops.object.modifier_apply(modifier=m.name)

# ------------------------------------------------------------------ суставы исходного рига (мир)
def J(name, end='head'):
    b = src.data.bones[name]
    return np.array(src.matrix_world @ (b.head_local if end == 'head' else b.tail_local))
yc = J('DEF-spine')[1]                                   # центр тела по глубине
s = HEAD_TOP / (J('DEF-spine.006', 'tail')[2])           # масштаб
def P(v): v = np.array(v, float); return Vector(((v[0]) * s, (v[1] - yc) * s, v[2] * s))
print('МАСШТАБ', round(s, 4), 'центр y', round(yc, 3))

# меш в мировые координаты → масштаб/центр
T = Matrix.Scale(s, 4) @ Matrix.Translation((0, -yc, 0))
for o in meshes:
    mw = o.matrix_world.copy()
    o.parent = None
    o.data.transform(T @ mw)
    o.matrix_world = Matrix.Identity(4)

# ------------------------------------------------------------------ 3) карта DEF → наш скелет
def target_of(g):
    n = g
    if re.match(r'DEF-spine$', n) or n.startswith('DEF-pelvis'): return 'Hips'
    if n in ('DEF-spine.001', 'DEF-spine.002'): return 'Spine'
    if n == 'DEF-spine.003' or n.startswith('DEF-breast'): return 'Chest'
    if n in ('DEF-spine.004', 'DEF-spine.005'): return 'Neck'
    if n == 'DEF-spine.006' or n == 'head': return 'Head'
    m = re.match(r'DEF-(\w+?)(?:\.\d+)?\.([LR])(?:\.\d+)?$', n)
    if m:
        part, side = m.group(1), m.group(2)
        mp = {'shoulder': 'Shoulder', 'upper_arm': 'UpperArm', 'forearm': 'Forearm', 'hand': 'Hand', 'palm': 'Hand', 'thumb': 'Hand',
              'f_index': 'Hand', 'f_middle': 'Hand', 'f_ring': 'Hand', 'f_pinky': 'Hand',
              'thigh': 'UpperLeg', 'shin': 'LowerLeg', 'foot': 'Foot', 'toe': 'Foot'}
        if part in mp: return mp[part] + '.' + side
    if n in ('Wakizashi', 'Katana'): return 'Hips'
    if n.startswith('DEF-'): return 'Head'           # лицо: брови, веки, челюсть, уши, нос, губы, язык
    return None

names = ['Hips', 'Spine', 'Chest', 'Neck', 'Head'] + [f'{b}.{sd}' for sd in 'LR' for b in ('Shoulder', 'UpperArm', 'Forearm', 'Hand', 'UpperLeg', 'LowerLeg', 'Foot')]
unmapped = set()
for o in meshes:
    gi = {g.index: g.name for g in o.vertex_groups}
    W = np.zeros((len(o.data.vertices), len(names)))
    ix = {n: i for i, n in enumerate(names)}
    for v in o.data.vertices:
        for ge in v.groups:
            t = target_of(gi[ge.group])
            if t is None: unmapped.add(gi[ge.group]); continue
            W[v.index, ix[t]] += ge.weight
    for g in list(o.vertex_groups): o.vertex_groups.remove(g)
    groups = [o.vertex_groups.new(name=n) for n in names]
    tot = W.sum(axis=1)
    if (tot <= 1e-6).any(): print('  без весов:', o.name, int((tot <= 1e-6).sum()))
    for vi in range(len(W)):
        if tot[vi] <= 1e-6:
            groups[0].add([vi], 1.0, 'REPLACE'); continue
        top = np.argsort(-W[vi])[:4]
        tw = W[vi, top].sum()
        for k in top:
            w = W[vi, k] / tw
            if w > 0.01: groups[k].add([vi], float(w), 'REPLACE')
print('НЕ СОПОСТАВЛЕНЫ:', sorted(unmapped))

# ------------------------------------------------------------------ наш скелет по суставам исходного
bpy.ops.object.armature_add(enter_editmode=True, location=(0, 0, 0))
rig = bpy.context.object
rig.name = 'Samurai_Rig'; rig.data.name = 'Samurai_Armature'
for b in list(rig.data.edit_bones): rig.data.edit_bones.remove(b)
def bone(name, head, tail, parent=None, deform=True):
    b = rig.data.edit_bones.new(name); b.head = head; b.tail = tail
    if parent: b.parent = rig.data.edit_bones[parent]
    b.use_deform = deform
hip = P(J('DEF-spine')); hip.y = 0
bone('Root', (0, 0, 0.05), (0, 0, 0.25), None, False)
bone('Hips', hip, P(J('DEF-spine.001')), 'Root')
bone('Spine', P(J('DEF-spine.001')), P(J('DEF-spine.003')), 'Hips')
bone('Chest', P(J('DEF-spine.003')), P(J('DEF-spine.004')), 'Spine')
bone('Neck', P(J('DEF-spine.004')), P(J('DEF-spine.006')), 'Chest')
bone('Head', P(J('DEF-spine.006')), P(J('DEF-spine.006', 'tail')), 'Neck')
for sd in 'LR':
    bone('Shoulder.' + sd, P(J(f'DEF-shoulder.{sd}')), P(J(f'DEF-upper_arm.{sd}')), 'Chest')
    bone('UpperArm.' + sd, P(J(f'DEF-upper_arm.{sd}')), P(J(f'DEF-forearm.{sd}')), 'Shoulder.' + sd)
    bone('Forearm.' + sd, P(J(f'DEF-forearm.{sd}')), P(J(f'DEF-hand.{sd}')), 'UpperArm.' + sd)
    bone('Hand.' + sd, P(J(f'DEF-hand.{sd}')), P(J(f'DEF-palm.02.{sd}', 'tail')), 'Forearm.' + sd)
    bone('UpperLeg.' + sd, P(J(f'DEF-thigh.{sd}')), P(J(f'DEF-shin.{sd}')), 'Hips')
    bone('LowerLeg.' + sd, P(J(f'DEF-shin.{sd}')), P(J(f'DEF-foot.{sd}')), 'UpperLeg.' + sd)
    bone('Foot.' + sd, P(J(f'DEF-foot.{sd}')), P(J(f'DEF-toe.{sd}', 'tail')), 'LowerLeg.' + sd)
bpy.ops.object.mode_set(mode='OBJECT')
bpy.data.objects.remove(src, do_unlink=True)
for o in meshes:
    o.parent = rig
    m = o.modifiers.new('Armature', 'ARMATURE'); m.object = rig

# ------------------------------------------------------------------ 5) A-поза: руки вниз, запекаем как покой
PB = rig.pose.bones
for sd, sg in (('L', 1), ('R', -1)):
    pb = PB['UpperArm.' + sd]
    hd = rig.matrix_world @ pb.head
    Rw = Matrix.Rotation(math.radians(A_POSE * sg), 4, 'Y')   # вокруг оси вперёд-назад: кисть уходит вниз
    pb.matrix = Matrix.Translation(hd) @ Rw @ Matrix.Translation(-hd) @ pb.matrix
    bpy.context.view_layer.update()
for o in meshes:
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier='Armature')
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='POSE'); bpy.ops.pose.select_all(action='SELECT'); bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode='OBJECT')
for o in meshes:
    m = o.modifiers.new('Armature', 'ARMATURE'); m.object = rig

# ------------------------------------------------------------------ материалы SAM_<папка>
for o in meshes:
    for i, m in enumerate(o.data.materials):
        folder = None
        if m and m.use_nodes:
            for n in m.node_tree.nodes:
                if n.type == 'TEX_IMAGE' and n.image and 'Base_Color' in n.image.filepath:
                    folder = n.image.filepath.replace('\\', '/').split('texturen/')[-1].split('/')[0]
        if folder:
            nm = 'SAM_' + folder
            mat = bpy.data.materials.get(nm) or m
            mat.name = nm
            o.data.materials[i] = mat
print('МАТЕРИАЛЫ:', sorted({m.name for o in meshes for m in o.data.materials if m}))

# одна модель = один SkinnedMeshRenderer (один проход скиннинга вместо 21), материалы — подмешами
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
body = next(o for o in meshes if o.name == 'LP')
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
body.name = 'Samurai_Body'; body.data.name = 'Samurai_Body_Mesh'
meshes = [body]
print('ПОДМЕШЕЙ:', len(body.data.materials))

marker = bpy.data.objects.new('FrontMarker', None)
marker.location = (0, -0.5, hip.z)
sc.collection.objects.link(marker); marker.parent = rig

# ------------------------------------------------------------------ 6) клипы — тот же код, что у Ronin
for act in list(bpy.data.actions): bpy.data.actions.remove(act)   # клип Swing исходного рига и shape-key действия не нужны
src_ronin = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'build_ronin.py'), encoding='utf-8').read()
anim = src_ronin[src_ronin.index('# ------------------------------------------------------------------ анимации'):src_ronin.index('# ------------------------------------------------------------------ сохранение')]
exec(anim, {'bpy': bpy, 'math': math, 'Vector': Vector, 'Quaternion': Quaternion, 'Matrix': Matrix, 'rig': rig, 'sc': sc})

bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
bpy.ops.object.select_all(action='DESELECT')
for o in [rig, marker] + meshes: o.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=FBX_PATH, use_selection=True, object_types={'ARMATURE', 'MESH', 'EMPTY'},
    apply_unit_scale=True, bake_space_transform=False, add_leaf_bones=False, armature_nodetype='NULL',
    use_armature_deform_only=False, mesh_smooth_type='FACE', path_mode='STRIP', embed_textures=False,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0)
V = sum(len(o.data.vertices) for o in meshes)
print('ЭКСПОРТ:', FBX_PATH, os.path.getsize(FBX_PATH) // 1024, 'КБ, вершин', V)
