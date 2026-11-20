using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>Надетая вещь своего персонажа: что (id из каталога), цвет, подгонка.</summary>
    [System.Serializable]
    public class WearLook
    {
        public int slot;                   // 0 — причёска, 1 — голова (шляпа, шлем, капюшон), 2 — лицо (маска, повязка)
        public string id;                  // пусто — ничего
        public bool tint;
        public Color color = Color.white;
        public float size = 1f;            // 0,85…1,2
        public float up, back;             // сдвиг, метры
    }

    /// <summary>
    /// Надеваемые вещи редактора персонажей: модели Meshy, подогнанные к голове основы (tools/blender/build_accessory.py,
    /// список acc_list.txt) — Resources/Accessories/&lt;Id&gt;_M|F (FBX) и &lt;id&gt;_albedo. Меш вещи — в пространстве тела основы:
    /// под кость Head она ставится по bindpose этой кости и дальше двигается вместе с головой.
    /// </summary>
    public static class CharacterWear
    {
        public const string Prefix = "ACC:";
        public static readonly string[] SlotNames = { "ПРИЧЁСКА", "ГОЛОВА", "ЛИЦО" };

        public struct Item { public string id, name; public int slot; public Item(string i, string n, int s) { id = i; name = n; slot = s; } }
        public static readonly Item[] Catalog =
        {
            new Item("Hair_Short", "Короткие лохматые", 0), new Item("Hair_Long", "Длинные волнистые", 0),
            new Item("Hair_Topknot", "Пучок самурая", 0), new Item("Hair_Ponytail", "Высокий хвост", 0),
            new Item("Kasa_Straw", "Соломенная каса", 1), new Item("Jingasa", "Лакированная дзингаса", 1),
            new Item("Kabuto", "Кабуто", 1), new Item("Hood", "Капюшон", 1),
            new Item("Mempo", "Мэмпо", 2), new Item("Mask_Cloth", "Платок на лицо", 2), new Item("Headband_Red", "Красная хатимаки", 2),
        };
        public static string Name(string id) { foreach (Item i in Catalog) if (i.id == id) return i.name; return "—"; }

        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Texture2D> texs = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, bool> missing = new Dictionary<string, bool>();

        /// <summary>Есть ли модель вещи в сборке.</summary>
        public static bool Has(string id, bool female) { return GetMesh(id, female) != null; }

        static Mesh GetMesh(string id, bool female)
        {
            string key = id + (female ? "_F" : "_M");
            Mesh m;
            if (meshes.TryGetValue(key, out m)) return m;
            if (missing.ContainsKey(key)) return null;
            GameObject go = Resources.Load<GameObject>("Accessories/" + key);
            MeshFilter mf = go != null ? go.GetComponentInChildren<MeshFilter>() : null;
            m = mf != null ? mf.sharedMesh : null;
            if (m == null) { missing[key] = true; return null; }
            meshes[key] = m; return m;
        }
        static Texture2D GetTex(string id)
        {
            Texture2D t;
            if (texs.TryGetValue(id, out t)) return t;
            t = Resources.Load<Texture2D>("Accessories/" + id.ToLowerInvariant() + "_albedo");
            texs[id] = t; return t;
        }

        static Material baseMat;
        /// <summary>Надеть на модель основы вещи персонажа (прежние снимаются).</summary>
        public static void Dress(GameObject model, CharData d)
        {
            if (model == null) return;
            SkinnedMeshRenderer body = null;
            foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (!r.name.StartsWith(Prefix) && (body == null || r.sharedMesh.vertexCount > body.sharedMesh.vertexCount)) body = r;
            if (body == null || body.sharedMesh == null) return;
            int hi = -1;
            for (int i = 0; i < body.bones.Length; i++) if (body.bones[i] != null && body.bones[i].name == "Head") { hi = i; break; }
            if (hi < 0) return;
            Transform head = body.bones[hi];
            // снять прежние
            List<GameObject> old = new List<GameObject>();
            foreach (Transform c in head) if (c.name.StartsWith(Prefix)) old.Add(c.gameObject);
            foreach (GameObject g in old)
            {
                foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true)) Object.Destroy(r.sharedMaterial);
                g.SetActive(false); Object.Destroy(g);
            }
            if (d == null || d.wear == null) return;
            Matrix4x4 bind = body.sharedMesh.bindposes[hi];
            if (baseMat == null) { Renderer br = body; baseMat = br.sharedMaterial; }
            foreach (WearLook w in d.wear)
            {
                if (string.IsNullOrEmpty(w.id)) continue;
                Mesh mesh = GetMesh(w.id, d.female);
                if (mesh == null) continue;
                // вершины вещи — в пространстве тела основы: bindpose переводит их в пространство кости головы.
                // Обёртка в центре вещи: размер — вокруг центра, сдвиг вверх/назад — по осям модели (в позе на момент надевания)
                Vector3 cHead = bind.MultiplyPoint3x4(mesh.bounds.center);
                Vector3 offW = model.transform.up * w.up - model.transform.forward * w.back;
                GameObject wrap = new GameObject(Prefix + w.id);
                wrap.transform.SetParent(head, false);
                wrap.transform.localPosition = cHead + head.InverseTransformVector(offW);
                wrap.transform.localScale = Vector3.one * Mathf.Clamp(w.size, 0.6f, 1.6f);
                GameObject g = new GameObject(Prefix + w.id + ":mesh");
                g.layer = body.gameObject.layer; wrap.layer = g.layer;
                g.transform.SetParent(wrap.transform, false);
                Matrix4x4 m = Matrix4x4.Translate(-cHead) * bind;
                g.transform.localPosition = m.GetColumn(3);
                g.transform.localRotation = m.rotation;
                g.transform.localScale = m.lossyScale;
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer mr = g.AddComponent<MeshRenderer>();
                Material mat = new Material(body.sharedMaterial != null ? body.sharedMaterial : baseMat);
                Texture2D t = GetTex(w.id);
                mat.mainTexture = t;
                mat.color = w.tint ? w.color * 1.15f : new Color(1.1f, 1.08f, 1.05f, 1f);
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }
    }
}
