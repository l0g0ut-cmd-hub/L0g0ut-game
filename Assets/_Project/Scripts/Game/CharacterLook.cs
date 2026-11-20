using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Свой персонаж (редактор персонажей): основа (мужская / женская — BaseMale/BaseFemale, развёртка по зонам) и настройка
    /// каждой зоны: цвет, своя картинка, яркость, износ; своё лицо — фото, выровненное по меткам глаз (рамка редактора).
    /// Зоны и точки лица — Resources/Characters/base*_zones.json (tools/blender/zone_uv.py). Собранная текстура сохраняется
    /// рядом с персонажем (persistentDataPath/characters/char_N.png) — в игре она берётся готовой, без пересборки.
    /// Облик героя в походе и в бою — свой персонаж, если он включён (характеристики героя — от выбранного странника).
    /// </summary>
    [System.Serializable]
    public class ZoneLook
    {
        public string id;
        public bool tint;                  // перекрасить в цвет
        public Color color = Color.white;
        public float bright = 1f;          // яркость 0,4…1,6
        public float wear;                 // износ 0…1 (грязь, потёртости)
        public string image;               // своя картинка (путь на телефоне), пусто — нет
    }

    [System.Serializable]
    public class FaceLook
    {
        public string photo;               // путь к фото лица; пусто — лицо основы
        public float cx, cy;               // центр фото относительно центра рамки, в единицах рамки (полрасстояния между глазами)
        public float scale = 1f;           // единиц рамки на ширину фото
        public float rot;                  // поворот фото, градусы
        public float skin = 0.8f;          // насколько кожа головы, шеи и кистей подстраивается под тон фото
        public float feather = 0.5f;       // мягкость края лица
    }

    [System.Serializable]
    public class CharData
    {
        public string name = "СВОЙ ПЕРСОНАЖ";
        public bool female;
        public List<ZoneLook> zones = new List<ZoneLook>();
        public FaceLook face = new FaceLook();
        public List<WearLook> wear = new List<WearLook>();   // надетые вещи (CharacterWear): причёска, голова, лицо
        public WearLook Wear(int slot)
        {
            foreach (WearLook w in wear) if (w.slot == slot) return w;
            WearLook n = new WearLook { slot = slot }; wear.Add(n); return n;
        }
        public ZoneLook Zone(string id)
        {
            foreach (ZoneLook z in zones) if (z.id == id) return z;
            ZoneLook n = new ZoneLook { id = id }; zones.Add(n); return n;
        }
    }

    [System.Serializable]
    public class ZoneInfo { public string id, name; public float[] rect; }
    [System.Serializable]
    public class BaseInfo
    {
        public int atlas = 2048;
        public ZoneInfo[] zones;
        public Landmarks landmarks;
    }
    [System.Serializable]
    public class Landmarks { public float[] eyeL, eyeR, nose, mouth, chin, faceL, faceR, top; }

    public static class CharacterLook
    {
        public const int Slots = 5;
        public const int SkinMale = 3, SkinFemale = 4;   // индексы основ в PlayerSkins.models
        static string Dir { get { return Path.Combine(Application.persistentDataPath, "characters"); } }
        static string DataPath(int s) { return Path.Combine(Dir, "char_" + s + ".json"); }
        static string TexPath(int s) { return Path.Combine(Dir, "char_" + s + ".png"); }

        /// <summary>Какой свой персонаж — облик героя (−1 — нет, обычный облик странника).</summary>
        public static int Active
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt("char_active", -1), -1, Slots - 1); }
            set { PlayerPrefs.SetInt("char_active", Mathf.Clamp(value, -1, Slots - 1)); PlayerPrefs.Save(); }
        }

        public static bool Exists(int s) { return File.Exists(DataPath(s)); }
        public static CharData Load(int s)
        {
            try { if (File.Exists(DataPath(s))) { CharData d = JsonUtility.FromJson<CharData>(File.ReadAllText(DataPath(s))); if (d != null) return d; } }
            catch (System.Exception e) { Debug.LogWarning("CharacterLook: персонаж " + s + " не читается: " + e.Message); }
            return new CharData { name = "СВОЙ №" + (s + 1) };
        }
        public static void Save(int s, CharData d, Texture2D composed)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(DataPath(s), JsonUtility.ToJson(d, true));
            if (composed != null) File.WriteAllBytes(TexPath(s), composed.EncodeToPNG());
        }
        public static Texture2D LoadComposed(int s)
        {
            try
            {
                if (!File.Exists(TexPath(s))) return null;
                Texture2D t = new Texture2D(2, 2, TextureFormat.RGB24, true);
                t.LoadImage(File.ReadAllBytes(TexPath(s)));
                t.wrapMode = TextureWrapMode.Clamp; t.anisoLevel = 2;
                return t;
            }
            catch { return null; }
        }

        // ---------------------------------------------------------------- основы

        static readonly Dictionary<bool, BaseInfo> infos = new Dictionary<bool, BaseInfo>();
        static readonly Dictionary<bool, Texture2D> bases = new Dictionary<bool, Texture2D>();
        public static BaseInfo Info(bool female)
        {
            BaseInfo b;
            if (infos.TryGetValue(female, out b)) return b;
            TextAsset ta = Resources.Load<TextAsset>("Characters/" + (female ? "basefemale" : "basemale") + "_zones");
            b = ta != null ? JsonUtility.FromJson<BaseInfo>(ta.text) : null;
            infos[female] = b; return b;
        }
        public static Texture2D BaseTex(bool female)
        {
            Texture2D t;
            if (bases.TryGetValue(female, out t) && t != null) return t;
            t = Resources.Load<Texture2D>("Characters/" + (female ? "basefemale" : "basemale") + "_albedo");
            bases[female] = t; return t;
        }

        /// <summary>Картинка с телефона (своя картинка зоны, фото лица); кэш по пути.</summary>
        static readonly Dictionary<string, Texture2D> images = new Dictionary<string, Texture2D>();
        public static Texture2D Image(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Texture2D t;
            if (images.TryGetValue(path, out t) && t != null) return t;
            try
            {
                if (!File.Exists(path)) return null;
                t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!t.LoadImage(File.ReadAllBytes(path))) return null;
                t.wrapMode = TextureWrapMode.Clamp;
                images[path] = t; return t;
            }
            catch { return null; }
        }
        public static void Remember(string path, Texture2D t) { if (!string.IsNullOrEmpty(path) && t != null) images[path] = t; }
        /// <summary>Скопировать картинку в папку персонажей (галерея может удалить или переименовать исходник).</summary>
        public static string Keep(Texture2D t, string tag)
        {
            Directory.CreateDirectory(Dir);
            string p = Path.Combine(Dir, "img_" + tag + "_" + System.DateTime.Now.ToString("yyyyMMddHHmmss") + ".png");
            File.WriteAllBytes(p, t.EncodeToPNG());
            images[p] = t;
            return p;
        }

        // ---------------------------------------------------------------- лицо: атлас ↔ рамка

        /// <summary>Точка атласа (пиксели, y вверх) → рамка (единица — полрасстояния между глазами, y вниз, центр — между глазами).</summary>
        public static Vector2 AtlasToFrame(BaseInfo b, Vector2 p)
        {
            float S = b.atlas;
            Vector2 l1 = new Vector2(b.landmarks.eyeL[0] * S, -b.landmarks.eyeL[1] * S), l2 = new Vector2(b.landmarks.eyeR[0] * S, -b.landmarks.eyeR[1] * S);
            Vector2 q = new Vector2(p.x, -p.y), m = (l1 + l2) * 0.5f, d = l2 - l1;
            Vector2 z = q - m;
            // z / d (комплексное деление) × 2
            float dd = d.x * d.x + d.y * d.y;
            return new Vector2((z.x * d.x + z.y * d.y) / dd * 2f, (z.y * d.x - z.x * d.y) / dd * 2f);
        }
        public static Vector2 LandmarkFrame(BaseInfo b, float[] uv) { return AtlasToFrame(b, new Vector2(uv[0] * b.atlas, uv[1] * b.atlas)); }

        /// <summary>Рамка → пиксель фото (u, v в долях, v вверх) по положению фото под рамкой.</summary>
        public static Vector2 FrameToPhoto(FaceLook f, Texture2D photo, Vector2 fr)
        {
            Vector2 r = fr - new Vector2(f.cx, f.cy);
            float a = -f.rot * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            r = new Vector2(r.x * c - r.y * s, r.x * s + r.y * c);
            float w = Mathf.Max(0.01f, f.scale), h = w * photo.height / Mathf.Max(1f, photo.width);
            return new Vector2(0.5f + r.x / w, 0.5f - r.y / h);
        }

        // ---------------------------------------------------------------- сборка текстуры

        /// <summary>Собрать текстуру персонажа: основа → зоны (цвет / картинка, яркость, износ) → лицо с фото. size — сторона атласа.</summary>
        public static Texture2D Compose(CharData d, int size)
        {
            BaseInfo info = Info(d.female);
            Texture2D bt = BaseTex(d.female);
            Texture2D outT = new Texture2D(size, size, TextureFormat.RGB24, true);
            if (bt == null || info == null) return outT;
            Color32[] src;
            try { src = Scaled(bt, size); }
            catch (System.Exception e) { Debug.LogWarning("CharacterLook: основа не читается: " + e.Message); return outT; }
            Color32[] px = (Color32[])src.Clone();
            // тон кожи под фото лица
            Texture2D photo = Image(d.face.photo);
            Color skinK = Color.white;
            if (photo != null && d.face.skin > 0.01f) skinK = SkinRatio(info, d.face, photo, src, size);
            foreach (ZoneInfo zi in info.zones)
            {
                ZoneLook zl = d.Zone(zi.id);
                bool skinZone = zi.id == "face" || zi.id == "skin" || zi.id == "hands";
                Texture2D img = zi.id == "face" ? null : Image(zl.image);
                int x0 = Mathf.FloorToInt(zi.rect[0] * size), y0 = Mathf.FloorToInt(zi.rect[1] * size);
                int x1 = Mathf.Min(size, Mathf.CeilToInt((zi.rect[0] + zi.rect[2]) * size)), y1 = Mathf.Min(size, Mathf.CeilToInt((zi.rect[1] + zi.rect[3]) * size));
                // средняя яркость зоны — чтобы перекраска сохранила складки и тени ткани
                double sum = 0; int cnt = 0;
                for (int y = y0; y < y1; y += 2) for (int x = x0; x < x1; x += 2) { Color32 c = src[y * size + x]; int l = c.r + c.g + c.b; if (l > 24) { sum += l; cnt++; } }
                float mean = cnt > 0 ? (float)(sum / cnt) / 765f : 0.5f;
                float rw = x1 - x0, rh = y1 - y0;
                float ia = img != null ? img.width / (float)img.height : 1f, ra = rw / Mathf.Max(1f, rh);
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        int i = y * size + x;
                        Color32 c0 = src[i];
                        if (c0.r + c0.g + c0.b <= 24) continue;   // пустое место атласа
                        Color c = c0;
                        float lum = (c.r * 0.3f + c.g * 0.59f + c.b * 0.11f);
                        float shade = Mathf.Clamp(lum / Mathf.Max(0.05f, mean), 0.2f, 1.8f);
                        if (img != null)
                        {   // своя картинка «по размеру зоны» (обрезая лишнее), складки ткани остаются
                            float u = (x - x0) / rw, v = (y - y0) / rh;
                            if (ia > ra) u = 0.5f + (u - 0.5f) * ra / ia; else v = 0.5f + (v - 0.5f) * ia / ra;
                            Color ic = img.GetPixelBilinear(u, v);
                            c = Color.Lerp(c, ic * (0.45f + 0.55f * shade), ic.a);
                        }
                        else if (zl.tint) c = zl.color * shade * 0.9f;
                        if (skinZone && skinK != Color.white) c = new Color(c.r * skinK.r, c.g * skinK.g, c.b * skinK.b);
                        c *= zl.bright;
                        if (zl.wear > 0.01f)
                        {
                            float n = Mathf.PerlinNoise(x * 0.018f, y * 0.018f) * 0.7f + Mathf.PerlinNoise(x * 0.09f, y * 0.09f) * 0.3f;
                            float k = zl.wear * Mathf.Clamp01((n - 0.35f) * 2f);
                            c = Color.Lerp(c, new Color(c.r * 0.55f + 0.06f, c.g * 0.5f + 0.045f, c.b * 0.42f + 0.03f), k);
                        }
                        px[i] = new Color32((byte)Mathf.Clamp(c.r * 255f, 0, 255), (byte)Mathf.Clamp(c.g * 255f, 0, 255), (byte)Mathf.Clamp(c.b * 255f, 0, 255), 255);
                    }
            }
            if (photo != null) PaintFace(info, d.face, photo, px, size);
            outT.SetPixels32(px);
            outT.Apply(true);
            outT.wrapMode = TextureWrapMode.Clamp; outT.anisoLevel = 2;
            return outT;
        }

        static readonly Dictionary<long, Color32[]> scaled = new Dictionary<long, Color32[]>();
        static Color32[] Scaled(Texture2D t, int size)
        {
            long key = ((long)t.GetInstanceID() << 16) ^ size;
            Color32[] r;
            if (scaled.TryGetValue(key, out r)) return r;
            r = ScaledNow(t, size);
            if (size <= 1024) scaled[key] = r;   // превью собирается часто — основа малого размера в памяти
            return r;
        }
        static Color32[] ScaledNow(Texture2D t, int size)
        {
            if (t.width == size && t.height == size) return t.GetPixels32();
            Color32[] r = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    r[y * size + x] = t.GetPixelBilinear((x + 0.5f) / size, (y + 0.5f) / size);
            return r;
        }

        /// <summary>Овал лица в рамке: центр, полуоси (из точек лица основы).</summary>
        public static void FaceOval(BaseInfo b, out Vector2 c, out Vector2 r)
        {
            Vector2 fl = LandmarkFrame(b, b.landmarks.faceL), fr = LandmarkFrame(b, b.landmarks.faceR);
            Vector2 tp = LandmarkFrame(b, b.landmarks.top), ch = LandmarkFrame(b, b.landmarks.chin);
            c = new Vector2((fl.x + fr.x) * 0.5f, (tp.y + ch.y) * 0.5f);
            r = new Vector2(Mathf.Abs(fr.x - fl.x) * 0.5f, Mathf.Abs(ch.y - tp.y) * 0.5f);
        }

        static Color SkinRatio(BaseInfo b, FaceLook f, Texture2D photo, Color32[] src, int size)
        {
            // средний цвет щёк и лба: на фото и у основы (внутренняя часть овала, без глаз и рта — по кольцу)
            Vector2 oc, orr; FaceOval(b, out oc, out orr);
            Color ps = Color.black, bs = Color.black; int n = 0;
            for (int k = 0; k < 64; k++)
            {
                float a = k / 64f * Mathf.PI * 2f;
                Vector2 fr = oc + new Vector2(Mathf.Cos(a) * orr.x * 0.6f, Mathf.Sin(a) * orr.y * 0.55f);
                Vector2 pu = FrameToPhoto(f, photo, fr);
                if (pu.x < 0f || pu.x > 1f || pu.y < 0f || pu.y > 1f) continue;
                ps += photo.GetPixelBilinear(pu.x, pu.y);
                Vector2 ap = FrameToAtlas(b, fr) * (size / (float)b.atlas);
                int ix = Mathf.Clamp((int)ap.x, 0, size - 1), iy = Mathf.Clamp((int)ap.y, 0, size - 1);
                bs += (Color)src[iy * size + ix]; n++;
            }
            if (n < 8) return Color.white;
            ps /= n; bs /= n;
            Color k2 = new Color(ps.r / Mathf.Max(0.05f, bs.r), ps.g / Mathf.Max(0.05f, bs.g), ps.b / Mathf.Max(0.05f, bs.b));
            k2 = new Color(Mathf.Clamp(k2.r, 0.4f, 2f), Mathf.Clamp(k2.g, 0.4f, 2f), Mathf.Clamp(k2.b, 0.4f, 2f));
            return Color.Lerp(Color.white, k2, f.skin);
        }

        /// <summary>Рамка → пиксель атласа (y вверх) — обратное к AtlasToFrame.</summary>
        public static Vector2 FrameToAtlas(BaseInfo b, Vector2 fr)
        {
            float S = b.atlas;
            Vector2 l1 = new Vector2(b.landmarks.eyeL[0] * S, -b.landmarks.eyeL[1] * S), l2 = new Vector2(b.landmarks.eyeR[0] * S, -b.landmarks.eyeR[1] * S);
            Vector2 m = (l1 + l2) * 0.5f, d = l2 - l1;
            Vector2 z = fr * 0.5f;
            Vector2 q = m + new Vector2(z.x * d.x - z.y * d.y, z.x * d.y + z.y * d.x);
            return new Vector2(q.x, -q.y);
        }

        static void PaintFace(BaseInfo b, FaceLook f, Texture2D photo, Color32[] px, int size)
        {
            ZoneInfo fz = null;
            foreach (ZoneInfo z in b.zones) if (z.id == "face") fz = z;
            if (fz == null) return;
            Vector2 oc, orr; FaceOval(b, out oc, out orr);
            float feather = Mathf.Lerp(0.04f, 0.35f, f.feather);
            float k = b.atlas / (float)size;
            int x0 = Mathf.FloorToInt(fz.rect[0] * size), y0 = Mathf.FloorToInt(fz.rect[1] * size);
            int x1 = Mathf.Min(size, Mathf.CeilToInt((fz.rect[0] + fz.rect[2]) * size)), y1 = Mathf.Min(size, Mathf.CeilToInt((fz.rect[1] + fz.rect[3]) * size));
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    int i = y * size + x;
                    Color32 c0 = px[i];
                    if (c0.r + c0.g + c0.b <= 24) continue;
                    Vector2 fr = AtlasToFrame(b, new Vector2((x + 0.5f) * k, (y + 0.5f) * k));
                    Vector2 e = new Vector2((fr.x - oc.x) / orr.x, (fr.y - oc.y) / orr.y);
                    float rr = e.magnitude;
                    if (rr >= 1f) continue;
                    float a = Mathf.Clamp01((1f - rr) / feather);
                    a = a * a * (3f - 2f * a);
                    Vector2 pu = FrameToPhoto(f, photo, fr);
                    if (pu.x < 0f || pu.x > 1f || pu.y < 0f || pu.y > 1f) continue;
                    Color pc = photo.GetPixelBilinear(pu.x, pu.y);
                    Color c = Color.Lerp(c0, pc, a * pc.a);
                    px[i] = new Color32((byte)Mathf.Clamp(c.r * 255f, 0, 255), (byte)Mathf.Clamp(c.g * 255f, 0, 255), (byte)Mathf.Clamp(c.b * 255f, 0, 255), 255);
                }
        }

        // ---------------------------------------------------------------- облик героя

        static Texture2D activeTex; static int activeSlot = -2;
        /// <summary>Надеть облик своего персонажа на героя (или вернуть облик странника, если свой выключен).</summary>
        public static bool ApplyTo(PlayerSkins skins, CharData d = null, Texture2D tex = null)
        {
            if (skins == null || skins.models == null || skins.models.Length <= SkinFemale) return false;
            int s = Active;
            if (d == null)
            {
                if (s < 0 || !Exists(s)) return false;
                d = Load(s);
                if (activeSlot != s || activeTex == null) { activeTex = LoadComposed(s) ?? Compose(d, 2048); activeSlot = s; }
                tex = activeTex;
            }
            int idx = d.female ? SkinFemale : SkinMale;
            if (skins.models[idx] == null) return false;
            skins.Apply(idx);
            if (tex != null)
                foreach (Renderer r in skins.models[idx].GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name.StartsWith(CharacterWear.Prefix)) continue;   // у надетых вещей своя текстура
                    foreach (Material m in r.materials) if (m.HasProperty("_MainTex")) m.mainTexture = tex;
                }
            CharacterWear.Dress(skins.models[idx], d);
            return true;
        }
        /// <summary>После сохранения: облик в игре пересобрать из файла.</summary>
        public static void Forget() { activeSlot = -2; activeTex = null; }
    }
}
