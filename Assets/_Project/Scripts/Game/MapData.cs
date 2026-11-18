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
        public int foot;            // у водопада: 0 — ничего, 1 — брызги, 2 — водяная пыль ущелья
    }

    [System.Serializable]
    public class MapData
    {
        public int version = 1;
        public string name;
        public string texture;
        public int width = 2610;
        public int order;
        public int boss = 1;
        public float lairX, lairY;
        public MapPlace[] places = new MapPlace[0];
        public MapPoint[] points = new MapPoint[0];
        public MapRoad[] roads = new MapRoad[0];
        public MapEffect[] effects = new MapEffect[0];

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
            MapData m;
            if (cache.TryGetValue(region, out m)) return m;
            m = null;
            try
            {
                string p = UserPath(region);
                if (File.Exists(p)) m = JsonUtility.FromJson<MapData>(File.ReadAllText(p));
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
