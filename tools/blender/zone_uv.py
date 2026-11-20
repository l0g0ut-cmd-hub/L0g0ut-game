# Выполняется внутри build_meshy_char.py (exec) перед клипами и экспортом, если задан ZONE_UV=1.
# Основы редактора персонажей: развёртка по зонам (лицо, волосы, кисти, стопы, куртка, рукава, пояс, штаны, обмотки).
# Каждая зона — свой прямоугольник атласа: своя картинка игрока ложится ровно на свою часть тела.
# Зона грани — по главной кости вершин (веса W, имена names) и цвету исходной текстуры (кожа / светлая ткань / тёмная ткань).
# Лицо и волосы — цилиндрическая развёртка вокруг головы (лицо — по центру прямоугольника), остальное — smart project.
# Исходная текстура запекается в новую развёртку (Cycles, emission). Выход: ZONE_OUT/<NAME>_albedo.png и <NAME>_uv.json
# (зона каждой грани и её UV — по ним локально рисуются маска зон и выкройки).
import json as _json
ZONE_OUT = os.environ.get('ZONE_OUT', 'out/Zones'); os.makedirs(ZONE_OUT, exist_ok=True)
ALB = os.environ.get('ALBEDO', '')
ZONES = ['face', 'hair', 'hands', 'feet', 'jacket', 'sleeves', 'belt', 'pants', 'wraps']
RECT = {   # u0, v0, ширина, высота (v вверх)
    'face': (0.0, 0.75, 0.25, 0.25), 'hair': (0.25, 0.75, 0.25, 0.25), 'hands': (0.5, 0.75, 0.25, 0.25), 'feet': (0.75, 0.75, 0.25, 0.25),
    'jacket': (0.0, 0.25, 0.5, 0.5), 'sleeves': (0.5, 0.25, 0.5, 0.5),
    'pants': (0.0, 0.0, 0.5, 0.25), 'wraps': (0.5, 0.0, 0.25, 0.25), 'belt': (0.75, 0.0, 0.25, 0.25),
}
me = body.data
old_uv = me.uv_layers.active.name if me.uv_layers.active else None
img = bpy.data.images.load(ALB) if ALB and os.path.exists(ALB) else None
if img is None or old_uv is None:
    log('ЗОНЫ: нет текстуры или UV — развёртка по зонам пропущена', ALB)
else:
    IW, IH = img.size
    PX = np.array(img.pixels[:], dtype=np.float32).reshape(IH, IW, 4)
    nl = len(me.loops)
    LUV = np.empty(nl * 2, dtype=np.float32); me.uv_layers[old_uv].data.foreach_get('uv', LUV); LUV = LUV.reshape(-1, 2)
    LV = np.empty(nl, dtype=np.int64); me.loops.foreach_get('vertex_index', LV)
    npoly = len(me.polygons)
    LS = np.empty(npoly, dtype=np.int64); me.polygons.foreach_get('loop_start', LS)
    LT = np.empty(npoly, dtype=np.int64); me.polygons.foreach_get('loop_total', LT)
    dom = W.argmax(1)   # главная кость вершины
    zone = []
    for f in range(npoly):
        ls = range(LS[f], LS[f] + LT[f])
        uv = LUV[list(ls)].mean(0)
        c = PX[int(np.clip(uv[1], 0, 0.9999) * IH), int(np.clip(uv[0], 0, 0.9999) * IW), :3]
        lum = float(c.mean()); warm = c[0] - c[2] > 0.06 and c[0] > c[1] >= c[2] - 0.02 and lum > 0.18
        bones = [names[dom[LV[l]]] for l in ls]
        b = max(set(bones), key=bones.count)
        z = float(V[LV[list(ls)], 2].mean()) / k   # высота в долях фигуры 1,72 м
        if b in ('Head', 'Neck'): zn = 'face' if warm else 'hair'
        elif b.startswith('Hand'): zn = 'hands'
        elif b.startswith('Foot'): zn = 'feet'
        elif b.startswith('LowerLeg'): zn = 'wraps' if z < 0.40 else 'pants'
        elif b.startswith(('UpperArm', 'Forearm')): zn = 'hands' if warm and b.startswith('Forearm') else 'sleeves'
        elif b.startswith('UpperLeg'): zn = 'jacket' if lum > 0.33 else 'pants'
        elif b == 'Hips': zn = 'jacket' if lum > 0.33 else ('belt' if z > 0.95 else 'pants')
        else: zn = 'face' if warm and z > 1.35 else ('belt' if lum <= 0.33 and 0.95 < z < 1.12 else 'jacket')
        zone.append(zn)
    zone = np.array(zone)
    log('ЗОНЫ: граней по зонам', {zn: int((zone == zn).sum()) for zn in ZONES})
    # новая развёртка: каждая зона — «труба» вокруг своей оси (тело, рука, нога, голова), перёд — в середине по горизонтали.
    # Так зона раскладывается одним куском, как выкройка: нарисованное на ней ложится на тело без мешанины кусочков.
    zl = me.uv_layers.new(name='Zones'); me.uv_layers.active = zl
    NUV = np.zeros((nl, 2), dtype=np.float32)
    def J(bn, tail=False):
        b_ = rig.data.bones[bn]; return np.array(rig.matrix_world @ (b_.tail_local if tail else b_.head_local))
    cx = float(np.median(V[:, 0]))
    FACE_SIDE = np.array([np.sign(V[LV[LS[f]:LS[f] + LT[f]], 0].mean() - cx) for f in range(npoly)])   # +1 — левая (+X)
    def axis_for(zn, sd):
        S = '.L' if sd > 0 else '.R'
        if zn in ('face', 'hair'): return J('Head', True), J('Neck')
        if zn in ('jacket', 'belt'):
            top = J('Neck'); bot = J('Hips'); bot = np.array([top[0], top[1], bot[2] - 0.3 * k]); return top, bot
        if zn == 'sleeves': return J('UpperArm' + S), J('Hand' + S)
        if zn == 'hands': return J('Forearm' + S, True), J('Hand' + S, True)
        if zn == 'pants': return J('UpperLeg' + S), J('LowerLeg' + S, True)
        if zn == 'wraps': return J('LowerLeg' + S), J('LowerLeg' + S, True)
        if zn == 'feet': return J('Foot' + S) + np.array([0, 0, 0.12 * k]), J('Foot' + S, True)
        return J('Neck'), J('Hips')
    SPLIT = ('sleeves', 'hands', 'pants', 'wraps', 'feet')   # по половине прямоугольника на сторону
    FRONT = np.array([0.0, -1.0, 0.0])
    for zn in ZONES:
        sides = (1, -1) if zn in SPLIT else (0,)
        for sd in sides:
            fs = np.where((zone == zn) & ((FACE_SIDE == sd) | (sd == 0) | ((FACE_SIDE == 0) & (sd == 1))))[0]
            if len(fs) == 0: continue
            a_, b_ = axis_for(zn, sd if sd != 0 else 1)
            d = b_ - a_; L = max(np.linalg.norm(d), 1e-6); d = d / L
            f_ = FRONT - np.dot(FRONT, d) * d; f_ /= max(np.linalg.norm(f_), 1e-6); side = np.cross(d, f_)
            fl_list = [np.arange(LS[f], LS[f] + LT[f]) for f in fs]
            fl = np.concatenate(fl_list)
            p = V[LV[fl]] - a_
            t = p @ d; r = p - np.outer(t, d)
            ang = np.arctan2(r @ side, r @ f_)
            u = 0.5 + ang / (2 * np.pi); vv = -t
            # шов на спине: у граней, перешагнувших шов, малые u переносятся за 1
            pos = 0
            for arr in fl_list:
                seg = slice(pos, pos + len(arr)); uu = u[seg]
                if uu.max() - uu.min() > 0.5: uu[uu < 0.5] += 1.0; u[seg] = uu
                pos += len(arr)
            rad = float(np.median(np.linalg.norm(r, axis=1)))
            circ = 2 * np.pi * max(rad, 1e-3); height = max(float(vv.max() - vv.min()), 1e-3)
            uspan = max(float(u.max() - u.min()), 1e-6)
            ux = (u - u.min()) / uspan * circ * uspan; vy = (vv - vv.min())   # в метрах: ширина по окружности, высота по оси
            u0, v0, w, h = RECT[zn]
            if zn in SPLIT: w = w * 0.5; u0 = u0 + (0.0 if sd > 0 else w)
            pad = 0.01
            sc_ = min((w - 2 * pad) / max(ux.max(), 1e-6), (h - 2 * pad) / max(vy.max(), 1e-6))
            ox = u0 + (w - ux.max() * sc_) * 0.5; oy = v0 + (h - vy.max() * sc_) * 0.5
            NUV[fl] = np.stack([ox + ux * sc_, oy + vy * sc_], 1)
    zl.data.foreach_set('uv', NUV.ravel())
    # запекание исходной текстуры в новую развёртку
    out_img = bpy.data.images.new(NAME + '_Zones', 2048, 2048, alpha=False)
    mat = bpy.data.materials.new('ZoneBake'); mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear()
    o_ = nt.nodes.new('ShaderNodeOutputMaterial'); em = nt.nodes.new('ShaderNodeEmission')
    tx = nt.nodes.new('ShaderNodeTexImage'); tx.image = img; tx.interpolation = 'Cubic'
    uvn = nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = old_uv
    nt.links.new(uvn.outputs['UV'], tx.inputs['Vector']); nt.links.new(tx.outputs['Color'], em.inputs['Color']); nt.links.new(em.outputs[0], o_.inputs['Surface'])
    tgt = nt.nodes.new('ShaderNodeTexImage'); tgt.image = out_img; nt.nodes.active = tgt
    keep_mats = [m for m in me.materials]
    me.materials.clear(); me.materials.append(mat)
    sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 1
    zl.active_render = True
    bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); bpy.context.view_layer.objects.active = body
    bpy.ops.object.bake(type='EMIT', margin=12, use_clear=True)
    out_img.filepath_raw = os.path.join(ZONE_OUT, NAME + '_albedo.png'); out_img.file_format = 'PNG'; out_img.save()
    me.materials.clear()
    for m in keep_mats: me.materials.append(m)
    # старая развёртка больше не нужна: в игре одна — по зонам
    me.uv_layers.remove(me.uv_layers[old_uv]); me.uv_layers.active = me.uv_layers['Zones']; me.uv_layers['Zones'].active_render = True
    tris = []
    for f in range(npoly):
        l0 = LS[f]
        for t in range(1, LT[f] - 1):
            tris.append([ZONES.index(zone[f])] + [round(float(x), 5) for l in (l0, l0 + t, l0 + t + 1) for x in NUV[l]])
    _json.dump({'zones': ZONES, 'rects': RECT, 'tris': tris}, open(os.path.join(ZONE_OUT, NAME + '_uv.json'), 'w'))
    log('ЗОНЫ: готово —', os.path.join(ZONE_OUT, NAME + '_albedo.png'), 'треугольников', len(tris))
