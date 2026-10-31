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
        public Vector2 radii = new Vector2(1.0f, 0.5f);
        // берег пруда — та же формула, что в EnvironmentBuilder.AddPond (бобовидный контур), и запас под камни бордюра
        public Vector2 pondR = new Vector2(1.45f, 0.85f);
        public float margin = 0.68f;
        float[] ang, spd, wob, pause;
        ParticleSystem rings; AudioSource plopSrc; static AudioClip plop; float nextRise;
        // лягушки у пруда ночью: «керо-керо» сериями (в дождик — охотнее), не в снег и не днём; по общей очереди звуков
        AudioSource frogSrc; static AudioClip kero; float frogAt; int frogLeft; float frogNext;
        ParticleSystem mist; bool mistOn;   // на рассвете и в туман над водой стелется пар

        void Start()
        {
            int n = fish != null ? fish.Length : 0;
            ang = new float[n]; spd = new float[n]; wob = new float[n]; pause = new float[n];
            for (int i = 0; i < n; i++) { ang[i] = Random.value * 6.28f; spd[i] = Random.Range(0.12f, 0.22f) * (Random.value < 0.5f ? 1f : -1f); wob[i] = Random.value * 6.28f; }
            nextRise = Time.time + Random.Range(5f, 12f);
            rings = BuildRings();
            plopSrc = gameObject.AddComponent<AudioSource>(); plopSrc.playOnAwake = false; plopSrc.spatialBlend = 0.7f; plopSrc.minDistance = 2f; plopSrc.maxDistance = 22f;
            if (plop == null) plop = Plop();
            frogSrc = gameObject.AddComponent<AudioSource>(); frogSrc.playOnAwake = false; frogSrc.spatialBlend = 0.75f; frogSrc.minDistance = 2f; frogSrc.maxDistance = 24f;
            if (kero == null) kero = Kero();
            frogAt = Time.time + Random.Range(15f, 35f);
            mist = BuildMist();
        }

        ParticleSystem BuildMist()
        {
            GameObject g = new GameObject("PondMist");
            g.transform.SetParent(transform, false);
            g.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main; main.loop = true; main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f); main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.2f); main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.startColor = new Color(0.8f, 0.84f, 0.9f, 0.13f); main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 4f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(1.9f, 0.02f, 0.9f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.08f); vel.y = new ParticleSystem.MinMaxCurve(0.01f, 0.04f); vel.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.5f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            var r = g.GetComponent<ParticleSystemRenderer>();
            Material m = new Material(Shader.Find("Sprites/Default")) { name = "PondMist", mainTexture = SoftSprites.Fog };
            r.material = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return ps;
        }

        /// <summary>
        /// «Керо»: кваканье японской квакши — два слога, каждый — пачка коротких звонких импульсов (горловой мешок
        /// щёлкает ~65 раз в секунду), импульс — затухающий тон ~800 Гц («ке» ниже, «ро» выше и короче).
        /// </summary>
        static AudioClip Kero()
        {
            const int rate = 22050;
            int n = (int)(rate * 0.42f);
            float[] d = new float[n];
            System.Random r = new System.Random(77);
            float peak = 1e-4f;
            float[] s0 = { 0f, 0.22f }, len = { 0.16f, 0.13f }, f = { 760f, 980f }, pr = { 62f, 70f };
            for (int k = 0; k < 2; k++)
            {
                float tt = 0f;
                while (tt < len[k])
                {
                    float env = Mathf.Sin(Mathf.PI * tt / len[k]);
                    int i0 = (int)((s0[k] + tt) * rate);
                    float fk = f[k] * (1f - 0.08f * tt / len[k]) * (0.97f + 0.06f * (float)r.NextDouble());
                    for (int j = 0; j < (int)(0.012f * rate) && i0 + j < n; j++)
                    {
                        float u = j / (float)rate;
                        d[i0 + j] += env * Mathf.Sin(2f * Mathf.PI * fk * u) * Mathf.Exp(-u * 380f) * (1f + 0.4f * Mathf.Sin(2f * Mathf.PI * fk * 2f * u));
                    }
                    tt += 1f / pr[k];
                }
            }
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            for (int i = 0; i < n; i++) d[i] *= 0.8f / peak;
            AudioClip c = AudioClip.Create("frog_kero", n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }

        void Frogs(float t)
        {
            if (frogSrc == null || kero == null) return;
            Weather w = Weather.Instance;
            bool night = w != null && !w.Daytime && !w.Snowing;
            if (frogLeft > 0)
            {
                if (t < frogNext) return;
                frogLeft--; frogNext = t + Random.Range(0.42f, 0.6f);
                frogSrc.pitch = Random.Range(0.95f, 1.06f);
                frogSrc.PlayOneShot(kero, Random.Range(0.12f, 0.2f));
                return;
            }
            if (t < frogAt) return;
            frogAt = t + Random.Range(18f, 40f) * (w != null && w.Raining ? 0.6f : 1f);
            if (!night) return;
            int k = Random.Range(2, 6);
            if (!Ambience.Claim(k * 0.5f, 3f, 7f)) { frogAt = t + Random.Range(4f, 8f); return; }
            frogLeft = k; frogNext = t;
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
            if (plopSrc != null && plop != null && Ambience.Claim(0.3f, 3f, 7f)) { plopSrc.transform.position = head; plopSrc.pitch = Random.Range(0.85f, 1.2f); plopSrc.PlayOneShot(plop, 0.25f); }
        }

        /// <summary>Точка (локально от центра пруда) внутри контура воды с запасом margin.</summary>
        bool Inside(Vector3 q)
        {
            float x = q.x / pondR.x, z = q.z / pondR.y;
            float a = Mathf.Atan2(z, x);
            float k = 1f + 0.12f * Mathf.Sin(a * 3f + 0.7f) + 0.07f * Mathf.Sin(a * 5f + 2.1f);
            return Mathf.Sqrt(x * x + z * z) <= k * margin;
        }

        void Update()
        {
            if (fish == null) return;
            bool hide = Weather.Instance != null && Weather.Instance.Snowing;
            float dt = Time.deltaTime, t = Time.time;
            Frogs(t);
            Weather wm = Weather.Instance;
            bool wantMist = wm != null && (wm.Dawn || wm.Foggy) && !wm.Raining;
            if (mist != null && wantMist != mistOn) { mistOn = wantMist; if (mistOn) mist.Play(); else mist.Stop(); }
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
                // рыба целиком (голова и хвост) остаётся в воде: если край выходит за берег под камнями — ближе к центру
                float half = f.localScale.z * 0.5f;
                Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                for (int it = 0; it < 12 && (!Inside(p + fwd * half) || !Inside(p - fwd * half)); it++) p *= 0.9f;
                f.localPosition = new Vector3(p.x, f.localPosition.y, p.z);
                f.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }
    }
}
