"""Боец так, как он стоит на поле пошагового боя: ортокамера под 34°, стойка (клип idle/stance), повёрнут к врагу
в три четверти (как в Tactics: направление (±1, 0.35) на экране). Прозрачный фон.
env: FBX, ALBEDO, OUT, NAME, SIDE (ours — смотрит вправо, enemy — влево). Модели смотрят в −Y."""
import bpy, os, math
from mathutils import Vector
FBX = os.environ['FBX']; ALB = os.environ.get('ALBEDO', ''); OUT = os.environ.get('OUT', 'out'); NAME = os.environ.get('NAME', 'unit')
SIDE = os.environ.get('SIDE', 'ours')
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
sc = bpy.context.scene
meshes = [o for o in sc.objects if o.type == 'MESH']
arms = [o for o in sc.objects if o.type == 'ARMATURE']
acts = [a.name for a in bpy.data.actions]
print('ACTIONS', acts)
pick = next((a for a in acts if 'stance' in a.lower()), None) or next((a for a in acts if 'idle' in a.lower() and 'idle1' not in a.lower()), None) or (acts[0] if acts else None)
for a in arms:
    if pick:
        if not a.animation_data: a.animation_data_create()
        a.animation_data.action = bpy.data.actions[pick]
sc.frame_set(12)
# поворот к врагу: перед модели (−Y) → (±1, −0.215) в плоскости XY
sx = 1.0 if SIDE == 'ours' else -1.0
want = math.atan2(0.0, sx); front = math.atan2(-1.0, 0.0)
root = bpy.data.objects.new('Root', None); sc.collection.objects.link(root)
for o in sc.objects:
    if o.parent is None and o is not root: o.parent = root
root.rotation_euler = (0, 0, want - front)
bpy.context.view_layer.update()
# боевая стойка разворачивает плечи — поправка, как в Tactics.ChestYaw: грудь к врагу (85%)
def shoulder_ang():
    for a in arms:
        pb = a.pose.bones
        l = next((b for b in pb if b.name.endswith('LeftArm')), None); r = next((b for b in pb if b.name.endswith('RightArm')), None)
        if l and r:
            v = a.matrix_world @ r.head - a.matrix_world @ l.head
            return math.atan2(v.y, v.x)
    return None
pose_a = shoulder_ang()
for a in arms: a.data.pose_position = 'REST'
bpy.context.view_layer.update(); rest_a = shoulder_ang()
for a in arms: a.data.pose_position = 'POSE'
bpy.context.view_layer.update()
if pose_a is not None and rest_a is not None:
    d = (pose_a - rest_a + math.pi) % (2 * math.pi) - math.pi
    d = max(-1.22, min(1.22, d)); print('STANCE_YAW', math.degrees(d))
    root.rotation_euler = (0, 0, want - front - 0.85 * d)
    bpy.context.view_layer.update()
if ALB and os.path.exists(ALB):
    m = bpy.data.materials.new('Alb'); m.use_nodes = True; nt = m.node_tree
    bsdf = nt.nodes['Principled BSDF']; img = nt.nodes.new('ShaderNodeTexImage'); img.image = bpy.data.images.load(ALB)
    nt.links.new(img.outputs['Color'], bsdf.inputs['Base Color']); bsdf.inputs['Roughness'].default_value = 0.7
    for o in meshes: o.data.materials.clear(); o.data.materials.append(m)
dg = bpy.context.evaluated_depsgraph_get()
pts = []
for o in meshes:
    e = o.evaluated_get(dg); me = e.to_mesh()
    pts += [e.matrix_world @ v.co for v in me.vertices]; e.to_mesh_clear()
mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))); mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
c = (mn + mx) * 0.5; H = mx.z - mn.z; Wd = max(mx.x - mn.x, mx.y - mn.y)
print('BBOX', mn, mx, 'action', pick)
sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 40; sc.cycles.use_denoising = True
sc.render.film_transparent = True
w = bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.6, 0.65, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 0.9
s = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN')); s.data.energy = 3.2; s.rotation_euler = (math.radians(42), 0, math.radians(-35)); sc.collection.objects.link(s)
cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'
pitch = math.radians(34)
cam.rotation_euler = (math.radians(90) - pitch, 0, 0)   # смотрит в +Y, наклон вниз на 34°
dirv = Vector((0, math.cos(pitch), -math.sin(pitch)))
tgt = Vector((c.x, c.y, mn.z + H * 0.5))
cam.location = tgt - dirv * (H * 6)
span = max(H * 1.15, Wd * 1.2)
cam.data.ortho_scale = span
sc.render.resolution_x = 640; sc.render.resolution_y = 640
sc.render.filepath = os.path.join(OUT, NAME + '.png')
bpy.ops.render.render(write_still=True)
