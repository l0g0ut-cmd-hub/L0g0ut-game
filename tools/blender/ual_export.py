"""
Quaternius Universal Animation Library 1/2 (CC0, tools/mocap/ual) → BVH выбранных клипов + листы превью.
Запуск: blender -b --factory-startup --python ual_export.py   (env UAL_OUT — папка результата)
"""
import bpy, os, math
from mathutils import Vector
HERE = os.path.dirname(os.path.abspath(__file__)); SRC = os.path.join(HERE, '..', 'mocap', 'ual')
OUT = os.environ.get('UAL_OUT', '/tmp/ual'); os.makedirs(OUT, exist_ok=True)
WANT = ['A_TPose', 'Sword_Idle', 'Sword_Attack', 'Sword_Regular_A', 'Sword_Regular_A_Rec', 'Sword_Regular_B', 'Sword_Regular_B_Rec',
        'Sword_Regular_C', 'Sword_Regular_Combo', 'Sword_Block', 'Sword_Dash_RM', 'Hit_Chest', 'Hit_Head', 'Hit_Knockback',
        'Death01', 'Roll', 'Idle_Loop', 'Walk_Loop', 'Jog_Fwd_Loop', 'Jump_Start', 'Jump_Loop', 'Jump_Land',
        'Melee_Hook', 'Punch_Jab', 'Punch_Cross', 'Idle_Shield_Break', 'NinjaJump_Start', 'NinjaJump_Land']
def log(*a): print('UAL', *a, flush=True)
done = {}
for f in ('UAL1_Standard.glb', 'UAL2_Standard.glb'):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene; sc.render.fps = 30
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, f))
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    mesh = [o for o in bpy.data.objects if o.type == 'MESH']
    log(f, 'кости', len(arm.data.bones), 'клипов', len(bpy.data.actions), 'fps', sc.render.fps)
    log('rest', {b.name: tuple(round(v, 3) for v in (arm.matrix_world @ b.head_local)) for b in arm.data.bones if b.name in ('pelvis', 'spine_01', 'upperarm_l', 'lowerarm_l', 'hand_l', 'thigh_l', 'calf_l', 'foot_l', 'Head')})
    if arm.animation_data is None: arm.animation_data_create()
    # камера и свет для листов превью
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.6
    sc.render.engine = 'BLENDER_WORKBENCH'; sc.render.resolution_x = 220; sc.render.resolution_y = 300
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'SINGLE'
    for act in bpy.data.actions:
        if act.name in done or not any(act.name == w or act.name.endswith('|' + w) or act.name.startswith(w) for w in WANT): continue
        nm = next(w for w in WANT if act.name == w or act.name.endswith('|' + w) or act.name.startswith(w))
        if nm in done: continue
        arm.animation_data.action = act
        f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        sc.frame_start, sc.frame_end = f0, f1
        bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); bpy.context.view_layer.objects.active = arm
        bpy.ops.export_anim.bvh(filepath=os.path.join(OUT, nm + '.bvh'), frame_start=f0, frame_end=f1, rotate_mode='NATIVE', root_transform_only=False)
        done[nm] = (f0, f1)
        log('клип', nm, 'кадры', f0, f1, 'сек', round((f1 - f0) / 30.0, 2))
        # лист превью: 6 кадров, вид сбоку (боец смотрит вдоль +Y или −Y — видно силуэт удара)
        if nm.startswith(('Sword', 'Hit', 'Death', 'Roll', 'Melee', 'Punch', 'Idle_Shield')):
            for k in range(6):
                fr = f0 + round((f1 - f0) * k / 5); sc.frame_set(fr)
                cam.location = (3.5, -3.5, 1.0); cam.rotation_euler = (Vector((0, 0, 0.95)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
                sc.render.filepath = os.path.join(OUT, 'prev', '%s_%d.png' % (nm, k)); bpy.ops.render.render(write_still=True)
log('ГОТОВО', len(done), sorted(done))
