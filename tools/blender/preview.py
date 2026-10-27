"""Превью модели: 4 вида Workbench-рендером → один лист. env: PREVIEW_BLEND, PREVIEW_PREFIX, PREVIEW_RIG, PREVIEW_FRAME(кадр Idle/WalkF)."""
import bpy, math, os
from mathutils import Vector
blend = os.environ['PREVIEW_BLEND']; prefix = os.environ['PREVIEW_PREFIX']
rig_name = os.environ.get('PREVIEW_RIG', 'Samurai_Rig'); action = os.environ.get('PREVIEW_ACTION', '')
frame = int(os.environ.get('PREVIEW_FRAME', '1'))
bpy.ops.wm.open_mainfile(filepath=blend)
sc = bpy.context.scene
if action:
    bpy.data.objects[rig_name].animation_data.action = bpy.data.actions[action]
sc.frame_set(frame)
sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading; sh.light = 'STUDIO'; sh.color_type = 'MATERIAL'; sh.show_shadows = True
sc.render.resolution_x = 600; sc.render.resolution_y = 900
sc.world = bpy.data.worlds.new('w'); sc.world.color = (0.12, 0.12, 0.15)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.9
target = Vector((0, 0.1, 1.05))
views = {'front': (0, -6), 'side': (6, 0), 'back': (0, 6), 'three': (4.2, -4.2)}
files = []
for name, (x, y) in views.items():
    cam.location = (x, y, 1.15)
    d = target - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    f = f'{prefix}_{name}.png'; sc.render.filepath = f
    bpy.ops.render.render(write_still=True); files.append(f)
print('PREVIEW DONE', files)
