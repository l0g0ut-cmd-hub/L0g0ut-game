import bpy, time, os, sys
S='/tmp/claude-0/-home-claude/2368feb0-8f6b-5bb1-9870-8b56f7279369/scratchpad'
TARGET=int(os.environ.get('TARGET_TRIS','30000'))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=S+'/meshy_glb/ronin.glb')
o=[x for x in bpy.data.objects if x.type=='MESH'][0]
bpy.context.view_layer.objects.active=o; o.select_set(True)
# ноги на пол, центр по X/Y в начале
import mathutils
zs=[v.co.z for v in o.data.vertices]; zmin=min(zs)
for v in o.data.vertices: v.co.z-=zmin
o.data.update()
t=time.time()
m=o.modifiers.new('Dec','DECIMATE'); m.decimate_type='COLLAPSE'; m.ratio=TARGET/len(o.data.polygons)
m.use_collapse_triangulate=True
bpy.ops.object.modifier_apply(modifier='Dec')
print('DECIMATE',round(time.time()-t,1),'s polys',len(o.data.polygons),'verts',len(o.data.vertices))
# картинки
for img in bpy.data.images:
    print('IMG',img.name,img.size[:], img.filepath)
bpy.ops.wm.save_as_mainfile(filepath=S+'/ronin/ronin_low.blend')
