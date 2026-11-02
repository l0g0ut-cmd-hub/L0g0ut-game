"""Base color из Meshy GLB → JPG для Unity (Assets/_Project/Art/<Имя>/<имя>_albedo.jpg) + печать PBR-коэффициентов.
Запуск: python3 tools/meshy_textures.py <glb> <Имя> <папка Art>"""
import io, json, os, struct, sys
from PIL import Image

glb, name, art = sys.argv[1], sys.argv[2], sys.argv[3]
b = open(glb, 'rb').read()
n = struct.unpack('<I', b[12:16])[0]
j = json.loads(b[20:20 + n])
binstart = 20 + n + 8
mat = j['materials'][0]
pbr = mat.get('pbrMetallicRoughness', {})
tex = j['textures'][pbr['baseColorTexture']['index']]
img = j['images'][tex['source']]
bv = j['bufferViews'][img['bufferView']]
data = b[binstart + bv.get('byteOffset', 0): binstart + bv.get('byteOffset', 0) + bv['byteLength']]
out_dir = os.path.join(art, name); os.makedirs(out_dir, exist_ok=True)
im = Image.open(io.BytesIO(data)).convert('RGB')
path = os.path.join(out_dir, name.lower() + '_albedo.jpg')
im.save(path, quality=92)
print('TEX', name, im.size, path, os.path.getsize(path) // 1024, 'KB',
      'metallic', pbr.get('metallicFactor', 1.0), 'roughness', pbr.get('roughnessFactor', 1.0))
