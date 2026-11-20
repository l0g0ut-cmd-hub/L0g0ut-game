using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Редактор персонажей (меню разработчика): основа мужская / женская, зоны одежды и тела — свой цвет, своя картинка
    /// из галереи, яркость, износ; лицо — фото из галереи или снимок камерой по рамке (глаза, нос, рот, овал), фото
    /// подгоняется пальцами и сразу натягивается на модель. Справа — герой на арене, крутится пальцем.
    /// Сохранение — CharacterLook (5 мест), «ОБЛИК ГЕРОЯ» — свой персонаж вместо облика странника.
    /// </summary>
    public static class CharacterEditor
    {
        public static bool Open { get; private set; }

        static readonly Color Gold = new Color(0.9f, 0.76f, 0.47f), Ink = new Color(0.93f, 0.89f, 0.8f), Dim = new Color(0.68f, 0.64f, 0.56f);
        static readonly Color Panel = new Color(0.09f, 0.07f, 0.055f, 0.95f), Edge = new Color(0.59f, 0.44f, 0.24f, 1f);
        static readonly Color[] Palette =
        {
            new Color(0.92f, 0.9f, 0.84f), new Color(0.12f, 0.11f, 0.1f), new Color(0.45f, 0.45f, 0.47f), new Color(0.14f, 0.18f, 0.36f),
            new Color(0.2f, 0.27f, 0.5f), new Color(0.13f, 0.32f, 0.36f), new Color(0.6f, 0.1f, 0.08f), new Color(0.36f, 0.06f, 0.07f),
            new Color(0.72f, 0.48f, 0.16f), new Color(0.75f, 0.64f, 0.3f), new Color(0.32f, 0.36f, 0.18f), new Color(0.17f, 0.28f, 0.16f),
            new Color(0.4f, 0.26f, 0.15f), new Color(0.3f, 0.18f, 0.35f),
        };

        static float W;
        static int slot;
        static CharData data;
        static int zone;                 // выбранная зона (индекс в основе)
        static bool dirty;
        static int ver, composedVer = -1; static float composeAt;
        static Texture2D preview;        // собранная текстура для превью (512)
        static bool showAtlas;
        static string note; static float noteAt;
        static string confirm; static float confirmAt;

        // превью героя
        static Camera cam; static RenderTexture rt;
        static float yaw = 200f;
        const int PreviewLayer = 30;
        static PlayerSkins skins;

        // лицо по рамке
        static bool faceOpen;
        static WebCamTexture webcam; static int camIndex; static bool camWanted;
        static Rect frameR; static Vector2 frameC; static float U;

        // ---------------------------------------------------------------- открыть / закрыть

        public static void Show()
        {
            Open = true; faceOpen = false; showAtlas = false;
            slot = Mathf.Max(0, CharacterLook.Active);
            LoadSlot(slot);
            skins = null;
            if (DuelManager.Instance != null && DuelManager.Instance.player != null)
                skins = DuelManager.Instance.player.GetComponent<PlayerSkins>();
            if (skins == null || skins.models == null || skins.models.Length <= CharacterLook.SkinFemale || skins.models[CharacterLook.SkinMale] == null)
                Note("В этой сборке нет основ персонажей — видна только текстура");
        }

        static void Close()
        {
            StopCam();
            Open = false; faceOpen = false;
            if (cam != null) Object.Destroy(cam.gameObject);
            if (rt != null) { rt.Release(); Object.Destroy(rt); }
            cam = null; rt = null;
            CharacterLook.Forget();
            if (skins != null && !CharacterLook.ApplyTo(skins)) skins.Apply(JourneyChoice.Hero);
        }

        static void LoadSlot(int s)
        {
            slot = s; data = CharacterLook.Load(s);
            BaseInfo b = CharacterLook.Info(data.female);
            zone = 0;
            if (b != null) for (int i = 0; i < b.zones.Length; i++) if (b.zones[i].id == "face") zone = i;
            dirty = false; ver++;
        }

        static void Changed() { dirty = true; ver++; }
        static void Note(string s) { note = s; noteAt = Time.unscaledTime; }
        static bool Confirm(string what, string text)
        {
            if (confirm == what && Time.unscaledTime - confirmAt < 4f) { confirm = null; return true; }
            confirm = what; confirmAt = Time.unscaledTime; Note(text); return false;
        }

        // ---------------------------------------------------------------- кадр

        public static void Draw(float w)
        {
            W = w;
            Event e = Event.current;
            if (e.type == EventType.Repaint)
            {
                if (ver != composedVer && Time.unscaledTime - composeAt > 0.15f) Recompose();
                if (camWanted) TryStartCam();
                WatchCam();
            }
            UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0.05f, 0.04f, 0.03f, 1f));
            if (faceOpen) DrawFace(); else DrawMain();
            if (note != null && Time.unscaledTime - noteAt < 3f)
            {
                Rect nr = new Rect(W * 0.5f - 300f, 60f, 600f, 30f);
                UiKit.Rect(nr, new Color(0f, 0f, 0f, 0.8f));
                UiKit.Text(nr, note, 15f, Gold);
            }
        }

        static void Recompose()
        {
            composedVer = ver; composeAt = Time.unscaledTime;
            if (preview != null) Object.Destroy(preview);
            preview = CharacterLook.Compose(data, 512);
            if (skins != null) CharacterLook.ApplyTo(skins, data, preview);
        }

        static void Box(Rect r)
        {
            Texture2D pt = r.height > 120f ? UiKit.Tex("UI/editor_panel") : null;
            if (pt != null) { UiKit.Rect(new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 8f), new Color(0.05f, 0.02f, 0.02f, 0.96f)); UiKit.Slice(r, pt, 0.42f, 0.15f, 0f, 0.075f, 0f, Color.white); return; }
            UiKit.Rect(r, Panel); Frame(r, Edge, 1.5f);
        }
        // значки зон (UI/char_zone_icons, 4×3): лицо, волосы, кожа, кисти, стопы, куртка, рукава, пояс, штаны, обмотки, шляпа, маска
        static readonly string[] IconOrder = { "face", "hair", "skin", "hands", "feet", "jacket", "sleeves", "belt", "pants", "wraps", "hat", "mask" };
        static void ZoneIcon(Rect r, string id)
        {
            Texture2D t = UiKit.Tex("UI/char_zone_icons");
            int i = System.Array.IndexOf(IconOrder, id);
            if (t == null || i < 0) return;
            GUI.DrawTextureWithTexCoords(r, t, new Rect((i % 4) / 4f, 1f - (i / 4 + 1) / 3f, 0.25f, 1f / 3f));
        }
        static void Frame(Rect r, Color c, float th)
        {
            UiKit.Rect(new Rect(r.x, r.y, r.width, th), c); UiKit.Rect(new Rect(r.x, r.yMax - th, r.width, th), c);
            UiKit.Rect(new Rect(r.x, r.y, th, r.height), c); UiKit.Rect(new Rect(r.xMax - th, r.y, th, r.height), c);
        }
        static bool Btn(Rect r, string s, bool on = true, bool red = false, bool sel = false) { return VisualIdentity.KitButton(r, s, on, red, sel); }

        static int sliderId = -1;
        static float Slider(int id, Rect r, string label, float v, float min, float max)
        {
            UiKit.Text(new Rect(r.x, r.y, r.width, 16f), label, 12f, Dim, TextAnchor.MiddleLeft);
            Rect tr = new Rect(r.x + 8f, r.y + 26f, r.width - 16f, 6f);
            UiKit.Rect(tr, new Color(0f, 0f, 0f, 0.6f));
            float k = Mathf.InverseLerp(min, max, v);
            UiKit.Rect(new Rect(tr.x, tr.y, tr.width * k, tr.height), new Color(Gold.r, Gold.g, Gold.b, 0.7f));
            UiKit.Disc(new Vector2(tr.x + tr.width * k, tr.center.y), 9f, Gold);
            Event e = Event.current;
            Rect hit = new Rect(r.x, r.y + 12f, r.width, 32f);
            if (e.type == EventType.MouseDown && hit.Contains(e.mousePosition)) { sliderId = id; e.Use(); }
            if (sliderId == id && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag || e.type == EventType.Used))
            {
                float nv = Mathf.Lerp(min, max, Mathf.Clamp01((e.mousePosition.x - tr.x) / tr.width));
                if (Mathf.Abs(nv - v) > 0.0001f) { v = nv; Changed(); }
                if (e.type == EventType.MouseDrag) e.Use();
            }
            if (e.rawType == EventType.MouseUp && sliderId == id) sliderId = -1;
            return v;
        }

        // ---------------------------------------------------------------- главный экран

        static void DrawMain()
        {
            BaseInfo b = CharacterLook.Info(data.female);
            // верх
            Box(new Rect(0f, 0f, W, 48f));
            UiKit.Text(new Rect(10f, 6f, 210f, 36f), "РЕДАКТОР ПЕРСОНАЖЕЙ", 17f, Gold, TextAnchor.MiddleLeft);
            float x = 222f, gap = 6f, avail = W - x - 8f;
            bool active = CharacterLook.Active == slot;
            string[] labels = { "МЕСТО " + (slot + 1) + (CharacterLook.Exists(slot) ? "" : " · ПУСТО"), data.female ? "МОДЕЛЬ: ЖЕНСКАЯ ▸" : "МОДЕЛЬ: МУЖСКАЯ ▸",
                                "СОХРАНИТЬ", active ? "ОБЛИК ГЕРОЯ: ДА" : "ОБЛИК ГЕРОЯ: НЕТ", showAtlas ? "МОДЕЛЬ" : "ТЕКСТУРА", "ВЫЙТИ" };
            float[] wt = { 1.2f, 1.5f, 1.1f, 1.6f, 1f, 0.8f };
            float sum = 0f; foreach (float f in wt) sum += f;
            float unit = (avail - gap * (wt.Length - 1)) / sum;
            for (int i = 0; i < labels.Length; i++)
            {
                Rect br = new Rect(x, 6f, wt[i] * unit, 36f);
                x += wt[i] * unit + gap;
                if (!Btn(br, labels[i], true, (i == 2 && dirty) || (i == 3 && active))) continue;
                switch (i)
                {
                    case 0:
                        if (!dirty || Confirm("slot", "Есть несохранённое — нажми ещё раз, чтобы перейти без сохранения"))
                        { LoadSlot((slot + 1) % CharacterLook.Slots); Note(CharacterLook.Exists(slot) ? "Место " + (slot + 1) + ": " + data.name : "Место " + (slot + 1) + " пустое — новый персонаж"); }
                        break;
                    case 1: data.female = !data.female; Changed(); break;
                    case 2: SaveNow(); break;
                    case 3:
                        if (active) { CharacterLook.Active = -1; Note("Герой снова в облике странника"); }
                        else { if (dirty || !CharacterLook.Exists(slot)) SaveNow(); CharacterLook.Active = slot; Note("Свой персонаж — облик героя в походе и в бою"); }
                        CharacterLook.Forget();
                        break;
                    case 4: showAtlas = !showAtlas; break;
                    case 5:
                        if (!dirty || Confirm("exit", "Есть несохранённое — нажми ещё раз, чтобы выйти без сохранения")) { Close(); return; }
                        break;
                }
            }
            if (b == null) { UiKit.Text(new Rect(0f, 200f, W, 40f), "Нет файла зон основы (Resources/Characters)", 18f, Ink); return; }

            // зоны слева
            Rect zl = new Rect(0f, 52f, 170f, 540f - 52f);
            Box(zl);
            int rows = b.zones.Length + CharacterWear.SlotNames.Length;
            float zy = zl.y + 8f, zh = Mathf.Min(38f, (zl.height - 16f) / rows - 4f);
            for (int i = 0; i < b.zones.Length; i++)
            {
                ZoneInfo zi = b.zones[i];
                ZoneLook z = data.Zone(zi.id);
                Rect r = new Rect(zl.x + 8f, zy, zl.width - 16f, zh);
                string n = zi.name.ToUpper();
                bool custom = zi.id == "face" ? !string.IsNullOrEmpty(data.face.photo) : (z.tint || !string.IsNullOrEmpty(z.image) || z.wear > 0.01f || Mathf.Abs(z.bright - 1f) > 0.01f);
                if (Btn(r, "", true, false, i == zone)) { zone = i; }
                float ic = r.height - 4f;
                ZoneIcon(new Rect(r.x + 3f, r.y + 2f, ic, ic), zi.id);
                UiKit.Text(new Rect(r.x + ic + 8f, r.y, r.width - ic - 22f, r.height), custom ? n + " •" : n, 12f, i == zone ? Gold : Ink, TextAnchor.MiddleLeft);
                if (z.tint && zi.id != "face") UiKit.Rect(new Rect(r.xMax - 12f, r.y + 6f, 6f, r.height - 12f), z.color);
                zy += zh + 4f;
            }
            // надеваемые вещи: причёска, голова, лицо
            string[] wicon = { "hair", "hat", "mask" };
            for (int k = 0; k < CharacterWear.SlotNames.Length; k++)
            {
                int i = b.zones.Length + k;
                WearLook wl = data.Wear(k);
                Rect r = new Rect(zl.x + 8f, zy, zl.width - 16f, zh);
                if (Btn(r, "", true, false, i == zone)) zone = i;
                float ic = r.height - 4f;
                ZoneIcon(new Rect(r.x + 3f, r.y + 2f, ic, ic), wicon[k]);
                string n = CharacterWear.SlotNames[k] + (string.IsNullOrEmpty(wl.id) ? "" : " •");
                UiKit.Text(new Rect(r.x + ic + 8f, r.y, r.width - ic - 12f, r.height), n, 12f, i == zone ? Gold : new Color(0.95f, 0.8f, 0.55f), TextAnchor.MiddleLeft);
                zy += zh + 4f;
            }

            // превью
            Rect pv = new Rect(176f, 52f, W - 176f - 276f, 540f - 52f);
            Box(pv);
            if (showAtlas || skins == null) DrawAtlas(pv, b);
            else DrawPreview(pv, b);
            // имя персонажа и «случайный облик» — поверх превью
            if (nameStyle == null) { nameStyle = new GUIStyle(GUI.skin.textField); nameStyle.fontSize = 16; nameStyle.normal.textColor = Ink; nameStyle.focused.textColor = Ink; nameStyle.alignment = TextAnchor.MiddleLeft; }
            string nn = GUI.TextField(new Rect(pv.x + 10f, pv.y + 10f, Mathf.Min(300f, pv.width - 150f), 32f), data.name ?? "", 24, nameStyle);
            if (nn != data.name) { data.name = nn; dirty = true; }
            if (Btn(new Rect(pv.xMax - 136f, pv.y + 8f, 128f, 36f), "СЛУЧАЙНО")) RandomLook(b);

            // настройки зоны справа
            Rect sp = new Rect(W - 270f, 52f, 270f, 540f - 52f);
            Box(sp);
            if (zone >= b.zones.Length) { DrawWearSettings(sp, zone - b.zones.Length); return; }
            ZoneInfo cur = b.zones[Mathf.Clamp(zone, 0, b.zones.Length - 1)];
            if (cur.id == "face") DrawFaceSettings(sp); else DrawZoneSettings(sp, cur);
        }

        static void DrawAtlas(Rect pv, BaseInfo b)
        {
            float s = Mathf.Min(pv.width, pv.height) - 16f;
            Rect ar = new Rect(pv.center.x - s * 0.5f, pv.center.y - s * 0.5f, s, s);
            if (preview != null) GUI.DrawTexture(ar, preview);
            if (zone >= b.zones.Length) return;
            ZoneInfo zi = b.zones[zone];
            // rect зоны в долях атласа, y снизу
            Rect zr = new Rect(ar.x + zi.rect[0] * s, ar.y + (1f - zi.rect[1] - zi.rect[3]) * s, zi.rect[2] * s, zi.rect[3] * s);
            Frame(zr, Gold, 2f);
            UiKit.Text(new Rect(pv.x + 8f, pv.yMax - 24f, pv.width - 16f, 20f), "Развёртка: каждая зона — свой кусок, как выкройка", 11f, Dim);
        }

        // ---------------------------------------------------------------- герой на арене

        static Transform Head()
        {
            if (skins == null) return null;
            foreach (HeroRigDriver r in skins.GetComponentsInChildren<HeroRigDriver>())
                if (r.gameObject.activeInHierarchy) return r.head;
            return null;
        }

        static void DrawPreview(Rect pv, BaseInfo b)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDrag && pv.Contains(e.mousePosition) && sliderId < 0) { yaw += e.delta.x * 0.6f; e.Use(); }
            if (e.type == EventType.Repaint)
            {
                int pw = Mathf.Clamp(Mathf.RoundToInt(pv.width * UiKit.Scale), 64, 1400), ph = Mathf.Clamp(Mathf.RoundToInt(pv.height * UiKit.Scale), 64, 1400);
                if (rt == null || rt.width != pw || rt.height != ph)
                {
                    if (rt != null) { rt.Release(); Object.Destroy(rt); }
                    rt = new RenderTexture(pw, ph, 24, RenderTextureFormat.ARGB32); rt.antiAliasing = 2;
                }
                if (cam == null)
                {
                    GameObject g = new GameObject("CharacterEditorCam");
                    cam = g.AddComponent<Camera>();
                    cam.enabled = false; cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.07f, 0.06f, 0.05f, 1f);
                    cam.nearClipPlane = 0.05f; cam.farClipPlane = 30f; cam.allowMSAA = true;
                }
                cam.targetTexture = rt;
                Texture2D bg = UiKit.Tex("UI/char_editor_bg");
                if (bg != null)
                {   // фон — комната додзё (по покрытию), камера стирает только глубину: арены за героем не видно
                    float ar = rt.width / (float)rt.height, ab = bg.width / (float)bg.height;
                    Vector2 sc = ar < ab ? new Vector2(ar / ab, 1f) : new Vector2(1f, ab / ar);
                    RenderTexture keep = RenderTexture.active;
                    Graphics.Blit(bg, rt, sc, (Vector2.one - sc) * 0.5f);
                    RenderTexture.active = keep;   // иначе интерфейс дальше рисовался бы в превью
                    cam.clearFlags = CameraClearFlags.Depth;
                    cam.cullingMask = 1 << PreviewLayer;
                }
                else { cam.clearFlags = CameraClearFlags.SolidColor; cam.cullingMask = ~0; }
                Transform root = skins.transform, head = Head();
                bool faceZone = zone >= b.zones.Length || b.zones[Mathf.Clamp(zone, 0, b.zones.Length - 1)].id == "face" || b.zones[Mathf.Clamp(zone, 0, b.zones.Length - 1)].id == "hair";
                Vector3 target = faceZone && head != null ? head.position + Vector3.up * 0.04f : root.position + Vector3.up * 0.95f;
                float dist = faceZone ? 0.85f : 3.4f;
                cam.fieldOfView = faceZone ? 28f : 32f;
                Quaternion q = Quaternion.Euler(6f, root.eulerAngles.y + yaw, 0f);
                cam.transform.position = target - q * Vector3.forward * dist;
                cam.transform.rotation = q;
                // на время снимка — только герой (свой слой)
                Renderer[] rs = skins.GetComponentsInChildren<Renderer>();
                int[] was = new int[rs.Length];
                for (int i = 0; i < rs.Length; i++) { was[i] = rs[i].gameObject.layer; rs[i].gameObject.layer = PreviewLayer; }
                cam.Render();
                for (int i = 0; i < rs.Length; i++) if (rs[i] != null) rs[i].gameObject.layer = was[i];
            }
            if (rt != null) GUI.DrawTexture(pv, rt, ScaleMode.ScaleAndCrop, false);
            UiKit.Text(new Rect(pv.x + 8f, pv.yMax - 24f, pv.width - 16f, 20f), "Проведи пальцем — повернуть героя", 11f, Dim);
        }

        // ---------------------------------------------------------------- настройки зон

        static void DrawZoneSettings(Rect sp, ZoneInfo zi)
        {
            ZoneLook z = data.Zone(zi.id);
            float x = sp.x + 12f, w = sp.width - 24f, y = sp.y + 10f;
            UiKit.Text(new Rect(x, y, w, 24f), zi.name.ToUpper(), 18f, Gold, TextAnchor.MiddleLeft); y += 30f;
            UiKit.Text(new Rect(x, y, w, 16f), "ЦВЕТ", 12f, Dim, TextAnchor.MiddleLeft); y += 18f;
            float cs = (w - 6f * 6f) / 7f;
            for (int i = 0; i <= Palette.Length; i++)
            {
                Rect c = new Rect(x + (i % 7) * (cs + 6f), y + (i / 7) * (cs + 6f), cs, cs);
                if (i == Palette.Length)
                {   // «как есть»
                    UiKit.Rect(c, new Color(0.2f, 0.17f, 0.13f)); UiKit.Text(c, "—", 16f, Ink);
                    if (!z.tint) Frame(c, Gold, 2f);
                    if (GUI.Button(c, GUIContent.none, GUIStyle.none)) { z.tint = false; Changed(); }
                    continue;
                }
                UiKit.Rect(c, Palette[i]);
                bool on = z.tint && (z.color - Palette[i]).maxColorComponent < 0.01f && (Palette[i] - z.color).maxColorComponent < 0.01f;
                Frame(c, on ? Gold : new Color(0f, 0f, 0f, 0.6f), on ? 2.5f : 1f);
                if (GUI.Button(c, GUIContent.none, GUIStyle.none)) { z.tint = true; z.color = Palette[i]; Changed(); }
            }
            y += 3f * (cs + 6f) + 4f;
            z.bright = Slider(1, new Rect(x, y, w, 44f), "ЯРКОСТЬ", z.bright, 0.4f, 1.6f); y += 48f;
            z.wear = Slider(2, new Rect(x, y, w, 44f), "ИЗНОС (грязь, потёртости)", z.wear, 0f, 1f); y += 52f;
            UiKit.Text(new Rect(x, y, w, 16f), "СВОЯ КАРТИНКА (ткань, узор, доспех)", 12f, Dim, TextAnchor.MiddleLeft); y += 20f;
            if (Btn(new Rect(x, y, w, 38f), string.IsNullOrEmpty(z.image) ? "ИЗ ГАЛЕРЕИ" : "ДРУГАЯ КАРТИНКА")) PickImage(1024, (t) => { z.image = CharacterLook.Keep(t, zi.id); Changed(); });
            y += 44f;
            if (!string.IsNullOrEmpty(z.image) && Btn(new Rect(x, y, w, 34f), "УБРАТЬ КАРТИНКУ")) { z.image = null; Changed(); }
            y += 40f;
            if (Btn(new Rect(x, sp.yMax - 88f, w, 36f), "ШАБЛОН ЗОНЫ")) ShareTemplate(zi);
            if (Btn(new Rect(x, sp.yMax - 46f, w, 36f), "СБРОСИТЬ ЗОНУ")) { z.tint = false; z.bright = 1f; z.wear = 0f; z.image = null; Changed(); }
        }

        static void DrawWearSettings(Rect sp, int slot)
        {
            WearLook w = data.Wear(slot);
            float x = sp.x + 12f, w2 = sp.width - 24f, y = sp.y + 10f;
            UiKit.Text(new Rect(x, y, w2, 24f), CharacterWear.SlotNames[slot], 18f, Gold, TextAnchor.MiddleLeft); y += 28f;
            // выбор вещи: «нет» и вещи этого места (ещё не собранные — серые)
            float bw = (w2 - 6f) * 0.5f; int col = 0;
            if (Btn(new Rect(x, y, bw, 30f), "НЕТ", true, false, string.IsNullOrEmpty(w.id))) { w.id = null; Changed(); }
            col = 1;
            foreach (CharacterWear.Item it in CharacterWear.Catalog)
            {
                if (it.slot != slot) continue;
                bool has = CharacterWear.Has(it.id, data.female);
                Rect r = new Rect(x + col * (bw + 6f), y, bw, 30f);
                if (Btn(r, it.name.ToUpper(), has, false, w.id == it.id)) { w.id = it.id; Changed(); }
                col++; if (col == 2) { col = 0; y += 34f; }
            }
            if (col != 0) y += 34f;
            if (!CharacterWear.Has("Hair_Short", data.female) && !CharacterWear.Has("Kabuto", data.female))
            { UiKit.Text(new Rect(x, y, w2, 30f), "Модели вещей ещё собираются — будут в следующей сборке", 10f, Dim, TextAnchor.UpperLeft); y += 30f; }
            y += 4f;
            UiKit.Text(new Rect(x, y, w2, 16f), "ЦВЕТ", 12f, Dim, TextAnchor.MiddleLeft); y += 18f;
            float cs = (w2 - 6f * 6f) / 7f;
            for (int i = 0; i <= Palette.Length; i++)
            {
                Rect c = new Rect(x + (i % 7) * (cs + 6f), y + (i / 7) * (cs + 6f), cs, cs);
                if (i == Palette.Length)
                {
                    UiKit.Rect(c, new Color(0.2f, 0.17f, 0.13f)); UiKit.Text(c, "—", 16f, Ink);
                    if (!w.tint) Frame(c, Gold, 2f);
                    if (GUI.Button(c, GUIContent.none, GUIStyle.none)) { w.tint = false; Changed(); }
                    continue;
                }
                UiKit.Rect(c, Palette[i]);
                bool on = w.tint && Mathf.Abs(w.color.r - Palette[i].r) + Mathf.Abs(w.color.g - Palette[i].g) + Mathf.Abs(w.color.b - Palette[i].b) < 0.02f;
                Frame(c, on ? Gold : new Color(0f, 0f, 0f, 0.6f), on ? 2.5f : 1f);
                if (GUI.Button(c, GUIContent.none, GUIStyle.none)) { w.tint = true; w.color = Palette[i]; Changed(); }
            }
            y += 2f * (cs + 6f) + 6f;
            w.size = Slider(10 + slot * 3, new Rect(x, y, w2, 44f), "РАЗМЕР", w.size, 0.85f, 1.2f); y += 46f;
            w.up = Slider(11 + slot * 3, new Rect(x, y, w2, 44f), "ВЫШЕ / НИЖЕ", w.up, -0.05f, 0.05f); y += 46f;
            w.back = Slider(12 + slot * 3, new Rect(x, y, w2, 44f), "ВПЕРЁД / НАЗАД", w.back, -0.05f, 0.05f); y += 46f;
            if (Btn(new Rect(x, sp.yMax - 46f, w2, 36f), "ПОДГОНКА КАК БЫЛА")) { w.size = 1f; w.up = 0f; w.back = 0f; Changed(); }
        }

        static void DrawFaceSettings(Rect sp)
        {
            FaceLook f = data.face;
            float x = sp.x + 12f, w = sp.width - 24f, y = sp.y + 10f;
            UiKit.Text(new Rect(x, y, w, 24f), "ЛИЦО", 18f, Gold, TextAnchor.MiddleLeft); y += 30f;
            UiKit.Text(new Rect(x, y, w, 34f), "Своё фото: глаза — в метки рамки,\nи лицо сразу ляжет на модель", 11f, Dim, TextAnchor.UpperLeft); y += 40f;
            if (Btn(new Rect(x, y, w, 38f), "ФОТО ИЗ ГАЛЕРЕИ", true, true)) PickImage(1024, (t) => { f.photo = CharacterLook.Keep(t, "face"); FitPhoto(t); Changed(); faceOpen = true; });
            y += 44f;
            if (Btn(new Rect(x, y, w, 38f), "СНЯТЬ КАМЕРОЙ ПО РАМКЕ")) { faceOpen = true; camAsked = false; camWanted = true; }
            y += 44f;
            bool has = CharacterLook.Image(f.photo) != null;
            if (Btn(new Rect(x, y, w, 38f), "ПОДОГНАТЬ ПО РАМКЕ", has)) faceOpen = true;
            y += 50f;
            f.skin = Slider(3, new Rect(x, y, w, 44f), "ТОН КОЖИ ТЕЛА ПОД ФОТО", f.skin, 0f, 1f); y += 48f;
            f.feather = Slider(4, new Rect(x, y, w, 44f), "МЯГКОСТЬ КРАЯ ЛИЦА", f.feather, 0f, 1f); y += 52f;
            ZoneLook z = data.Zone("face");
            z.bright = Slider(5, new Rect(x, y, w, 44f), "ЯРКОСТЬ ЛИЦА", z.bright, 0.5f, 1.5f); y += 50f;
            if (has && Btn(new Rect(x, sp.yMax - 46f, w, 36f), "УБРАТЬ ФОТО")) { f.photo = null; Changed(); }
        }

        // ---------------------------------------------------------------- лицо по рамке

        static void Layout()
        {
            frameR = new Rect(20f, 60f, W - 20f - 300f - 20f, 540f - 60f - 20f);
            BaseInfo b = CharacterLook.Info(data.female);
            Vector2 oc, orr; CharacterLook.FaceOval(b, out oc, out orr);
            U = Mathf.Min(frameR.width / (orr.x * 2f * 1.5f), frameR.height / (orr.y * 2f * 1.18f));
            frameC = frameR.center - oc * U;
        }

        /// <summary>Фото из галереи: сначала по центру рамки, во всю высоту овала — дальше пальцами.</summary>
        static void FitPhoto(Texture2D t)
        {
            Layout();
            FaceLook f = data.face;
            BaseInfo b = CharacterLook.Info(data.female);
            Vector2 oc, orr; CharacterLook.FaceOval(b, out oc, out orr);
            f.rot = 0f; f.cx = oc.x; f.cy = oc.y;
            // фото по высоте ≈ 1,6 овала (портрет обычно с волосами и шеей)
            float h = orr.y * 2f * 1.6f;
            f.scale = h * t.width / Mathf.Max(1f, t.height);
        }

        static void DrawFace()
        {
            Layout();
            BaseInfo b = CharacterLook.Info(data.female);
            FaceLook f = data.face;
            Texture2D photo = CharacterLook.Image(f.photo);
            bool live = webcam != null && webcam.isPlaying;
            if (!live) HandleFaceGestures(photo);

            // под рамкой: живая камера или фото
            if (live && webcam.width > 16)
            {
                int ang = ((webcam.videoRotationAngle % 360) + 360) % 360;
                bool side = ang == 90 || ang == 270;
                float rw = side ? webcam.height : webcam.width, rh = side ? webcam.width : webcam.height;
                float k = Mathf.Max(frameR.width / rw, frameR.height / rh);
                float w0 = webcam.width * k, h0 = webcam.height * k;
                Matrix4x4 m = GUI.matrix;
                GUIUtility.RotateAroundPivot(ang, frameR.center);
                Rect tc = webcam.videoVerticallyMirrored ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
                GUI.DrawTextureWithTexCoords(new Rect(frameR.center.x - w0 * 0.5f, frameR.center.y - h0 * 0.5f, w0, h0), webcam, tc);
                GUI.matrix = m;
            }
            else if (photo != null)
            {
                float pw = f.scale * U, ph = pw * photo.height / Mathf.Max(1f, photo.width);
                Vector2 c = frameC + new Vector2(f.cx, f.cy) * U;
                Matrix4x4 m = GUI.matrix;
                GUIUtility.RotateAroundPivot(f.rot, c);
                GUI.DrawTexture(new Rect(c.x - pw * 0.5f, c.y - ph * 0.5f, pw, ph), photo);
                GUI.matrix = m;
            }
            else UiKit.Text(frameR, camWanted || live ? "Включаю камеру…" : "Нет фото", 18f, Dim);

            // шторки вокруг рамки (фото за краем не видно)
            Color sh = new Color(0.05f, 0.04f, 0.03f, 1f);
            UiKit.Rect(new Rect(0f, 0f, W, frameR.y), sh);
            UiKit.Rect(new Rect(0f, frameR.yMax, W, 540f - frameR.yMax), sh);
            UiKit.Rect(new Rect(0f, frameR.y, frameR.x, frameR.height), sh);
            UiKit.Rect(new Rect(frameR.xMax, frameR.y, W - frameR.xMax, frameR.height), sh);
            Frame(frameR, Edge, 1.5f);
            DrawGuides(b);

            Box(new Rect(0f, 0f, W, 52f));
            UiKit.Text(new Rect(14f, 8f, W - 330f, 36f), live ? "Смотри прямо, лицо — в овал, глаза — в кружки. Свет — спереди." : "Глаза — в кружки, нос и рот — на линии. Одним пальцем двигать, двумя — размер и поворот.", 13f, Ink, TextAnchor.MiddleLeft);

            // справа
            Rect sp = new Rect(W - 300f, 52f, 300f, 540f - 52f);
            Box(sp);
            float x = sp.x + 12f, w = sp.width - 24f, y = sp.y + 12f;
            // само лицо на модели (живьём)
            Rect fr = new Rect(x, y, w, w * 0.9f);
            if (skins != null && !live) DrawPreview(fr, b);
            else if (preview != null)
            {
                ZoneInfo fz = null; foreach (ZoneInfo z in b.zones) if (z.id == "face") fz = z;
                if (fz != null) GUI.DrawTextureWithTexCoords(fr, preview, new Rect(fz.rect[0], fz.rect[1], fz.rect[2], fz.rect[3]));
            }
            Frame(fr, Edge, 1f);
            y = fr.yMax + 10f;
            if (live)
            {
                if (Btn(new Rect(x, y, w, 46f), "СНЯТЬ", true, true)) Capture();
                y += 52f;
                if (WebCamTexture.devices.Length > 1 && Btn(new Rect(x, y, w, 36f), "ДРУГАЯ КАМЕРА")) { camIndex++; StopCam(); camWanted = true; }
                y += 42f;
                if (Btn(new Rect(x, y, w, 36f), "СИСТЕМНАЯ КАМЕРА")) SystemCamera();
            }
            else
            {
                float hw = (w - 8f) * 0.5f;
                if (Btn(new Rect(x, y, hw, 34f), "↺ ПОВОРОТ", photo != null)) { f.rot -= 2f; Changed(); }
                if (Btn(new Rect(x + hw + 8f, y, hw, 34f), "ПОВОРОТ ↻", photo != null)) { f.rot += 2f; Changed(); }
                y += 40f;
                if (Btn(new Rect(x, y, hw, 34f), "МЕНЬШЕ", photo != null)) { f.scale *= 0.97f; Changed(); }
                if (Btn(new Rect(x + hw + 8f, y, hw, 34f), "КРУПНЕЕ", photo != null)) { f.scale *= 1.03f; Changed(); }
                y += 40f;
                if (Btn(new Rect(x, y, hw, 34f), "ГАЛЕРЕЯ")) PickImage(1024, (t) => { f.photo = CharacterLook.Keep(t, "face"); FitPhoto(t); Changed(); });
                if (Btn(new Rect(x + hw + 8f, y, hw, 34f), "КАМЕРА")) { camAsked = false; camWanted = true; }
                y += 40f;
                if (Btn(new Rect(x, y, w, 34f), "СИСТЕМНАЯ КАМЕРА")) SystemCamera();
            }
            if (Btn(new Rect(x, sp.yMax - 48f, w, 38f), "ГОТОВО", true, !live)) { StopCam(); faceOpen = false; }
        }

        static Vector2 FP(BaseInfo b, float[] lm) { return frameC + CharacterLook.LandmarkFrame(b, lm) * U; }

        static void DrawGuides(BaseInfo b)
        {
            Vector2 oc, orr; CharacterLook.FaceOval(b, out oc, out orr);
            Color g = new Color(Gold.r, Gold.g, Gold.b, 0.9f), soft = new Color(1f, 1f, 1f, 0.45f);
            Vector2 c = frameC + oc * U, prev = Vector2.zero;
            for (int i = 0; i <= 72; i++)
            {
                float a = i / 72f * Mathf.PI * 2f;
                Vector2 p = c + new Vector2(Mathf.Cos(a) * orr.x, Mathf.Sin(a) * orr.y) * U;
                if (i > 0) { UiKit.Line(prev, p, 3.2f, new Color(0f, 0f, 0f, 0.5f)); UiKit.Line(prev, p, 1.8f, g); }
                prev = p;
            }
            Vector2 el = FP(b, b.landmarks.eyeL), er = FP(b, b.landmarks.eyeR), no = FP(b, b.landmarks.nose), mo = FP(b, b.landmarks.mouth), ch = FP(b, b.landmarks.chin);
            float er0 = Mathf.Max(8f, U * 0.28f);
            foreach (Vector2 e in new[] { el, er }) { UiKit.Ring(e, er0, new Color(0f, 0f, 0f, 0.5f)); UiKit.Ring(e, er0 - 1f, g, true); UiKit.Disc(e, 2.5f, g); }
            UiKit.Line(new Vector2(el.x - er0 * 2f, el.y), new Vector2(er.x + er0 * 2f, er.y), 1f, soft);   // линия глаз
            UiKit.Line(new Vector2(c.x, c.y - orr.y * U), new Vector2(c.x, c.y + orr.y * U), 1f, soft);      // середина лица
            UiKit.Line(new Vector2(no.x - U * 0.35f, no.y), new Vector2(no.x + U * 0.35f, no.y), 2f, g);     // кончик носа
            UiKit.Line(new Vector2(mo.x - U * 0.55f, mo.y), new Vector2(mo.x + U * 0.55f, mo.y), 2f, g);     // губы
            UiKit.Line(new Vector2(ch.x - U * 0.3f, ch.y), new Vector2(ch.x + U * 0.3f, ch.y), 2f, g);       // подбородок
            UiKit.Text(new Rect(el.x - 40f, el.y - er0 - 22f, 80f, 16f), "глаз", 11f, Ink);
            UiKit.Text(new Rect(er.x - 40f, er.y - er0 - 22f, 80f, 16f), "глаз", 11f, Ink);
            UiKit.Text(new Rect(no.x + U * 0.4f, no.y - 8f, 60f, 16f), "нос", 11f, Ink, TextAnchor.MiddleLeft);
            UiKit.Text(new Rect(mo.x + U * 0.6f, mo.y - 8f, 60f, 16f), "рот", 11f, Ink, TextAnchor.MiddleLeft);
            UiKit.Text(new Rect(ch.x + U * 0.35f, ch.y - 8f, 100f, 16f), "подбородок", 11f, Ink, TextAnchor.MiddleLeft);
        }

        // пальцы по фото
        static bool dragFace; static Vector2 dragLast;
        static bool pinch; static float pinchDist0, pinchAng0, pinchScale0, pinchRot0; static Vector2 pinchMid0, pinchC0;

        static Vector2 Gui(Vector2 screen) { return new Vector2(screen.x / UiKit.Scale, (Screen.height - screen.y) / UiKit.Scale); }

        static void HandleFaceGestures(Texture2D photo)
        {
            if (photo == null) return;
            FaceLook f = data.face;
            Event e = Event.current;
            if (Input.touchCount >= 2)
            {
                if (e.type != EventType.Repaint) { if (e.isMouse) e.Use(); return; }
                Vector2 a = Gui(Input.GetTouch(0).position), bb = Gui(Input.GetTouch(1).position);
                Vector2 mid = (a + bb) * 0.5f, d = bb - a;
                float dist = Mathf.Max(10f, d.magnitude), ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                if (!pinch)
                {
                    if (!frameR.Contains(mid)) return;
                    pinch = true; dragFace = false;
                    pinchDist0 = dist; pinchAng0 = ang; pinchScale0 = f.scale; pinchRot0 = f.rot; pinchMid0 = mid; pinchC0 = new Vector2(f.cx, f.cy);
                }
                f.scale = Mathf.Clamp(pinchScale0 * dist / pinchDist0, 0.5f, 40f);
                f.rot = pinchRot0 + Mathf.DeltaAngle(pinchAng0, ang);
                Vector2 c = pinchC0 + (mid - pinchMid0) / U;
                f.cx = c.x; f.cy = c.y;
                Changed();
                return;
            }
            if (pinch) { if (Input.touchCount == 0) pinch = false; if (e.isMouse) e.Use(); return; }
            if (e.type == EventType.MouseDown && frameR.Contains(e.mousePosition)) { dragFace = true; dragLast = e.mousePosition; e.Use(); }
            else if (e.type == EventType.MouseDrag && dragFace)
            {
                Vector2 dd = (e.mousePosition - dragLast) / U; dragLast = e.mousePosition;
                f.cx += dd.x; f.cy += dd.y; Changed(); e.Use();
            }
            else if (e.rawType == EventType.MouseUp) dragFace = false;
            else if (e.type == EventType.ScrollWheel && frameR.Contains(e.mousePosition)) { f.scale *= e.delta.y > 0f ? 0.96f : 1.04f; Changed(); e.Use(); }
        }

        // ---------------------------------------------------------------- камера

        static void TryStartCam()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                if (!camAsked)
                {   // спросить доступ; отказ — не ждать вечно, а сказать, что делать
                    camAsked = true;
                    var cb = new UnityEngine.Android.PermissionCallbacks();
                    cb.PermissionDenied += (p) => { camWanted = false; camAsked = false; Note("Нет доступа к камере — разреши его или нажми «СИСТЕМНАЯ КАМЕРА»"); };
                    cb.PermissionDeniedAndDontAskAgain += (p) => { camWanted = false; camAsked = false; Note("Доступ к камере запрещён в настройках телефона — «СИСТЕМНАЯ КАМЕРА» работает и так"); };
                    UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera, cb);
                }
                return;
            }
#endif
            camWanted = false;
            WebCamDevice[] ds = WebCamTexture.devices;
            if (ds.Length == 0) { Note("Камера не найдена — нажми «СИСТЕМНАЯ КАМЕРА»"); return; }
            // сначала фронтальная (селфи)
            int start = 0;
            for (int i = 0; i < ds.Length; i++) if (ds[i].isFrontFacing) { start = i; break; }
            int idx = (start + camIndex) % ds.Length;
            try
            {
                webcam = new WebCamTexture(ds[idx].name, 1280, 960, 30);
                webcam.Play();
                camStartAt = Time.unscaledTime;
            }
            catch (System.Exception e) { webcam = null; Note("Камера не включилась: " + e.Message); }
        }
        static float camStartAt;
        /// <summary>Камера включена, а кадров нет дольше 4 с — выключить и сказать.</summary>
        static void WatchCam()
        {
            if (webcam != null && webcam.isPlaying && webcam.width <= 16 && Time.unscaledTime - camStartAt > 4f)
            { StopCam(); Note("Камера не даёт картинку — нажми «СИСТЕМНАЯ КАМЕРА»"); }
        }

        /// <summary>Снимок системной камерой телефона (NativeCamera): фото ложится под рамку, дальше — пальцами.</summary>
        static void SystemCamera()
        {
            StopCam();
            NativeCamera.TakePicture((path) =>
            {
                if (string.IsNullOrEmpty(path)) return;
                Texture2D t = NativeCamera.LoadImageAtPath(path, 1024, false, false);
                if (t == null) { Note("Снимок не открылся"); return; }
                data.face.photo = CharacterLook.Keep(t, "face"); FitPhoto(t); Changed(); faceOpen = true;
                Note("Глаза — в кружки: подвинь фото пальцами");
            }, 1024, true, NativeCamera.PreferredCamera.Front);
        }
#pragma warning disable 0414
        static bool camAsked;
#pragma warning restore 0414

        static void StopCam()
        {
            camWanted = false; camAsked = false;
            if (webcam != null) { webcam.Stop(); Object.Destroy(webcam); webcam = null; }
        }

        /// <summary>Снимок: кадр камеры прямо (поворот и отражение как на экране), положение — как было под рамкой.</summary>
        static void Capture()
        {
            if (webcam == null || webcam.width <= 16) return;
            int w = webcam.width, h = webcam.height;
            Color32[] px = webcam.GetPixels32();
            if (webcam.videoVerticallyMirrored)
            {
                Color32[] fl = new Color32[px.Length];
                for (int y = 0; y < h; y++) System.Array.Copy(px, (h - 1 - y) * w, fl, y * w, w);
                px = fl;
            }
            int ang = ((webcam.videoRotationAngle % 360) + 360) % 360;
            for (int n = 0; n < ang / 90; n++)
            {   // поворот по часовой на 90° (как на экране)
                Color32[] r = new Color32[px.Length];
                int nw = h, nh = w;
                for (int Y = 0; Y < nh; Y++)          // Y, X — сверху вниз, слева направо
                    for (int X = 0; X < nw; X++)
                    {
                        int ox = Y, oy = h - 1 - X;         // старая точка (сверху вниз)
                        r[(nh - 1 - Y) * nw + X] = px[(h - 1 - oy) * w + ox];
                    }
                px = r; w = nw; h = nh;
            }
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.SetPixels32(px); t.Apply();
            // как на экране: кадр по центру рамки, «по покрытию»
            float k = Mathf.Max(frameR.width / w, frameR.height / h);
            FaceLook f = data.face;
            f.rot = 0f; f.scale = w * k / U;
            Vector2 c = (frameR.center - frameC) / U; f.cx = c.x; f.cy = c.y;
            f.photo = CharacterLook.Keep(t, "face");
            StopCam();
            Changed();
            Note("Снято — подправь пальцами, если глаза не точно в кружках");
        }

        // ---------------------------------------------------------------- галерея и сохранение

        static void PickImage(int maxSize, System.Action<Texture2D> done)
        {
            NativeGallery.GetImageFromGallery((path) =>
            {
                if (string.IsNullOrEmpty(path)) return;
                Texture2D t = NativeGallery.LoadImageAtPath(path, maxSize, false, false);
                if (t == null) { Note("Картинка не открылась"); return; }
                done(t);
            }, "Выбери картинку");
        }

        static GUIStyle nameStyle;

        /// <summary>Случайный облик: одежда — цвета палитры, износ, причёска (лицо и свои картинки не трогает).</summary>
        static void RandomLook(BaseInfo b)
        {
            string[] cloth = { "jacket", "sleeves", "belt", "pants", "wraps", "feet" };
            Color main = Palette[Random.Range(1, Palette.Length)], second = Palette[Random.Range(0, Palette.Length)];
            foreach (string id in cloth)
            {
                ZoneLook z = data.Zone(id);
                if (!string.IsNullOrEmpty(z.image)) continue;
                z.tint = Random.value < 0.85f;
                z.color = id == "belt" || id == "wraps" ? second : id == "sleeves" && Random.value < 0.3f ? second : main;
                z.bright = Random.Range(0.85f, 1.1f);
                z.wear = Random.value < 0.5f ? Random.Range(0.1f, 0.6f) : 0f;
            }
            WearLook hw = data.Wear(0);
            List<string> hairs = new List<string>();
            foreach (CharacterWear.Item it in CharacterWear.Catalog) if (it.slot == 0 && CharacterWear.Has(it.id, data.female)) hairs.Add(it.id);
            hw.id = hairs.Count > 0 && Random.value < 0.8f ? hairs[Random.Range(0, hairs.Count)] : null;
            Changed();
            Note("Случайный облик — что не нравится, поправь по зонам");
        }

        /// <summary>Шаблон зоны: кусок текстуры основы (как он лежит на модели) — поделиться, разрисовать и вернуть как «СВОЯ КАРТИНКА».</summary>
        static void ShareTemplate(ZoneInfo zi)
        {
            try
            {
                Texture2D bt = CharacterLook.BaseTex(data.female);
                if (bt == null) { Note("Нет текстуры основы"); return; }
                int x0 = Mathf.FloorToInt(zi.rect[0] * bt.width), y0 = Mathf.FloorToInt(zi.rect[1] * bt.height);
                int w = Mathf.Max(1, Mathf.FloorToInt(zi.rect[2] * bt.width)), h = Mathf.Max(1, Mathf.FloorToInt(zi.rect[3] * bt.height));
                Texture2D t = new Texture2D(w, h, TextureFormat.RGB24, false);
                t.SetPixels(bt.GetPixels(x0, y0, w, h)); t.Apply();
                string p = System.IO.Path.Combine(Application.temporaryCachePath, "BladePath_" + zi.id + (data.female ? "_f" : "_m") + "_template.png");
                System.IO.File.WriteAllBytes(p, t.EncodeToPNG());
                Object.Destroy(t);
                new NativeShare().AddFile(p).SetSubject("Шаблон зоны «" + zi.name + "»").SetText("Разрисуй поверх (форма кусков — как они лежат на модели) и загрузи обратно: «СВОЯ КАРТИНКА» → «ИЗ ГАЛЕРЕИ».").Share();
            }
            catch (System.Exception e) { Note("Не получилось: " + e.Message); }
        }

        static void SaveNow()
        {
            if (string.IsNullOrEmpty(data.name)) data.name = "СВОЙ №" + (slot + 1);
            Texture2D full = CharacterLook.Compose(data, 2048);
            CharacterLook.Save(slot, data, full);
            Object.Destroy(full);
            CharacterLook.Forget();
            dirty = false;
            Note("Сохранено: место " + (slot + 1));
        }
    }
}
