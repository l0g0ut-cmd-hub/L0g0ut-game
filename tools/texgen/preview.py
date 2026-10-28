import sys, numpy as np
from PIL import Image
d=sys.argv[1]; pre=sys.argv[2]
a=np.asarray(Image.open(f'{d}/{pre}_albedo.png').convert('RGB')).astype(float)/255
n=np.asarray(Image.open(f'{d}/{pre}_normal.png').convert('RGB')).astype(float)/255*2-1
ms=np.asarray(Image.open(f'{d}/{pre}_ms.png')).astype(float)/255
L=np.array([0.5,0.6,0.62]); L/=np.linalg.norm(L)
lin=a**2.2
ndl=np.clip((n*L).sum(-1),0,1)
H=(L+np.array([0,0,1.0])); H/=np.linalg.norm(H)
spec=np.clip((n*H).sum(-1),0,1)**(2+ms[...,3]*120)*ms[...,3]*0.8
c=lin*(0.15+ndl[...,None]*1.6)+spec[...,None]*0.5
img=np.clip(c**(1/2.2),0,1)
w=np.concatenate([a, img],1)
Image.fromarray((w*255).astype(np.uint8)).resize((1536,768)).save(f'{d}/preview.png')
