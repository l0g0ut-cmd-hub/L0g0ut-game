import cv2, numpy as np
R='bladepath/Assets/_Project/Resources/UI/map_wide_0.jpg'
im=cv2.imread(R)
x0,y0,x1,y1=1540,520,1840,740
roi=im[y0:y1,x0:x1]
hsv=cv2.cvtColor(roi,cv2.COLOR_BGR2HSV).astype(int)
s,v=hsv[...,1],hsv[...,2]
m=((s<70)&(v>95)).astype(np.uint8)
poly=np.array([[1688,738],[1728,738],[1770,728],[1795,728],[1800,600],[1760,590],[1700,555],[1600,555],[1560,600],[1580,640],[1660,665],[1688,700]])-[x0,y0]
pm=np.zeros_like(m); cv2.fillPoly(pm,[poly],1); m=m*pm
m=cv2.morphologyEx(m,cv2.MORPH_CLOSE,np.ones((9,9),np.uint8))
m=cv2.dilate(m,np.ones((11,11),np.uint8))
# protect kilns (domes) — their tops at y>~728 near vents
M=np.zeros(im.shape[:2],np.float32); M[y0:y1,x0:x1]=m
M[728:,:]=0
ys,xs=np.nonzero(M); bx0,by0,bx1,by1=xs.min(),ys.min(),xs.max()+1,ys.max()+1
print('bbox',bx0,by0,bx1,by1)
ring=cv2.dilate((M>0).astype(np.uint8),np.ones((15,15),np.uint8))-(M>0).astype(np.uint8)
best=[]
H,W=im.shape[:2]
for dy in range(-260,261,10):
    for dx in range(-420,421,10):
        if abs(dx)<120 and abs(dy)<120: continue
        sx0,sy0,sx1,sy1=bx0+dx,by0+dy,bx1+dx,by1+dy
        if sx0<0 or sy0<0 or sx1>W or sy1>H: continue
        src=im[sy0:sy1,sx0:sx1].astype(np.float32); dst=im[by0:by1,bx0:bx1].astype(np.float32)
        r=ring[by0:by1,bx0:bx1]>0
        # source must itself be smoke-free: low share of greyish pixels
        hs=cv2.cvtColor(im[sy0:sy1,sx0:sx1],cv2.COLOR_BGR2HSV)
        grey=((hs[...,1]<60)&(hs[...,2]>100)).mean()
        if grey>0.06: continue
        d=np.abs(src[r]-dst[r]).mean()
        best.append((d,dx,dy))
best.sort(); print(best[:8])
for n,(d,dx,dy) in enumerate(best[:4]):
    out=im.copy().astype(np.float32)
    a=cv2.GaussianBlur(M,(0,0),5); a=np.clip(a*1.6,0,1); a[M>0]=np.maximum(a[M>0],0.97)
    a=a[...,None]
    shifted=np.roll(np.roll(im,-dy,0),-dx,1).astype(np.float32)
    out=out*(1-a)+shifted*a
    out=out.astype(np.uint8)
    cv2.imwrite('smoke/opt%d.jpg'%n,cv2.resize(np.hstack([im[y0-20:y1+30,x0-40:x1+40],out[y0-20:y1+30,x0-40:x1+40]]),None,fx=1.4,fy=1.4))
    if n==0: cv2.imwrite('smoke/best.png',out)
np.save('smoke/M.npy',M)
