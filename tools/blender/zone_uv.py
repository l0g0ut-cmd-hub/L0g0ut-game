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
    # новая развёртка
    zl = me.uv_layers.new(name='Zones'); me.uv_layers.active = zl
    bpy.context.view_layer.objects.active = body; body.select_set(True)
    for zn in ZONES:
        sel = zone == zn
        if not sel.any(): continue
        if zn in ('face', 'hair'): continue   # ниже — цилиндром
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_mode(type='FACE')
        import bmesh as _bm
        bm = _bm.from_edit_mesh(me); bm.faces.ensure_lookup_table()
        for v_ in bm.verts: v_.select_set(False)
        for f_ in bm.faces: f_.select_set(bool(sel[f_.index]))
        bm.select_flush(True); _bm.update_edit_mesh(me)
        bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.02, area_weight=1.0, correct_aspect=True, scale_to_bounds=True)
        bpy.ops.object.mode_set(mode='OBJECT')
    NUV = np.empty(nl * 2, dtype=np.float32); zl.data.foreach_get('uv', NUV); NUV = NUV.reshape(-1, 2)
    # голова: цилиндр вокруг оси головы, лицо (−Y) в центре
    hx, hy = float(np.median(V[dom == names.index('Head'), 0])), float(np.median(V[dom == names.index('Head'), 1]))
    for zn in ('face', 'hair'):
        fl = np.concatenate([np.arange(LS[f], LS[f] + LT[f]) for f in np.where(zone == zn)[0]]) if (zone == zn).any() else np.array([], dtype=np.int64)
        if len(fl) == 0: continue
        p = V[LV[fl]]
        ang = np.arctan2(p[:, 0] - hx, -(p[:, 1] - hy))   # 0 — лицо, ±π — затылок
        u = 0.5 + ang / (2 * np.pi); vv = p[:, 2]
        vv = (vv - vv.min()) / max(1e-6, vv.max() - vv.min())
        NUV[fl] = np.stack([0.02 + 0.96 * u, 0.02 + 0.96 * vv], 1)
    # каждая зона — в свой прямоугольник (с полями)
    for zn in ZONES:
        fs = np.where(zone == zn)[0]
        if len(fs) == 0: continue
        fl = np.concatenate([np.arange(LS[f], LS[f] + LT[f]) for f in fs])
        q = NUV[fl]; mn, mx = q.min(0), q.max(0); span = np.maximum(mx - mn, 1e-6)
        u0, v0, w, h = RECT[zn]; pad = 0.012
        s = min((w - 2 * pad) / span[0], (h - 2 * pad) / span[1]) if zn not in ('face', 'hair') else None
        if s is None: NUV[fl] = np.stack([u0 + pad + (q[:, 0] - mn[0]) / span[0] * (w - 2 * pad), v0 + pad + (q[:, 1] - mn[1]) / span[1] * (h - 2 * pad)], 1)
        else: NUV[fl] = np.stack([u0 + pad + (q[:, 0] - mn[0]) * s, v0 + pad + (q[:, 1] - mn[1]) * s], 1)
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
