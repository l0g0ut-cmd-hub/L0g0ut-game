"""
Проверка моделей персонажей на «дыры»: каждый FBX из Assets/_Project/Models рендерится с 8 сторон.
Лицевые грани — серые, изнанка — ярко-красная (её видно там, где у одинарной поверхности смотрим с обратной стороны:
с отсечением изнанки в игре на этом месте просвечивал фон), фон — зелёный (сквозная дыра в силуэте — зелёное внутри фигуры).
Плюс счёт по сетке: открытые рёбра (край без соседней грани) и доля изнанки на снимках.
Запуск: blender -b --factory-startup --python hole_check.py   env: CHECK_DIR (FBX), CHECK_OUT (папка снимков)
"""
import bpy, bmesh, math, os, glob, json
from mathutils import Vector

SRC = os.environ.get('CHECK_DIR', 'Assets/_Project/Models'); OUT = os.environ.get('CHECK_OUT', 'out/holes'); os.makedirs(OUT, exist_ok=True)
report = {}

def mat():
    m = bpy.data.materials.new('Check'); m.use_nodes = True; nt = m.node_tree; nt.nodes.clear()
    o = nt.nodes.new('ShaderNodeOutputMaterial'); g = nt.nodes.new('ShaderNodeNewGeometry')
    mix = nt.nodes.new('ShaderNodeMixShader'); a = nt.nodes.new('ShaderNodeBsdfDiffuse'); b = nt.nodes.new('ShaderNodeEmission')
    a.inputs['Color'].default_value = (0.62, 0.62, 0.6, 1); b.inputs['Color'].default_value = (1, 0.02, 0.02, 1); b.inputs['Strength'].default_value = 1.0
    nt.links.new(g.outputs['Backfacing'], mix.inputs[0]); nt.links.new(a.outputs[0], mix.inputs[1]); nt.links.new(b.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], o.inputs['Surface']); return m

for fbx in sorted(glob.glob(os.path.join(SRC, '*.fbx'))):
    name = os.path.splitext(os.path.basename(fbx))[0]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    try: bpy.ops.import_scene.fbx(filepath=fbx)
    except Exception as e: report[name] = {'error': str(e)}; continue
    sc = bpy.context.scene
    meshes = [o for o in sc.objects if o.type == 'MESH']
    if not meshes: report[name] = {'error': 'нет мешей'}; continue
    for a in [o for o in sc.objects if o.type == 'ARMATURE']: a.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    # открытые рёбра
    open_e = 0; tot_e = 0; tris = 0
    for o in meshes:
        bm = bmesh.new(); bm.from_mesh(o.data)
        tot_e += len(bm.edges); open_e += sum(1 for e in bm.edges if e.is_boundary); tris += sum(len(f.verts) - 2 for f in bm.faces); bm.free()
    m = mat()
    for o in meshes: o.data.materials.clear(); o.data.materials.append(m)
    # рамка
    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))); mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    c = (mn + mx) * 0.5; size = max(mx - mn)
    sc.render.engine = 'CYCLES'   # без видеокарты на раннере EEVEE не работает; Cycles на CPU, мало выборок — хватает
    sc.cycles.device = 'CPU'; sc.cycles.samples = 6; sc.cycles.use_denoising = False; sc.cycles.max_bounces = 1
    sc.render.resolution_x = 360; sc.render.resolution_y = 540; sc.render.film_transparent = False
    w = bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.0, 0.6, 0.1, 1)
    cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = size * 1.08
    sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN')); sc.collection.objects.link(sun); sun.rotation_euler = (0.8, 0.2, 0.6)
    up_z = (mx.z - mn.z) >= (mx.y - mn.y)   # FBX может прийти Y-вверх
    shots = []
    for i, ang in enumerate(range(0, 360, 45)):
        a = math.radians(ang); r = size * 3
        if up_z: cam.location = c + Vector((math.sin(a) * r, -math.cos(a) * r, 0)); look = c - cam.location
        else: cam.location = c + Vector((math.sin(a) * r, 0, math.cos(a) * r)); look = c - cam.location
        cam.rotation_euler = look.to_track_quat('-Z', 'Z' if up_z else 'Y').to_euler()
        p = os.path.join(OUT, f'{name}_{ang:03d}.png'); sc.render.filepath = p
        bpy.ops.render.render(write_still=True); shots.append(p)
    report[name] = {'tris': tris, 'edges': tot_e, 'open_edges': open_e, 'open_pct': round(100.0 * open_e / max(1, tot_e), 2)}
    print('HOLES', name, report[name], flush=True)
json.dump(report, open(os.path.join(OUT, 'report.json'), 'w'), ensure_ascii=False, indent=1)
