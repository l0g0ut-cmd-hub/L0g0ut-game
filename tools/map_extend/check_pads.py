# Проверка мест домиков: на широкой карте рисуется настоящий спрайт домика (основание — точка пада, 78% высоты ячейки),
# значок места и подпись. Выход — плитки по 6 мест (увеличение), чтобы глазами проверить: на земле ли, не на воде/дереве/обрыве.
import json, cv2, numpy as np, sys
P=json.load(open('tools/map_pads.json')); D=json.load(open('tools/map_roads.json'))
EX={'0':[('X1',0.33,0.636),('X2',0.212,0.835)],'1':[('X1',0.275,0.69)],'2':[('X1',0.24,0.545),('X2',0.91,0.33),('X3',0.8,0.432)]}
atl={r:cv2.imread('Assets/_Project/Resources/UI/%s.png'%a,cv2.IMREAD_UNCHANGED) for r,a in (('0','map_buildings_0'),('1','map_buildings'),('2','map_buildings_2'))}
ic=cv2.imread('Assets/_Project/Resources/UI/map_icons.png',cv2.IMREAD_UNCHANGED)
def blit(dst,src,x0,y0,side):
    s=cv2.resize(src,(side,side),interpolation=cv2.INTER_AREA); H,W=dst.shape[:2]
    sx0,sy0=max(0,-x0),max(0,-y0); x1,y1=min(W,x0+side),min(H,y0+side)
    if x1<=max(0,x0) or y1<=max(0,y0): return
    a=s[sy0:sy0+y1-max(0,y0),sx0:sx0+x1-max(0,x0)]; al=a[...,3:4]/255.0
    reg=dst[max(0,y0):y1,max(0,x0):x1]; reg[:]=(reg*(1-al)+a[...,:3]*al).astype(np.uint8)
for r in (sys.argv[1:] or '012'):
    m=D[r]; W=m['width']; base=cv2.imread('Assets/_Project/Resources/UI/map_wide_%s.jpg'%r)
    spots=[(n,x*W,y*1024) for l in m['spots'] for n,x,y in l]+[(n,x*1536,y*1024) for n,x,y in EX[r]]
    full=base.copy(); A=atl[r]; cells=[A[0:256,i*256:(i+1)*256] for i in range(4)]+[A[256:512,i*256:(i+1)*256] for i in range(4)]
    k=0
    for n,x,y in spots:
        pad=P[r].get(n)
        if pad:
            cx,cy,s=pad; side=int(140*s)
            blit(full,cells[k%7],int(cx-side/2),int(cy-0.78*side),side); k+=1
            cv2.circle(full,(int(cx),int(cy)),3,(255,0,255),-1)
    for n,x,y in spots:
        blit(full,ic[0:128,0:128],int(x-49),int(y-49),98)
        cv2.putText(full,n,(int(x)-12,int(y)+64),0,0.6,(0,0,0),4); cv2.putText(full,n,(int(x)-12,int(y)+64),0,0.6,(0,255,255),2)
    tiles=[]
    for n,x,y in spots:
        x0,y0=int(x-230),int(y-200); c=np.zeros((360,460,3),np.uint8)
        x0=(x0//20)*20; y0=(y0//20)*20
        sx0,sy0=max(0,x0),max(0,y0); sx1,sy1=min(W,x0+460),min(1024,y0+360)
        c[sy0-y0:sy1-y0,sx0-x0:sx1-x0]=full[sy0:sy1,sx0:sx1]
        for gx in range(0,460,20):
            col=(255,255,255) if (x0+gx)%100==0 else (90,90,90)
            cv2.line(c,(gx,0),(gx,359),col,1)
            if (x0+gx)%100==0: cv2.putText(c,str(x0+gx),(gx+1,10),0,0.32,(0,255,255),1)
        for gy in range(0,360,20):
            col=(255,255,255) if (y0+gy)%100==0 else (90,90,90)
            cv2.line(c,(0,gy),(459,gy),col,1)
            if (y0+gy)%100==0: cv2.putText(c,str(y0+gy),(1,gy-1),0,0.32,(0,255,255),1)
        pass
        cv2.putText(c,'%s %s %s'%(r,n,P[r].get(n)),(4,352),0,0.5,(0,255,0),2); tiles.append(c)
    for t in range(0,len(tiles),6):
        g=tiles[t:t+6]
        while len(g)<6: g.append(np.zeros_like(tiles[0]))
        cv2.imwrite('../pads/c%s_%d.jpg'%(r,t//6),np.vstack([np.hstack(g[:3]),np.hstack(g[3:])]))
    cv2.imwrite('../pads/full_%s.jpg'%r,cv2.resize(full,None,fx=0.62,fy=0.62,interpolation=cv2.INTER_AREA))
print('ok')
