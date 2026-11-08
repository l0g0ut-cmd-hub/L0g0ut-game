# Подготовка листа кольца пламени: 1254² → 2048² (кадр 512×256), каждый кадр делается бесшовным по горизонтали
# (смесь кадра с его же копией, сдвинутой на полширины: на краях — плотная середина), низ (жёсткая белая линия) смягчён.
from PIL import Image; import numpy as np, sys
im=Image.open(sys.argv[1]).convert('RGBA').resize((2048,2048),Image.LANCZOS)
a=np.array(im).astype(np.float32)
cw,ch=512,256
x=np.arange(cw); m=np.sin(np.pi*(x+0.5)/cw)**2          # 0 на краях, 1 в середине
m=m[None,:,None]
fade=np.clip((ch-1-np.arange(ch))/(ch*0.08),0,1)[:,None]   # нижние ~4,5% — мягко гаснут
out=a.copy()
for f in range(32):
    y0=(f//4)*ch; x0=(f%4)*cw
    c=a[y0:y0+ch,x0:x0+cw]
    r=np.roll(c,cw//2,axis=1)
    # премультипликация, чтобы смешивать огонь корректно
    cp=c.copy(); cp[...,:3]*=cp[...,3:4]/255; rp=r.copy(); rp[...,:3]*=rp[...,3:4]/255
    t=cp*m+rp*(1-m)
    al=np.clip(t[...,3:4],1e-3,None); rgb=t[...,:3]/al*255
    t=np.concatenate([np.clip(rgb,0,255),t[...,3:4]],-1)
    t[...,3]*=fade
    out[y0:y0+ch,x0:x0+cw]=t
Image.fromarray(np.clip(out,0,255).astype(np.uint8)).save(sys.argv[2],optimize=True)
