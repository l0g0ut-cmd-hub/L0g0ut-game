using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Карта области — файл карты (Resources/Maps/map_N.json из tools/export_map_data.py; правки редактора карт —
    /// persistentDataPath/maps/map_N.json, они важнее встроенных). Места и развилки — доли широкой картины (x/ширина, y/1024),
    /// эффекты — пиксели картины. Если файла нет — игра берёт карты, зашитые в код (Campaign.Spots, TerrainRoads.MakeGraphs).
    /// </summary>
    [System.Serializable]
    public class MapPlace
    {
        public string id;
        public float x, y;
        public int layer;
        public int type = -1;   // NodeType; −1 — случайный (разыгрывается в каждом походе)
        public int kind;        // 0 — место ряда (иногда выпадает: дорога сходится в точку), 1 — доп. место, 2 — постоянное место
        public bool bridge;     // место на мосту (там только бой)
        public string custom;   // своё место (редактор карт): id из MapData.customs — тогда тип места «СВОЁ МЕСТО»
    }

    /// <summary>Выбор в карточке своего места: надпись на кнопке и что будет.</summary>
    [System.Serializable]
    public class MapChoice
    {
        public string label = "ДАЛЬШЕ";
        public int outcome;          // MapCustom.Outcomes: 0 ничего, 1 моны +, 2 плата −, 3 опыт, 4 вещь, 5 свиток, 6 благословение, 7 проклятие, 8 бой
        public int amount;
        public string result = "";   // что написать после выбора
    }

    /// <summary>Своё место (строение, событие) из редактора карт: значок и картина с телефона, текст, выборы.</summary>
    [System.Serializable]
    public class MapCustom
    {
        public string id;
        public string name = "СВОЁ МЕСТО";
        public string icon;          // «file:maps/obj/…» — круглый значок на карте; пусто — медальон события
        public string art;           // «file:maps/obj/…» — картина карточки
        public string text = "";
        public int foe;              // противник для выбора «БОЙ»: 0 — по области, 1…4 — обитатели леса, 52 — алый самурай
        public bool strong;          // бой как с сильным противником
        public string setFlag;       // цепочка: после любого выбора ставится метка (на всю область)
        public string needFlag;      // цепочка: место открыто только с этой меткой
        public string lockedText;    // что написать, пока метки нет
        public MapChoice[] choices = new MapChoice[0];

        public static readonly string[] Outcomes = { "НИЧЕГО", "МОНЫ +", "ПЛАТА −", "ОПЫТ +", "ВЕЩЬ", "СВИТОК", "БЛАГОСЛОВЕНИЕ", "ПРОКЛЯТИЕ", "БОЙ" };
        public static readonly int[] OutcomeDefault = { 0, 50, 30, 100, 1, 0, 0, 0, 0 };
        public static readonly int[] FoeCodes = { 0, 1, 2, 3, 4, 52 };
        public static readonly string[] FoeNames = { "ПО ОБЛАСТИ", "ЛЕСНОЙ РАЗБОЙНИК", "ЯМАБУСИ", "АСИГАРУ-ДЕЗЕРТИР", "МАСКА КИЦУНЭ", "АЛЫЙ САМУРАЙ" };
    }

    [System.Serializable]
    public class MapPoint
    {
        public string id;
        public float x, y;
        public bool bridge;
    }

    [System.Serializable]
    public class MapRoad
    {
        public string a, b;
        public float[] pts = new float[0];   // изгибы между концами: x0, y0, x1, y1…
        public bool water;                   // переправа по воде (лодка): голубой пунктир
        public bool trail;                   // тропы нет на картине — игра рисует пунктир
    }

    [System.Serializable]
    public class MapEffect
    {
        public string kind;         // fall_fade, fall_mid, fall_lip, stream, smoke, campfire, mist…
        public float x, y, w, h;    // левый верхний угол и размер, пиксели картины
        public float alpha = 1f, speed = 1f;
        public float angle;        // clockwise degrees in map screen coordinates, waterfall lip is pivot
        public int foot;            // у водопада: 0 — ничего, 1 — брызги, 2 — водяная пыль ущелья
    }

    [System.Serializable]
    public class MapData
    {
        public int version = 1;
        public string name;
        public string texture;
        public int width = 2610;
        public int height;   // высота картины (0 — 1024)
        public int order;
        public int boss = 1;
        public int like;            // своя область (редактор карт): по правилам какой области — враги, музыка, погода (0…2)
        public float lairX, lairY;
        public MapPlace[] places = new MapPlace[0];
        public MapPoint[] points = new MapPoint[0];
        public MapRoad[] roads = new MapRoad[0];
        public MapEffect[] effects = new MapEffect[0];
        public MapCustom[] customs = new MapCustom[0];   // свои места (редактор карт)

        public MapCustom Custom(string id)
        {
            if (string.IsNullOrEmpty(id) || customs == null) return null;
            foreach (MapCustom c in customs) if (c.id == id) return c;
            return null;
        }

        public int Layers { get { int l = 0; foreach (MapPlace p in places) l = Mathf.Max(l, p.layer + 1); return l; } }

        /// <summary>Координаты места или развилки по имени (null — нет такого).</summary>
        public Vector2? Pos(string id)
        {
            foreach (MapPlace p in places) if (p.id == id) return new Vector2(p.x, p.y);
            foreach (MapPoint p in points) if (p.id == id) return new Vector2(p.x, p.y);
            return null;
        }
        public bool IsBridge(string id)
        {
            foreach (MapPlace p in places) if (p.id == id) return p.bridge;
            foreach (MapPoint p in points) if (p.id == id) return p.bridge;
            return false;
        }

        /// <summary>Дорога целиком: конец a, изгибы, конец b.</summary>
        public List<Vector2> Line(MapRoad r)
        {
            List<Vector2> l = new List<Vector2>(2 + r.pts.Length / 2);
            Vector2? a = Pos(r.a), b = Pos(r.b);
            if (a.HasValue) l.Add(a.Value);
            for (int i = 0; i + 1 < r.pts.Length; i += 2) l.Add(new Vector2(r.pts[i], r.pts[i + 1]));
            if (b.HasValue) l.Add(b.Value);
            return l;
        }
    }

    /// <summary>Файлы карт: встроенные (Resources/Maps) и правки с телефона (persistentDataPath/maps).</summary>
    public static class MapStore
    {
        public const int BuiltIn = 3;
        static readonly Dictionary<int, MapData> cache = new Dictionary<int, MapData>();
        static string Dir { get { return Path.Combine(Application.persistentDataPath, "maps"); } }
        static string UserPath(int region) { return Path.Combine(Dir, "map_" + region + ".json"); }

        /// <summary>Карта области (null — файла нет, игра берёт карты из кода).</summary>
        public static MapData Get(int region)
        {
            if (Custom >= 0 && region == customRegion) { MapData c = GetCustom(Custom); if (c != null && c.places != null && c.places.Length > 0) return c; }
            return GetPlain(region);
        }

        /// <summary>Карта встроенной области (без своей области поверх неё).</summary>
        public static MapData GetPlain(int region)
        {
            MapData m;
            if (cache.TryGetValue(region, out m)) return m;
            m = null;
            try
            {
                string p = UserPath(region);
                if (File.Exists(p)) m = JsonUtility.FromJson<MapData>(File.ReadAllText(p));
                // правка встроенной области под старую картину (другой размер) — к новой картине не подходит, берём встроенную
                if (m != null && region >= 0 && region < MapGeom.TexW.Length && (m.width != MapGeom.TexW[region] || Mathf.Max(1024, m.height) != MapGeom.TexHt[region])) m = null;
            }
            catch (System.Exception e) { Debug.LogWarning("MapStore: правка карты " + region + " не читается: " + e.Message); m = null; }
            if (m == null)
            {
                TextAsset ta = Resources.Load<TextAsset>("Maps/map_" + region);
                if (ta != null) { try { m = JsonUtility.FromJson<MapData>(ta.text); } catch (System.Exception e) { Debug.LogWarning("MapStore: карта " + region + ": " + e.Message); } }
            }
            if (m != null && (m.places == null || m.places.Length == 0)) m = null;   // пустой файл — как будто его нет
            cache[region] = m;
            return m;
        }

        // ---------------------------------------------------------------- свои области (редактор карт)
        // persistentDataPath/maps/custom_N.json и картина custom_N.png. В походе своя область идёт вместо области «like»
        // (её враги, музыка, погода), пока поход не уйдёт в следующую область.

        public const int MaxCustom = 20;
        static readonly Dictionary<int, MapData> customs = new Dictionary<int, MapData>();
        public static int Custom { get; private set; } = -1;
        static int customRegion = -1;
        static string CustomPath(int n) { return Path.Combine(Dir, "custom_" + n + ".json"); }
        public static string CustomImage(int n) { return "maps/custom_" + n + ".jpg"; }   // путь от persistentDataPath (UiKit.Tex «file:»)

        /// <summary>Своя область n идёт вместо области region (−1 — своих нет).</summary>
        public static void SetActive(int custom, int region)
        {
            if (custom == Custom && (custom < 0 || region == customRegion)) return;
            Custom = custom; customRegion = custom >= 0 ? region : -1;
            TerrainRoads.Reset();
        }
        public static bool IsCustom(int region) { return Custom >= 0 && region == customRegion && GetCustom(Custom) != null; }

        public static bool CustomExists(int n) { return File.Exists(CustomPath(n)); }
        public static List<int> Customs()
        {
            List<int> l = new List<int>();
            for (int i = 0; i < MaxCustom; i++) if (CustomExists(i)) l.Add(i);
            return l;
        }
        public static int FreeCustom() { for (int i = 0; i < MaxCustom; i++) if (!CustomExists(i)) return i; return -1; }
        public static MapData GetCustom(int n)
        {
            MapData m;
            if (customs.TryGetValue(n, out m)) return m;
            m = null;
            try { if (CustomExists(n)) m = JsonUtility.FromJson<MapData>(File.ReadAllText(CustomPath(n))); }
            catch (System.Exception e) { Debug.LogWarning("MapStore: своя область " + n + " не читается: " + e.Message); }
            customs[n] = m;
            return m;
        }
        public static void SaveCustom(int n, MapData m)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(CustomPath(n), JsonUtility.ToJson(m, true));
            customs[n] = m;
            if (n == Custom) TerrainRoads.Reset();
        }
        public static void DeleteCustom(int n)
        {
            try { if (File.Exists(CustomPath(n))) File.Delete(CustomPath(n)); } catch { }
            try { string ip = Path.Combine(Application.persistentDataPath, CustomImage(n)); if (File.Exists(ip)) File.Delete(ip); } catch { }
            customs.Remove(n);
            UiKit.Forget("file:" + CustomImage(n));
        }

        /// <summary>Есть ли правка этой области, сделанная на телефоне.</summary>
        public static bool Edited(int region) { return File.Exists(UserPath(region)); }

        /// <summary>Сохранить правку области на телефоне (редактор карт).</summary>
        public static void Save(int region, MapData m)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(UserPath(region), JsonUtility.ToJson(m, true));
            cache[region] = m;
            TerrainRoads.Reset();
        }

        /// <summary>Вернуть встроенную область как была (правка удаляется).</summary>
        public static void Revert(int region)
        {
            try { if (File.Exists(UserPath(region))) File.Delete(UserPath(region)); } catch { }
            cache.Remove(region);
            TerrainRoads.Reset();
        }
    }
}
