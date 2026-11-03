"""
Mixamo Great Sword Pack (tools/mocap/mixamo) → сырые данные по кадрам (JSON) + листы превью с мечом.
Система бойца F: x — вправо, y — вверх, z — вперёд (как в Unity), начало — под тазом в позе покоя X Bot.
Позиции суставов в метрах. Дальше tools/mocap_to_tracks.py делает из этого ключи ударов (PoseTrack).
Запуск: blender -b --factory-startup --python mixamo_extract.py   (env MIX_OUT)
"""
import bpy, os, json, math, glob
from mathutils import Vector, Matrix
HERE = os.path.dirname(os.path.abspath(__file__)); SRC = os.path.join(HERE, '..', 'mocap', 'mixamo')
OUT = os.environ.get('MIX_OUT', '/tmp/mix'); os.makedirs(os.path.join(OUT, 'prev'), exist_ok=True)
J = ['Hips', 'Spine', 'Spine2', 'Neck', 'Head', 'HeadTop_End', 'LeftShoulder', 'RightShoulder', 'LeftArm', 'RightArm', 'LeftForeArm', 'RightForeArm',
     'LeftHand', 'RightHand', 'LeftHandMiddle1', 'RightHandMiddle1', 'LeftHandIndex1', 'RightHandIndex1', 'LeftHandPinky1', 'RightHandPinky1',
     'LeftHandThumb1', 'RightHandThumb1', 'LeftUpLeg', 'RightUpLeg', 'LeftLeg', 'RightLeg', 'LeftFoot', 'RightFoot', 'LeftToeBase', 'RightToeBase']
def log(*a): print('MIX', *a, flush=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene; sc.render.fps = 30
bpy.ops.import_scene.fbx(filepath=os.path.join(SRC, 'X Bot.fbx'))
bot = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]; bot.name = 'Bot'
def bn(name):
    for b in bot.pose.bones:
        if b.name.split(':')[-1] == name: return b
    return None
def P(name): b = bn(name); return bot.matrix_world @ b.head if b else None
# система бойца по позе покоя
bot.data.pose_position = 'REST'; bpy.context.view_layer.update()
up = Vector((0, 0, 1)); right = P('RightArm') - P('LeftArm'); right.z = 0; right.normalize()
fwd = up.cross(right); fwd.normalize()
origin = P('Hips').copy(); origin.z = 0
height = P('HeadTop_End').z if bn('HeadTop_End') else P('Head').z + 0.18
log('покой: вперёд', tuple(round(v, 3) for v in fwd), 'вправо', tuple(round(v, 3) for v in right), 'рост', round(height, 3))
bot.data.pose_position = 'POSE'
def F(v): d = v - origin; return [round(d.dot(right), 4), round(d.dot(up), 4), round(d.dot(fwd), 4)]

# меч для превью: тонкий брус, двигаем по кадрам
bpy.ops.mesh.primitive_cube_add(size=1); sword = bpy.context.object; sword.name = 'Sword'
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 3.4
sc.render.engine = 'BLENDER_WORKBENCH'; sc.render.resolution_x = 230; sc.render.resolution_y = 300
sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'OBJECT'
sword.color = (0.9, 0.2, 0.1, 1)
meta = {'fwd': list(fwd), 'right': list(right), 'height': height, 'clips': {}}
for f in sorted(glob.glob(os.path.join(SRC, '*.fbx'))):
    nm = os.path.splitext(os.path.basename(f))[0]
    if nm == 'X Bot': continue
    before = set(bpy.data.objects); acts0 = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=f)
    new = [o for o in bpy.data.objects if o not in before]
    acts = [a for a in bpy.data.actions if a not in acts0]
    for o in new: bpy.data.objects.remove(o, do_unlink=True)
    if not acts: log('нет клипа', nm); continue
    act = max(acts, key=lambda a: a.frame_range[1] - a.frame_range[0])
    if bot.animation_data is None: bot.animation_data_create()
    bot.animation_data.action = act
    f0, f1 = int(round(act.frame_range[0])), int(round(act.frame_range[1]))
    frames = []
    for fr in range(f0, f1 + 1):
        sc.frame_set(fr)
        frames.append({j: F(P(j)) for j in J if bn(j)})
    meta['clips'][nm] = {'f0': f0, 'f1': f1}
    json.dump({'name': nm, 'fps': 30, 'frames': frames}, open(os.path.join(OUT, nm + '.json'), 'w'))
    log('клип', nm, 'кадров', f1 - f0 + 1)
    # превью 8 кадров: меч от правой кисти вдоль (правая − левая)
    for k in range(8):
        fr = f0 + round((f1 - f0) * k / 7); sc.frame_set(fr)
        gR = (P('RightHand') + P('RightHandMiddle1')) * 0.5; gL = (P('LeftHand') + P('LeftHandMiddle1')) * 0.5
        a = gR - gL
        if a.length < 1e-4: a = Vector(fwd)
        a.normalize()
        sword.location = gR + a * 0.5; sword.scale = (0.025, 0.025, 1.0)
        sword.rotation_euler = a.to_track_quat('Z', 'Y').to_euler()
        hp = P('Hips'); tgt = Vector((hp.x, hp.y, 1.0))
        cam.location = tgt + fwd * 3.0 + right * 3.0 + up * 0.6
        cam.rotation_euler = (tgt - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(OUT, 'prev', '%s__%d.png' % (nm, k)); bpy.ops.render.render(write_still=True)
json.dump(meta, open(os.path.join(OUT, 'meta.json'), 'w'))
log('ГОТОВО', len(meta['clips']))
