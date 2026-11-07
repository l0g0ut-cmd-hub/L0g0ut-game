# Кандидаты под домик у каждого места: 8 рамок (видимая часть домика 90×75 px над основанием), номер в углу.
import json, cv2, numpy as np, sys
D=json.load(open('tools/map_roads.json'))
EX={'0':[('X1',0.33,0.636),('X2',0.212,0.835)],'1':[('X1',0.275,0.69)],'2':[('X1',0.24,0.545),('X2',0.91,0.33),('X3',0.8,0.432)]}
OFF=[(90,15),(-90,15),(75,-45),(-75,-45),(0,-70),(75,85),(-75,85),(0,105)]
FW,FH=90,75
COL=[(0,255,255),(255,0,255),(0,255,0),(255,128,0),(255,255,255),(0,128,255),(255,255,0),(128,0,255)]
for r in (sys.argv[1:] or '012'):
    m=D[r]; W=m['width']; base=cv2.imread('Assets/_Project/Resources/UI/map_wide_%s.jpg'%r)
    pos={}
    for l in m['spots']:
        for n,x,y in l: pos[n]=(x*W,y*1024)
    for k,v in m['points'].items(): pos[k]=(v[0]*W,v[1]*1024)
    full=base.copy()
    for a,mids,b in m['roads']:
        pts=np.array([pos[a]]+[(p[0]*W,p[1]*1024) for p in mids]+[pos[b]],np.int32)
        cv2.polylines(full,[pts],False,(0,0,255),2,cv2.LINE_AA)
    spots=[(n,x*W,y*1024) for l in m['spots'] for n,x,y in l if n not in ('S','E','Z')]+[(n,x*1536,y*1024) for n,x,y in EX[r]]
    allsp=[(n,x*W,y*1024) for l in m['spots'] for n,x,y in l]+[(n,x*1536,y*1024) for n,x,y in EX[r]]
    for n,x,y in allsp: cv2.circle(full,(int(x),int(y)),46,(40,40,200),3)
    tiles=[]
    for n,x,y in spots:
        c=full.copy()
        for i,(dx,dy) in enumerate(OFF):
            bx,by=int(x+dx),int(y+dy)
            cv2.rectangle(c,(bx-FW//2,by-FH),(bx+FW//2,by),COL[i],2)
            cv2.putText(c,str(i+1),(bx-FW//2+3,by-FH+16),0,0.55,(0,0,0),4); cv2.putText(c,str(i+1),(bx-FW//2+3,by-FH+16),0,0.55,COL[i],2)
        x0,y0=int(x-220),int(y-210); t=np.zeros((360,440,3),np.uint8)
        sx0,sy0=max(0,x0),max(0,y0); sx1,sy1=min(W,x0+440),min(1024,y0+360)
        t[sy0-y0:sy1-y0,sx0-x0:sx1-x0]=c[sy0:sy1,sx0:sx1]
        cv2.putText(t,'%s %s'%(r,n),(6,350),0,0.8,(0,0,0),5); cv2.putText(t,'%s %s'%(r,n),(6,350),0,0.8,(0,255,0),2)
        tiles.append(t)
    for q in range(0,len(tiles),6):
        g=tiles[q:q+6]
        while len(g)<6: g.append(np.zeros_like(tiles[0]))
        cv2.imwrite('../pads/k%s_%d.jpg'%(r,q//6),np.vstack([np.hstack(g[:3]),np.hstack(g[3:])]))
    print(r,len(spots))
