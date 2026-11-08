using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Дух полон — сплошное кольцо пламени вокруг ног бойца (2026-11-09, по просьбе владельца: прежнее пламя ки было плоской
    /// полосой в одну линию). Лист FX/ki_ring_fire — стена огня, сделана владельцем в ChatGPT по нашему промту (4×8 = 32 кадра,
    /// монохромная), подготовлена tools/fx/prep_ki_ring.py: кадры бесшовные по горизонтали, жёсткая линия у основания смягчена.
    ///
    /// Стенка-цилиндр на полу (основание на земле), три дуги по 120° — лист повторяется по кругу без шва. Два слоя:
    /// основной и зеркальный со сдвигом на полдуги и на 13 кадров (рисунок не повторяется). Кадры с наплывом — без рывков.
    /// Цвет задаётся кодом (герой — синий, враг — красный, Тень клана — фиолетовый), у основания огонь белее.
    /// Задняя половина кольца сама прячется за бойцом (тест глубины), стенка «ребром» к камере прозрачнее — бока не вспыхивают.
    /// Появляется и гаснет плавно, кольцо медленно поворачивается.
    /// </summary>
    public class KiRing : MonoBehaviour
    {
        const int Cols = 4, Rows = 8, Frames = 32, Arcs = 3, Seg = 16;
        const float Fps = 12f, Height = 0.85f;
        // кольцо — вокруг обеих стоп: центр между стопами, радиус — половина расстояния между ними + запас
        // (владелец: на макете круг был не по центру бойца, ноги в стойке выходили за кольцо)
        const float MinRadius = 0.62f, Margin = 0.28f;
        float radius = MinRadius; Vector3 center; bool hasCenter;
        HeroRigDriver rig;

        Transform target;
        Color tint;
        float k, want = 1f, spin;
        Mesh mesh; Material mat;
        Vector3[] v; Vector2[] uv; Color[] col; Vector3[] normals;
        const int Rings = 4;   // основной: кадр и следующий; зеркальный: кадр и следующий

        public static KiRing Create(Transform follow, Color c)
        {
            Texture2D tex = Resources.Load<Texture2D>("FX/ki_ring_fire");
            Shader add = Shader.Find("Legacy Shaders/Particles/Additive");
            if (tex == null || add == null || follow == null) return null;
            GameObject g = new GameObject("KiRing");
            KiRing r = g.AddComponent<KiRing>();
            r.target = follow; r.tint = c;
            r.rig = follow.GetComponentInChildren<HeroRigDriver>();
            r.mat = new Material(add) { mainTexture = tex };
            if (r.mat.HasProperty("_TintColor")) r.mat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            r.Build();
            g.AddComponent<MeshFilter>().sharedMesh = r.mesh;
            MeshRenderer mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = r.mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            r.spin = Random.value * 360f;
            r.LateUpdate();
            return r;
        }

        /// <summary>Погасить плавно и удалить.</summary>
        public void FadeOut() { want = 0f; }
        public void FadeIn() { want = 1f; }

        void Build()
        {
            int perRing = Arcs * (Seg + 1) * 2;
            v = new Vector3[perRing * Rings]; uv = new Vector2[v.Length]; col = new Color[v.Length]; normals = new Vector3[v.Length];
            int[] tri = new int[Rings * Arcs * Seg * 6];
            int t = 0;
            for (int ring = 0; ring < Rings; ring++)
                for (int a = 0; a < Arcs; a++)
                {
                    int b0 = ring * perRing + a * (Seg + 1) * 2;
                    for (int s = 0; s < Seg; s++)
                    {
                        int i = b0 + s * 2;
                        tri[t++] = i; tri[t++] = i + 1; tri[t++] = i + 2;
                        tri[t++] = i + 1; tri[t++] = i + 3; tri[t++] = i + 2;
                    }
                }
            mesh = new Mesh { name = "KiRing" };
            mesh.MarkDynamic();
            mesh.vertices = v; mesh.uv = uv; mesh.colors = col;
            mesh.triangles = tri;
        }

        void LateUpdate()
        {
            if (target == null || !target.gameObject.activeInHierarchy) { Destroy(gameObject); return; }
            float dt = Time.unscaledDeltaTime;
            k = Mathf.MoveTowards(k, want, dt / (want > 0f ? 0.35f : 0.45f));
            if (k <= 0f && want <= 0f) { Destroy(gameObject); return; }
            spin += 9f * dt;
            if (rig == null || !rig.isActiveAndEnabled) rig = target.GetComponentInChildren<HeroRigDriver>();   // облик мог смениться
            Vector3 want3 = target.position; float wantR = MinRadius;
            if (rig != null && rig.footL != null && rig.footR != null)
            {
                Vector3 a = rig.footL.position, b = rig.footR.position; a.y = b.y = 0f;
                want3 = (a + b) * 0.5f;
                wantR = Mathf.Max(MinRadius, Vector3.Distance(a, b) * 0.5f + Margin);
            }
            // сглажено — кольцо не дёргается за каждым шагом
            float sm = 1f - Mathf.Exp(-10f * dt);
            if (!hasCenter) { center = want3; radius = wantR; hasCenter = true; }
            else { center = Vector3.Lerp(center, want3, sm); radius = Mathf.Lerp(radius, wantR, 1f - Mathf.Exp(-4f * dt)); }
            Vector3 p = center; p.y = 0.01f;   // основание — на полу
            transform.SetPositionAndRotation(p, Quaternion.Euler(0f, spin, 0f));

            Camera cam = Camera.main;
            Vector3 view = cam != null ? cam.transform.forward : Vector3.forward; view.y = 0f;
            view = view.sqrMagnitude > 1e-4f ? view.normalized : Vector3.forward;
            view = Quaternion.Euler(0f, -spin, 0f) * view;   // в локальные координаты кольца

            float ft = Time.unscaledTime * Fps;
            int f0 = (int)ft; float kf = ft - f0; kf = kf * kf * (3f - 2f * kf);
            float breathe = 0.92f + 0.08f * Mathf.Sin(Time.unscaledTime * 2.3f);
            FillRing(0, f0 % Frames, 0f, false, (1f - kf), 1f, view, breathe);
            FillRing(1, (f0 + 1) % Frames, 0f, false, kf, 1f, view, breathe);
            int g0 = f0 + 13;
            FillRing(2, g0 % Frames, 180f / Arcs, true, (1f - kf) * 0.5f, 0.85f, view, breathe);
            FillRing(3, (g0 + 1) % Frames, 180f / Arcs, true, kf * 0.5f, 0.85f, view, breathe);
            mesh.vertices = v; mesh.uv = uv; mesh.colors = col;
            mesh.RecalculateBounds();
        }

        void FillRing(int ring, int frame, float rotDeg, bool mirror, float alpha, float hK, Vector3 view, float breathe)
        {
            int perRing = Arcs * (Seg + 1) * 2;
            float fu = (frame % Cols) / (float)Cols, fv = 1f - (frame / Cols + 1) / (float)Rows;
            float du = 1f / Cols, dv = 1f / Rows;
            float h = Height * hK * breathe * Mathf.Lerp(0.6f, 1f, k);
            Color top = new Color(tint.r, tint.g, tint.b, 1f);
            Color bot = Color.Lerp(top, Color.white, 0.3f);
            for (int a = 0; a < Arcs; a++)
            {
                int b0 = ring * perRing + a * (Seg + 1) * 2;
                for (int s = 0; s <= Seg; s++)
                {
                    float u = s / (float)Seg;
                    float ang = (rotDeg + (a + u) * 360f / Arcs) * Mathf.Deg2Rad;
                    Vector3 n = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    Vector3 basePos = n * radius;
                    int i = b0 + s * 2;
                    v[i] = basePos; v[i + 1] = basePos + Vector3.up * h;
                    float tu = fu + (mirror ? 1f - u : u) * du;
                    uv[i] = new Vector2(tu, fv); uv[i + 1] = new Vector2(tu, fv + dv);
                    // стенка «ребром» к камере (по краям кольца на экране) прозрачнее: иначе там огонь накладывается и вспыхивает
                    float facing = Mathf.Abs(Vector3.Dot(n, view));
                    float a2 = alpha * k * (0.15f + 0.85f * facing);
                    col[i] = new Color(bot.r, bot.g, bot.b, a2);
                    col[i + 1] = new Color(top.r, top.g, top.b, a2);
                }
            }
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (mat != null) Destroy(mat);
        }
    }
}
