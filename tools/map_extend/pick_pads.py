import json
D=json.load(open('tools/map_roads.json'))
EX={'0':[('X1',0.33,0.636),('X2',0.212,0.835)],'1':[('X1',0.275,0.69)],'2':[('X1',0.24,0.545),('X2',0.91,0.33),('X3',0.8,0.432)]}
OFF=[(90,15),(-90,15),(75,-45),(-75,-45),(0,-70),(75,85),(-75,85),(0,105)]
PICK={'0':{'A1':1,'B1':8,'B2':1,'A3':1,'B3':1,'A4':2,'B4':7,'A5':8,'N2':1,'N5':7,'N4':2,'X1':8},
      '1':{'A1':1,'B1':6,'A2':7,'A4':3,'B4':6,'A5':1,'B5':3,'D3':6},
      '2':{'A3':1,'B3':6,'A4':7,'B4':6,'A5':7,'B5':6,'G1':4,'G2':1,'G7':1,'G3':1,'G8':2,'X3':6}}
out={}
for r,m in D.items():
    W=m['width']; sp={n:(x*W,y*1024) for l in m['spots'] for n,x,y in l}
    for n,x,y in EX[r]: sp[n]=(x*1536,y*1024)
    out[r]={n:[round(sp[n][0]+OFF[k-1][0]),round(sp[n][1]+OFF[k-1][1]),1.0] for n,k in PICK[r].items()}
json.dump(out,open('tools/map_pads.json','w'),indent=1)
print({r:len(v) for r,v in out.items()})
