"""Headless, non-destructive topology audit for EVERY shipped FBX/OBJ.

Launch: blender -b --factory-startup --python tools/blender/audit_game_meshes.py
Emits build/model-holes/report.json. This only audits source geometry; holes are
NOT auto-filled because the rims of sleeves, collars and armor are intentional.
"""
import bpy
import bmesh
import collections
import glob
import json
import math
import os
import traceback

SRC = "Assets/_Project/Models"
DEST = "build/model-holes/report.json"
os.makedirs(os.path.dirname(DEST), exist_ok=True)

paths = sorted(
    p for pattern in ("**/*.fbx", "**/*.obj")
    for p in glob.glob(os.path.join(SRC, pattern), recursive=True)
)
report = []
for ix, path in enumerate(paths):
    print("MODEL_AUDIT_BEGIN", ix + 1, len(paths), path, flush=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    try:
        if path.lower().endswith(".fbx"):
            bpy.ops.import_scene.fbx(filepath=os.path.abspath(path), use_anim=False)
        else:
            # Blender 3.x (apt) supports import_scene.obj; 4.x supports wm.obj_import.
            if hasattr(bpy.ops.wm, "obj_import"):
                bpy.ops.wm.obj_import(filepath=os.path.abspath(path))
            else:
                bpy.ops.import_scene.obj(filepath=os.path.abspath(path))
        models = [ob for ob in bpy.context.scene.objects if ob.type == "MESH"]
        item = dict(path=path, objects=len(models), tris=0, open_edges=0,
                    total_edges=0, nonmanifold_edges=0, loose_edges=0, components=0,
                    loops=[], meshes=[], error=None)
        for ob in models:
            bm = bmesh.new()
            bm.from_mesh(ob.data)
            bm.verts.ensure_lookup_table()
            bm.edges.ensure_lookup_table()
            bm.faces.ensure_lookup_table()
            boundaries = [e for e in bm.edges if e.is_boundary]
            loose = sum(1 for e in bm.edges if e.is_wire)
            mult = sum(1 for e in bm.edges if not e.is_manifold and not e.is_boundary and not e.is_wire)
            tris = sum(len(f.verts)-2 for f in bm.faces)
            # A loop with 3+ edges can be a geometric hole OR an intentional
            # garment opening. Record both; never silently cap an intentional rim.
            neighbors = collections.defaultdict(list)
            for e in boundaries:
                a, b = e.verts
                neighbors[a].append((b, e))
                neighbors[b].append((a, e))
            used = set()
            loops = []
            chains = []
            for edge in boundaries:
                if edge in used:
                    continue
                edge_list = [edge]
                used.add(edge)
                origin = edge.verts[0]
                current = edge.verts[1]
                prev = origin
                verts = [origin, current]
                while len(edge_list) <= len(boundaries):
                    opts = [(v,e) for (v,e) in neighbors[current]
                            if e not in used and v != prev]
                    if len(opts) != 1:
                        # For branched rims, don't guess which way to continue.
                        break
                    nxt, e = opts[0]
                    used.add(e)
                    edge_list.append(e)
                    prev, current = current, nxt
                    verts.append(current)
                    if current == origin:
                        break
                closed = current == origin
                if closed and len(edge_list) >= 3:
                    points = [v.co for v in verts[:-1]]
                    perimeter = sum((verts[k].co-verts[k+1].co).length
                                    for k in range(len(verts)-1))
                    avg = sum(points, points[0]*0.0) / len(points)
                    span = max((p-avg).length for p in points)
                    # Projection-independent polygon area (Newell).
                    area_vec = points[0]*0.0
                    for a,b in zip(points, points[1:]+points[:1]):
                        area_vec += a.cross(b)
                    area = 0.5*area_vec.length
                    loops.append(dict(n=len(edge_list),
                                      perimeter=round(perimeter, 7),
                                      area=round(area, 8), span=round(span, 7)))
                else:
                    chains.append(len(edge_list))
            bbox_max = max((max(ob.dimensions)), 0.000001)
            for L in loops:
                L["relative_area"] = round(L["area"] / (bbox_max*bbox_max), 8)
                L["relative_span"] = round(L["span"] / bbox_max, 7)
            loops.sort(key=lambda L: L["relative_area"])
            mesh = dict(name=ob.name, verts=len(bm.verts), tris=tris,
                        total_edges=len(bm.edges), boundary_edges=len(boundaries),
                        nonmanifold_edges=mult, loose_edges=loose,
                        boundary_loops=len(loops), open_chains=len(chains),
                        loops=loops[:120])
            item["meshes"].append(mesh)
            item["tris"] += tris
            item["total_edges"] += len(bm.edges)
            item["open_edges"] += len(boundaries)
            item["nonmanifold_edges"] += mult
            item["loose_edges"] += loose
            item["components"] += len(loops)
            item["loops"].extend([dict(mesh=ob.name, **L) for L in loops[:120]])
            bm.free()
        report.append(item)
        print("MODEL_AUDIT_RESULT", json.dumps(dict(path=path, objects=item["objects"],
              tris=item["tris"], open_edges=item["open_edges"], loops=item["components"],
              nonmanifold=item["nonmanifold_edges"])), flush=True)
    except Exception as ex:
        report.append(dict(path=path, error=str(ex), traceback=traceback.format_exc()))
        print("MODEL_AUDIT_ERROR", path, str(ex), flush=True)

with open(DEST, "w", encoding="utf-8") as f:
    json.dump(dict(files=len(paths), results=report), f, ensure_ascii=False, indent=2)
print("MODEL_AUDIT_DONE", len(paths), "errors", sum(bool(r.get("error")) for r in report),
      "open_edges", sum(r.get("open_edges", 0) for r in report),
      "loops", sum(r.get("components", 0) for r in report), flush=True)
