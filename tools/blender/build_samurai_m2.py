"""
Игрок «Путь клинка» из модели samurai-rigged.fbx (один меш 174k вершин, 2K-текстуры внутри FBX, автоскелет в стиле Mixamo,
клипы walk/run). Приводит к нашему скелету из 20 костей — те же имена, что у остальных моделей (HeroRigDriver, IK, Animator, клипы).
Запуск: SAMURAI2_FBX=…/samurai-rigged.fbx HERO_OUT=… TEX_OUT=…/Assets/_Project/Art/Samurai2 python3 build_samurai_m2.py

 1. Decimate до ~TARGET вершин (мобильный бюджет; детали остаются в карте нормалей), UV и веса сохраняются;
 2. пальцы сгибаются в хват рукояти и запекаются в меш;
 3. веса 52 костей сливаются в наши 20 (не больше 4 влияний на вершину);
 4. масштаб: макушка ≈ 1.72 м; лицо в -Y, левая сторона = +X;
 5. текстуры из FBX: base color и нормали — как есть (JPG), metallic+roughness → одна карта Unity (R = металл, A = гладкость);
 6. клипы Idle/WalkF/Jump/Hit — код из build_ronin.py.
"""
import bpy, math, os
import numpy as np
from mathutils import Vector, Matrix, Quaternion

SRC = os.environ['SAMURAI2_FBX']
OUT_DIR = os.environ.get('HERO_OUT', '/tmp/hero'); os.makedirs(OUT_DIR, exist_ok=True)
TEX_OUT = os.environ.get('TEX_OUT', os.path.join(OUT_DIR, 'Samurai2')); os.makedirs(TEX_OUT, exist_ok=True)
FBX_PATH = os.path.join(OUT_DIR, 'Samurai2_Player.fbx'); BLEND_PATH = os.path.join(OUT_DIR, 'Samurai2_Player.blend')
TARGET = int(os.environ.get('TARGET', '60000'))
HEAD_TOP = float(os.environ.get('HEAD_TOP', '1.72'))
CURL = [float(x) for x in os.environ.get('CURL', '80,95,60').split(',')]
THUMB = [float(x) for x in os.environ.get('THUMB', '18,30,30').split(',')]
TEX_SIZE = int(os.environ.get('TEX_SIZE', '1024'))

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene; sc.render.fps = 30
bpy.ops.import_scene.fbx(filepath=SRC)
src = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
for o in list(bpy.data.objects):
    if o.type in ('CAMERA', 'LIGHT') or o.name == 'Cube': bpy.data.objects.remove(o, do_unlink=True)
body = next(o for o in bpy.data.objects if o.type == 'MESH')
if src.animation_data: src.animation_data.action = None
for pb in src.pose.bones: pb.matrix_basis = Matrix.Identity(4)
bpy.context.view_layer.update()

# ------------------------------------------------------------------ текстуры
img = {}
for im in bpy.data.images:
    for key in ('basecolor', 'normal', 'metallic', 'roughness'):
        if im.name.endswith(key) or im.name.endswith('_' + key): img[key] = im
print('ТЕКСТУРЫ:', {k: (v.name, tuple(v.size)) for k, v in img.items()})
def write_raw(im, path):
    with open(path, 'wb') as f: f.write(im.packed_file.data if im.packed_file else open(bpy.path.abspath(im.filepath), 'rb').read())
write_raw(img['basecolor'], os.path.join(TEX_OUT, 'samurai2_albedo.jpg'))
write_raw(img['normal'], os.path.join(TEX_OUT, 'samurai2_normal.jpg'))
def px(im, size):
    im2 = im.copy(); im2.scale(size, size)
    a = np.array(im2.pixels[:], dtype=np.float32).reshape(size, size, 4); bpy.data.images.remove(im2); return a
met = px(img['metallic'], TEX_SIZE)[:, :, 0]; rough = px(img['roughness'], TEX_SIZE)[:, :, 0]
ms = np.zeros((TEX_SIZE, TEX_SIZE, 4), np.float32); ms[:, :, 0] = met; ms[:, :, 1] = met; ms[:, :, 2] = met; ms[:, :, 3] = 1.0 - rough
out = bpy.data.images.new('ms', TEX_SIZE, TEX_SIZE, alpha=True); out.pixels = ms.ravel()
out.filepath_raw = os.path.join(TEX_OUT, 'samurai2_metal_smooth.png'); out.file_format = 'PNG'; out.save()
print('МЕТАЛЛ: среднее', round(float(met.mean()), 3), 'гладкость', round(float((1 - rough).mean()), 3))

# ------------------------------------------------------------------ пальцы в хват (кисти в A-позе: ладонь к бедру)
def curl(name, deg, side):
    pb = src.pose.bones.get(name)
    if pb is None: return
    hd = src.matrix_world @ pb.head; tl = src.matrix_world @ pb.tail
    d = (tl - hd).normalized()
    n = Vector((-side, 0, 0)); n = (n - d * n.dot(d)).normalized()     # нормаль ладони: к оси тела
    ax = d.cross(n)
    R = Matrix.Rotation(math.radians(deg), 4, ax.normalized())
    pb.matrix = src.matrix_world.inverted() @ Matrix.Translation(hd) @ R @ Matrix.Translation(-hd) @ src.matrix_world @ pb.matrix
    bpy.context.view_layer.update()
for sd, sg in (('Left', 1), ('Right', -1)):
    for f in ('Index', 'Middle', 'Ring', 'Pinky'):
        for i, dg in enumerate(CURL): curl(f'{sd}Hand{f}{i + 1}', dg, sg)
    for i, dg in enumerate(THUMB): curl(f'{sd}HandThumb{i + 1}', dg, sg)
bpy.context.view_layer.objects.active = body
for m in list(body.modifiers):
    if m.type == 'ARMATURE': bpy.ops.object.modifier_apply(modifier=m.name)

# ------------------------------------------------------------------ Decimate (веса и UV переносятся)
n0 = len(body.data.vertices)
dm = body.modifiers.new('dec', 'DECIMATE'); dm.ratio = min(1.0, TARGET / n0); dm.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier='dec')
print('ВЕРШИН:', n0, '→', len(body.data.vertices))

# ------------------------------------------------------------------ суставы, масштаб, центр
def J(name, end='head'):
    b = src.data.bones[name]; return np.array(src.matrix_world @ (b.head_local if end == 'head' else b.tail_local))
yc = J('Hips')[1]
s = HEAD_TOP / J('head_end')[2]
def P(v): v = np.array(v, float); return Vector((v[0] * s, (v[1] - yc) * s, v[2] * s))
mw = body.matrix_world.copy(); body.parent = None
body.data.transform(Matrix.Scale(s, 4) @ Matrix.Translation((0, -yc, 0)) @ mw); body.matrix_world = Matrix.Identity(4)
print('МАСШТАБ', round(s, 4))

# ------------------------------------------------------------------ веса → наш скелет
MAP = {'Hips': 'Hips', 'Spine02': 'Spine', 'Spine01': 'Spine', 'Spine': 'Chest', 'neck': 'Neck', 'Head': 'Head', 'head_end': 'Head', 'headfront': 'Head'}
for sd, s2 in (('Left', 'L'), ('Right', 'R')):
    MAP.update({sd + 'Shoulder': 'Shoulder.' + s2, sd + 'Arm': 'UpperArm.' + s2, sd + 'ForeArm': 'Forearm.' + s2, sd + 'Hand': 'Hand.' + s2,
                sd + 'UpLeg': 'UpperLeg.' + s2, sd + 'Leg': 'LowerLeg.' + s2, sd + 'Foot': 'Foot.' + s2, sd + 'ToeBase': 'Foot.' + s2})
def target_of(g):
    if g in MAP: return MAP[g]
    for sd, s2 in (('Left', 'L'), ('Right', 'R')):
        if g.startswith(sd + 'Hand'): return 'Hand.' + s2
    return None
names = ['Hips', 'Spine', 'Chest', 'Neck', 'Head'] + [f'{b}.{sd}' for sd in 'LR' for b in ('Shoulder', 'UpperArm', 'Forearm', 'Hand', 'UpperLeg', 'LowerLeg', 'Foot')]
ix = {n: i for i, n in enumerate(names)}
gi = {g.index: g.name for g in body.vertex_groups}
W = np.zeros((len(body.data.vertices), len(names))); miss = set()
for v in body.data.vertices:
    for ge in v.groups:
        t = target_of(gi[ge.group])
        if t is None: miss.add(gi[ge.group]); continue
        W[v.index, ix[t]] += ge.weight
print('НЕ СОПОСТАВЛЕНЫ:', sorted(miss))
for g in list(body.vertex_groups): body.vertex_groups.remove(g)
groups = [body.vertex_groups.new(name=n) for n in names]
tot = W.sum(axis=1)
for vi in range(len(W)):
    if tot[vi] <= 1e-6: groups[0].add([vi], 1.0, 'REPLACE'); continue
    top = np.argsort(-W[vi])[:4]; tw = W[vi, top].sum()
    for k in top:
        w = W[vi, k] / tw
        if w > 0.01: groups[k].add([vi], float(w), 'REPLACE')

# ------------------------------------------------------------------ наш скелет
bpy.ops.object.armature_add(enter_editmode=True, location=(0, 0, 0))
rig = bpy.context.object; rig.name = 'Samurai_Rig'; rig.data.name = 'Samurai_Armature'
for b in list(rig.data.edit_bones): rig.data.edit_bones.remove(b)
def bone(name, head, tail, parent=None, deform=True):
    b = rig.data.edit_bones.new(name); b.head = head; b.tail = tail
    if parent: b.parent = rig.data.edit_bones[parent]
    b.use_deform = deform
hip = P(J('Hips')); hip.y = 0
bone('Root', (0, 0, 0.05), (0, 0, 0.25), None, False)
bone('Hips', hip, P(J('Spine02')), 'Root')
bone('Spine', P(J('Spine02')), P(J('Spine')), 'Hips')
bone('Chest', P(J('Spine')), P(J('neck')), 'Spine')
bone('Neck', P(J('neck')), P(J('Head')), 'Chest')
bone('Head', P(J('Head')), P(J('head_end')), 'Neck')
for sd, s2 in (('Left', 'L'), ('Right', 'R')):
    bone('Shoulder.' + s2, P(J(sd + 'Shoulder')), P(J(sd + 'Arm')), 'Chest')
    bone('UpperArm.' + s2, P(J(sd + 'Arm')), P(J(sd + 'ForeArm')), 'Shoulder.' + s2)
    bone('Forearm.' + s2, P(J(sd + 'ForeArm')), P(J(sd + 'Hand')), 'UpperArm.' + s2)
    bone('Hand.' + s2, P(J(sd + 'Hand')), P(J(sd + 'HandMiddle1')), 'Forearm.' + s2)
    bone('UpperLeg.' + s2, P(J(sd + 'UpLeg')), P(J(sd + 'Leg')), 'Hips')
    bone('LowerLeg.' + s2, P(J(sd + 'Leg')), P(J(sd + 'Foot')), 'UpperLeg.' + s2)
    bone('Foot.' + s2, P(J(sd + 'Foot')), P(J(sd + 'ToeBase', 'tail')), 'LowerLeg.' + s2)
bpy.ops.object.mode_set(mode='OBJECT')
bpy.data.objects.remove(src, do_unlink=True)
body.parent = rig
m = body.modifiers.new('Armature', 'ARMATURE'); m.object = rig
body.name = 'Samurai_Body'; body.data.name = 'Samurai_Body_Mesh'
for i, mt in enumerate(body.data.materials):
    if mt: mt.name = 'M2_Body'

marker = bpy.data.objects.new('FrontMarker', None); marker.location = (0, -0.5, hip.z)
sc.collection.objects.link(marker); marker.parent = rig

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
print('ЭКСПОРТ:', FBX_PATH, os.path.getsize(FBX_PATH) // 1024, 'КБ, вершин', len(body.data.vertices))
