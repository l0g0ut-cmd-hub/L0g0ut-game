"""Render diagnostic orthographic previews of the most gap-prone imported rigs.

The models are NOT changed. Green = background exposed through geometry,
red = a back-facing surface, grey = a front face. Render 8 angles for each
target; inspecting silhouettes alone cannot distinguish costume openings
from true mesh holes, so use with the topology and skin-seam audit.
"""
import bpy, math, os, traceback
from mathutils import Vector

OUT = "build/model-holes/views"
os.makedirs(OUT, exist_ok=True)
TARGETS = [
    "Dzin_Player.fbx", "Ren_Player.fbx", "Akiko_Player.fbx",
    "Fallen_Enemy.fbx", "Ronin_Player.fbx", "Samurai2_Player.fbx",
    "Samurai_Shadow.fbx", "Oni_Enemy.fbx"
]
os.makedirs("build/model-holes", exist_ok=True)
rows = []
for file in TARGETS:
    path = os.path.abspath("Assets/_Project/Models/" + file)
    print("MODEL_RENDER_BEGIN", file, flush=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    try:
        from io_scene_fbx import import_fbx
        class Reporter:
            def report(self, level, msg):
                print("FBX_DIAG", level, msg, flush=True)
        status = import_fbx.load(Reporter(), bpy.context, filepath=path)
        if status != {'FINISHED'}:
            raise RuntimeError(repr(status))
        sc = bpy.context.scene
        meshes = [obj for obj in sc.objects if obj.type == "MESH"]
        if not meshes:
            raise RuntimeError("FBX has no mesh")
        for arm in [o for o in sc.objects if o.type == "ARMATURE"]:
            arm.data.pose_position = "REST"
            arm.animation_data_clear()
        bpy.context.view_layer.update()

        front = bpy.data.materials.new("Front-grey_backface-red")
        front.use_nodes = True
        nt = front.node_tree
        nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        geo = nt.nodes.new("ShaderNodeNewGeometry")
        diff = nt.nodes.new("ShaderNodeBsdfDiffuse")
        diff.inputs["Color"].default_value = (0.7, 0.7, 0.72, 1)
        red = nt.nodes.new("ShaderNodeEmission")
        red.inputs["Color"].default_value = (1, 0.03, 0.025, 1)
        mix = nt.nodes.new("ShaderNodeMixShader")
        nt.links.new(geo.outputs["Backfacing"], mix.inputs[0])
        nt.links.new(diff.outputs[0], mix.inputs[1])
        nt.links.new(red.outputs[0], mix.inputs[2])
        nt.links.new(mix.outputs[0], out.inputs["Surface"])
        for o in meshes:
            o.data.materials.clear()
            o.data.materials.append(front)
        pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
        mn = Vector(tuple(min(p[i] for p in pts) for i in range(3)))
        mx = Vector(tuple(max(p[i] for p in pts) for i in range(3)))
        center = (mn + mx) * 0.5
        size = max(mx - mn)
        zup = (mx.z - mn.z) >= (mx.y - mn.y)
        world = bpy.data.worlds.new("Hole-diagnostic-green")
        sc.world = world
        world.use_nodes = True
        world.node_tree.nodes["Background"].inputs[0].default_value = (0.01, 0.53, 0.035, 1)
        world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
        cam = bpy.data.objects.new("DiagCam", bpy.data.cameras.new("DiagCam"))
        sc.collection.objects.link(cam)
        sc.camera = cam
        cam.data.type = "ORTHO"
        cam.data.ortho_scale = max(size, 0.1)*1.12
        sun = bpy.data.objects.new("DiagLight", bpy.data.lights.new("DiagLight", "SUN"))
        sc.collection.objects.link(sun)
        sun.data.energy = 3.0
        sun.rotation_euler = (math.radians(50), 0.2, -0.5)
        sc.render.engine = "CYCLES"
        sc.cycles.device = "CPU"
        sc.cycles.samples = 4
        sc.cycles.max_bounces = 1
        sc.render.resolution_x = 300
        sc.render.resolution_y = 450
        sc.render.resolution_percentage = 100
        sc.render.image_settings.file_format = "PNG"
        sc.render.film_transparent = False
        for deg in range(0, 360, 45):
            a = math.radians(deg)
            r = max(2.5, size*3.0)
            if zup:
                cam.location = center + Vector((math.sin(a)*r, -math.cos(a)*r, 0))
                up = 'Z'
            else:
                cam.location = center + Vector((math.sin(a)*r, 0, math.cos(a)*r))
                up = 'Y'
            cam.rotation_euler = (center-cam.location).to_track_quat('-Z', up).to_euler()
            dest = os.path.join(OUT, file.replace('.fbx', '') + '_' + str(deg).zfill(3) + '.png')
            sc.render.filepath = os.path.abspath(dest)
            bpy.ops.render.render(write_still=True)
        rows.append((file, "OK"))
        print("MODEL_RENDER_DONE", file, flush=True)
    except Exception as ex:
        rows.append((file, "ERROR " + str(ex)))
        print("MODEL_RENDER_ERROR", file, traceback.format_exc(), flush=True)
with open("build/model-holes/render-summary.txt", "w") as f:
    for a,b in rows:
        f.write(a + " " + b + "\n")
if any(status != "OK" for _,status in rows):
    raise SystemExit(1)
