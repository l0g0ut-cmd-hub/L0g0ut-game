# Надеваемая вещь редактора персонажей (причёска, шляпа, шлем, маска, повязка, капюшон) — Meshy GLB → подогнана к голове основы.
# blender -b --factory-startup --python tools/blender/build_accessory.py
#   ACC_GLB   модель Meshy (image-to-3d)            ACC_NAME  имя результата (Hair_Topknot)
#   ACC_KIND  hair | hat | helmet | hood | mask | band
#   BASE_FBX  основа (Assets/_Project/Models/BaseMale_Player.fbx)    BASE_TAG  M | F
#   ACC_W     ширина вещи в ширинах головы       ACC_DZ  сдвиг по высоте, м      ACC_DY  сдвиг вперёд(−)/назад(+), м
#   ACC_ROT   поворот вокруг вертикали, градусы (если Meshy повернул вещь как на картинке)    ACC_TRIS  предел треугольников
# Выход: OUT/<ACC_NAME>_<BASE_TAG>.fbx — меш в пространстве тела основы (как вершины SkinnedMesh основы: в игре вещь
# ставится под кость Head по bindpose этой кости), OUT/<ACC_NAME>_albedo.jpg, превью OUT/<ACC_NAME>_<BASE_TAG>_{front,side}.png.
import bpy, os, math, numpy as np
from mathutils import Vector, Matrix

GLB = os.environ['ACC_GLB']; NAME = os.environ['ACC_NAME']; KIND = os.environ.get('ACC_KIND', 'hat')
BASE = os.environ['BASE_FBX']; TAG = os.environ.get('BASE_TAG', 'M')
OUT = os.environ.get('ACC_OUT', 'out/Accessories'); os.makedirs(OUT, exist_ok=True)
WF = float(os.environ.get('ACC_W', '0') or 0); DZ = float(os.environ.get('ACC_DZ', '0') or 0); DY = float(os.environ.get('ACC_DY', '0') or 0)
ROT = float(os.environ.get('ACC_ROT', '0') or 0); TRIS = int(os.environ.get('ACC_TRIS', '8000') or 8000)
def log(*a): print('ACC', NAME, TAG, *a, flush=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene

# ------------------------------------------------------------------ 1. основа: голова в позе покоя
bpy.ops.import_scene.fbx(filepath=BASE, automatic_bone_orientation=False, ignore_leaf_bones=False)
rig = next(o for o in sc.objects if o.type == 'ARMATURE')
rig.data.pose_position = 'REST'
for o in sc.objects:
    if o.animation_data: o.animation_data.action = None
bpy.context.view_layer.update()
body = max((o for o in sc.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
gi = body.vertex_groups['Head'].index
mw = body.matrix_world
V = np.array([(mw @ v.co)[:] for v in body.data.vertices])
hw = np.zeros(len(V))
for v in body.data.vertices:
    for g in v.groups:
        if g.group == gi: hw[v.index] = g.weight
H = V[hw > 0.6]
top = H[:, 2].max(); chin = np.percentile(H[:, 2], 2)
# ширина и глубина головы — на уровне глаз (без ушей и шеи): срез 40…60 % высоты головы
band = H[(H[:, 2] > chin + 0.45 * (top - chin)) & (H[:, 2] < chin + 0.7 * (top - chin))]
cx = float(np.median(band[:, 0])); cy = float((band[:, 1].min() + band[:, 1].max()) * 0.5)
hwid = float(np.percentile(band[:, 0], 98) - np.percentile(band[:, 0], 2))
front = float(np.percentile(band[:, 1], 2))   # лицо смотрит в −Y
hh = top - chin
eye = chin + 0.56 * hh; nose = chin + 0.36 * hh; brow = chin + 0.68 * hh
log('голова: верх %.3f подбородок %.3f ширина %.3f центр (%.3f, %.3f) лицо y %.3f' % (top, chin, hwid, cx, cy, front))

# ------------------------------------------------------------------ 2. вещь
before = set(sc.objects)
bpy.ops.import_scene.gltf(filepath=GLB)
new = [o for o in sc.objects if o not in before]
meshes = [o for o in new if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
acc = bpy.context.view_layer.objects.active
for o in new:
    if o != acc and o.name in sc.objects and o.type != 'MESH':
        try: bpy.data.objects.remove(o)
        except Exception: pass
acc.parent = None
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
# лишние треугольники — долой (на телефоне голова мелкая)
nt = sum(len(p.vertices) - 2 for p in acc.data.polygons)
if nt > TRIS:
    m = acc.modifiers.new('dec', 'DECIMATE'); m.ratio = TRIS / nt
    bpy.ops.object.modifier_apply(modifier='dec')
    log('треугольников', nt, '→', sum(len(p.vertices) - 2 for p in acc.data.polygons))
def rot_z(P, a):   # поворот вершин вокруг вертикали (против часовой, если смотреть сверху)
    c, sn = math.cos(a), math.sin(a)
    return np.stack([P[:, 0] * c - P[:, 1] * sn, P[:, 0] * sn + P[:, 1] * c, P[:, 2]], 1)
P = np.array([v.co[:] for v in acc.data.vertices])
if ROT: P = rot_z(P, math.radians(ROT))
if KIND == 'band' and not ROT:
    # узел и хвосты повязки — назад: куда свисает нижняя десятая часть вершин, туда и затылок (+Y)
    c0 = P.mean(0); low = P[P[:, 2] < np.percentile(P[:, 2], 10)]
    dv = low.mean(0) - c0; ang = math.atan2(dv[0], dv[1])   # угол от +Y к направлению хвостов
    if np.hypot(dv[0], dv[1]) > 1e-4:
        P = rot_z(P, ang)
        log('повязка: хвосты повёрнуты назад на %.0f°' % math.degrees(ang))
amin, amax = P.min(0), P.max(0); asz = amax - amin
log('вещь: размер %.3f × %.3f × %.3f' % tuple(asz))

# ------------------------------------------------------------------ 3. подгонка к голове (по виду вещи)
DEF = {   # ширина в ширинах головы; куда ставится
    'hair':   1.42, 'hat': 3.0, 'helmet': 1.75, 'hood': 1.6, 'mask': 1.12, 'band': 1.22,
}
wf = WF or DEF.get(KIND, 1.2)
s = wf * hwid / max(1e-6, asz[0])
P = (P - np.array([(amin[0] + amax[0]) * 0.5, (amin[1] + amax[1]) * 0.5, amin[2]])) * s   # центр по x/y, низ — 0
h = asz[2] * s; d = asz[1] * s
if KIND == 'hair':      # макушка чуть выше головы, центр чуть назад
    off = (cx, cy + 0.08 * hwid, top + 0.012 - h)
elif KIND == 'hat':     # конусная шляпа: опирается на темя там, где конус шире головы (r(z) — внешний радиус на высоте z)
    rr = np.sqrt(P[:, 0] ** 2 + P[:, 1] ** 2); zs = P[:, 2]
    need = 0.62 * hwid; zstar = 0.0
    for zb in np.linspace(0, h, 60):
        sel = (zs >= zb) & (zs < zb + h / 60 + 1e-6)
        if sel.any() and rr[sel].max() >= need: zstar = zb
    off = (cx, cy, top - 0.015 - zstar)
    log('шляпа: опора на высоте %.3f из %.3f' % (zstar, h))
elif KIND == 'helmet':  # шлем: купол над макушкой, назатыльник ниже затылка
    off = (cx, cy + 0.06 * hwid, top + 0.03 - h * 0.70)
elif KIND == 'hood':    # капюшон: верх над макушкой, полы на плечи
    off = (cx, cy + 0.1 * hwid, top + 0.035 - h)
elif KIND == 'mask':    # маска на нижнюю половину лица: верх — у переносицы, передом к лицу
    off = (cx, front + d * 0.5 - 0.012, eye - 0.01 - h)
else:                   # повязка: верх кольца — у линии волос (хвосты узла свисают ниже)
    off = (cx, cy, top - 0.035 - h)
off = np.array(off) + np.array([0.0, DY, DZ])
P = P + off
for i, v in enumerate(acc.data.vertices): v.co = Vector(P[i])
acc.data.update()
# всё, что ушло внутрь головы (пряди, кольцо повязки, края капюшона и маски), выталкивается наружу на 5 мм
if KIND != 'hat':
    sw = acc.modifiers.new('wrap', 'SHRINKWRAP'); sw.target = body; sw.wrap_method = 'NEAREST_SURFACEPOINT'
    sw.wrap_mode = 'OUTSIDE'; sw.offset = 0.005
    bpy.context.view_layer.objects.active = acc; bpy.ops.object.modifier_apply(modifier='wrap')
    P = np.array([v.co[:] for v in acc.data.vertices])
log('масштаб %.3f, низ %.3f, верх %.3f' % (s, P[:, 2].min(), P[:, 2].max()))

# ------------------------------------------------------------------ 4. текстура
img = None
for mt in acc.data.materials:
    if mt and mt.use_nodes:
        for n in mt.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image: img = n.image; break
    if img: break
if img:
    sc.render.image_settings.file_format = 'JPEG'; sc.render.image_settings.quality = 90
    try: img.save_render(filepath=os.path.join(OUT, NAME.lower() + '_albedo.jpg'))
    except Exception as e: log('текстура не сохранилась', e)
    sc.render.image_settings.file_format = 'PNG'
for mt in acc.data.materials:
    if mt: mt.name = 'ACC_' + NAME
while len(acc.data.materials) > 1: acc.data.materials.pop(index=1)

# ------------------------------------------------------------------ 5. экспорт: в пространстве тела основы
inv = body.matrix_world.inverted()
acc.matrix_world = Matrix.Identity(4)
for v in acc.data.vertices: v.co = inv @ v.co
acc.matrix_world = body.matrix_world.copy()
acc.name = NAME
bpy.ops.object.select_all(action='DESELECT'); acc.select_set(True); bpy.context.view_layer.objects.active = acc
fbx = os.path.join(OUT, NAME + '_' + TAG + '.fbx')
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={'MESH'}, apply_unit_scale=True, bake_space_transform=False,
                         mesh_smooth_type='FACE', path_mode='STRIP', embed_textures=False, bake_anim=False)
log('ЭКСПОРТ', fbx, os.path.getsize(fbx) // 1024, 'КБ')

# ------------------------------------------------------------------ 6. превью на голове основы
sc.render.engine = 'CYCLES'; sc.cycles.samples = 10; sc.cycles.device = 'CPU'
try: sc.cycles.use_denoising = False
except Exception: pass
sc.render.resolution_x = 360; sc.render.resolution_y = 420
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs[0].default_value = (0.2, 0.2, 0.22, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 0.9
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.0
sun.rotation_euler = (math.radians(50), 0, math.radians(-30)); sc.collection.objects.link(sun)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = max(0.55, hwid * wf * 1.25, (P[:, 2].max() - min(P[:, 2].min(), chin - 0.1)) * 1.3)
tgt = Vector((cx, cy, (max(P[:, 2].max(), top) + min(P[:, 2].min(), chin - 0.1)) * 0.5))
for tag, pos in (('front', (cx, cy - 3, tgt.z)), ('side', (cx + 3, cy, tgt.z)), ('q', (cx + 2.1, cy - 2.1, tgt.z + 0.3))):
    cam.location = pos; cam.rotation_euler = (tgt - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(OUT, NAME + '_' + TAG + '_' + tag + '.png'); bpy.ops.render.render(write_still=True)
log('ГОТОВО')
