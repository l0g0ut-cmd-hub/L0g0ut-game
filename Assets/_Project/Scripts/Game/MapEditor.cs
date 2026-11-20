using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Редактор карт (меню разработчика на карте похода → «РЕДАКТОР КАРТ»). Правит файл карты области (MapData):
    /// места, развилки, дороги с изгибами, переправы, эффекты (водопады, дым, костёр, туман).
    /// Всё двигается пальцем: место, развилка, точка изгиба, эффект (за середину; за угол — размер). Кружок между точками
    /// дороги — потянуть, появится новая точка изгиба. Долгое нажатие на точку изгиба — удалить. Пустое место — двигать карту,
    /// двумя пальцами — двигать и приближать. Каждое действие можно отменить (30 шагов).
    /// Сохранение — MapStore.Save (persistentDataPath/maps), прежняя версия уходит в историю (10 последних).
    /// </summary>
    public static class MapEditor
    {
        public static bool Open { get; private set; }

        enum Tool { Select, Place, Road, Water, Effect, Erase }
        static readonly string[] ToolNames = { "ВЫБОР", "МЕСТО", "ДОРОГА", "ПЕРЕПРАВА", "ЭФФЕКТ", "ЛАСТИК" };
        static readonly string[] ToolHints = { "перенести", "поставить", "провести", "по воде", "вода, дым…", "убрать" };

        // что под пальцем / что выбрано
        const int HNone = 0, HPlace = 1, HPoint = 2, HBend = 3, HMid = 4, HEffect = 5, HCorner = 6, HRoad = 7;
        struct Hit { public int kind, i, k; }

        static int region;
        static int custom = -1;   // своя область (MapStore.GetCustom); −1 — встроенная region
        static MapData data;
        static Texture2D tex;
        static Tool tool;
        static Hit sel;
        static int libSel;
        static bool dirty;
        static readonly List<string> undo = new List<string>();

        static float W;
        static Rect mapR;
        static float zoom = 0.5f, vx, vy;   // точек экрана на пиксель картины; левый верхний угол видимой части (пиксели картины)

        // палец
        static bool down, dragging, panning, pinching, undoPushed;
        static Vector2 downPos, lastPos;
        static float downAt;
        static Hit downHit;
        static float pinchDist0, pinchZoom0; static Vector2 pinchTex0;

        // дорога, которую сейчас проводят
        static string roadFrom;
        static readonly List<Vector2> roadPts = new List<Vector2>();   // изгибы в долях картины
        static bool roadFromNew;

        // окна и подтверждения
        static bool historyOpen;
        static string confirm;   // какое действие ждёт второго нажатия
        static float confirmAt;
        static string note; static float noteAt;

        // проверка
        static readonly List<string> issues = new List<string>();
        static readonly HashSet<string> bad = new HashSet<string>();
        static int checkVer = -1, ver;
        static int issueCursor;

        static readonly Color Gold = new Color(0.9f, 0.76f, 0.47f), Ink = new Color(0.93f, 0.89f, 0.8f), Dim = new Color(0.68f, 0.64f, 0.56f);
        static readonly Color Panel = new Color(0.09f, 0.07f, 0.055f, 0.95f), Edge = new Color(0.59f, 0.44f, 0.24f, 1f);
        static readonly Color RoadC = new Color(1f, 0.92f, 0.67f, 0.95f), WaterC = new Color(0.55f, 0.82f, 1f, 0.95f), BadC = new Color(1f, 0.25f, 0.2f, 1f);

        // ---------------------------------------------------------------- открыть / закрыть

        public static void Show(int r)
        {
            Open = true; tool = Tool.Select; historyOpen = false; confirm = null;
            if (MapStore.IsCustom(r)) LoadCustom(MapStore.Custom); else Load(r);   // поход идёт по своей области — открыть её
        }

        static void Load(int r)
        {
            region = Mathf.Clamp(r, 0, MapStore.BuiltIn - 1); custom = -1;
            MapData src = MapStore.GetPlain(region);
            data = src != null ? Clone(src) : new MapData { name = Campaign.RegionNames[region], texture = "UI/map_wide_" + region, width = MapGeom.TexW[region] };
            AfterLoad();
        }

        static void LoadCustom(int n)
        {
            MapData src = MapStore.GetCustom(n);
            if (src == null) { Load(0); return; }
            custom = n; region = Mathf.Clamp(src.like, 0, MapStore.BuiltIn - 1);
            data = Clone(src);
            AfterLoad();
        }

        /// <summary>Следующая область по кругу: встроенные, потом свои.</summary>
        static void NextRegion()
        {
            List<int> cs = MapStore.Customs();
            if (custom < 0)
            {
                if (region + 1 < MapStore.BuiltIn) Load(region + 1);
                else if (cs.Count > 0) LoadCustom(cs[0]);
                else Load(0);
                return;
            }
            int k = cs.IndexOf(custom);
            if (k >= 0 && k + 1 < cs.Count) LoadCustom(cs[k + 1]); else Load(0);
        }

        static void AfterLoad()
        {
            if (data.places == null) data.places = new MapPlace[0];
            if (data.points == null) data.points = new MapPoint[0];
            if (data.roads == null) data.roads = new MapRoad[0];
            if (data.effects == null) data.effects = new MapEffect[0];
            tex = UiKit.Tex(string.IsNullOrEmpty(data.texture) ? "UI/map_wide_" + region : data.texture);
            undo.Clear(); dirty = false; sel = new Hit(); CancelRoad(); ver++;
            zoom = -1f;   // подогнать при первом кадре
        }

        static MapData Clone(MapData m) { return JsonUtility.FromJson<MapData>(JsonUtility.ToJson(m)); }
        static float TW { get { return data.width > 0 ? data.width : 2610f; } }
        const float TH = 1024f;

        // ---------------------------------------------------------------- координаты

        static Vector2 Scr(float tx, float ty) { return new Vector2(mapR.x + (tx - vx) * zoom, mapR.y + (ty - vy) * zoom); }
        static Vector2 ScrF(float fx, float fy) { return Scr(fx * TW, fy * TH); }
        static Vector2 TexAt(Vector2 s) { return new Vector2(vx + (s.x - mapR.x) / zoom, vy + (s.y - mapR.y) / zoom); }
        static Vector2 FracAt(Vector2 s) { Vector2 t = TexAt(s); return new Vector2(Mathf.Clamp01(t.x / TW), Mathf.Clamp01(t.y / TH)); }
        static float MinZoom { get { return Mathf.Max(mapR.height / TH, mapR.width / TW); } }
        static void ClampView()
        {
            zoom = Mathf.Clamp(zoom, MinZoom, 3f);
            vx = Mathf.Clamp(vx, 0f, Mathf.Max(0f, TW - mapR.width / zoom));
            vy = Mathf.Clamp(vy, 0f, Mathf.Max(0f, TH - mapR.height / zoom));
        }
        static void ZoomAround(Vector2 screen, float newZoom)
        {
            Vector2 t = TexAt(screen);
            zoom = Mathf.Clamp(newZoom, MinZoom, 3f);
            vx = t.x - (screen.x - mapR.x) / zoom; vy = t.y - (screen.y - mapR.y) / zoom;
            ClampView();
        }

        // ---------------------------------------------------------------- данные

        static void Push()
        {
            undo.Add(JsonUtility.ToJson(data));
            if (undo.Count > 30) undo.RemoveAt(0);
            dirty = true; ver++;
        }
        static void Undo()
        {
            if (undo.Count == 0) return;
            data = JsonUtility.FromJson<MapData>(undo[undo.Count - 1]);
            undo.RemoveAt(undo.Count - 1);
            sel = new Hit(); CancelRoad(); dirty = true; ver++;
        }

        static T[] Add<T>(T[] a, T v) { List<T> l = new List<T>(a); l.Add(v); return l.ToArray(); }
        static T[] RemoveAt<T>(T[] a, int i) { List<T> l = new List<T>(a); l.RemoveAt(i); return l.ToArray(); }

        static string NewId(string prefix)
        {
            for (int n = 1; ; n++)
            {
                string id = prefix + n;
                if (!data.Pos(id).HasValue) return id;
            }
        }
        static string IdOf(Hit h)
        {
            if (h.kind == HPlace) return data.places[h.i].id;
            if (h.kind == HPoint) return data.points[h.i].id;
            return null;
        }
        static void MoveId(string id, Vector2 f)
        {
            foreach (MapPlace p in data.places) if (p.id == id) { p.x = Round(f.x); p.y = Round(f.y); }
            foreach (MapPoint p in data.points) if (p.id == id) { p.x = Round(f.x); p.y = Round(f.y); }
        }
        static float Round(float v) { return Mathf.Round(v * 10000f) / 10000f; }

        /// <summary>Убрать место или развилку вместе с дорогами к ней. Развилка ровно с двумя дорогами — дороги сливаются в одну.</summary>
        static void DeleteId(string id)
        {
            List<int> touch = new List<int>();
            for (int r = 0; r < data.roads.Length; r++) if (data.roads[r].a == id || data.roads[r].b == id) touch.Add(r);
            bool isPoint = false;
            for (int i = 0; i < data.points.Length; i++) if (data.points[i].id == id) { isPoint = true; data.points = RemoveAt(data.points, i); break; }
            for (int i = 0; i < data.places.Length; i++) if (data.places[i].id == id) { data.places = RemoveAt(data.places, i); break; }
            if (isPoint && touch.Count == 2)
            {
                MapRoad r1 = data.roads[touch[0]], r2 = data.roads[touch[1]];
                // точка развилки становится точкой изгиба слитой дороги
                List<float> pts = new List<float>();
                string a, b;
                List<Vector2> l1 = Bends(r1), l2 = Bends(r2);
                if (r1.b != id) { l1.Reverse(); a = r1.b; } else a = r1.a;
                if (r2.a != id) { l2.Reverse(); b = r2.a; } else b = r2.b;
                foreach (Vector2 v in l1) { pts.Add(v.x); pts.Add(v.y); }
                pts.Add(lastDeleted.x); pts.Add(lastDeleted.y);
                foreach (Vector2 v in l2) { pts.Add(v.x); pts.Add(v.y); }
                MapRoad m = new MapRoad { a = a, b = b, pts = pts.ToArray(), water = r1.water && r2.water, trail = r1.trail || r2.trail };
                List<MapRoad> keep = new List<MapRoad>();
                for (int r = 0; r < data.roads.Length; r++) if (r != touch[0] && r != touch[1]) keep.Add(data.roads[r]);
                if (a != b) keep.Add(m);
                data.roads = keep.ToArray();
            }
            else
            {
                List<MapRoad> keep = new List<MapRoad>();
                foreach (MapRoad r in data.roads) if (r.a != id && r.b != id) keep.Add(r);
                data.roads = keep.ToArray();
            }
        }
        static Vector2 lastDeleted;
        static List<Vector2> Bends(MapRoad r)
        {
            List<Vector2> l = new List<Vector2>();
            for (int i = 0; i + 1 < r.pts.Length; i += 2) l.Add(new Vector2(r.pts[i], r.pts[i + 1]));
            return l;
        }
        static void SetBends(MapRoad r, List<Vector2> l)
        {
            float[] a = new float[l.Count * 2];
            for (int i = 0; i < l.Count; i++) { a[i * 2] = Round(l[i].x); a[i * 2 + 1] = Round(l[i].y); }
            r.pts = a;
        }

        /// <summary>Точку изгиба дороги — в развилку: дорога делится на две, к развилке можно вести новые дороги.</summary>
        static string SplitAt(int road, int bend)
        {
            MapRoad r = data.roads[road];
            List<Vector2> l = Bends(r);
            Vector2 p = l[bend];
            string id = NewId("J");
            data.points = Add(data.points, new MapPoint { id = id, x = p.x, y = p.y });
            MapRoad r1 = new MapRoad { a = r.a, b = id, water = r.water, trail = r.trail }, r2 = new MapRoad { a = id, b = r.b, water = r.water, trail = r.trail };
            SetBends(r1, l.GetRange(0, bend));
            SetBends(r2, l.GetRange(bend + 1, l.Count - bend - 1));
            data.roads[road] = r1;
            data.roads = Add(data.roads, r2);
            return id;
        }

        // ---------------------------------------------------------------- поиск под пальцем

        static float DistSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float t = ab.sqrMagnitude < 0.001f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }
        static List<Vector2> ScreenLine(MapRoad r)
        {
            List<Vector2> l = data.Line(r), s = new List<Vector2>(l.Count);
            foreach (Vector2 v in l) s.Add(ScrF(v.x, v.y));
            return s;
        }
        static Rect EffRect(MapEffect e) { Vector2 a = Scr(e.x, e.y); return new Rect(a.x, a.y, e.w * zoom, e.h * zoom); }

        static Hit HitAt(Vector2 s, bool forDrag)
        {
            Hit h = new Hit();
            if (sel.kind == HEffect && sel.i < data.effects.Length)
            {
                Rect er = EffRect(data.effects[sel.i]);
                if (Vector2.Distance(s, new Vector2(er.xMax, er.yMax)) < 16f) { h.kind = HCorner; h.i = sel.i; return h; }
            }
            float best = 18f;
            for (int i = 0; i < data.places.Length; i++)
            {
                float d = Vector2.Distance(s, ScrF(data.places[i].x, data.places[i].y));
                if (d < best) { best = d; h.kind = HPlace; h.i = i; }
            }
            if (h.kind != HNone) return h;
            best = 12f;
            for (int i = 0; i < data.points.Length; i++)
            {
                float d = Vector2.Distance(s, ScrF(data.points[i].x, data.points[i].y));
                if (d < best) { best = d; h.kind = HPoint; h.i = i; }
            }
            if (h.kind != HNone) return h;
            best = 11f;
            for (int r = 0; r < data.roads.Length; r++)
            {
                List<Vector2> l = ScreenLine(data.roads[r]);
                for (int k = 1; k < l.Count - 1; k++)
                {
                    float d = Vector2.Distance(s, l[k]);
                    if (d < best) { best = d; h.kind = HBend; h.i = r; h.k = k - 1; }
                }
            }
            if (h.kind != HNone) return h;
            if (forDrag)
            {
                best = 9f;
                for (int r = 0; r < data.roads.Length; r++)
                {
                    List<Vector2> l = ScreenLine(data.roads[r]);
                    for (int k = 0; k + 1 < l.Count; k++)
                    {
                        if (Vector2.Distance(l[k], l[k + 1]) < 30f) continue;
                        float d = Vector2.Distance(s, (l[k] + l[k + 1]) * 0.5f);
                        if (d < best) { best = d; h.kind = HMid; h.i = r; h.k = k; }
                    }
                }
                if (h.kind != HNone) return h;
            }
            for (int i = data.effects.Length - 1; i >= 0; i--)
                if (EffRect(data.effects[i]).Contains(s)) { h.kind = HEffect; h.i = i; return h; }
            best = 8f;
            for (int r = 0; r < data.roads.Length; r++)
            {
                List<Vector2> l = ScreenLine(data.roads[r]);
                for (int k = 0; k + 1 < l.Count; k++)
                {
                    float d = DistSeg(s, l[k], l[k + 1]);
                    if (d < best) { best = d; h.kind = HRoad; h.i = r; }
                }
            }
            return h;
        }

        // ---------------------------------------------------------------- кадр

        public static void Draw(float w)
        {
            W = w;
            if (customEdit != null) { DrawCustomEdit(); return; }   // своё место — на весь экран
            mapR = new Rect(112f, 52f, W - 112f - 236f, 540f - 52f - 30f);
            if (zoom < 0f) { zoom = MinZoom * 1.6f; vx = TW * 0.5f - mapR.width / zoom * 0.5f; vy = TH * 0.3f; }
            ClampView();
            if (Event.current.type == EventType.Repaint) { HandlePinch(); }
            if (!historyOpen) HandleMap();
            if (checkVer != ver) { Check(); checkVer = ver; }

            UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0.05f, 0.04f, 0.03f, 1f));
            if (tex != null)
                GUI.DrawTextureWithTexCoords(mapR, tex, new Rect(vx / TW, 1f - (vy + mapR.height / zoom) / TH, mapR.width / zoom / TW, mapR.height / zoom / TH));
            DrawOverlay();
            locked = historyOpen;
            DrawPanels();
            locked = false;
            if (historyOpen) DrawHistory();
            if (note != null && Time.unscaledTime - noteAt < 3f)
            {
                Rect nr = new Rect(mapR.x + 20f, mapR.y + 10f, mapR.width - 40f, 30f);
                UiKit.Rect(nr, new Color(0f, 0f, 0f, 0.72f));
                UiKit.Text(nr, note, 15f, Gold);
            }
        }

        static void Note(string s) { note = s; noteAt = Time.unscaledTime; }
        static bool Confirm(string what, string text)
        {
            if (confirm == what && Time.unscaledTime - confirmAt < 4f) { confirm = null; return true; }
            confirm = what; confirmAt = Time.unscaledTime; Note(text); return false;
        }

        // ---------------------------------------------------------------- палец по карте

        static void HandlePinch()
        {
            if (Input.touchCount >= 2)
            {
                Vector2 a = Gui(Input.GetTouch(0).position), b = Gui(Input.GetTouch(1).position);
                float dist = Mathf.Max(10f, Vector2.Distance(a, b)); Vector2 mid = (a + b) * 0.5f;
                if (!pinching)
                {
                    if (!mapR.Contains(mid)) return;
                    pinching = true; pinchDist0 = dist; pinchZoom0 = zoom; pinchTex0 = TexAt(mid);
                    if (dragging && undoPushed) Undo();   // начатое перетаскивание отменяется — второй палец двигает карту
                    down = false; dragging = false; panning = false;
                }
                zoom = Mathf.Clamp(pinchZoom0 * dist / pinchDist0, MinZoom, 3f);
                vx = pinchTex0.x - (mid.x - mapR.x) / zoom; vy = pinchTex0.y - (mid.y - mapR.y) / zoom;
                ClampView();
            }
            else if (pinching && Input.touchCount == 0) pinching = false;
        }
        static Vector2 Gui(Vector2 screen) { return new Vector2(screen.x / UiKit.Scale, (Screen.height - screen.y) / UiKit.Scale); }

        static void HandleMap()
        {
            Event e = Event.current;
            if (pinching) { if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag || e.type == EventType.MouseUp) e.Use(); return; }
            if (e.type == EventType.ScrollWheel && mapR.Contains(e.mousePosition)) { ZoomAround(e.mousePosition, zoom * (e.delta.y > 0f ? 0.9f : 1.1f)); e.Use(); return; }
            if (e.type == EventType.MouseDown && e.button == 0 && mapR.Contains(e.mousePosition))
            {
                down = true; dragging = false; panning = false; undoPushed = false;
                downPos = lastPos = e.mousePosition; downAt = Time.unscaledTime;
                downHit = HitAt(e.mousePosition, true);
                e.Use();
                return;
            }
            if (!down) return;
            if (e.type == EventType.MouseDrag)
            {
                Vector2 p = e.mousePosition;
                if (!dragging && !panning && Vector2.Distance(p, downPos) > 7f)
                {
                    bool movable = tool != Tool.Erase && downHit.kind != HNone && downHit.kind != HRoad;
                    if (movable) { dragging = true; StartDrag(); } else panning = true;
                }
                if (dragging) DragTo(p);
                else if (panning) { vx -= (p.x - lastPos.x) / zoom; vy -= (p.y - lastPos.y) / zoom; ClampView(); }
                lastPos = p;
                e.Use();
                return;
            }
            if (e.type == EventType.MouseUp)
            {
                down = false;
                if (dragging) EndDrag(e.mousePosition);
                else if (!panning) Tap(e.mousePosition, Time.unscaledTime - downAt > 0.6f);
                dragging = false; panning = false;
                e.Use();
            }
        }

        static void StartDrag()
        {
            Push(); undoPushed = true;
            if (downHit.kind == HMid)
            {   // потянули кружок между точками — там появляется новая точка изгиба
                MapRoad r = data.roads[downHit.i];
                List<Vector2> l = Bends(r);
                l.Insert(downHit.k, FracAt(downPos));
                SetBends(r, l);
                downHit.kind = HBend;
            }
            if (downHit.kind == HCorner) sel = new Hit { kind = HEffect, i = downHit.i };
            else sel = downHit;
        }

        static void DragTo(Vector2 p)
        {
            Vector2 f = FracAt(p);
            switch (downHit.kind)
            {
                case HPlace: case HPoint: MoveId(IdOf(downHit), f); break;
                case HBend:
                {
                    MapRoad r = data.roads[downHit.i];
                    List<Vector2> l = Bends(r);
                    if (downHit.k < l.Count) { l[downHit.k] = f; SetBends(r, l); }
                    break;
                }
                case HEffect:
                {
                    MapEffect fx = data.effects[downHit.i];
                    Vector2 d = (p - lastPos) / zoom;
                    fx.x = Mathf.Round(fx.x + d.x); fx.y = Mathf.Round(fx.y + d.y);
                    break;
                }
                case HCorner:
                {
                    MapEffect fx = data.effects[downHit.i];
                    Vector2 t = TexAt(p);
                    fx.w = Mathf.Max(6f, Mathf.Round(t.x - fx.x)); fx.h = Mathf.Max(6f, Mathf.Round(t.y - fx.y));
                    break;
                }
            }
            ver++;
        }

        /// <summary>Отпустили: развилку, брошенную на место или другую развилку, — слить с ними (дороги прилипают).</summary>
        static void EndDrag(Vector2 p)
        {
            if (downHit.kind != HPoint) return;
            string id = data.points[downHit.i].id;
            for (int i = 0; i < data.places.Length; i++)
                if (Vector2.Distance(p, ScrF(data.places[i].x, data.places[i].y)) < 16f) { Merge(id, data.places[i].id); return; }
            for (int i = 0; i < data.points.Length; i++)
                if (data.points[i].id != id && Vector2.Distance(p, ScrF(data.points[i].x, data.points[i].y)) < 12f) { Merge(id, data.points[i].id); return; }
        }
        static void Merge(string from, string into)
        {
            foreach (MapRoad r in data.roads) { if (r.a == from) r.a = into; if (r.b == from) r.b = into; }
            List<MapRoad> keep = new List<MapRoad>();
            foreach (MapRoad r in data.roads) if (r.a != r.b || r.pts.Length > 0) keep.Add(r);
            data.roads = keep.ToArray();
            for (int i = 0; i < data.points.Length; i++) if (data.points[i].id == from) { data.points = RemoveAt(data.points, i); break; }
            sel = new Hit(); ver++;
            Note("Развилка прилипла — дороги соединены");
        }

        static void Tap(Vector2 p, bool longPress)
        {
            Hit h = HitAt(p, false);
            switch (tool)
            {
                case Tool.Erase:
                    if (h.kind == HNone) return;
                    Push(); Erase(h); sel = new Hit(); return;
                case Tool.Place:
                    if (h.kind == HPlace) { sel = h; return; }
                    Push(); AddPlace(FracAt(p)); return;
                case Tool.Road: case Tool.Water:
                    RoadTap(p, h); return;
                case Tool.Effect:
                    if (h.kind == HEffect || h.kind == HCorner) { sel = new Hit { kind = HEffect, i = h.i }; return; }
                    Push(); AddEffect(TexAt(p)); return;
            }
            if (longPress && h.kind == HBend) { Push(); Erase(h); sel = new Hit(); Note("Точка изгиба удалена"); return; }
            sel = h;
        }

        static void Erase(Hit h)
        {
            switch (h.kind)
            {
                case HPlace: DeleteId(data.places[h.i].id); break;
                case HPoint: lastDeleted = new Vector2(data.points[h.i].x, data.points[h.i].y); DeleteId(data.points[h.i].id); break;
                case HBend: { MapRoad r = data.roads[h.i]; List<Vector2> l = Bends(r); if (h.k < l.Count) { l.RemoveAt(h.k); SetBends(r, l); } break; }
                case HRoad: data.roads = RemoveAt(data.roads, h.i); break;
                case HEffect: case HCorner: data.effects = RemoveAt(data.effects, h.i); break;
            }
            ver++;
        }

        static void AddPlace(Vector2 f)
        {
            // ряд — как у ближайшего места
            int layer = 1; float best = float.MaxValue;
            foreach (MapPlace q in data.places)
            {
                float d = Vector2.Distance(new Vector2(q.x * TW, q.y * TH), new Vector2(f.x * TW, f.y * TH));
                if (d < best) { best = d; layer = Mathf.Max(1, q.layer); }
            }
            data.places = Add(data.places, new MapPlace { id = NewId("P"), x = Round(f.x), y = Round(f.y), layer = layer, type = -1, kind = 1 });
            sel = new Hit { kind = HPlace, i = data.places.Length - 1 };
            Note("Место поставлено: ряд " + layer + ", тип — случайно (поменяй справа)");
        }

        static void AddEffect(Vector2 t)
        {
            Vector4 d = MapLife.FxDefaults[libSel];
            data.effects = Add(data.effects, new MapEffect { kind = MapLife.FxKinds[libSel], x = Mathf.Round(t.x - d.x * 0.5f), y = Mathf.Round(t.y - d.y * 0.5f),
                                                              w = d.x, h = d.y, alpha = d.z, speed = 1f, foot = (int)d.w });
            sel = new Hit { kind = HEffect, i = data.effects.Length - 1 };
        }

        // ---- дорога: касания по очереди — начало (место, развилка или новая развилка), изгибы, конец (место или развилка)
        static void RoadTap(Vector2 p, Hit h)
        {
            string id = null;
            if (h.kind == HPlace || h.kind == HPoint) id = IdOf(h);
            else if (h.kind == HBend) { Push(); id = SplitAt(h.i, h.k); }
            if (roadFrom == null)
            {
                if (id == null) { Push(); id = NewId("J"); Vector2 f = FracAt(p); data.points = Add(data.points, new MapPoint { id = id, x = Round(f.x), y = Round(f.y) }); roadFromNew = true; }
                else roadFromNew = false;
                roadFrom = id; roadPts.Clear();
                Note("Дорога: касайся по пути — изгибы; коснись места или развилки — конец");
                return;
            }
            if (id == null) { roadPts.Add(FracAt(p)); return; }
            FinishRoad(id);
        }
        static void FinishRoad(string to)
        {
            if (roadFrom == null || to == roadFrom && roadPts.Count == 0) { CancelRoad(); return; }
            Push();
            MapRoad r = new MapRoad { a = roadFrom, b = to, water = tool == Tool.Water, trail = false };
            SetBends(r, roadPts);
            data.roads = Add(data.roads, r);
            sel = new Hit { kind = HRoad, i = data.roads.Length - 1 };
            roadFrom = null; roadPts.Clear(); roadFromNew = false; ver++;
            Note(r.water ? "Переправа проложена" : "Дорога проложена");
        }
        /// <summary>«ЗАВЕРШИТЬ»: последний изгиб становится новой развилкой и концом дороги.</summary>
        static void FinishAtLast()
        {
            if (roadFrom == null || roadPts.Count == 0) return;
            Vector2 last = roadPts[roadPts.Count - 1]; roadPts.RemoveAt(roadPts.Count - 1);
            string id = NewId("J");
            data.points = Add(data.points, new MapPoint { id = id, x = Round(last.x), y = Round(last.y) });
            FinishRoad(id);
        }
        static void CancelRoad()
        {
            if (roadFrom != null && roadFromNew && data != null)
            {   // развилка, поставленная для начала дороги и ни к чему не подключённая, — убрать
                bool used = false; foreach (MapRoad r in data.roads) if (r.a == roadFrom || r.b == roadFrom) used = true;
                if (!used) for (int i = 0; i < data.points.Length; i++) if (data.points[i].id == roadFrom) { data.points = RemoveAt(data.points, i); break; }
            }
            roadFrom = null; roadPts.Clear(); roadFromNew = false;
        }

        // ---------------------------------------------------------------- рисование карты

        static void DrawOverlay()
        {
            float t = Time.unscaledTime;
            Texture2D fog = SoftSprites.Fog;
            for (int i = 0; i < data.effects.Length; i++)
            {
                MapEffect fx = data.effects[i];
                Rect r = EffRect(fx);
                if (r.xMax < mapR.x - 60f || r.x > mapR.xMax + 60f || r.yMax < mapR.y - 60f || r.y > mapR.yMax + 60f) continue;
                MapLife.DrawEffect(fx, r, t * Mathf.Max(0.05f, fx.speed) + i * 0.37f, fog);
                GUI.color = Color.white;
                bool s = sel.kind == HEffect && sel.i == i;
                Color c = s ? Gold : new Color(0.55f, 0.82f, 1f, tool == Tool.Effect ? 0.9f : 0.45f);
                Frame(r, c, s ? 2f : 1f);
                if (s) UiKit.Rect(new Rect(r.xMax - 6f, r.yMax - 6f, 12f, 12f), Gold);
            }
            // дороги
            for (int ri = 0; ri < data.roads.Length; ri++)
            {
                MapRoad rd = data.roads[ri];
                List<Vector2> l = ScreenLine(rd);
                bool s = (sel.kind == HRoad || sel.kind == HBend) && sel.i == ri;
                Color c = rd.water ? WaterC : RoadC;
                if (!s) c.a = 0.75f;
                for (int k = 0; k + 1 < l.Count; k++)
                {
                    UiKit.Line(l[k], l[k + 1], s ? 4f : 3f, new Color(0f, 0f, 0f, 0.45f));
                    UiKit.Line(l[k], l[k + 1], s ? 2.6f : 1.8f, c);
                }
                for (int k = 1; k < l.Count - 1; k++)
                {
                    bool bs = sel.kind == HBend && sel.i == ri && sel.k == k - 1;
                    UiKit.Disc(l[k], bs ? 6f : 4f, new Color(0f, 0f, 0f, 0.8f));
                    UiKit.Disc(l[k], bs ? 4.5f : 3f, bs ? Gold : Color.white);
                }
                if (tool == Tool.Select || tool == Tool.Road || tool == Tool.Water)
                    for (int k = 0; k + 1 < l.Count; k++)
                        if (Vector2.Distance(l[k], l[k + 1]) >= 30f) UiKit.Ring((l[k] + l[k + 1]) * 0.5f, 5f, new Color(1f, 1f, 1f, 0.45f), true);
            }
            // дорога, которую проводят
            if (roadFrom != null)
            {
                Vector2? a = data.Pos(roadFrom);
                if (a.HasValue)
                {
                    Vector2 prev = ScrF(a.Value.x, a.Value.y);
                    foreach (Vector2 f in roadPts) { Vector2 q = ScrF(f.x, f.y); UiKit.Line(prev, q, 2.4f, new Color(0.55f, 0.82f, 1f, 1f)); UiKit.Disc(q, 4f, new Color(0.55f, 0.82f, 1f, 1f)); prev = q; }
                    UiKit.Ring(prev, 10f + 2f * Mathf.Sin(t * 6f), new Color(0.55f, 0.82f, 1f, 1f));
                }
            }
            // развилки
            for (int i = 0; i < data.points.Length; i++)
            {
                MapPoint q = data.points[i];
                Vector2 p = ScrF(q.x, q.y);
                bool s = sel.kind == HPoint && sel.i == i;
                UiKit.Disc(p, s ? 8f : 6.5f, new Color(0f, 0f, 0f, 0.85f));
                UiKit.Disc(p, s ? 6f : 4.5f, q.bridge ? new Color(0.75f, 0.55f, 0.3f) : s ? Gold : new Color(0.95f, 0.95f, 0.95f));
                if (bad.Contains(q.id)) UiKit.Ring(p, 11f, BadC);
            }
            // места
            int last = data.Layers - 1;
            for (int i = 0; i < data.places.Length; i++)
            {
                MapPlace q = data.places[i];
                Vector2 p = ScrF(q.x, q.y);
                if (p.x < mapR.x - 30f || p.x > mapR.xMax + 30f) continue;
                bool s = sel.kind == HPlace && sel.i == i;
                Color ring = q.kind == 2 ? new Color(0.45f, 0.95f, 0.5f) : q.kind == 1 ? new Color(0.92f, 0.92f, 0.92f) : Gold;
                UiKit.Disc(p, 15f, new Color(0.07f, 0.05f, 0.04f, 0.92f));
                UiKit.Ring(p, 15f, ring);
                if (s) UiKit.Ring(p, 20f + 1.5f * Mathf.Sin(t * 5f), Gold);
                if (bad.Contains(q.id)) UiKit.Ring(p, 24f, BadC);
                string lbl = q.kind == 0 && q.layer == 0 ? "СТАРТ" : q.kind == 0 && q.layer == last ? "ВЛАД." : q.type < 0 ? "?" : Short(q.type);
                UiKit.Text(new Rect(p.x - 26f, p.y - 9f, 52f, 18f), lbl, lbl.Length <= 2 ? 16f : 10f, Ink);
                UiKit.Text(new Rect(p.x - 20f, p.y + 14f, 40f, 14f), "ряд " + q.layer, 9f, Dim);
            }
        }

        static void Frame(Rect r, Color c, float th)
        {
            UiKit.Rect(new Rect(r.x, r.y, r.width, th), c); UiKit.Rect(new Rect(r.x, r.yMax - th, r.width, th), c);
            UiKit.Rect(new Rect(r.x, r.y, th, r.height), c); UiKit.Rect(new Rect(r.xMax - th, r.y, th, r.height), c);
        }

        static string TypeName(int t) { return t < 0 ? "СЛУЧАЙНО" : t < Campaign.TypeNames.Length ? Campaign.TypeNames[t] : "ТИП " + t; }
        static string Short(int t)
        {
            string n = TypeName(t);
            int sp = n.IndexOf(' ');
            if (sp > 0 && sp <= 7) n = n.Substring(0, sp);
            return n.Length > 7 ? n.Substring(0, 6) + "." : n;
        }

        // ---------------------------------------------------------------- панели

        static void Box(Rect r)
        {
            // высокие панели — лакированная доска с золотой каймой (UI/editor_panel), полосы — как раньше
            Texture2D pt = r.height > 120f ? UiKit.Tex("UI/editor_panel") : null;
            if (pt != null) { UiKit.Rect(new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 8f), new Color(0.05f, 0.02f, 0.02f, 0.96f)); UiKit.Slice(r, pt, 0.42f, 0.15f, 0f, 0.075f, 0f, Color.white); return; }
            UiKit.Rect(r, Panel); Frame(r, Edge, 1.5f);
        }
        static bool locked;   // открыто окно истории — кнопки под ним не нажимаются
        static bool Btn(Rect r, string s, bool on = true, bool red = false) { return VisualIdentity.KitButton(r, s, on, red) && !locked; }

        static void DrawPanels()
        {
            // верх
            Box(new Rect(0f, 0f, W, 48f));
            UiKit.Text(new Rect(10f, 6f, 150f, 36f), "РЕДАКТОР КАРТ", 17f, Gold, TextAnchor.MiddleLeft);
            float x = 160f, gap = 6f, avail = W - x - 8f;
            string[] labels = { "ОБЛАСТЬ: " + data.name, "ОТМЕНИТЬ (" + undo.Count + ")", "СОХРАНИТЬ", "ИСТОРИЯ", "СБРОС", "ПРОЙТИ КАРТУ", "ВЫЙТИ" };
            float[] wt = { 2.3f, 1.35f, 1.25f, 1f, 0.85f, 1.45f, 0.85f };
            float sum = 0f; foreach (float f in wt) sum += f;
            float unit = (avail - gap * (wt.Length - 1)) / sum;
            for (int i = 0; i < labels.Length; i++)
            {
                Rect b = new Rect(x, 6f, wt[i] * unit, 36f);
                x += wt[i] * unit + gap;
                if (!Btn(b, labels[i], i != 1 || undo.Count > 0, i == 2 && dirty)) continue;
                switch (i)
                {
                    case 0:
                        if (!dirty || Confirm("region", "Есть несохранённое — нажми ещё раз, чтобы сменить область без сохранения"))
                        { NextRegion(); Note("Область: " + data.name + (custom >= 0 ? " (своя)" : "")); }
                        break;
                    case 1: Undo(); break;
                    case 2: SaveNow(); break;
                    case 3: historyOpen = true; break;
                    case 4:
                        if (custom >= 0)
                        { if (Confirm("reset", "Отменить всё несохранённое в своей области? Нажми ещё раз")) { LoadCustom(custom); Note("Как было при сохранении"); } }
                        else if (Confirm("reset", MapStore.Edited(region) ? "Вернуть область как в игре (правки с телефона удалятся)? Нажми ещё раз" : "Отменить всё несохранённое? Нажми ещё раз"))
                        { MapStore.Revert(region); Load(region); Note("Область возвращена как в игре"); }
                        break;
                    case 5:
                        if (Confirm("play", "Сохранить и начать поход по этой области заново (текущий поход пропадёт)? Нажми ещё раз"))
                        { SaveNow(); if (custom >= 0) Campaign.DebugCustom(custom, data.like); else Campaign.DebugRegion(region); Open = false; }
                        break;
                    case 6:
                        if (!dirty || Confirm("exit", "Есть несохранённое — нажми ещё раз, чтобы выйти без сохранения")) { CancelRoad(); Open = false; }
                        break;
                }
            }
            // инструменты слева
            Box(new Rect(0f, 50f, 108f, 540f - 50f - 30f));
            for (int i = 0; i < ToolNames.Length; i++)
            {
                Rect b = new Rect(8f, 58f + i * 66f, 92f, 60f);
                bool on = (int)tool == i;
                if (Btn(b, "", true, on)) { if (tool == Tool.Road || tool == Tool.Water) CancelRoad(); tool = (Tool)i; if (tool != Tool.Select) sel = new Hit(); }
                Texture2D ti = UiKit.Tex("UI/editor_tools");   // значки 3×2: выбор, место, дорога, переправа, эффект, ластик
                if (ti != null)
                {
                    GUI.DrawTextureWithTexCoords(new Rect(b.center.x - 19f, b.y + 3f, 38f, 38f), ti, new Rect((i % 3) / 3f, 1f - (i / 3 + 1) / 2f, 1f / 3f, 0.5f));
                    UiKit.Text(new Rect(b.x, b.y + 41f, b.width, 16f), ToolNames[i], 11f, on ? Gold : Ink);
                }
                else
                {
                    UiKit.Text(new Rect(b.x, b.y + 10f, b.width, 22f), ToolNames[i], 13f, Ink);
                    UiKit.Text(new Rect(b.x, b.y + 32f, b.width, 16f), ToolHints[i], 10f, Dim);
                }
            }
            // инспектор справа
            Rect ins = new Rect(W - 232f, 50f, 232f, 540f - 50f - 30f);
            Box(ins);
            DrawInspector(new Rect(ins.x + 10f, ins.y + 8f, ins.width - 20f, ins.height - 16f));
            // низ: проверка
            Rect bar = new Rect(0f, 540f - 28f, W, 28f);
            Box(bar);
            string st = "Мест: " + data.places.Length + " · развилок: " + data.points.Length + " · дорог: " + data.roads.Length + " · эффектов: " + data.effects.Length + " · ";
            st += issues.Count == 0 ? "проверка: всё в порядке" : "замечаний: " + issues.Count + " — " + issues[Mathf.Clamp(issueCursor, 0, issues.Count - 1)];
            UiKit.Text(new Rect(10f, bar.y + 2f, W - 140f, 24f), st, 12f, issues.Count == 0 ? Ink : new Color(1f, 0.6f, 0.5f), TextAnchor.MiddleLeft);
            if (issues.Count > 1 && Btn(new Rect(W - 126f, bar.y + 2f, 118f, 24f), "ДАЛЬШЕ")) issueCursor = (issueCursor + 1) % issues.Count;
        }

        static void DrawInspector(Rect r)
        {
            float x = r.x, w = r.width, y = r.y;
            float half = (w - 6f) * 0.5f;
            if (roadFrom != null)
            {
                UiKit.Text(new Rect(x, y, w, 24f), tool == Tool.Water ? "ПЕРЕПРАВА" : "ДОРОГА", 18f, Gold, TextAnchor.MiddleLeft); y += 30f;
                Hint(ref y, x, w, "Касайся карты по пути — это изгибы. Коснись места или развилки — дорога закончится там. Точку изгиба чужой дороги можно сделать развилкой — коснись её.");
                if (Btn(new Rect(x, y, w, 34f), "ЗАВЕРШИТЬ ЗДЕСЬ", roadPts.Count > 0, true)) FinishAtLast(); y += 40f;
                if (Btn(new Rect(x, y, w, 34f), "УБРАТЬ ПОСЛЕДНИЙ ИЗГИБ", roadPts.Count > 0)) roadPts.RemoveAt(roadPts.Count - 1); y += 40f;
                if (Btn(new Rect(x, y, w, 34f), "ОТМЕНА")) CancelRoad();
                return;
            }
            // выбранное могли удалить (отмена, ластик) — выбор сбрасывается
            if (sel.kind == HPlace && sel.i >= data.places.Length || sel.kind == HPoint && sel.i >= data.points.Length ||
                (sel.kind == HRoad || sel.kind == HBend) && sel.i >= data.roads.Length || (sel.kind == HEffect || sel.kind == HCorner) && sel.i >= data.effects.Length)
                sel = new Hit();
            switch (sel.kind)
            {
                case HPlace:
                {
                    MapPlace q = data.places[sel.i];
                    UiKit.Text(new Rect(x, y, w, 24f), "МЕСТО " + q.id, 18f, Gold, TextAnchor.MiddleLeft); y += 30f;
                    Label(ref y, x, w, "Тип");
                    if (Btn(new Rect(x, y, 34f, 32f), "<")) { Push(); q.type = StepType(q.type, -1); }
                    UiKit.Text(new Rect(x + 36f, y, w - 72f, 32f), TypeName(q.type), 12f, Ink);
                    if (Btn(new Rect(x + w - 34f, y, 34f, 32f), ">")) { Push(); q.type = StepType(q.type, 1); q.custom = null; }
                    y += 38f;
                    MapCustom qc = data.Custom(q.custom);
                    if (qc != null)
                    {
                        if (Btn(new Rect(x, y, w - 40f, 32f), "СВОЁ: " + (qc.name ?? "").ToUpper(), true, true)) customEdit = qc;
                        if (Btn(new Rect(x + w - 34f, y, 34f, 32f), "×")) { Push(); q.custom = null; q.type = -1; }
                    }
                    else if (Btn(new Rect(x, y, w, 32f), "СДЕЛАТЬ СВОИМ МЕСТОМ")) { Push(); customEdit = MakeCustom(q); }
                    y += 38f;
                    Label(ref y, x, w, "Ряд (этап пути: 0 — начало, последний — владыка)");
                    if (Btn(new Rect(x, y, 34f, 32f), "<", q.layer > 0)) { Push(); q.layer--; }
                    UiKit.Text(new Rect(x + 36f, y, w - 72f, 32f), q.layer.ToString(), 16f, Ink);
                    if (Btn(new Rect(x + w - 34f, y, 34f, 32f), ">", q.layer < 30)) { Push(); q.layer++; }
                    y += 38f;
                    Label(ref y, x, w, "Вид места");
                    string[] kinds = { "МЕСТО РЯДА", "ДОП. МЕСТО", "ПОСТОЯННОЕ" };
                    if (Btn(new Rect(x, y, w, 32f), kinds[Mathf.Clamp(q.kind, 0, 2)])) { Push(); q.kind = (q.kind + 1) % 3; }
                    y += 38f;
                    if (Btn(new Rect(x, y, w, 32f), q.bridge ? "НА МОСТУ: ДА" : "НА МОСТУ: НЕТ")) { Push(); q.bridge = !q.bridge; }
                    y += 38f;
                    Hint(ref y, x, w, q.kind == 0 ? "Место ряда: иногда выпадает (дорога сходится в одну точку). Тип «СЛУЧАЙНО» разыгрывается в каждом походе."
                        : q.kind == 1 ? "Доп. место: всегда есть, тип по умолчанию разыгрывается." : "Постоянное: всегда есть, тип всегда один (задай тип).");
                    if (Btn(new Rect(x, r.yMax - 34f, w, 34f), "УДАЛИТЬ МЕСТО")) { Push(); Erase(sel); sel = new Hit(); }
                    return;
                }
                case HPoint:
                {
                    MapPoint q = data.points[sel.i];
                    UiKit.Text(new Rect(x, y, w, 24f), "РАЗВИЛКА " + q.id, 18f, Gold, TextAnchor.MiddleLeft); y += 30f;
                    if (Btn(new Rect(x, y, w, 32f), q.bridge ? "МОСТ: ДА" : "МОСТ: НЕТ")) { Push(); q.bridge = !q.bridge; }
                    y += 40f;
                    Hint(ref y, x, w, "Развилку можно перетащить на место или другую развилку — дороги прилипнут. «Мост» — дорога идёт по мосту (там не ставят домов).");
                    if (Btn(new Rect(x, r.yMax - 34f, w, 34f), "УДАЛИТЬ РАЗВИЛКУ")) { Push(); Erase(sel); sel = new Hit(); }   // Erase запоминает точку — слитая дорога пройдёт через неё
                    return;
                }
                case HRoad: case HBend:
                {
                    if (sel.i >= data.roads.Length) break;
                    MapRoad rd = data.roads[sel.i];
                    UiKit.Text(new Rect(x, y, w, 24f), rd.water ? "ПЕРЕПРАВА" : "ДОРОГА", 18f, Gold, TextAnchor.MiddleLeft); y += 28f;
                    UiKit.Text(new Rect(x, y, w, 18f), rd.a + " → " + rd.b + " · изгибов " + rd.pts.Length / 2, 12f, Dim, TextAnchor.MiddleLeft); y += 24f;
                    if (Btn(new Rect(x, y, w, 32f), rd.water ? "ПО ВОДЕ: ДА" : "ПО ВОДЕ: НЕТ")) { Push(); rd.water = !rd.water; }
                    y += 38f;
                    if (Btn(new Rect(x, y, w, 32f), rd.trail ? "ПУНКТИР НА КАРТЕ: ДА" : "ПУНКТИР НА КАРТЕ: НЕТ")) { Push(); rd.trail = !rd.trail; }
                    y += 38f;
                    if (sel.kind == HBend)
                    {
                        if (Btn(new Rect(x, y, w, 32f), "СДЕЛАТЬ РАЗВИЛКОЙ")) { Push(); string id = SplitAt(sel.i, sel.k); for (int i = 0; i < data.points.Length; i++) if (data.points[i].id == id) sel = new Hit { kind = HPoint, i = i }; }
                        y += 38f;
                        if (Btn(new Rect(x, y, w, 32f), "УДАЛИТЬ ТОЧКУ")) { Push(); Erase(sel); sel = new Hit { kind = HRoad, i = sel.i }; }
                        y += 38f;
                    }
                    Hint(ref y, x, w, "Точки тянутся пальцем, кружок между точками — потянуть, будет новая точка; долгое нажатие на точку — удалить.");
                    if (Btn(new Rect(x, r.yMax - 34f, w, 34f), "УДАЛИТЬ ДОРОГУ")) { Push(); data.roads = RemoveAt(data.roads, sel.i); sel = new Hit(); ver++; }
                    return;
                }
                case HEffect: case HCorner:
                {
                    if (sel.i >= data.effects.Length) break;
                    MapEffect fx = data.effects[sel.i];
                    UiKit.Text(new Rect(x, y, w, 24f), MapLife.FxName(fx).ToUpper(), 15f, Gold, TextAnchor.MiddleLeft); y += 30f;
                    Stepper(ref y, x, w, "Прозрачность", Mathf.RoundToInt(fx.alpha * 100f) + "%", () => fx.alpha = Mathf.Clamp(fx.alpha - 0.05f, 0.05f, 1f), () => fx.alpha = Mathf.Clamp(fx.alpha + 0.05f, 0.05f, 1f));
                    Stepper(ref y, x, w, "Скорость", fx.speed.ToString("0.0"), () => fx.speed = Mathf.Clamp(fx.speed - 0.1f, 0.1f, 3f), () => fx.speed = Mathf.Clamp(fx.speed + 0.1f, 0.1f, 3f));
                    if (fx.kind != null && fx.kind.StartsWith("fall_"))
                    {
                        string[] foot = { "ПОДНОЖИЕ: НИЧЕГО", "ПОДНОЖИЕ: БРЫЗГИ", "ПОДНОЖИЕ: ВОДЯНАЯ ПЫЛЬ" };
                        if (Btn(new Rect(x, y, w, 32f), foot[Mathf.Clamp(fx.foot, 0, 2)])) { Push(); fx.foot = (fx.foot + 1) % 3; }
                        y += 38f;
                    }
                    UiKit.Text(new Rect(x, y, w, 18f), "x " + fx.x + "  y " + fx.y + "  " + fx.w + "×" + fx.h, 11f, Dim, TextAnchor.MiddleLeft); y += 22f;
                    Hint(ref y, x, w, "Тяни эффект за середину — двигать, за угол справа внизу — менять размер.");
                    if (Btn(new Rect(x, r.yMax - 34f, w, 34f), "УДАЛИТЬ ЭФФЕКТ")) { Push(); data.effects = RemoveAt(data.effects, sel.i); sel = new Hit(); ver++; }
                    return;
                }
            }
            // ничего не выбрано — подсказка по инструменту (у эффектов — библиотека)
            if (tool == Tool.Effect)
            {
                UiKit.Text(new Rect(x, y, w, 24f), "ЭФФЕКТЫ", 18f, Gold, TextAnchor.MiddleLeft); y += 28f;
                for (int i = 0; i < MapLife.FxNames.Length; i++)
                {
                    if (i == 0) { Label(ref y, x, w, "ВОДА"); }
                    if (i == 7) { y += 2f; Label(ref y, x, w, "ДЫМ И ОГОНЬ"); }
                    if (Btn(new Rect(x, y, w, 24f), MapLife.FxNames[i], true, libSel == i)) libSel = i;
                    y += 26f;
                }
                return;
            }
            UiKit.Text(new Rect(x, y, w, 24f), ToolNames[(int)tool], 18f, Gold, TextAnchor.MiddleLeft); y += 30f;
            string[] help =
            {
                "Коснись места, развилки, дороги или эффекта — выбрать. Тяни — двигать. Пустое место — двигать карту, двумя пальцами — приближать.",
                "Коснись карты — встанет новое место. Тип, ряд и вид — справа.",
                "Коснись места или развилки (или пустого места — будет новая развилка), дальше — по пути, конец — на месте или развилке.",
                "Как дорога, только по воде (лодка): рисуется голубым пунктиром.",
                "Выбери эффект и коснись карты.",
                "Коснись того, что убрать: место, развилку, точку изгиба, дорогу или эффект.",
            };
            Hint(ref y, x, w, help[(int)tool]);
            if (tool == Tool.Select) { DrawRegionBox(x, ref y, w, r.yMax - 58f); return; }
            UiKit.Text(new Rect(x, r.yMax - 54f, w, 50f), "Кольцо места: золотое — место ряда, белое — доп., зелёное — постоянное. Красное — замечание проверки.", 10f, Dim, TextAnchor.LowerLeft);
        }

        // ---------------------------------------------------------------- своё место (строение, событие)

        static MapCustom customEdit;
        static int foeIdx(int code) { int i = System.Array.IndexOf(MapCustom.FoeCodes, code); return i < 0 ? 0 : i; }

        static MapCustom MakeCustom(MapPlace q)
        {
            string id = "c_" + q.id; int k = 2;
            while (data.Custom(id) != null) id = "c_" + q.id + "_" + k++;
            MapCustom c = new MapCustom
            {
                id = id, name = "Своё место", text = "Опиши, что здесь происходит.",
                choices = new[] { new MapChoice { label = "ВЗЯТЬ НАГРАДУ", outcome = 1, amount = 50, result = "" }, new MapChoice { label = "ПРОЙТИ МИМО", outcome = 0 } },
            };
            List<MapCustom> l = new List<MapCustom>(data.customs ?? new MapCustom[0]); l.Add(c); data.customs = l.ToArray();
            q.custom = id; q.type = (int)NodeType.Custom; q.kind = 2;   // своё место всегда есть и всегда своё
            dirty = true; ver++;
            return c;
        }

        static GUIStyle area;
        static string TextField(Rect r, string v, int max, bool multi = false)
        {
            if (field == null) { field = new GUIStyle(GUI.skin.textField); field.fontSize = 15; field.normal.textColor = Ink; field.focused.textColor = Ink; field.alignment = TextAnchor.MiddleLeft; }
            if (area == null) { area = new GUIStyle(GUI.skin.textArea); area.fontSize = 14; area.wordWrap = true; area.normal.textColor = Ink; area.focused.textColor = Ink; }
            string nv = multi ? GUI.TextArea(r, v ?? "", max, area) : GUI.TextField(r, v ?? "", max, field);
            if (nv != v) { dirty = true; }
            return nv;
        }

        static void DrawCustomEdit()
        {
            MapCustom c = customEdit;
            UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0.05f, 0.04f, 0.03f, 1f));
            Box(new Rect(0f, 0f, W, 48f));
            UiKit.Text(new Rect(12f, 6f, 400f, 36f), "СВОЁ МЕСТО — КАРТОЧКА", 17f, Gold, TextAnchor.MiddleLeft);
            if (Btn(new Rect(W - 150f, 6f, 140f, 36f), "ГОТОВО", true, true)) { customEdit = null; ver++; return; }
            if (Btn(new Rect(W - 330f, 6f, 170f, 36f), "УДАЛИТЬ МЕСТО") && Confirm("delcust", "Убрать своё место (на карте останется обычное)? Нажми ещё раз"))
            {
                Push();
                foreach (MapPlace q in data.places) if (q.custom == c.id) { q.custom = null; q.type = -1; }
                List<MapCustom> l = new List<MapCustom>(data.customs); l.Remove(c); data.customs = l.ToArray();
                customEdit = null; ver++; return;
            }

            // слева — значок и картина
            Rect lp = new Rect(0f, 52f, 250f, 488f);
            Box(lp);
            float x = lp.x + 14f, w = lp.width - 28f, y = lp.y + 12f;
            UiKit.Text(new Rect(x, y, w, 16f), "ЗНАЧОК НА КАРТЕ", 12f, Dim, TextAnchor.MiddleLeft); y += 20f;
            Rect ir = new Rect(x + w * 0.5f - 46f, y, 92f, 92f);
            Texture2D it = string.IsNullOrEmpty(c.icon) ? null : UiKit.Tex(c.icon);
            if (it != null) GUI.DrawTexture(ir, it); else { UiKit.Disc(ir.center, 44f, new Color(0.15f, 0.12f, 0.09f)); UiKit.Text(ir, "нет", 13f, Dim); }
            y += 98f;
            if (Btn(new Rect(x, y, w, 32f), "ЗНАЧОК ИЗ ГАЛЕРЕИ")) PickObjImage(c, true);
            y += 44f;
            UiKit.Text(new Rect(x, y, w, 16f), "КАРТИНА КАРТОЧКИ", 12f, Dim, TextAnchor.MiddleLeft); y += 20f;
            Rect ar = new Rect(x, y, w, 170f);
            Texture2D at = string.IsNullOrEmpty(c.art) ? null : UiKit.Tex(c.art);
            if (at != null) GUI.DrawTexture(ar, at, ScaleMode.ScaleAndCrop); else { UiKit.Rect(ar, new Color(0.15f, 0.12f, 0.09f)); UiKit.Text(ar, "нет", 13f, Dim); }
            Frame(ar, Edge, 1f);
            y += 176f;
            if (Btn(new Rect(x, y, w, 32f), "КАРТИНА ИЗ ГАЛЕРЕИ")) PickObjImage(c, false);

            // посередине — название и текст
            Rect mp = new Rect(256f, 52f, W - 256f - 6f, 488f);
            Box(mp);
            x = mp.x + 14f; w = mp.width - 28f; y = mp.y + 12f;
            Label(ref y, x, w, "Название");
            c.name = TextField(new Rect(x, y, Mathf.Min(w, 420f), 32f), c.name, 32); y += 40f;
            Label(ref y, x, w, "Текст карточки");
            c.text = TextField(new Rect(x, y, w, 96f), c.text, 600, true); y += 104f;
            // бой
            Label(ref y, x, w, "Если выбор — «БОЙ»: противник и сила");
            float bw = Mathf.Min(260f, (w - 8f) * 0.5f);
            if (Btn(new Rect(x, y, bw, 32f), MapCustom.FoeNames[foeIdx(c.foe)])) { c.foe = MapCustom.FoeCodes[(foeIdx(c.foe) + 1) % MapCustom.FoeCodes.Length]; dirty = true; }
            if (Btn(new Rect(x + bw + 8f, y, bw, 32f), c.strong ? "СИЛЬНЫЙ: ДА" : "СИЛЬНЫЙ: НЕТ")) { c.strong = !c.strong; dirty = true; }
            y += 42f;
            // выборы
            Label(ref y, x, w, "Кнопки (до трёх): надпись · что даёт · сколько · что написать после");
            List<MapChoice> ch = new List<MapChoice>(c.choices ?? new MapChoice[0]);
            int remove = -1;
            for (int i = 0; i < ch.Count; i++)
            {
                MapChoice o = ch[i];
                float cx = x;
                o.label = TextField(new Rect(cx, y, 170f, 30f), o.label, 24); cx += 176f;
                if (Btn(new Rect(cx, y, 130f, 30f), MapCustom.Outcomes[Mathf.Clamp(o.outcome, 0, MapCustom.Outcomes.Length - 1)]))
                { o.outcome = (o.outcome + 1) % MapCustom.Outcomes.Length; o.amount = MapCustom.OutcomeDefault[o.outcome]; dirty = true; }
                cx += 136f;
                bool amt = o.outcome >= 1 && o.outcome <= 4;
                if (amt)
                {
                    int step = o.outcome == 4 ? 1 : o.outcome == 3 ? 20 : 10, max = o.outcome == 4 ? 3 : 2000, min = o.outcome == 4 ? 1 : 0;
                    if (Btn(new Rect(cx, y, 28f, 30f), "−")) { o.amount = Mathf.Max(min, o.amount - step); dirty = true; }
                    UiKit.Text(new Rect(cx + 28f, y, 52f, 30f), o.outcome == 4 ? new[] { "", "ОБЫЧН.", "РЕДК.", "ЛЕГЕНД." }[Mathf.Clamp(o.amount, 1, 3)] : o.amount.ToString(), 13f, Ink);
                    if (Btn(new Rect(cx + 80f, y, 28f, 30f), "+")) { o.amount = Mathf.Min(max, o.amount + step); dirty = true; }
                }
                cx += 114f;
                o.result = TextField(new Rect(cx, y, Mathf.Max(80f, x + w - cx - 40f), 30f), o.result, 200);
                if (Btn(new Rect(x + w - 34f, y, 34f, 30f), "×")) remove = i;
                y += 38f;
            }
            if (remove >= 0) { ch.RemoveAt(remove); dirty = true; }
            if (ch.Count < 3 && Btn(new Rect(x, y, 200f, 32f), "+ КНОПКА")) { ch.Add(new MapChoice { label = "ДАЛЬШЕ" }); dirty = true; }
            c.choices = ch.ToArray();
            if (note != null && Time.unscaledTime - noteAt < 3f)
            {
                Rect nr = new Rect(W * 0.5f - 300f, 54f, 600f, 30f);
                UiKit.Rect(nr, new Color(0f, 0f, 0f, 0.8f)); UiKit.Text(nr, note, 15f, Gold);
            }
            UiKit.Text(new Rect(x, mp.yMax - 40f, w, 32f), "«ПЛАТА» нельзя нажать, если мон не хватает. «БОЙ» превращает место в бой с выбранным противником. Пустое «после» — игра напишет сама.", 11f, Dim, TextAnchor.UpperLeft);
        }

        /// <summary>Картинка своего места из галереи: значок — круг 256 с золотым кольцом (png), картина — до 1024 (jpg).</summary>
        static void PickObjImage(MapCustom c, bool icon)
        {
            NativeGallery.GetImageFromGallery((path) =>
            {
                if (string.IsNullOrEmpty(path)) return;
                Texture2D src = NativeGallery.LoadImageAtPath(path, icon ? 1024 : 2048, false, false);
                if (src == null) { Note("Картинка не открылась"); return; }
                string dir = Path.Combine(Application.persistentDataPath, "maps", "obj");
                Directory.CreateDirectory(dir);
                string stamp = c.id + "_" + System.DateTime.Now.ToString("yyyyMMddHHmmss");
                string rel;
                if (icon)
                {
                    int S = 256, side = Mathf.Min(src.width, src.height);
                    Texture2D t = new Texture2D(S, S, TextureFormat.RGBA32, false);
                    Color32[] px = new Color32[S * S];
                    float ox = (src.width - side) * 0.5f / src.width, oy = (src.height - side) * 0.5f / src.height;
                    for (int y = 0; y < S; y++)
                        for (int x = 0; x < S; x++)
                        {
                            float dx = x + 0.5f - S * 0.5f, dy = y + 0.5f - S * 0.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
                            Color col = src.GetPixelBilinear(ox + (x + 0.5f) / S * side / src.width, oy + (y + 0.5f) / S * side / src.height);
                            float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 118f) / 7f);                // золотое кольцо
                            col = Color.Lerp(col, new Color(0.86f, 0.66f, 0.34f), ring);
                            col = Color.Lerp(col, new Color(0.25f, 0.15f, 0.06f), Mathf.Clamp01(1f - Mathf.Abs(d - 126f) / 2.5f));   // тёмный край
                            col.a = Mathf.Clamp01(127.5f - d);
                            px[y * S + x] = col;
                        }
                    t.SetPixels32(px); t.Apply();
                    rel = "maps/obj/" + stamp + "_icon.png";
                    File.WriteAllBytes(Path.Combine(Application.persistentDataPath, rel), t.EncodeToPNG());
                    Object.Destroy(t);
                }
                else
                {
                    rel = "maps/obj/" + stamp + "_art.jpg";
                    File.WriteAllBytes(Path.Combine(Application.persistentDataPath, rel), src.EncodeToJPG(88));
                }
                Object.Destroy(src);
                if (icon) c.icon = "file:" + rel; else c.art = "file:" + rel;
                dirty = true; ver++;
            }, icon ? "Значок места" : "Картина места");
        }

        // ---------------------------------------------------------------- своя область

        static GUIStyle field;
        static void DrawRegionBox(float x, ref float y, float w, float bottom)
        {
            y += 4f;
            UiKit.Text(new Rect(x, y, w, 22f), custom >= 0 ? "СВОЯ ОБЛАСТЬ" : "ОБЛАСТЬ ИГРЫ", 15f, Gold, TextAnchor.MiddleLeft); y += 24f;
            if (custom >= 0)
            {
                Label(ref y, x, w, "Название");
                if (field == null) { field = new GUIStyle(GUI.skin.textField); field.fontSize = 15; field.normal.textColor = Ink; field.focused.textColor = Ink; field.alignment = TextAnchor.MiddleLeft; }
                string nn = GUI.TextField(new Rect(x, y, w, 30f), data.name ?? "", 28, field);
                if (nn != data.name) { data.name = nn; dirty = true; }
                y += 36f;
                Label(ref y, x, w, "Враги, музыка, погода — как в области:");
                if (Btn(new Rect(x, y, w, 30f), Campaign.RegionNames[Mathf.Clamp(data.like, 0, Campaign.RegionNames.Length - 1)])) { Push(); data.like = (data.like + 1) % MapStore.BuiltIn; region = data.like; }
                y += 36f;
                if (Btn(new Rect(x, y, w, 30f), "ДРУГАЯ КАРТИНА")) PickMapImage(custom, false);
                y += 36f;
                if (Btn(new Rect(x, y, w, 30f), "ПОДЕЛИТЬСЯ ФАЙЛОМ")) ExportCustom();
                y += 36f;
                if (Btn(new Rect(x, y, w, 30f), "УДАЛИТЬ ОБЛАСТЬ", true, true) && Confirm("delcustom", "Удалить свою область «" + data.name + "» насовсем? Нажми ещё раз"))
                { MapStore.DeleteCustom(custom); Load(0); Note("Своя область удалена"); }
                y += 36f;
            }
            else
            {
                if (Btn(new Rect(x, y, w, 30f), "ПОДЕЛИТЬСЯ ФАЙЛОМ")) ExportCustom();
                y += 36f;
            }
            if (y < bottom - 34f)
                if (Btn(new Rect(x, bottom - 34f, w, 34f), "НОВАЯ ОБЛАСТЬ ИЗ КАРТИНКИ", true, custom < 0))
                {
                    if (dirty && !Confirm("newregion", "Есть несохранённое — нажми ещё раз, чтобы начать новую область без сохранения")) return;
                    int n = MapStore.FreeCustom();
                    if (n < 0) Note("Своих областей уже " + MapStore.MaxCustom + " — удали какую-нибудь"); else PickMapImage(n, true);
                }
        }

        /// <summary>Картина области из галереи: по высоте 1024 (как у карт игры), ширина — своя (не уже 1500 и не шире 4096).</summary>
        static void PickMapImage(int n, bool fresh)
        {
            NativeGallery.GetImageFromGallery((path) =>
            {
                if (string.IsNullOrEmpty(path)) return;
                Texture2D src = NativeGallery.LoadImageAtPath(path, 4096, false, false);
                if (src == null) { Note("Картинка не открылась"); return; }
                int h = 1024, w = Mathf.Clamp(Mathf.RoundToInt(src.width * 1024f / Mathf.Max(1, src.height)), 1024, 4096);
                RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                RenderTexture keep = RenderTexture.active;
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                Texture2D dst = new Texture2D(w, h, TextureFormat.RGB24, false);
                dst.ReadPixels(new Rect(0, 0, w, h), 0, 0); dst.Apply();
                RenderTexture.active = keep; RenderTexture.ReleaseTemporary(rt);
                Object.Destroy(src);
                string rel = MapStore.CustomImage(n);
                Directory.CreateDirectory(Path.Combine(Application.persistentDataPath, "maps"));
                File.WriteAllBytes(Path.Combine(Application.persistentDataPath, rel), dst.EncodeToJPG(90));
                Object.Destroy(dst);
                UiKit.Forget("file:" + rel);
                if (fresh)
                {
                    float sx = 1f;   // доли по x — от ширины картины
                    MapData m = new MapData
                    {
                        name = "Новая область " + (n + 1), texture = "file:" + rel, width = w, like = 0,
                        places = new[] { new MapPlace { id = "s1", x = 0.06f * sx, y = 0.5f, layer = 0, kind = 0 }, new MapPlace { id = "b1", x = 0.94f, y = 0.5f, layer = 1, kind = 0 } },
                        roads = new[] { new MapRoad { a = "s1", b = "b1" } },
                    };
                    MapStore.SaveCustom(n, m);
                    LoadCustom(n);
                    Note("Новая область: ставь места и прокладывай дороги, имя и правила — справа");
                }
                else
                {
                    Push();
                    float k = data.width > 0 ? data.width / (float)w : 1f;   // места остаются на тех же долях картины
                    data.texture = "file:" + rel; data.width = w;
                    if (Mathf.Abs(k - 1f) > 0.001f) foreach (MapEffect fx in data.effects) { fx.x /= k; fx.w /= k; }
                    tex = UiKit.Tex(data.texture);
                    zoom = -1f;
                    Note("Картина заменена");
                }
            }, "Картина области");
        }

        /// <summary>Своя область — одним файлом (картина внутри): отправить себе или в чат, чтобы область вошла в игру у всех.</summary>
        static void ExportCustom()
        {
            if (dirty) SaveNow();
            try
            {
                // карта и все картинки с телефона (картина области, значки и картины своих мест) — одним файлом
                CustomExport ex = new CustomExport { region = custom >= 0 ? -1 : region, map = JsonUtility.ToJson(data) };
                List<string> ps = new List<string>();
                if (!string.IsNullOrEmpty(data.texture) && data.texture.StartsWith("file:")) ps.Add(data.texture.Substring(5));
                if (data.customs != null)
                    foreach (MapCustom c in data.customs)
                    {
                        if (!string.IsNullOrEmpty(c.icon) && c.icon.StartsWith("file:")) ps.Add(c.icon.Substring(5));
                        if (!string.IsNullOrEmpty(c.art) && c.art.StartsWith("file:")) ps.Add(c.art.Substring(5));
                    }
                List<string> names = new List<string>(), blobs = new List<string>();
                foreach (string rp in ps)
                {
                    string fp = Path.Combine(Application.persistentDataPath, rp);
                    if (!File.Exists(fp) || names.Contains(rp)) continue;
                    names.Add(rp); blobs.Add(System.Convert.ToBase64String(File.ReadAllBytes(fp)));
                }
                ex.files = names.ToArray(); ex.data = blobs.ToArray();
                string safe = System.Text.RegularExpressions.Regex.Replace(data.name ?? "oblast", @"[^\w\-]+", "_");
                string outp = Path.Combine(Application.temporaryCachePath, "BladePath_" + safe + ".blademap.json");
                File.WriteAllText(outp, JsonUtility.ToJson(ex));
                new NativeShare().AddFile(outp).SetSubject("Путь клинка — область «" + data.name + "»").Share();
            }
            catch (System.Exception e) { Note("Не получилось: " + e.Message); }
        }
        [System.Serializable] class CustomExport { public int version = 2; public int region = -1; public string map; public string[] files; public string[] data; }

        static int StepType(int t, int d)
        {
            int n = Campaign.TypeNames.Length;
            for (int g = 0; g < n + 2; g++)
            {
                t += d;
                if (t < -1) t = n - 1;
                if (t >= n) t = -1;
                if (t != (int)NodeType.Start && t != (int)NodeType.Boss && t != (int)NodeType.Custom) return t;
            }
            return -1;
        }
        static void Label(ref float y, float x, float w, string s) { UiKit.Text(new Rect(x, y, w, 16f), s, 11f, Dim, TextAnchor.MiddleLeft); y += 18f; }
        static void Hint(ref float y, float x, float w, string s)
        {
            // перенос строк вручную: ~34 знака в строке
            List<string> lines = new List<string>(); string cur = "";
            foreach (string word in s.Split(' '))
            {
                if (cur.Length + word.Length + 1 > 34) { lines.Add(cur); cur = word; }
                else cur = cur.Length == 0 ? word : cur + " " + word;
            }
            if (cur.Length > 0) lines.Add(cur);
            foreach (string l in lines) { UiKit.Text(new Rect(x, y, w, 15f), l, 11f, Dim, TextAnchor.MiddleLeft, false); y += 15f; }
            y += 6f;
        }
        static void Stepper(ref float y, float x, float w, string label, string val, System.Action minus, System.Action plus)
        {
            Label(ref y, x, w, label);
            if (Btn(new Rect(x, y, 34f, 30f), "<")) { Push(); minus(); }
            UiKit.Text(new Rect(x + 36f, y, w - 72f, 30f), val, 15f, Ink);
            if (Btn(new Rect(x + w - 34f, y, 34f, 30f), ">")) { Push(); plus(); }
            y += 36f;
        }

        // ---------------------------------------------------------------- сохранение и история

        static string HistDir { get { return Path.Combine(Application.persistentDataPath, "maps", "history"); } }

        static void SaveNow()
        {
            if (custom >= 0)
            {
                try { MapStore.SaveCustom(custom, Clone(data)); dirty = false; Note("Своя область сохранена. «ПРОЙТИ КАРТУ» — поход по ней"); }
                catch (System.Exception e) { Note("Не сохранилось: " + e.Message); }
                return;
            }
            try
            {
                string cur = Path.Combine(Application.persistentDataPath, "maps", "map_" + region + ".json");
                if (File.Exists(cur))
                {
                    Directory.CreateDirectory(HistDir);
                    File.Copy(cur, Path.Combine(HistDir, "map_" + region + "_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json"), true);
                    List<string> old = History();
                    for (int i = 10; i < old.Count; i++) File.Delete(old[i]);
                }
                MapStore.Save(region, Clone(data));
                dirty = false;
                Note("Сохранено. Новые места появятся в новом походе по этой области (или «ПРОЙТИ КАРТУ»)");
            }
            catch (System.Exception e) { Note("Не сохранилось: " + e.Message); }
        }

        /// <summary>Сохранённые прежде версии этой области, новые сверху.</summary>
        static List<string> History()
        {
            List<string> l = new List<string>();
            if (custom >= 0) return l;   // у своей области истории пока нет
            try { if (Directory.Exists(HistDir)) l.AddRange(Directory.GetFiles(HistDir, "map_" + region + "_*.json")); } catch { }
            l.Sort(); l.Reverse();
            return l;
        }

        static void DrawHistory()
        {
            UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, 0.7f));
            Rect r = new Rect(W * 0.5f - 230f, 60f, 460f, 420f);
            Box(r);
            UiKit.Text(new Rect(r.x + 16f, r.y + 10f, r.width - 32f, 28f), "ИСТОРИЯ: " + data.name, 18f, Gold, TextAnchor.MiddleLeft);
            List<string> h = History();
            float y = r.y + 48f;
            if (h.Count == 0) UiKit.Text(new Rect(r.x + 16f, y, r.width - 32f, 40f), "Пока пусто: версия попадает сюда, когда сохраняешь поверх неё.", 13f, Dim, TextAnchor.MiddleLeft);
            for (int i = 0; i < h.Count && i < 10; i++)
            {
                string n = Path.GetFileNameWithoutExtension(h[i]);
                string stamp = n.Substring(n.LastIndexOf('_', n.Length - 8) + 1);   // yyyyMMdd_HHmmss
                string label = stamp.Length >= 15 ? stamp.Substring(6, 2) + "." + stamp.Substring(4, 2) + "." + stamp.Substring(0, 4) + "  " + stamp.Substring(9, 2) + ":" + stamp.Substring(11, 2) + ":" + stamp.Substring(13, 2) : n;
                if (Btn(new Rect(r.x + 16f, y, r.width - 32f, 30f), "ВЕРНУТЬ ВЕРСИЮ ОТ " + label))
                {
                    try
                    {
                        MapData m = JsonUtility.FromJson<MapData>(File.ReadAllText(h[i]));
                        if (m != null && m.places != null) { Push(); data = m; sel = new Hit(); ver++; Note("Версия загружена — сохрани, если подходит"); }
                    }
                    catch (System.Exception e) { Note("Не читается: " + e.Message); }
                    historyOpen = false;
                }
                y += 34f;
            }
            if (Btn(new Rect(r.center.x - 70f, r.yMax - 42f, 140f, 34f), "ЗАКРЫТЬ")) historyOpen = false;
        }

        // ---------------------------------------------------------------- проверка

        static void Check()
        {
            issues.Clear(); bad.Clear();
            if (data == null) return;
            int last = data.Layers - 1;
            Dictionary<string, List<string>> adj = new Dictionary<string, List<string>>();
            System.Action<string, string> link = (a, b) => { if (!adj.ContainsKey(a)) adj[a] = new List<string>(); adj[a].Add(b); };
            foreach (MapRoad r in data.roads) { link(r.a, r.b); link(r.b, r.a); }
            // путь от начала до владыки
            List<string> starts = new List<string>(), bosses = new List<string>();
            foreach (MapPlace p in data.places) { if (p.kind == 0 && p.layer == 0) starts.Add(p.id); if (p.kind == 0 && p.layer == last) bosses.Add(p.id); }
            if (starts.Count == 0) issues.Add("нет начала пути (место ряда 0)");
            if (bosses.Count == 0) issues.Add("нет владыки (место последнего ряда)");
            HashSet<string> seen = new HashSet<string>(starts);
            Queue<string> qu = new Queue<string>(starts);
            while (qu.Count > 0) { string c = qu.Dequeue(); List<string> n; if (adj.TryGetValue(c, out n)) foreach (string x in n) if (seen.Add(x)) qu.Enqueue(x); }
            bool reach = false; foreach (string b in bosses) if (seen.Contains(b)) reach = true;
            if (starts.Count > 0 && bosses.Count > 0 && !reach) { issues.Add("от начала до владыки нет пути по дорогам"); foreach (string b in bosses) bad.Add(b); }
            // места без дорог / недостижимые
            int lonely = 0, cut = 0;
            foreach (MapPlace p in data.places)
            {
                if (!adj.ContainsKey(p.id)) { lonely++; bad.Add(p.id); }
                else if (starts.Count > 0 && !seen.Contains(p.id)) { cut++; bad.Add(p.id); }
            }
            if (lonely > 0) issues.Add("мест без дороги: " + lonely + " (обведены красным)");
            if (cut > 0) issues.Add("мест, куда не дойти от начала: " + cut);
            foreach (MapPoint p in data.points) if (!adj.ContainsKey(p.id)) { bad.Add(p.id); issues.Add("развилка " + p.id + " ни с чем не соединена"); }
            // домики налезут
            int close = 0;
            for (int i = 0; i < data.places.Length; i++)
                for (int j = i + 1; j < data.places.Length; j++)
                {
                    MapPlace a = data.places[i], b = data.places[j];
                    if (new Vector2((a.x - b.x) * TW, (a.y - b.y) * TH).magnitude < 45f) { close++; bad.Add(a.id); bad.Add(b.id); }
                }
            if (close > 0) issues.Add("мест слишком близко (домики налезут): " + close + " пар");
            // дорога через чужое место
            int through = 0;
            foreach (MapRoad r in data.roads)
            {
                List<Vector2> l = data.Line(r);
                foreach (MapPlace p in data.places)
                {
                    if (p.id == r.a || p.id == r.b) continue;
                    Vector2 pp = new Vector2(p.x * TW, p.y * TH);
                    for (int k = 0; k + 1 < l.Count; k++)
                        if (DistSeg(pp, new Vector2(l[k].x * TW, l[k].y * TH), new Vector2(l[k + 1].x * TW, l[k + 1].y * TH)) < 10f) { through++; bad.Add(p.id); break; }
                }
            }
            if (through > 0) issues.Add("дорога проходит через чужое место: " + through);
            // мост — только бой; постоянное место без типа
            foreach (MapPlace p in data.places)
            {
                if (p.bridge && p.type >= 0 && p.type != (int)NodeType.Duel && p.type != (int)NodeType.Elite && p.type != (int)NodeType.Ambush)
                { bad.Add(p.id); issues.Add("место " + p.id + " на мосту, но не бой (будет боем)"); }
                if (p.kind == 2 && p.type < 0) { bad.Add(p.id); issues.Add("постоянное место " + p.id + " без типа"); }
            }
            // торговец и травница: если все места заданы вручную — нужны хоть одни
            int random = 0; bool merchant = false, herb = false;
            foreach (MapPlace p in data.places)
            {
                if (p.kind == 0 && (p.layer == 0 || p.layer == last)) continue;
                if (p.type < 0) random++;
                if (p.type == (int)NodeType.Merchant) merchant = true;
                if (p.type == (int)NodeType.Herbalist) herb = true;
            }
            if (random == 0 && !merchant) issues.Add("нет торговца");
            if (random == 0 && !herb) issues.Add("нет травницы");
            issueCursor = Mathf.Clamp(issueCursor, 0, Mathf.Max(0, issues.Count - 1));
        }
    }
}
