using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Днём (не на закате, не в непогоду) над цветами у бордюров и перед храмом порхают 3 бабочки-махаона (фото из атласа):
    /// машут крыльями (раскрытые/сложенные кадры + сжатие по ширине), петляют вокруг «своей» клумбы на разной высоте,
    /// шарахаются от бойцов. Плоскость к камере, тельце развёрнуто по ходу полёта.
    /// </summary>
    public class Butterflies : MonoBehaviour
    {
        class B { public Transform t; public MeshFilter mf; public MeshRenderer mr; public Vector3 home, pos, vel; public float ph, rate, seed; }
        readonly List<B> list = new List<B>();
        Mesh open, closed; Material mat; MaterialPropertyBlock mpb;
        FighterController[] fighters;
        bool shown;
        static readonly Vector3[] Homes = { new Vector3(-4.4f, 0f, 0.4f), new Vector3(4.35f, 0f, -0.9f), new Vector3(-1.9f, 0f, 3.45f), new Vector3(2.6f, 0f, 3.5f), new Vector3(-5.2f, 0f, -1.4f) };

        void Start()
        {
            Texture2D tex = Resources.Load<Texture2D>("FX/butterfly");
            Shader sh = Shader.Find("Sprites/Default");
            if (tex == null || sh == null) { enabled = false; return; }
            mat = new Material(sh) { name = "Butterfly", mainTexture = tex };
            open = Quad(0f, 0.5f); closed = Quad(0.5f, 1f);
            mpb = new MaterialPropertyBlock();
            fighters = FindObjectsByType<FighterController>(FindObjectsSortMode.None);
            List<int> idx = new List<int> { 0, 1, 2, 3, 4 };
            for (int i = 0; i < 3; i++)
            {
                int k = idx[Random.Range(0, idx.Count)]; idx.Remove(k);
                GameObject g = new GameObject("Butterfly" + i); g.transform.SetParent(transform, false);
                B b = new B { t = g.transform, mf = g.AddComponent<MeshFilter>(), mr = g.AddComponent<MeshRenderer>(), home = Homes[k], seed = Random.Range(0f, 100f), rate = Random.Range(9f, 12f) };
                b.mr.sharedMaterial = mat; b.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; b.mr.receiveShadows = false;
                b.pos = b.home + Vector3.up * 0.6f;
                g.SetActive(false);
                list.Add(b);
            }
        }

        static Mesh Quad(float u0, float u1)
        {
            Mesh m = new Mesh { name = "ButterflyQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }

        void Update()
        {
            DayNight dn = DayNight.Instance; Weather w = Weather.Instance;
            bool on = dn != null && dn.Day && !dn.Dusk && !(w != null && (w.Raining || w.Snowing || w.Foggy));
            if (on != shown) { shown = on; foreach (B b in list) b.t.gameObject.SetActive(on); }
            if (!on) return;
            float dt = Time.deltaTime, now = Time.time;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 cf = cam.transform.forward, cr = cam.transform.right, cu = cam.transform.up;
            float tone = dn.Dawn ? 0.72f : 0.95f;
            foreach (B b in list)
            {
                // петли вокруг клумбы: цель плывёт по шуму, высота 0,25…1,3 м
                float s = b.seed;
                Vector3 target = b.home + new Vector3((Mathf.PerlinNoise(s, now * 0.35f) - 0.5f) * 2.2f, 0.25f + Mathf.PerlinNoise(s + 7f, now * 0.3f) * 1.05f, (Mathf.PerlinNoise(s + 13f, now * 0.35f) - 0.5f) * 1.6f);
                // от бойцов — прочь
                if (fighters != null)
                    foreach (FighterController f in fighters)
                    {
                        if (f == null) continue;
                        Vector3 d = b.pos - f.transform.position; d.y = 0f;
                        if (d.sqrMagnitude < 1.2f * 1.2f) target += d.normalized * 1.5f + Vector3.up * 0.6f;
                    }
                Vector3 acc = (target - b.pos) * 2.2f - b.vel * 1.6f;
                b.vel += acc * dt;
                b.pos += b.vel * dt;
                b.ph += dt * b.rate * Mathf.PI * 2f;
                float flap = Mathf.Sin(b.ph);
                Vector3 bob = Vector3.up * (Mathf.Abs(flap) * 0.035f);   // при каждом взмахе подскакивает
                b.t.position = b.pos + bob;
                // тельце (вверх в текстуре) — по экранной проекции скорости
                Vector2 sv = new Vector2(Vector3.Dot(b.vel, cr), Vector3.Dot(b.vel, cu));
                float ang = sv.sqrMagnitude > 1e-4f ? Mathf.Atan2(sv.y, sv.x) * Mathf.Rad2Deg - 90f : 0f;
                b.t.rotation = Quaternion.LookRotation(cf, cu) * Quaternion.Euler(0f, 0f, ang);
                bool isOpen = flap > -0.2f;
                b.mf.sharedMesh = isOpen ? open : closed;
                float sc = 0.13f;
                b.t.localScale = new Vector3(sc * (isOpen ? Mathf.Lerp(0.35f, 1f, Mathf.Abs(flap)) : 1f), sc, 1f);
                b.mr.GetPropertyBlock(mpb);
                mpb.SetColor("_Color", new Color(tone, tone, tone, 1f));
                b.mr.SetPropertyBlock(mpb);
            }
        }
    }
}
