#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Цельная арена из Meshy («Осенний сад святилища», Assets/_Project/Models/Arenas/ForestShrine.fbx) — тестовый облик арены.
    /// Модель ~1 м в исходнике: разворот на 180° (святилище — за бойцами, как храм), масштаб по ширине, ровная поляна — на высоту 0
    /// (лучами вниз по центру), середина поляны — в центр площадки боя. Под бойцами — невидимый пол (рэгдолл, тени).
    /// Двор храма не трогается: оба облика в сцене, ArenaVariants включает один.
    /// </summary>
    public static class ArenaBuilder
    {
        const string Dir = "Assets/_Project/Models/Arenas/";
        const string Fbx = Dir + "ForestShrine.fbx";
        const float TargetWidth = 8.6f;      // поляна ~0,84 ширины модели → ~7,2 м при площадке боя 6,2 м
        const float ClearingZ = -0.1f;       // центр поляны после разворота, в долях ширины (по разбору сетки)
        const float FightCenterZ = -0.2f;    // середина площадки боя (z от −2,3 до 1,9)

        public static void Build(GameObject[] templeRoots)
        {
            GameObject host = new GameObject("ArenaVariants");
            ArenaVariants av = host.AddComponent<ArenaVariants>();
            // в саду гаснет только сам двор: кедровый лес вокруг, луна, облака, птицы и смена дня и ночи остаются
            // (раньше гасло всё окружение — вокруг диорамы была пустота и белый туман до горизонта)
            HashSet<string> keep = new HashSet<string> { "Moon", "MoonHalo", "DayNight", "MoonClouds", "Birds", "CrowFlock", "ShootingStars" };
            List<GameObject> hide = new List<GameObject>();
            foreach (GameObject g in templeRoots)
            {
                if (g == null || keep.Contains(g.name)) continue;
                if (g.name == "Environment")
                {
                    foreach (Transform c in g.transform) if (c.name != "CedarForest") hide.Add(c.gameObject);
                    continue;
                }
                hide.Add(g);
            }
            av.temple = hide.ToArray();
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (asset == null) { Debug.Log("ArenaBuilder: нет " + Fbx + " — только двор храма"); return; }

            ModelImporter mi = AssetImporter.GetAtPath(Fbx) as ModelImporter;
            if (mi != null && (mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.importAnimation || mi.isReadable == false))
            {
                mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importAnimation = false; mi.isReadable = true;
                mi.SaveAndReimport();
                asset = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            }
            Texture2D albedo = Tex(Dir + "ForestShrine_albedo.jpg", false);
            Texture2D normal = Tex(Dir + "ForestShrine_normal.png", true);
            // фактура вблизи: бесшовные тайлы (земля, мох, камень, дерево, бамбук, лак), проекция по миру — BladePath/ArenaDetail
            Shader detail = Shader.Find("BladePath/ArenaDetail");
            Material m = new Material(detail != null ? detail : Shader.Find("Standard")) { name = "ForestShrine" };
            m.mainTexture = albedo;
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 0.8f); }
            m.SetFloat("_Glossiness", 0.12f); m.SetFloat("_Metallic", 0f);
            if (detail != null)
                foreach (string t in new[] { "Ground", "Moss", "Stone", "Wood", "Bamboo", "Lacquer" })
                {
                    Texture2D tx = Tile(Dir + "Detail/" + t.ToLowerInvariant() + ".jpg");
                    if (tx != null) m.SetTexture("_" + t, tx); else Debug.LogWarning("ArenaBuilder: нет тайла " + t);
                }
            Debug.Log("ArenaBuilder: материал " + m.shader.name);
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Materials")) AssetDatabase.CreateFolder("Assets/_Project", "Materials");
            AssetDatabase.DeleteAsset("Assets/_Project/Materials/ForestShrine.mat");
            AssetDatabase.CreateAsset(m, "Assets/_Project/Materials/ForestShrine.mat");

            GameObject root = new GameObject("Arena_ForestShrine");
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            model.transform.SetParent(root.transform, false);
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = m; r.sharedMaterials = ms;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true;
            }

            // масштаб по ширине и разворот
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
            Bounds b = Bounds(model);
            float s = TargetWidth / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
            model.transform.localScale = model.transform.localScale * s;
            b = Bounds(model);
            model.transform.position += new Vector3(-b.center.x, 0f, FightCenterZ - (b.center.z + ClearingZ * TargetWidth));

            // высота поляны: лучи вниз по площадке боя, медиана попаданий → 0
            foreach (GameObject g in templeRoots) if (g != null) g.SetActive(false);   // лучи не должны попасть в площадку храма
            MeshCollider[] tmp = AddColliders(model);
            List<float> ys = new List<float>();
            for (int ix = -2; ix <= 2; ix++)
                for (int iz = -2; iz <= 2; iz++)
                {
                    RaycastHit h;
                    Vector3 o = new Vector3(ix * 1.2f, 50f, FightCenterZ + iz * 0.8f);
                    if (Physics.Raycast(o, Vector3.down, out h, 200f)) ys.Add(h.point.y);
                }
            foreach (MeshCollider c in tmp) Object.DestroyImmediate(c);
            foreach (GameObject g in templeRoots) if (g != null) g.SetActive(true);
            ys.Sort();
            float floorY = ys.Count > 0 ? ys[ys.Count / 2] : b.min.y;
            model.transform.position += Vector3.down * floorY;
            Debug.Log("ArenaBuilder: сад святилища, масштаб " + s.ToString("F2") + ", пол по " + ys.Count + " лучам: " + floorY.ToString("F3") + " (разброс " + (ys.Count > 0 ? (ys[ys.Count - 1] - ys[0]).ToString("F3") : "-") + ")");

            // земля вокруг диорамы, до кедрового леса: мох (тот же тайл), чуть ниже основания модели
            Texture2D mossTx = Tile(Dir + "Detail/moss.jpg");
            if (mossTx != null)
            {
                GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
                ground.name = "ForestGround"; ground.transform.SetParent(root.transform, false);
                Object.DestroyImmediate(ground.GetComponent<Collider>());
                ground.transform.position = new Vector3(0f, -0.14f, 3f); ground.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                ground.transform.localScale = new Vector3(44f, 40f, 1f);
                Material gm = new Material(Shader.Find("Standard")) { name = "ForestGround", mainTexture = mossTx, mainTextureScale = new Vector2(16f, 14.5f) };
                gm.color = new Color(0.62f, 0.6f, 0.55f); gm.SetFloat("_Glossiness", 0.08f);
                AssetDatabase.DeleteAsset("Assets/_Project/Materials/ForestGround.mat");
                AssetDatabase.CreateAsset(gm, "Assets/_Project/Materials/ForestGround.mat");
                ground.GetComponent<MeshRenderer>().sharedMaterial = gm;
            }

            // невидимый пол под бойцами: рэгдолл падает на него, как на площадку храма
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "ForestFloorCollider"; floor.transform.SetParent(root.transform, false);
            floor.transform.position = new Vector3(0f, -0.05f, FightCenterZ); floor.transform.localScale = new Vector3(9f, 0.1f, 7f);
            Object.DestroyImmediate(floor.GetComponent<MeshRenderer>());

            root.SetActive(false);
            av.forest = root;
        }

        static Texture2D Tex(string path, bool normal)
        {
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return null;
            bool dirty = false;
            if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
            if (ti.maxTextureSize < 4096 && !normal) { ti.maxTextureSize = 4096; dirty = true; }
            if (dirty) ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Texture2D Tile(string path)
        {
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return null;
            if (ti.wrapMode != TextureWrapMode.Repeat || !ti.mipmapEnabled || ti.maxTextureSize != 1024)
            {
                ti.wrapMode = TextureWrapMode.Repeat; ti.mipmapEnabled = true; ti.maxTextureSize = 1024; ti.anisoLevel = 4;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Bounds Bounds(GameObject g)
        {
            Renderer[] rs = g.GetComponentsInChildren<Renderer>(true);
            Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(g.transform.position, Vector3.zero);
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static MeshCollider[] AddColliders(GameObject g)
        {
            List<MeshCollider> l = new List<MeshCollider>();
            foreach (MeshFilter mf in g.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshCollider c = mf.gameObject.AddComponent<MeshCollider>(); c.sharedMesh = mf.sharedMesh; l.Add(c);
            }
            Physics.SyncTransforms();
            return l.ToArray();
        }
    }
}
#endif
