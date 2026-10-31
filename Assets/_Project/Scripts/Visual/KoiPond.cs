using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Карпы кои в пруду: 4 рыбы медленно кружат под полупрозрачной тёмной водой, каждая — по своему эллипсу со своей
    /// скоростью, плавно меняя направление; хвост виляет (рыскание), иногда рыба замирает и снова трогается.
    /// В снег — уходят на дно (невидимы). Раз в 8–20 с карп подходит к самой глади и хватает воздух: от точки расходятся
    /// два светлых кольца и слышен тихий «плюх» (не в дождь — там и так всё рябит).
    /// </summary>
    public class KoiPond : MonoBehaviour
    {
        public Transform[] fish;
        public Vector2 radii = new Vector2(1.15f, 0.62f);
        float[] ang, spd, wob, pause;
        ParticleSystem rings; AudioSource plopSrc; static AudioClip plop; float nextRise;

        void Start()
        {
            int n = fish != null ? fish.Length : 0;
            ang = new float[n]; spd = new float[n]; wob = new float[n]; pause = new float[n];
            for (int i = 0; i < n; i++) { ang[i] = Random.value * 6.28f; spd[i] = Random.Range(0.12f, 0.22f) * (Random.value < 0.5f ? 1f : -1f); wob[i] = Random.value * 6.28f; }
            nextRise = Time.time + Random.Range(5f, 12f);
            rings = BuildRings();
            plopSrc = gameObject.AddComponent<AudioSource>(); plopSrc.playOnAwake = false; plopSrc.spatialBlend = 0.7f; plopSrc.minDistance = 2f; plopSrc.maxDistance = 22f;
            if (plop == null) plop = Plop();
        }

        /// <summary>Кольцо ряби: процедурная текстура (тонкое светлое кольцо с мягкими краями), плоские частицы на глади.</summary>
        ParticleSystem BuildRings()
        {
            const int N = 64;
            Texture2D tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "RippleRing" };
            Color[] px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Exp(-Mathf.Pow((r - 0.86f) / 0.05f, 2f)) + 0.35f * Mathf.Exp(-Mathf.Pow((r - 0.7f) / 0.04f, 2f));
                    px[y * N + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
            tex.SetPixels(px); tex.Apply();
            GameObject g = new GameObject("KoiRings");
            g.transform.SetParent(transform, false);
            g.transform.localPosition = new Vector3(0f, 0.005f, 0f);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main; main.loop = true; main.playOnAwake = false; main.startSpeed = 0f; main.maxParticles = 8;
            main.startLifetime = 1.8f; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(0.75f, 0.8f, 0.9f, 0.4f);
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.1f, 0f, 2f), new Keyframe(1f, 1f, 0.4f, 0f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            ParticleSystemRenderer pr = g.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            Material m = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, renderQueue = 3001 };   // поверх воды
            pr.material = m;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; pr.receiveShadows = false;
            ps.Play();
            return ps;
        }

        /// <summary>«Плюх» карпа: короткий пузырёк — синус, частота которого взлетает 300→900 Гц, и мягкий всплеск шума.</summary>
        static AudioClip Plop()
        {
            const int rate = 22050;
            int n = (int)(rate * 0.22f);
            float[] d = new float[n];
            System.Random rr = new System.Random(9);
            float ph = 0f, lp = 0f, peak = 1e-4f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float f = 300f + 600f * Mathf.Clamp01(t / 0.05f);
                ph += 2f * Mathf.PI * f / rate;
                float x = (float)(rr.NextDouble() * 2.0 - 1.0);
                lp += 0.25f * (x - lp);
                float v = Mathf.Sin(ph) * Mathf.Exp(-t * 45f) + lp * 0.5f * Mathf.Exp(-t * 18f);
                v *= Mathf.Min(1f, t * 2000f);
                d[i] = v; peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            for (int i = 0; i < n; i++) d[i] *= 0.7f / peak;
            AudioClip c = AudioClip.Create("koi_plop", n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }

        void Rise(Transform f)
        {
            if (rings == null) return;
            Vector3 head = f.position + f.forward * 0.12f; head.y = transform.position.y + 0.008f;
            ParticleSystem.EmitParams e = new ParticleSystem.EmitParams();
            e.position = head; e.startSize = Random.Range(0.55f, 0.75f); rings.Emit(e, 1);
            e.startSize *= 0.6f; e.startLifetime = 1.3f; rings.Emit(e, 1);
            if (plopSrc != null && plop != null) { plopSrc.transform.position = head; plopSrc.pitch = Random.Range(0.85f, 1.2f); plopSrc.PlayOneShot(plop, 0.25f); }
        }

        void Update()
        {
            if (fish == null) return;
            bool hide = Weather.Instance != null && Weather.Instance.Snowing;
            float dt = Time.deltaTime, t = Time.time;
            if (t >= nextRise)
            {
                nextRise = t + Random.Range(8f, 20f);
                bool rain = Weather.Instance != null && Weather.Instance.Raining;
                if (!hide && !rain && fish.Length > 0) { Transform f = fish[Random.Range(0, fish.Length)]; if (f != null) Rise(f); }
            }
            for (int i = 0; i < fish.Length; i++)
            {
                Transform f = fish[i];
                if (f == null) continue;
                if (f.gameObject.activeSelf == hide) f.gameObject.SetActive(!hide);
                if (hide) continue;
                // иногда замирает
                if (pause[i] > 0f) pause[i] -= dt; else if (Random.value < dt * 0.05f) pause[i] = Random.Range(1.5f, 4f);
                float s = pause[i] > 0f ? 0.15f : 1f;
                ang[i] += spd[i] * dt * s;
                float k = 0.55f + 0.35f * Mathf.Sin(t * 0.13f + i * 1.7f);   // радиус орбиты «дышит» — не по рельсам
                Vector3 p = new Vector3(Mathf.Cos(ang[i]) * radii.x * k, 0f, Mathf.Sin(ang[i]) * radii.y * k);
                Vector3 tangent = new Vector3(-Mathf.Sin(ang[i]) * radii.x, 0f, Mathf.Cos(ang[i]) * radii.y) * Mathf.Sign(spd[i]);
                wob[i] += dt * (s > 0.5f ? 7f : 2.5f);
                float yaw = Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg + Mathf.Sin(wob[i]) * 9f;
                f.localPosition = new Vector3(p.x, f.localPosition.y, p.z);
                f.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }
    }
}
