#!/usr/bin/env python3
"""Meshy API в облаке (GitHub Actions, ключ — секрет MESHY_KEY; в лог и в файлы ключ не попадает).

Заявка — tools/meshy/request.json (одна задача или список задач):
  {"action": "balance"}                                         — сколько кредитов осталось
  {"action": "text",  "name": "lantern", "prompt": "...",       — текст → модель (черновик, затем текстура)
   "refine": true, "texture_prompt": "...", "art_style": "realistic", "polycount": 20000, "topology": "triangle",
   "enable_pbr": false, "ai_model": "", "symmetry_mode": "auto"}
  {"action": "image", "name": "oni_mask", "image": "tools/meshy/in/oni.png", "rig": true, "height": 1.7,  — картинка → модель (+скелет Meshy)
   "polycount": 20000, "topology": "triangle", "texture": true, "enable_pbr": false, "pose_mode": "", "texture_prompt": ""}
  {"action": "rig",   "name": "ronin", "task_id": "..." | "model_url": "...", "height": 1.7}   — скелет + базовые анимации
  {"action": "status", "kind": "text-to-3d|image-to-3d|rigging|animations", "task_id": "..."}  — докачать готовую задачу
  {"action": "library"}                                                                         — список готовых анимаций (бесплатно)
  {"action": "animate", "name": "unit_archer", "rig_task_id": "...", "action_ids": [1, 2, 3], "fps": 30}
                                                                    — анимации из библиотеки Meshy на риг (один FBX, клип на действие)
Результат — out/<name>/: task.json (ответ API без ключа), model.glb/.fbx, превью, текстуры; общий журнал out/log.txt.
"""
import base64, json, mimetypes, os, sys, time, urllib.request, urllib.error

API = "https://api.meshy.ai/openapi"
KEYS = {"1": os.environ.get("MESHY_KEY", "").strip()}
for _i in range(1, 9):   # второй аккаунт: первый заданный из запасных секретов
    _v = os.environ.get("MESHY_KEY_ALT%d" % _i, "").strip()
    if _v and "2" not in KEYS: KEYS["2"] = _v; KEYS["2name"] = "ALT%d" % _i
KEY = KEYS["1"]
import hashlib as _h
if KEYS.get("1") and KEYS.get("2"): print("ключи аккаунтов", "ОДИНАКОВЫЕ" if KEYS["1"] == KEYS["2"] else "разные", flush=True)
OUT = sys.argv[2] if len(sys.argv) > 2 else "out"
LOG = []


def log(*a):
    s = " ".join(str(x) for x in a)
    print(s, flush=True); LOG.append(s)


def call(method, path, body=None):
    req = urllib.request.Request(API + path, method=method, headers={"Authorization": "Bearer " + KEY, "Content-Type": "application/json"})
    data = json.dumps(body).encode() if body is not None else None
    try:
        with urllib.request.urlopen(req, data=data, timeout=120) as r:
            return json.loads(r.read().decode() or "{}")
    except urllib.error.HTTPError as e:
        msg = e.read().decode(errors="replace")[:2000]
        raise RuntimeError(f"{method} {path} → HTTP {e.code}: {msg}")


def get_file(url, dst):
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    with urllib.request.urlopen(url, timeout=300) as r, open(dst, "wb") as f:
        f.write(r.read())
    log("  скачано", dst, os.path.getsize(dst), "байт")


def wait(kind, tid, limit=1800):
    t0 = time.time(); last = None
    while True:
        j = call("GET", f"{kind}/{tid}")
        st, pr = j.get("status"), j.get("progress")
        if (st, pr) != last: log(f"  {kind} {tid}: {st} {pr}%"); last = (st, pr)
        if st in ("SUCCEEDED", "FAILED", "CANCELED", "EXPIRED"): return j
        if time.time() - t0 > limit: raise RuntimeError("слишком долго ждём задачу " + tid)
        time.sleep(10)


def save(name, j, stage):
    d = os.path.join(OUT, name); os.makedirs(d, exist_ok=True)
    with open(os.path.join(d, f"task_{stage}.json"), "w") as f: json.dump(j, f, ensure_ascii=False, indent=1)
    if j.get("status") != "SUCCEEDED":
        log("  НЕ ВЫШЛО:", json.dumps(j.get("task_error") or j, ensure_ascii=False)[:600]); return
    for k, u in (j.get("model_urls") or {}).items():
        if u and k in ("glb", "fbx"): get_file(u, os.path.join(d, f"{name}_{stage}.{k}"))
    if j.get("thumbnail_url"): get_file(j["thumbnail_url"], os.path.join(d, f"{name}_{stage}_thumb.png"))
    for i, tx in enumerate(j.get("texture_urls") or []):
        for k, u in tx.items():
            if u and isinstance(u, str) and u.startswith("http"): get_file(u, os.path.join(d, f"{name}_{stage}_{k}{'' if i == 0 else i}.png"))
    res = j.get("result") or {}
    for k, u in res.items():
        if isinstance(u, str) and u.startswith("http"): get_file(u, os.path.join(d, f"{name}_{k}." + u.split("?")[0].rsplit(".", 1)[-1]))
        elif isinstance(u, dict):
            for k2, u2 in u.items():
                if isinstance(u2, str) and u2.startswith("http"): get_file(u2, os.path.join(d, f"{name}_{k2}." + u2.split("?")[0].rsplit(".", 1)[-1]))


def opt(body, r, key, field=None):
    v = r.get(key)
    if v not in (None, ""): body[field or key] = v


def do(r):
    global KEY
    acc = str(r.get("account", "1"))
    KEY = KEYS.get(acc, "")
    if not KEY: raise RuntimeError("нет ключа для аккаунта " + acc)
    a = r.get("action"); name = r.get("name", "model")
    log(f"== {a} {name}")
    if a == "balance":
        log("  баланс:", json.dumps(call("GET", "/v1/balance"), ensure_ascii=False)); return
    if a == "text":
        b = {"mode": "preview", "prompt": r["prompt"], "should_remesh": True}
        opt(b, r, "art_style"); opt(b, r, "ai_model"); opt(b, r, "topology"); opt(b, r, "polycount", "target_polycount"); opt(b, r, "symmetry_mode"); opt(b, r, "pose_mode")
        tid = call("POST", "/v2/text-to-3d", b)["result"]; log("  черновик:", tid)
        j = wait("/v2/text-to-3d", tid); save(name, j, "preview")
        if r.get("refine", True) and j.get("status") == "SUCCEEDED":
            b2 = {"mode": "refine", "preview_task_id": tid}
            opt(b2, r, "texture_prompt"); opt(b2, r, "enable_pbr"); opt(b2, r, "ai_model")
            tid2 = call("POST", "/v2/text-to-3d", b2)["result"]; log("  текстура:", tid2)
            save(name, wait("/v2/text-to-3d", tid2), "refine")
        return
    if a == "image":
        p = r["image"]
        if p.startswith("http"): url = p
        else:
            mt = mimetypes.guess_type(p)[0] or "image/png"
            url = f"data:{mt};base64," + base64.b64encode(open(p, "rb").read()).decode()
        b = {"image_url": url, "should_remesh": True, "should_texture": r.get("texture", True)}
        opt(b, r, "ai_model"); opt(b, r, "topology"); opt(b, r, "polycount", "target_polycount"); opt(b, r, "enable_pbr")
        opt(b, r, "pose_mode"); opt(b, r, "texture_prompt"); opt(b, r, "symmetry_mode")
        tid = call("POST", "/v1/image-to-3d", b)["result"]; log("  задача:", tid)
        j = wait("/v1/image-to-3d", tid, 2400); save(name, j, "image")
        if r.get("rig") and j.get("status") == "SUCCEEDED":   # сразу скелет Meshy (дальше наш скелет ставит build_meshy_char.py)
            rid = call("POST", "/v1/rigging", {"input_task_id": tid, "height_meters": r.get("height", 1.7)})["result"]; log("  риг:", rid)
            save(name, wait("/v1/rigging", rid), "rig")
        return
    if a == "rig":
        b = {"height_meters": r.get("height", 1.7)}
        opt(b, r, "task_id", "input_task_id"); opt(b, r, "model_url")
        tid = call("POST", "/v1/rigging", b)["result"]; log("  риг:", tid)
        save(name, wait("/v1/rigging", tid), "rig"); return
    if a == "library":
        j = call("GET", "/v1/animations/library")
        d = os.path.join(OUT, "_library"); os.makedirs(d, exist_ok=True)
        with open(os.path.join(d, "library.json"), "w") as f: json.dump(j, f, ensure_ascii=False, indent=1)
        n = len(j) if isinstance(j, list) else len(j.get("result", j.get("data", [])) or [])
        log("  анимаций в библиотеке:", n); return
    if a == "animate":
        b = {"rig_task_id": r["rig_task_id"]}
        if r.get("action_ids"): b["action_ids"] = r["action_ids"]
        else: b["action_id"] = r["action_id"]
        if r.get("fps"): b["post_process"] = {"operation_type": "change_fps", "fps": r["fps"]}
        tid = call("POST", "/v1/animations", b)["result"]; log("  анимация:", tid)
        save(name, wait("/v1/animations", tid), "anim"); return
    if a == "status":
        kind = {"text-to-3d": "/v2/text-to-3d", "image-to-3d": "/v1/image-to-3d", "rigging": "/v1/rigging", "animations": "/v1/animations"}[r["kind"]]
        save(name, wait(kind, r["task_id"]), "status"); return
    raise RuntimeError("неизвестное действие: " + str(a))


def main():
    os.makedirs(OUT, exist_ok=True); ok = True
    if not KEYS.get("1") and not KEYS.get("2"): log("нет ключей Meshy"); ok = False
    else:
        req = json.load(open(sys.argv[1] if len(sys.argv) > 1 else "tools/meshy/request.json"))
        for r in (req if isinstance(req, list) else [req]):
            try: do(r)
            except Exception as e: ok = False; log("  ОШИБКА:", str(e).replace(KEY, "***"))
    for acc in ("1", "2"):
        KEY = KEYS.get(acc, "")
        if not KEY: log("  аккаунт", acc, ": ключа нет"); continue
        try: log("  аккаунт", acc, (KEYS.get(acc + "name", "") if acc == "2" else "MESHY_KEY"), "— остаток кредитов:", call("GET", "/v1/balance").get("balance"))
        except Exception as e: log("  аккаунт", acc, ": баланс не получен:", str(e).replace(KEY, "***"))
    with open(os.path.join(OUT, "log.txt"), "w") as f: f.write("\n".join(LOG) + "\n")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
