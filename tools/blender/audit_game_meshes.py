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
from collections import defaultdict

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
            from io_scene_fbx import import_fbx
            class ImportReporter:
                def report(self, level, message):
                    print('FBX_IMPORT', level, message, flush=True)
            result = import_fbx.load(ImportReporter(), bpy.context, filepath=os.path.abspath(path))
            if result != {'FINISHED'}:
                raise RuntimeError('FBX importer did not finish: ' + repr(result))
        else:
            # Blender 3.x (apt) supports import_scene.obj; 4.x supports wm.obj_import.
            if hasattr(bpy.ops.wm, "obj_import"):
                bpy.ops.wm.obj_import(filepath=os.path.abspath(path))
            else:
                bpy.ops.import_scene.obj(filepath=os.path.abspath(path))
        models = [ob for ob in bpy.context.scene.objects if ob.type == "MESH"]
        item = dict(path=path, objects=len(models), tris=0, open_edges=0, raw_open_edges=0,
                    total_edges=0, nonmanifold_edges=0, loose_edges=0, components=0,
                    skin_seam_groups=0, skin_weight_mismatch=0, severe_skin_weight_mismatch=0,
                    loops=[], meshes=[], error=None)
        for ob in models:
            # Coincident skin vertices on UV/material seams must move together.
            # Report weight discontinuities that can become visible cracks when
            # the skeleton bends. This is diagnostic ONLY; never change weights.
            geometry_seams = defaultdict(list)
            tol = max(1e-8, max(ob.dimensions) * 1e-6)
            for vi, vert in enumerate(ob.data.vertices):
                key = tuple(round(p / tol) for p in vert.co)
                geometry_seams[key].append(vi)
            groups = [ids for ids in geometry_seams.values() if len(ids) > 1]
            mismatched = severe = 0
            largest_divergences = []
            if ob.vertex_groups:
                def get_weights(vi):
                    return {g.group: g.weight for g in ob.data.vertices[vi].groups}
                for ids in groups:
                    reference = get_weights(ids[0])
                    biggest = 0.0
                    for vi in ids[1:]:
                        w = get_weights(vi)
                        div = 0.5 * sum(abs(reference.get(b, 0.0) - w.get(b, 0.0))
                                        for b in reference.keys() | w.keys())
                        biggest = max(biggest, div)
                    if biggest > 0.15:
                        mismatched += 1
                        if biggest > 0.5: severe += 1
                        if biggest > 0.5 and len(largest_divergences) < 8:
                            largest_divergences.append(dict(pos=[round(v, 4) for v in ob.data.vertices[ids[0]].co], divergence=round(biggest, 3)))
            item["skin_seam_groups"] += len(groups)
            item["skin_weight_mismatch"] += mismatched
            item["severe_skin_weight_mismatch"] += severe
            bm = bmesh.new()
            bm.from_mesh(ob.data)
            raw_open = sum(1 for e in bm.edges if e.is_boundary)
            # UV/hard-normal splits duplicate vertices at identical positions.
            # They are not real missing geometry; weld on an in-memory copy ONLY.
            tolerance = max(1e-8, max(ob.dimensions) * 1e-6)
            bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=tolerance)
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
                        nonmanifold_edges=mult, loose_edges=loose, raw_boundary_edges=raw_open,
                        boundary_loops=len(loops), open_chains=len(chains),
                        loops=loops[:120], skin_seam_groups=len(groups), skin_weight_mismatch=mismatched,
                        severe_skin_weight_mismatch=severe, skin_seam_samples=largest_divergences)
            item["meshes"].append(mesh)
            item["tris"] += tris
            item["total_edges"] += len(bm.edges)
            item["open_edges"] += len(boundaries)
            item["raw_open_edges"] += raw_open
            item["nonmanifold_edges"] += mult
            item["loose_edges"] += loose
            item["components"] += len(loops)
            item["loops"].extend([dict(mesh=ob.name, **L) for L in loops[:120]])
            bm.free()
        report.append(item)
        print("MODEL_AUDIT_RESULT", json.dumps(dict(path=path, objects=item["objects"],
              tris=item["tris"], raw_open_edges=item["raw_open_edges"], geometric_open_edges=item["open_edges"], loops=item["components"],
              nonmanifold=item["nonmanifold_edges"], seam_groups=item["skin_seam_groups"],
              skin_mismatch=item["skin_weight_mismatch"], severe_skin_mismatch=item["severe_skin_weight_mismatch"])), flush=True)
    except Exception as ex:
        report.append(dict(path=path, error=str(ex), traceback=traceback.format_exc()))
        print("MODEL_AUDIT_ERROR", path, str(ex), flush=True)

with open(DEST, "w", encoding="utf-8") as f:
    json.dump(dict(files=len(paths), results=report), f, ensure_ascii=False, indent=2)
print("MODEL_AUDIT_DONE", len(paths), "errors", sum(bool(r.get("error")) for r in report),
      "open_edges", sum(r.get("open_edges", 0) for r in report),
      "loops", sum(r.get("components", 0) for r in report), flush=True)
