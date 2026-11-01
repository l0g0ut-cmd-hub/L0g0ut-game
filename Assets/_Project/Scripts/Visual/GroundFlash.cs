using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>Плоская вспышка на полу (кольцо волны, светящиеся трещины, пятно): растёт от s0 до s1 и гаснет.</summary>
    public class GroundFlash : MonoBehaviour
    {
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        float born, life, s0, s1, spin; Color col; bool hold; int cols = 1, rows = 1, frames = 1; MeshRenderer mr; MeshFilter mf; MaterialPropertyBlock mpb;
        int shownFrame = -1;
        static Mesh quad;
        static readonly Dictionary<string, Mesh[]> sheetQuads = new Dictionary<string, Mesh[]>();

        public static GroundFlash Spawn(string tex, Vector3 pos, float s0, float s1, Color col, float life, float spin, bool hold = false)
        {
            Material m;
            if (!mats.TryGetValue(tex, out m) || m == null)
            {
                Texture2D t = Resources.Load<Texture2D>(tex);
                Shader add = Shader.Find("BladePath/GroundAdditive");   // без «мягких частиц» — иначе у пола не видно
                if (add == null) add = Shader.Find("Legacy Shaders/Particles/Additive");
                if (t == null || add == null) return null;
                m = new Material(add) { mainTexture = t }; mats[tex] = m;
            }
            if (quad == null)
            {
                quad = new Mesh { name = "GroundFlashQuad" };
                quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
                quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                quad.RecalculateBounds();
            }
            GameObject g = new GameObject("GroundFlash");
            g.transform.position = new Vector3(pos.x, 0.026f, pos.z);
            g.transform.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
            MeshFilter filter = g.AddComponent<MeshFilter>(); filter.sharedMesh = quad;
            GroundFlash f = g.AddComponent<GroundFlash>();
            f.mf = filter;
            f.mr = g.AddComponent<MeshRenderer>(); f.mr.sharedMaterial = m; f.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            f.mpb = new MaterialPropertyBlock();
            f.born = Time.time; f.life = Mathf.Max(0.05f, life); f.s0 = s0; f.s1 = s1; f.col = col; f.spin = spin; f.hold = hold;
            f.Tick();
            return f;
        }

        /// <summary>То же, но покадровая анимация из листа cols×rows (кадры слева направо, сверху вниз).</summary>
        public static GroundFlash SpawnSheet(string tex, Vector3 pos, float s0, float s1, Color col, float life, int cols, int rows, int frames)
        {
            GroundFlash f = Spawn(tex, pos, s0, s1, col, life, 0f);
            if (f != null) { f.cols = Mathf.Max(1, cols); f.rows = Mathf.Max(1, rows); f.frames = Mathf.Clamp(frames, 1, f.cols * f.rows); f.Tick(); }
            return f;
        }

        static Mesh[] SheetMeshes(int columns, int rowsCount)
        {
            string key = columns + "x" + rowsCount;
            Mesh[] meshes;
            if (sheetQuads.TryGetValue(key, out meshes)) return meshes;
            meshes = new Mesh[columns * rowsCount];
            float du = 1f / columns, dv = 1f / rowsCount;
            for (int fr = 0; fr < meshes.Length; fr++)
            {
                float u = (fr % columns) * du;
                float v = 1f - (fr / columns + 1) * dv;
                Mesh mesh = new Mesh { name = "GroundFlash_" + key + "_" + fr };
                mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
                mesh.uv = new[] { new Vector2(u, v), new Vector2(u + du, v), new Vector2(u + du, v + dv), new Vector2(u, v + dv) };
                mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                mesh.RecalculateBounds();
                meshes[fr] = mesh;
            }
            sheetQuads.Add(key, meshes);
            return meshes;
        }

        void Update() { Tick(); }

        void Tick()
        {
            float k = Mathf.Clamp01((Time.time - born) / life);
            if (k >= 1f) { Destroy(gameObject); return; }
            float e = 1f - (1f - k) * (1f - k) * (1f - k);
            float s = hold ? Mathf.Lerp(s0, s1, Mathf.Clamp01(k * 6f)) : Mathf.Lerp(s0, s1, e);
            transform.localScale = new Vector3(s, s, 1f);
            if (spin != 0f) transform.Rotate(0f, 0f, spin * Time.deltaTime, Space.Self);
            // трещины горят и медленно остывают; кольцо и пятно — быстро гаснут
            float a = hold ? (k < 0.1f ? k / 0.1f : 1f - Mathf.SmoothStep(0f, 1f, (k - 0.1f) / 0.9f)) : 1f - e;
            mpb.SetColor("_TintColor", new Color(0.5f * Mathf.Lerp(1f, col.r, 0.8f), 0.5f * Mathf.Lerp(1f, col.g, 0.8f), 0.5f * Mathf.Lerp(1f, col.b, 0.8f), 0.5f * a));
            if (frames > 1)
            {
                int fr = Mathf.Min(frames - 1, (int)(k * frames));
                a = 1f - k * k * k * k;   // лист сам гаснет к концу
                mpb.SetColor("_TintColor", new Color(0.5f * Mathf.Lerp(1f, col.r, 0.8f), 0.5f * Mathf.Lerp(1f, col.g, 0.8f), 0.5f * Mathf.Lerp(1f, col.b, 0.8f), 0.5f * a));
                if (shownFrame != fr && mf != null)
                {
                    mf.sharedMesh = SheetMeshes(cols, rows)[fr];
                    shownFrame = fr;
                }
            }
            // Atlas UV coordinates are baked into cached mesh frames.
            // Avoid relying on _MainTex_ST, ignored by some additive shaders.
            mpb.SetVector("_MainTex_ST", new Vector4(1f, 1f, 0f, 0f));
            mr.SetPropertyBlock(mpb);
        }
    }
}
