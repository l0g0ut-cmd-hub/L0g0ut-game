"""Реф модели для генерации картинок: FBX → два снимка (весь рост и по грудь), спереди и в три четверти.
env: FBX, ALBEDO (текстура цвета), OUT (папка), NAME. Персонаж смотрит в −Y (как все наши модели)."""
import bpy, os, math
from mathutils import Vector
FBX = os.environ['FBX']; ALB = os.environ.get('ALBEDO', ''); OUT = os.environ.get('OUT', 'out'); NAME = os.environ.get('NAME', 'model')
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
sc = bpy.context.scene
meshes = [o for o in sc.objects if o.type == 'MESH']
for a in [o for o in sc.objects if o.type == 'ARMATURE']: a.data.pose_position = 'REST'
bpy.context.view_layer.update()
if ALB and os.path.exists(ALB):
    m = bpy.data.materials.new('Alb'); m.use_nodes = True; nt = m.node_tree
    bsdf = nt.nodes['Principled BSDF']; img = nt.nodes.new('ShaderNodeTexImage'); img.image = bpy.data.images.load(ALB)
    nt.links.new(img.outputs['Color'], bsdf.inputs['Base Color']); bsdf.inputs['Roughness'].default_value = 0.6
    for o in meshes: o.data.materials.clear(); o.data.materials.append(m)
pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))); mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
c = (mn + mx) * 0.5; H = mx.z - mn.z
print('BBOX', mn, mx)
sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 48; sc.cycles.use_denoising = True
w = bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.32, 0.32, 0.34, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 0.8
for rot, e in [((0.9, 0.0, -0.5), 3.0), ((1.2, 0.0, 2.6), 1.2)]:
    s = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN')); s.data.energy = e; s.rotation_euler = rot; sc.collection.objects.link(s)
tgt = bpy.data.objects.new('T', None); sc.collection.objects.link(tgt)
cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'
tc = cam.constraints.new('TRACK_TO'); tc.target = tgt; tc.track_axis = 'TRACK_NEGATIVE_Z'; tc.up_axis = 'UP_Y'
shots = [('full_front', 0, 0.5, 1.12, 768, 1024), ('full_34', 35, 0.5, 1.12, 768, 1024), ('bust_front', 0, 0.84, 0.36, 1024, 1024), ('bust_34', 30, 0.84, 0.36, 1024, 1024)]
for n, ang, zc, scale, rx, ry in shots:
    sc.render.resolution_x = rx; sc.render.resolution_y = ry
    tgt.location = Vector((c.x, c.y, mn.z + H * zc))
    a = math.radians(ang); d = H * 4
    cam.location = tgt.location + Vector((math.sin(a) * d, -math.cos(a) * d, 0))
    cam.data.ortho_scale = H * scale
    sc.render.filepath = os.path.join(OUT, NAME + '_' + n + '.png')
    bpy.ops.render.render(write_still=True)
