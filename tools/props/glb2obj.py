"""Модель из Meshy (GLB) → OBJ для Unity (UV и нормали) + текстура цвета 1024 (PNG) в Art/Textures.
Масштаб: высота модели приводится к height (м), низ — к y = 0, центр по xz — в 0. Ось Y вверх (как в glTF).
Запуск: python3 tools/props/glb2obj.py IN.glb NAME HEIGHT"""
import sys, numpy as np, trimesh
from PIL import Image
src, name, height = sys.argv[1], sys.argv[2], float(sys.argv[3])
s = trimesh.load(src)
geoms = list(s.geometry.values()) if hasattr(s, 'geometry') else [s]
g = trimesh.util.concatenate(geoms) if len(geoms) > 1 else geoms[0]
tex = geoms[0].visual.material.baseColorTexture
v = g.vertices.copy()
mn, mx = v.min(0), v.max(0)
k = height / (mx[1] - mn[1])
v = (v - np.array([(mn[0] + mx[0]) / 2, mn[1], (mn[2] + mx[2]) / 2])) * k
g.vertices = v
obj = trimesh.exchange.obj.export_obj(g, include_normals=True, include_color=False, include_texture=True, write_texture=False, mtl_name=None)
# убрать ссылку на mtl — материал назначает EnvironmentBuilder
obj = '\n'.join(l for l in obj.splitlines() if not l.startswith(('mtllib', 'usemtl')))
open(f'Assets/_Project/Models/Props/{name}.obj', 'w').write(obj)
tex.convert('RGB').resize((1024, 1024), Image.LANCZOS).save(f'Assets/_Project/Art/Textures/{name}.png')
print(name, 'tris', len(g.faces), 'size m', (v.max(0) - v.min(0)).round(3))
