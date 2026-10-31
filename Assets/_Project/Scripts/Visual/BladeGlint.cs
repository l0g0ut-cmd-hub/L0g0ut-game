using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Блик луны, бегущий по клинку (как в кино о самураях): звёздочка скользит от хабаки к острию за ~0,45 с.
    /// В стойке — изредка (раз в 6–11 с, когда клинок виден сбоку), в салюте перед боем — обязательно, когда клинок встал перед лицом.
    /// Окровавленный клинок бликует тусклее и краснее. Один аддитивный квад на клинок, всегда лицом к камере.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public class BladeGlint : MonoBehaviour
    {
        public WeaponController weapon;
        static Material mat;
        Transform q;
        MeshRenderer rend;
        MaterialPropertyBlock mpb;
        float nextAt, runT = -1f, runDur = 0.45f, runPow = 1f;
        int saluteId = -1;
        static readonly int TintId = Shader.PropertyToID("_TintColor");

        void Start()
        {
            if (weapon == null) weapon = GetComponent<WeaponController>();
            GameObject g = new GameObject("BladeGlint");
            q = g.transform;
            g.AddComponent<MeshFilter>().sharedMesh = Quad();
            rend = g.AddComponent<MeshRenderer>();
            rend.sharedMaterial = Mat();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rend.receiveShadows = false;
            rend.enabled = false;
            mpb = new MaterialPropertyBlock();
            nextAt = Time.time + Random.Range(3f, 8f);
        }

        void OnDestroy() { if (q == null) return; if (Application.isPlaying) Destroy(q.gameObject); else DestroyImmediate(q.gameObject); }

        /// <summary>Пустить блик сейчас (сила 0…1).</summary>
        public void Run(float power = 1f) { runT = 0f; runPow = power; runDur = Random.Range(0.4f, 0.5f); }

        void LateUpdate()
        {
            if (weapon == null || weapon.bladeRoot == null || q == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Transform b = weapon.bladeRoot;
            FighterController f = weapon.GetComponentInParent<FighterController>();
            bool alive = f == null || !f.Dead;

            // салют: клинок перед лицом — блик снизу вверх
            SwingProfile p = weapon.CurrentProfile;
            if (p != null && p.type == AttackType.Salute && weapon.Phase != SwingPhase.Idle && weapon.SwingId != saluteId && weapon.SwingTime > 0.5f)
            { saluteId = weapon.SwingId; Run(1.2f); }
            // в стойке — изредка, если клинок виден сбоку (не смотрит в камеру)
            if (runT < 0f && alive && weapon.Phase == SwingPhase.Idle && Time.time >= nextAt)
            {
                nextAt = Time.time + Random.Range(6f, 11f);
                float side = 1f - Mathf.Abs(Vector3.Dot(b.forward, (b.position - cam.transform.position).normalized));
                if (side > 0.45f) Run(0.8f);
            }
            if (runT < 0f) { rend.enabled = false; return; }

            runT += Time.deltaTime;
            float u = runT / runDur;
            if (u >= 1f || !alive) { runT = -1f; rend.enabled = false; return; }
            float along = Mathf.Lerp(0.12f, 0.97f, u * u * (3f - 2f * u));
            Vector3 pos = b.position + b.forward * (weapon.bladeLength * along);
            q.position = pos + (cam.transform.position - pos).normalized * 0.03f;   // чуть к камере — не тонет в стали
            q.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up) * Quaternion.Euler(0f, 0f, 12f + u * 30f);
            float pulse = Mathf.Sin(Mathf.PI * u);
            float size = (0.06f + 0.13f * pulse * pulse) * Mathf.Clamp(runPow, 0.5f, 1.3f);
            q.localScale = new Vector3(size, size, size);
            float blood = Juice.Instance != null ? Juice.Instance.BladeBlood(weapon) : 0f;
            Color c = Color.Lerp(new Color(0.78f, 0.88f, 1f), new Color(0.9f, 0.45f, 0.4f), blood * 0.8f) * (pulse * 0.75f * runPow * (1f - blood * 0.55f));
            c.a = 0.5f;
            rend.enabled = true;
            mpb.SetColor(TintId, c);
            rend.SetPropertyBlock(mpb);
        }

        static Mesh quad;
        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "glint_quad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateNormals(); quad.RecalculateBounds();
            return quad;
        }

        static Material Mat()
        {
            if (mat != null) return mat;
            Shader sh = Shader.Find("Legacy Shaders/Particles/Additive");
            if (sh == null) return null;
            mat = new Material(sh) { name = "BladeGlint", mainTexture = Glint() };
            mat.SetColor(TintId, new Color(0.5f, 0.5f, 0.5f, 0.5f));
            return mat;
        }

        /// <summary>Блик: яркое ядро, четыре тонких луча (длинные — по горизонтали и вертикали) и мягкий ореол.</summary>
        static Texture2D Glint()
        {
            const int n = 128;
            Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "blade_glint" };
            Color[] px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float core = Mathf.Exp(-r * r * 60f);
                    float halo = Mathf.Exp(-r * r * 9f) * 0.25f;
                    float rayH = Mathf.Exp(-v * v * 1800f) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(u)), 2.2f);
                    float rayV = Mathf.Exp(-u * u * 1800f) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(v)), 2.6f) * 0.8f;
                    float d1 = (u + v) * 0.7071f, d2 = (u - v) * 0.7071f;
                    float rayD = (Mathf.Exp(-d1 * d1 * 3000f) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(d2) * 1.8f), 3f)
                                + Mathf.Exp(-d2 * d2 * 3000f) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(d1) * 1.8f), 3f)) * 0.3f;
                    float a = Mathf.Clamp01(core + halo + rayH + rayV + rayD);
                    px[y * n + x] = new Color(a, a, a, a);
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }
    }
}
