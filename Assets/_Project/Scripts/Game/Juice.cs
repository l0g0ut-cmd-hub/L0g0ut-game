using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// «Сок» боя без внешних ассетов: процедурные звуки (взмах, удар, лязг блока, гул смерти), искры/кровь при попадании,
    /// светящийся след клинка во время удара. Всё создаётся в рантайме из кода.
    /// </summary>
    public class Juice : MonoBehaviour
    {
        public static Juice Instance { get; private set; }

        AudioSource[] voices;
        int voiceIdx;
        AudioClip kiaiA, kiaiB, gruntA, gruntB, dieA, dieB; float[] kiaiAt = new float[4];
        AudioClip swish, hit, hitHeavy, clang, boom, drum, heart, whoosh, step, growl, stepWet, stepSnow, danger, stab, squelch;
        AudioSource stepVoice;
        ParticleSystem footFx;
        ParticleSystem petals;
        AudioSource ambient, crickets;
        Material fxMat, softMat, splatMat, fogMat, dustMat, crackMat, streakMat, poolMat, burstMat;
        const int MaxDecals = 220;
        readonly List<Decal> decals = new List<Decal>();
        int decalIdx;
        // Per-round blood particle sources are separate from pooled ground decals.
        // They must not survive a reset or spawn new stains in the next round.
        readonly List<GameObject> transientBloodFx = new List<GameObject>();
        int battleFxGeneration;
        void TrackBloodFx(GameObject source)
        {
            if (source == null) return;
            if (transientBloodFx.Count > 256)
                transientBloodFx.RemoveAll(fx => fx == null);
            transientBloodFx.Add(source);
        }
        struct Decal { public Transform t; public Renderer r; public float born, life, size, grow; }
        MaterialPropertyBlock mpb;

        class Tracked
        {
            public WeaponController w; public SwingPhase last; public TrailRenderer trail;
            public FighterController f; public ParticleSystem aura; public Renderer[] blade; public float blood, bloodAge;
            public float stepT; public bool wasGrounded = true;
            public int bloodSteps;   // наступил в лужу крови — столько ещё кровавых отпечатков
            public float dripAt;     // тяжело ранен — капает кровь
            public float scrapeAt;   // остриё чиркнуло по камню (низкая подсечка) — искры не чаще раза в 60 мс
        }
        readonly List<Tracked> tracked = new List<Tracked>();

        void Awake()
        {
            Instance = this;
            LightBraziers();
            voices = new AudioSource[6];
            for (int i = 0; i < voices.Length; i++) { voices[i] = gameObject.AddComponent<AudioSource>(); voices[i].spatialBlend = 0f; voices[i].playOnAwake = false; }
            swish = Swish(); hit = Thump(0.22f, 140f, 55f, 0.55f); hitHeavy = Thump(0.34f, 100f, 38f, 0.9f); clang = Clang(); boom = Thump(1.0f, 70f, 28f, 0.8f);
            drum = Drum(); heart = Heart(); whoosh = Swish(); step = Step(); growl = Growl(); stepWet = StepWet(); stepSnow = StepSnow(); danger = Danger_(); stab = Stab(); squelch = Squelch();
            kiaiA = Kiai(31, 1f, 1150f); kiaiB = Kiai(32, 0.86f, 1500f);
            // боль: короткое глухое «ых» (гласная «у/о»); смерть — долгий выдох с падающим тоном
            gruntA = Kiai(41, 0.82f, 880f, 470f, 0.24f); gruntB = Kiai(42, 0.72f, 920f, 500f, 0.24f);
            dieA = Kiai(43, 0.78f, 1000f, 620f, 0.75f); dieB = Kiai(44, 0.68f, 1050f, 640f, 0.75f);
            // записанные звуки (Resources/Audio, CC0 Kenney и CC BY 4.0 Oculus Audio Pack — см. THIRD_PARTY_SOUNDS.md);
            // если файла нет — остаётся синтезированный
            swish = Sampled(swish, "swish", 6); whoosh = Sampled(whoosh, "swish", 6);
            hit = Sampled(hit, "hit", 4); hitHeavy = Sampled(hitHeavy, "hitheavy", 3); stab = Sampled(stab, "stab", 1);
            clang = Sampled(clang, "clang", 10);   // звон клинка о клинок (StarNinjas, CC0)
            step = Sampled(step, "step", 5); stepSnow = Sampled(stepSnow, "stepsnow", 5);
            stepVoice = gameObject.AddComponent<AudioSource>(); stepVoice.spatialBlend = 0f; stepVoice.playOnAwake = false;   // шаги не отнимают голоса у ударов
            ambient = gameObject.AddComponent<AudioSource>();
            ambient.clip = Wind(); ambient.loop = true; ambient.volume = 0.22f; ambient.spatialBlend = 0f; ambient.Play();
            crickets = gameObject.AddComponent<AudioSource>();   // ночные сверчки в саду храма; в дождь замолкают
            crickets.clip = Crickets();   // свои сверчки/цикады — не трогаем (просьба 2026-11-07) crickets.loop = true; crickets.volume = 0f; crickets.spatialBlend = 0f; crickets.Play();
            Shader sh = Shader.Find("Sprites/Default");
            if (sh != null)
            {
                fxMat = new Material(sh);
                softMat = new Material(sh); softMat.mainTexture = SoftSprites.Disc;
                splatMat = new Material(sh); Texture2D bs = Resources.Load<Texture2D>("FX/blood_splat"); splatMat.mainTexture = bs != null ? bs : SoftSprites.Splat;
                fogMat = new Material(sh); fogMat.mainTexture = SoftSprites.Fog;
                Texture2D dp = Resources.Load<Texture2D>("FX/dust_puff");   // клубы пыли из атласа (фото-облачко)
                dustMat = new Material(sh); dustMat.mainTexture = dp != null ? dp : SoftSprites.Fog;
                // варианты пятен (атлас тушью, в игре — тон крови): вытянутая полоса по ходу полёта и растёкшаяся лужа
                Texture2D bst = Resources.Load<Texture2D>("FX/blood_streak"), bpl = Resources.Load<Texture2D>("FX/blood_pool");
                streakMat = bst != null ? new Material(sh) { mainTexture = bst } : splatMat;
                poolMat = bpl != null ? new Material(sh) { mainTexture = bpl } : splatMat;
                // брызги удара: 16 кадров (атлас 36, исток — левый край по центру), запасные — 4 кадра (исток — левый нижний угол)
                Texture2D bb = Resources.Load<Texture2D>("FX/blood_spray");
                spray16 = bb != null;
                if (bb == null) bb = Resources.Load<Texture2D>("FX/blood_burst");
                if (bb != null) burstMat = new Material(sh) { mainTexture = bb };
                Texture2D cr = Resources.Load<Texture2D>("FX/ground_crack");
                if (cr != null) { crackMat = new Material(sh); crackMat.mainTexture = cr; }
            }
            mpb = new MaterialPropertyBlock();
        }

        public void Track(WeaponController w)
        {
            if (w == null) return;
            Tracked t = new Tracked(); t.w = w; t.last = SwingPhase.Idle;
            if (w.bladeRoot != null)
            {
                FighterController owner = w.GetComponentInParent<FighterController>();
                bool isPlayer = DuelManager.Instance != null && DuelManager.Instance.player == owner;
                WeaponTrail.Attach(w, isPlayer ? new Color(0.55f, 0.8f, 1f) : new Color(0.85f, 0.35f, 1f), isPlayer ? "trail_ice" : "trail_violet");   // ледяной след героя, фиолетовый — врага
                if (!isPlayer && owner != null) MakeEyes(owner);
                if (w.GetComponent<BladeGlint>() == null) w.gameObject.AddComponent<BladeGlint>().weapon = w;   // лунный блик по клинку
            }
            if (false && fxMat != null && w.bladeRoot != null)   // старый след только за остриём — заменён WeaponTrail
            {
                GameObject g = new GameObject("BladeTrail");
                g.transform.SetParent(w.bladeRoot, false);
                g.transform.localPosition = new Vector3(0f, 0f, w.bladeLength * 0.96f);
                TrailRenderer tr = g.AddComponent<TrailRenderer>();
                tr.material = fxMat;
                tr.time = 0.20f;
                tr.minVertexDistance = 0.02f;
                tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.12f), new Keyframe(1f, 0f));
                Gradient gr = new Gradient();
                gr.SetKeys(new GradientColorKey[] { new GradientColorKey(new Color(1f, 0.95f, 0.85f), 0f), new GradientColorKey(new Color(0.7f, 0.8f, 1f), 1f) },
                           new GradientAlphaKey[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
                tr.colorGradient = gr;
                tr.alignment = LineAlignment.View;
                tr.emitting = false;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                t.trail = tr;
            }
            t.f = w.GetComponentInParent<FighterController>();
            if (w.bladeRoot != null)
            {
                // только стальное лезвие (не рукоять, не цуба и не след клинка)
                List<Renderer> steel = new List<Renderer>();
                foreach (Renderer r in w.bladeRoot.GetComponentsInChildren<Renderer>(true))
                    if (r is MeshRenderer && r.sharedMaterial != null && r.sharedMaterial.name.Contains("Steel")) steel.Add(r);
                t.blade = steel.ToArray();
            }
            if (t.f != null && softMat != null) t.aura = MakeAura(t.f);
            tracked.Add(t);
        }

        /// <summary>
        /// Аура полного духа: редкие тонкие искры поднимаются от корпуса (аддитивные, цвет владельца).
        /// Раньше это были крупные диски у ног — выглядели как пузыри, заменено.
        /// </summary>
        ParticleSystem MakeAura(FighterController f)
        {
            GameObject g = new GameObject("KiAura");
            g.transform.SetParent(f.transform, false);
            g.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = true; main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.025f);
            bool isPlayer = DuelManager.Instance != null && DuelManager.Instance.player == f;
            main.startColor = isPlayer ? new Color(0.55f, 0.85f, 1f, 1f) : new Color(0.85f, 0.45f, 1f, 1f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;
            var em = ps.emission; em.rateOverTime = 0f;
            var sp = ps.shape; sp.shapeType = ParticleSystemShapeType.Sphere; sp.radius = 0.32f; sp.radiusThickness = 0.15f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f); vel.y = new ParticleSystem.MinMaxCurve(0.5f, 0.9f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);   // все оси в одном режиме (две константы)
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            ParticleSystemRenderer r = g.GetComponent<ParticleSystemRenderer>();
            Shader add = Shader.Find("Legacy Shaders/Particles/Additive");
            if (add != null)
            {
                Material m = new Material(add);
                m.mainTexture = SoftSprites.Fog;
                m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
                r.material = m;
            }
            else r.material = softMat;
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 1.5f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        readonly List<Material> eyeMats = new List<Material>();

        /// <summary>Горящие глаза нежити: два аддитивных огонька на кости головы (пропадают вместе с головой).</summary>
        void MakeEyes(FighterController f)
        {
            HeroRigDriver rig = f.GetComponentInChildren<HeroRigDriver>();
            if (rig == null || rig.head == null) return;
            Shader sh = Shader.Find("Legacy Shaders/Particles/Additive");
            if (sh == null) return;
            Material m = new Material(sh);
            m.mainTexture = SoftSprites.Disc;
            m.SetColor("_TintColor", new Color(0.8f, 0.26f, 0.95f, 0.7f));
            eyeMats.Add(m);
            Mesh q = QuadMesh();
            for (int s = -1; s <= 1; s += 2)
            {
                GameObject g = new GameObject("Eye");
                g.transform.SetParent(rig.head, true);
                g.transform.position = rig.head.position + f.transform.forward * 0.1f + f.transform.right * (0.034f * s) + Vector3.up * rig.eyeHeight;
                g.transform.localScale = Vector3.one * 0.05f / Mathf.Max(0.01f, rig.head.lossyScale.x);
                g.AddComponent<MeshFilter>().sharedMesh = q;
                MeshRenderer r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                GlowSprite gs = g.AddComponent<GlowSprite>(); gs.flicker = 0.25f;
                gs.noHaze = true;   // в дождь/туман не раздувать: два глаза сливались в одно белое пятно
            }
        }

        public void SetAuraColor(FighterController f, Color c)
        {
            for (int i = 0; i < eyeMats.Count; i++) eyeMats[i].SetColor("_TintColor", new Color(c.r, c.g, c.b, 0.7f));
            for (int i = 0; i < tracked.Count; i++)
                if (tracked[i].f == f && tracked[i].aura != null) { var m = tracked[i].aura.main; c.a = 0.7f; m.startColor = c; }
        }

        /// <summary>Клинок в крови: каждое попадание окрашивает лезвие, к новому раунду оно снова чистое.</summary>
        public void BloodyBlade(WeaponController w, float amount)
        {
            for (int i = 0; i < tracked.Count; i++) if (tracked[i].w == w) { tracked[i].blood = Mathf.Clamp01(tracked[i].blood + amount); tracked[i].bloodAge *= 0.25f; }   // свежая кровь поверх подсохшей
        }

        /// <summary>Сколько крови на клинке (0…1).</summary>
        public float BladeBlood(WeaponController w)
        {
            for (int i = 0; i < tracked.Count; i++) if (tracked[i].w == w) return tracked[i].blood;
            return 0f;
        }

        public void ShakeBlood(WeaponController w)
        {
            for (int i = 0; i < tracked.Count; i++) if (tracked[i].w == w) tracked[i].blood *= 0.35f;
        }

        /// <summary>Брызги с клинка при тибури: короткая дуга капель и пятна на земле.</summary>
        public void Drip(Vector3 pos)
        {
            Vector3 dir = Camera.main != null ? Vector3.Cross(Vector3.up, Camera.main.transform.forward).normalized : Vector3.right;
            Spray(pos, (dir + Vector3.down * 0.6f).normalized, 40, 3.2f, 30f);
            for (int i = 0; i < 6; i++)
                AddDecal(new Vector3(pos.x, 0.012f, pos.z) + new Vector3(dir.x, 0f, dir.z) * Random.Range(0.3f, 1.2f), Random.Range(0.06f, 0.14f), new Vector2(dir.x, dir.z), 40f, Random.Range(0.1f, 0.35f));
        }

        public void CleanBlades() { for (int i = 0; i < tracked.Count; i++) tracked[i].blood = 0f; }

        /// <summary>Клинок по доспеху: горсть искр и сухой металлический «тик» поверх крови.</summary>
        public void ArmorSparks(Vector3 point, Vector3 normal)
        {
            Sparks(point, normal, 14, 0.55f);
            Play(clang, 0.35f, Random.Range(1.5f, 1.8f));
            SparkLight(point, new Color(1f, 0.7f, 0.38f), 1.3f, 2f, 0.08f);
        }

        /// <summary>Лужа крови под павшим телом: медленно растекается (несколько пятен разного размера с задержкой).</summary>
        public void BloodPool(Vector3 pos)
        {
            AddDecal(pos, 0.55f, Vector2.zero, 60f, 0.1f, 4.5f);
            AddDecal(pos + new Vector3(Random.Range(-0.2f, 0.2f), 0f, Random.Range(-0.2f, 0.2f)), 0.4f, Vector2.zero, 60f, 0.6f, 5f);
            AddDecal(pos + new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f)), 0.3f, Vector2.zero, 60f, 1.2f, 6f);
        }

        void Update()
        {
            UpdateSparkLight();
            if (crickets != null)
            {
                bool rain = Weather.Instance != null && (Weather.Instance.Raining || Weather.Instance.Snowing || Weather.Instance.Daytime);   // днём сверчков нет — цикады (Cicadas)
                crickets.volume = Mathf.MoveTowards(crickets.volume, rain ? 0f : 0.09f, Time.unscaledDeltaTime * 0.05f);
            }
            for (int i = 0; i < tracked.Count; i++)
            {
                Tracked t = tracked[i];
                if (t.w == null) continue;
                SwingPhase ph = t.w.Phase;
                if (ph != t.last)
                {
                    if (ph == SwingPhase.Strike) { SwingSound(t.w.CurrentProfile); KiaiFor(i, t.w.CurrentProfile); if (t.trail != null) { t.trail.Clear(); t.trail.emitting = true; } }
                    if ((ph == SwingPhase.Recovery || ph == SwingPhase.Idle) && t.trail != null) t.trail.emitting = false;
                    t.last = ph;
                }
                // остриё чиркает по камню арены на низкой дуге: искры и скрежет
                if (t.w.bladeRoot != null && t.w.CurrentProfile != null && !t.w.CurrentProfile.scripted && (ph == SwingPhase.Strike || ph == SwingPhase.FollowThrough) && Time.time >= t.scrapeAt)   // не в добиваниях
                {
                    Vector3 tip = t.w.bladeRoot.position + t.w.bladeRoot.forward * t.w.bladeLength;
                    if (tip.y < 0.05f && tip.y > -0.3f)
                    {
                        t.scrapeAt = Time.time + 0.06f;
                        Vector3 at = new Vector3(tip.x, 0.01f, tip.z);
                        Sparks(at, Vector3.up, 6, 0.45f);
                        Play(clang, 0.16f, Random.Range(1.7f, 2.1f));
                    }
                }
                if (t.f != null)
                {
                    // шаги — от постановки стоп (HeroRigDriver → Footstep); здесь приземление после прыжка
                    if (t.f.Grounded && !t.wasGrounded)
                    {
                        Play(hit, 0.35f, 0.6f);
                        Dust(t.f.transform.position + Vector3.up * 0.05f, Vector3.up, 12);
                    }
                    t.wasGrounded = t.f.Grounded;
                }
                // тяжело ранен: кровь капает на камень (чем меньше здоровья, тем чаще) — след раненого на арене
                if (t.f != null && !t.f.Dead && DuelManager.Instance != null && DuelManager.Instance.State == DuelState.Fighting)
                {
                    float h = DuelManager.Instance.Hp01(t.f);
                    if (h < 0.4f && Time.time >= t.dripAt)
                    {
                        t.dripAt = Time.time + Mathf.Lerp(0.5f, 1.8f, h / 0.4f) * Random.Range(0.7f, 1.3f);
                        Vector3 p0 = t.f.transform.position + t.f.transform.right * Random.Range(-0.18f, 0.18f) + t.f.transform.forward * Random.Range(-0.1f, 0.15f);
                        AddDecal(new Vector3(p0.x, 0.012f, p0.z), Random.Range(0.035f, 0.07f), Vector2.zero, 30f, 0.25f);
                    }
                }
                if (t.aura != null && t.f != null)
                {
                    var em = t.aura.emission;
                    bool rage = DuelManager.Instance != null && DuelManager.Instance.Enraged && t.f == DuelManager.Instance.enemy;
                    em.rateOverTime = t.f.Dead ? 0f : (rage ? 45f : (t.f.KiFull ? 18f : 0f));
                }
                if (t.blade != null && mpb != null)
                {
                    // кровь на клинке (шейдер BladePath/Blade — по маске от острия; старый Standard — оттенок всего клинка):
                    // подсыхает за ~25 с, под дождём медленно смывается
                    t.bloodAge += Time.deltaTime;
                    if (t.blood > 0f && Weather.Instance != null && Weather.Instance.Raining) t.blood = Mathf.Max(0f, t.blood - Time.deltaTime * 0.012f);
                    float dry = Mathf.Clamp01(t.bloodAge / 25f);
                    Color c = Color.Lerp(Color.white, new Color(0.5f, 0.06f, 0.06f), t.blood * 0.85f);
                    for (int k = 0; k < t.blade.Length; k++)
                    {
                        Renderer r = t.blade[k];
                        if (r == null) continue;
                        r.GetPropertyBlock(mpb);
                        if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Blood")) { mpb.SetFloat("_Blood", t.blood); mpb.SetFloat("_BloodDry", dry * dry * (3f - 2f * dry)); }
                        else mpb.SetColor("_Color", c);
                        r.SetPropertyBlock(mpb);
                    }
                }
            }
        }

        /// <summary>Удар тайко: начало раунда, «БОЙ!».</summary>
        public void Taiko(float vol = 1f, float pitch = 1f) { Play(drum, vol, pitch); }
        /// <summary>Рык нежити перед боем.</summary>
        // рык отключён везде (2026-11-06): синтезированное жужжание 48 Гц звучало как пердёж; вызовы оставлены — звук можно вернуть здесь
        public void Growl(float pitch = 1f) { }
        /// <summary>Порыв лепестков сакуры (сильный удар, KO).</summary>
        public void PetalBurst(int n)
        {
            if (petals == null) { GameObject g = GameObject.Find("Petals"); if (g != null) petals = g.GetComponent<ParticleSystem>(); }
            if (Weather.LeafMode == 0) return;   // сильный снег — листьев нет
            if (Weather.LeafMode == 3) n = Mathf.Max(1, n / 4);   // последние листья
            n = Mathf.Clamp(Mathf.RoundToInt(n * MobileVfxBudget.ParticleScale), 1, 64);
            if (petals != null) petals.Emit(n);
        }
        /// <summary>Стук сердца на низком здоровье.</summary>
        public void Heartbeat() { Play(heart, 0.7f, 1f); }
        /// <summary>Опасная атака (危): низкий удар гонга с металлическим призвуком.</summary>
        public void Danger() { Play(danger, 0.9f, 1f); }
        /// <summary>Рывок.</summary>
        public void DashSound() { Play(whoosh, 0.45f, 0.6f); }

        /// <summary>Рывок поднимает с камня опавшие листья (или лепестки сакуры): горсть взлетает из-под ног и кружит позади.</summary>
        public void LeafKick(Vector3 pos, Vector3 dir)
        {
            int mode = Weather.LeafMode;
            if (mode == 0 || (Weather.Instance != null && Weather.Instance.Raining)) return;   // мокрые листья не взлетают
            if (petals == null) { GameObject g = GameObject.Find("Petals"); if (g != null) petals = g.GetComponent<ParticleSystem>(); }
            if (petals == null) return;
            bool sakura = mode == 2;
            int n = mode == 3 ? 2 : Random.Range(4, 8);
            ParticleSystem.EmitParams e = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                Vector2 c = Random.insideUnitCircle * 0.25f;
                e.position = pos + new Vector3(c.x, 0.04f, c.y);
                e.velocity = -dir.normalized * Random.Range(0.6f, 1.6f) + new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(0.8f, 1.8f), Random.Range(-0.5f, 0.5f));
                e.startSize = sakura ? Random.Range(0.035f, 0.06f) : Random.Range(0.07f, 0.12f);
                e.startLifetime = Random.Range(2.2f, 3.4f);
                e.startColor = sakura ? Color.Lerp(new Color(1f, 0.78f, 0.86f), new Color(1f, 0.93f, 0.96f), Random.value) : Color.Lerp(new Color(0.75f, 0.5f, 0.45f), new Color(1f, 0.82f, 0.75f), Random.value);
                petals.Emit(e, 1);
            }
        }

        /// <summary>Боец врезался в жаровню: глухой бронзовый удар, жаровня качается, из чаши взметается сноп искр.</summary>
        public void ObstacleBump(ArenaObstacle o, Vector3 vel)
        {
            if (o == null) return;
            Play(hitHeavy, 0.55f, 0.7f); Play(clang, 0.28f, 0.5f);
            Wobble wb = o.GetComponent<Wobble>(); if (wb == null) wb = o.gameObject.AddComponent<Wobble>();
            wb.Kick(vel, Mathf.Clamp(vel.magnitude * 2.2f, 4f, 10f));
            foreach (ParticleSystem ps in o.GetComponentsInChildren<ParticleSystem>())
            {
                if (ps.name != "Embers") continue;
                ParticleSystem.EmitParams e = new ParticleSystem.EmitParams();
                for (int i = 0; i < 28; i++)
                {
                    e.position = ps.transform.position + Random.insideUnitSphere * 0.12f;
                    e.velocity = new Vector3(Random.Range(-1f, 1f), Random.Range(1.6f, 3.6f), Random.Range(-1f, 1f)) + vel.normalized * 0.8f;
                    e.startLifetime = Random.Range(0.8f, 1.8f);
                    ps.Emit(e, 1);
                }
            }
        }

        /// <summary>Киай на тяжёлых ударах: сверху и в прыжке — через раз, добивание и натиск — всегда; не чаще раза в 1,5 с у бойца.</summary>
        /// <summary>Голос только у героя: синтезированные крик и стоны противников (ярость, боль, смерть) звучали как пердёж — убраны (2026-11-06).</summary>
        bool HeroVoice(int who)
        {
            DuelManager dm = DuelManager.Instance;
            return who >= 0 && who < tracked.Count && dm != null && tracked[who].f != null && tracked[who].f == dm.player;
        }
        void KiaiFor(int who, SwingProfile p)
        {
            if (p == null || who < 0 || who >= kiaiAt.Length || !HeroVoice(who)) return;
            bool db = p.type.ToString().StartsWith("Deathblow");
            bool heavy = p.type == AttackType.Overhead || p.type == AttackType.JumpStrike;
            if (!(db || p.type == AttackType.Charge || (heavy && Random.value < 0.5f))) return;
            if (Time.time < kiaiAt[who]) return;
            kiaiAt[who] = Time.time + 1.5f;
            Play(who == 0 ? kiaiA : kiaiB, db ? 0.42f : 0.32f, Random.Range(0.96f, 1.04f));
        }

        /// <summary>Голос раненого: на заметном попадании — глухое «ых» (через раз), на смертельном — долгий выдох. Голос — по бойцу.</summary>
        void HurtVoice(Transform at, float strength, bool kill)
        {
            if (at == null) return;
            FighterController f = at.GetComponentInParent<FighterController>();
            int who = -1;
            for (int i = 0; i < tracked.Count; i++) if (tracked[i].f == f && f != null) { who = i; break; }
            if (who < 0 || !HeroVoice(who)) return;
            if (kill) { Play(who == 0 ? dieA : dieB, 0.45f, Random.Range(0.97f, 1.03f)); return; }
            if (strength < 0.5f || Random.value > 0.6f) return;
            Play(who == 0 ? gruntA : gruntB, 0.36f, Random.Range(0.94f, 1.06f));
        }

        /// <summary>
        /// Свист клинка по характеру удара: укол — короткое тонкое «сс» (высоко и тихо), горизонтальный — ровный свист,
        /// рубящий сверху — тяжёлое низкое «вжух» с гулким подголоском; чем быстрее рабочая фаза удара, тем выше тон.
        /// Приветствие и шар духа не свистят.
        /// </summary>
        void SwingSound(SwingProfile p)
        {
            if (p == null) { Play(swish, 0.55f, Random.Range(0.9f, 1.15f)); return; }
            if (p.ranged || p.type == AttackType.Salute) return;
            float pitch = 1f, vol = 0.55f, low = 0f;
            switch (p.type)
            {
                case AttackType.Thrust: pitch = 1.38f; vol = 0.36f; break;
                case AttackType.Overhead: case AttackType.JumpStrike: pitch = 0.82f; vol = 0.62f; low = 0.3f; break;
                case AttackType.LegSweep: pitch = 0.9f; vol = 0.5f; break;
                case AttackType.Rising: pitch = 1.06f; break;
                case AttackType.Charge: pitch = 0.7f; vol = 0.4f; break;
                case AttackType.Mikiri: pitch = 1.25f; vol = 0.4f; break;
                default: if (p.type.ToString().StartsWith("Deathblow")) { pitch = 0.9f; vol = 0.6f; low = 0.25f; } break;
            }
            // быстрее рабочая фаза — выше и резче (0,14 с — обычный удар)
            pitch *= Mathf.Clamp(Mathf.Pow(0.14f / Mathf.Max(0.03f, p.strikeTime), 0.18f), 0.9f, 1.2f);
            Play(swish, vol, pitch * Random.Range(0.95f, 1.05f));
            if (low > 0f) Play(whoosh, low, pitch * 0.5f);
        }
        /// <summary>Добивание: клинок входит в грудь.</summary>
        AudioClip[] diceCup, diceRoll, coins;
        AudioClip[] LoadSet(string name, int n)
        {
            List<AudioClip> l = new List<AudioClip>(n);
            for (int i = 1; i <= n; i++) { AudioClip c = Resources.Load<AudioClip>("Audio/" + name + "_" + i); if (c != null) l.Add(c); }
            return l.ToArray();
        }
        void PlayOne(AudioClip[] set, float vol)
        {
            if (set == null || set.Length == 0 || voices == null) return;
            AudioSource a = voices[voiceIdx]; voiceIdx = (voiceIdx + 1) % voices.Length;
            a.pitch = Random.Range(0.96f, 1.04f); a.volume = vol; a.clip = set[Random.Range(0, set.Length)]; a.Play();
        }
        /// <summary>Кость пути на карте: стук кубика по столу.</summary>
        public void DiceSound() { if (diceRoll == null) diceRoll = LoadSet("dice", 3); PlayOne(diceRoll, 0.75f); }
        /// <summary>Чо-хан: кости гремят в стаканчике.</summary>
        public void DiceCupSound() { if (diceCup == null) diceCup = LoadSet("dicecup", 3); PlayOne(diceCup, 0.8f); }
        /// <summary>Покупка: звон монет.</summary>
        public void CoinSound() { if (coins == null) coins = LoadSet("coins", 2); PlayOne(coins, 0.6f); }

        public void StabSound() { Play(stab, 1f, Random.Range(0.94f, 1.04f)); }
        /// <summary>Добивание: клинок выдернут.</summary>
        public void SquelchSound() { Play(squelch, 0.95f, Random.Range(0.95f, 1.05f)); }

        readonly Dictionary<AudioClip, AudioClip[]> sampled = new Dictionary<AudioClip, AudioClip[]>();
        /// <summary>Набор записанных вариантов name_1…name_n; первый вариант — ключ, при игре берётся случайный.</summary>
        AudioClip Sampled(AudioClip synth, string name, int n)
        {
            List<AudioClip> l = new List<AudioClip>(n);
            for (int i = 1; i <= n; i++) { AudioClip c = Resources.Load<AudioClip>("Audio/" + name + "_" + i); if (c != null) l.Add(c); }
            if (l.Count == 0) return synth;
            AudioClip[] arr = l.ToArray();
            sampled[arr[0]] = arr;
            return arr[0];
        }
        /// <summary>Случайный вариант записи. Высоту тона запись терпит хуже синтеза: уход от 1 ужимается вдвое
        /// (синтезированный низкий «вжух» на 0,4 и звучал как пердёж).</summary>
        AudioClip Pick(AudioClip c, ref float pitch)
        {
            AudioClip[] arr;
            if (c == null || !sampled.TryGetValue(c, out arr)) return c;
            pitch = Mathf.Clamp(1f + (pitch - 1f) * 0.45f, 0.72f, 1.4f);
            return arr[Random.Range(0, arr.Length)];
        }

        void Play(AudioClip c, float vol, float pitch)
        {
            c = Pick(c, ref pitch);
            if (c == null || voices == null) return;
            AudioSource a = voices[voiceIdx]; voiceIdx = (voiceIdx + 1) % voices.Length;
            a.pitch = pitch; a.volume = vol; a.clip = c; a.Play();
        }

        /// <summary>Попадание. dir — направление удара (скорость клинка), strength ~0.4…2 (урон/крит/убийство).</summary>
        public void OnHit(Vector3 point, Vector3 normal, Vector3 dir, float strength, bool blocked, bool kill, Transform bleedAt)
        {
            if (blocked)
            {
                Play(clang, 0.8f, Random.Range(0.9f, 1.2f));
                Sparks(point, normal, 22, 0.8f);
                Flash(point, 0.22f, new Color(1f, 0.82f, 0.5f, 0.8f), 0.07f, false);
                Sheet("fx_clash", point, 0.8f, 0.24f, new Color(1f, 0.95f, 0.85f, 0.9f));
                // A single brief gold burst emphasizes a clean steel-on-steel
                // contact without making every block a full-screen explosion.
                if (DuelManager.Gfx == 1 && MobileVfxBudget.Level < 2)
                {
                    FxSprite metal = FxSprite.Spawn("sparks_gold", point, 0.38f,
                        6, 1, 6, 24f, false, 0.22f);
                    if (metal != null)
                        metal.tint = new Color(1f, 0.73f, 0.38f, 0.57f);
                }
                return;
            }
            Vector3 hd = dir;
            Sheet("fx_slash_hit", point, 0.9f + 0.35f * Mathf.Clamp(strength, 0f, 2f), 0.26f, new Color(1f, 0.96f, 0.92f, 0.85f), () => hd);   // полумесяц рассечения по ходу удара
            Play(strength > 1f ? hitHeavy : hit, 0.95f, Random.Range(0.92f, 1.08f));
            SlashFlash(point, dir, strength);
            Blood(point, normal, dir, strength, kill);
            if (strength > 0.6f) Shreds(point, dir, strength);
            HurtVoice(bleedAt, strength, kill);
            if (bleedAt != null) Bleed(bleedAt, Mathf.Lerp(1.2f, 3.5f, Mathf.Clamp01(strength - 0.4f)), strength);
            if (kill) { Play(boom, 1f, 1f); PetalBurst(60); }
            else if (strength > 1.2f) PetalBurst(12);
        }

        // ---------------------------------------------------------------- лоскуты

        ParticleSystem shreds;
        /// <summary>
        /// Клинок рассёк ткань: несколько тёмных лоскутков одежды вылетают по ходу удара, кувыркаясь, медленно планируют
        /// (сопротивление воздуха, трепет) и ложатся на камень. Одна общая система — без мусора на каждый удар.
        /// </summary>
        void Shreds(Vector3 pos, Vector3 dir, float strength)
        {
            if (shreds == null) shreds = BuildShreds();
            if (shreds == null) return;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();
            int n = Mathf.RoundToInt(2f + strength * 2.5f);
            ParticleSystem.EmitParams e = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                e.position = pos + Random.insideUnitSphere * 0.05f;
                e.velocity = (dir * Random.Range(1.2f, 2.6f) + Random.insideUnitSphere * 0.9f + Vector3.up * Random.Range(0.4f, 1.4f)) * Mathf.Lerp(0.8f, 1.2f, Mathf.Clamp01(strength - 0.6f));
                e.startSize3D = new Vector3(Random.Range(0.025f, 0.05f), Random.Range(0.05f, 0.1f), 1f);
                e.startLifetime = Random.Range(3.5f, 5f);
                float c = Random.Range(0.06f, 0.15f);
                e.startColor = new Color(c * 1.1f, c * 0.95f, c * 1.05f, 1f);
                e.rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
                e.angularVelocity3D = new Vector3(Random.Range(-600f, 600f), Random.Range(-300f, 300f), Random.Range(-600f, 600f));
                shreds.Emit(e, 1);
            }
        }

        ParticleSystem BuildShreds()
        {
            GameObject g = new GameObject("ClothShreds");
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.startSpeed = 0f; main.maxParticles = 60;
            main.gravityModifier = 0.22f; main.startRotation3D = true; main.startSize3D = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            // ткань тормозит о воздух и трепещет; у земли — ложится (плоскость столкновений)
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = new ParticleSystem.MinMaxCurve(0.9f); lim.dampen = 0.06f;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.5f; noise.frequency = 1.6f;
            var col = ps.collision; col.enabled = true; col.type = ParticleSystemCollisionType.Planes; col.dampen = 0.9f; col.bounce = 0.05f;
            GameObject floor = new GameObject("ShredFloor"); floor.transform.SetParent(g.transform, false); floor.transform.position = new Vector3(0f, 0.012f, 0f);
            col.SetPlane(0, floor.transform);
            var colLife = ps.colorOverLifetime; colLife.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            colLife.color = gr;
            ParticleSystemRenderer r = g.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.alignment = ParticleSystemRenderSpace.World;   // плоский лоскут кувыркается в 3D
            r.material = new Material(Shader.Find("Sprites/Default")) { name = "Shred" };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // ---------------------------------------------------------------- кровь

        void Blood(Vector3 pos, Vector3 normal, Vector3 dir, float strength, bool kill)
        {
            if (softMat == null) return;
            if (dir.sqrMagnitude < 1e-4f) dir = -normal;
            dir.Normalize();
            Vector3 cone = (dir * 0.75f + normal * 0.45f + Vector3.up * 0.35f).normalized;
            // Mobile quality budget reduces distant droplets, keeping the main spray
            // and impact silhouette legible even when the GPU is overloaded.
            float budget = DuelManager.Gfx == 1 ? MobileVfxBudget.ParticleScale : 0.52f;
            int count = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(50f, 150f,
                Mathf.Clamp01(strength - 0.3f)) * (kill ? 1.6f : 1f) * budget), 20, 160);

            // 1) струя брызг: вытянутые капли по ходу полёта
            Spray(pos, cone, count, 3.5f * Mathf.Lerp(0.9f, 1.4f, Mathf.Clamp01(strength - 0.5f)), 36f);
            // 2) веер вперёд по ходу клинка (длинные тонкие капли)
            Spray(pos, dir, Mathf.RoundToInt(count * 0.4f), 6.5f * Mathf.Lerp(0.9f, 1.3f, Mathf.Clamp01(strength - 0.5f)), 14f);
            // 3) кровавая взвесь и веер брызг крупным планом (покадровый спрайт из атласа: вылет — разлёт — капли)
            Mist(pos, cone, 10 + Mathf.RoundToInt(10 * strength), 0.9f);
            BloodBurst(pos, (dir * 0.85f + Vector3.up * 0.3f).normalized, (0.42f + 0.28f * Mathf.Clamp01(strength)) * (kill ? 1.35f : 1f));
            // 4) пятна на земле там, куда долетели капли
            int splats = Mathf.Clamp(Mathf.RoundToInt((14 * strength + (kill ? 18 : 0)) * budget), 2, 38);
            for (int i = 0; i < splats; i++)
            {
                Vector3 v = (Quaternion.AngleAxis(Random.Range(-35f, 35f), Vector3.up) * Quaternion.AngleAxis(Random.Range(-20f, 25f), Vector3.Cross(Vector3.up, cone)) * cone) * Random.Range(1.5f, 6.5f);
                float g = 9.81f, disc = v.y * v.y + 2f * g * Mathf.Max(0.05f, pos.y);
                float tf = (v.y + Mathf.Sqrt(disc)) / g;
                Vector3 land = pos + new Vector3(v.x, 0f, v.z) * tf;
                land.y = 0f;
                float sz = Random.Range(0.06f, 0.30f) * Mathf.Lerp(0.8f, 1.4f, Mathf.Clamp01(strength - 0.4f));
                AddDecal(land, sz, new Vector2(v.x, v.z), Random.Range(14f, 26f), tf * 0.9f);
            }
            if (kill) AddDecal(new Vector3(pos.x, 0f, pos.z), 0.25f, Vector2.zero, 40f, 0.9f, 1.15f);
        }

        void Spray(Vector3 pos, Vector3 dir, int count, float speed, float angle)
        {
            GameObject g = new GameObject("Blood");
            g.transform.position = pos;
            g.transform.rotation = Quaternion.LookRotation(dir);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = false; main.duration = 0.1f;
            // много мелких тёмных капель (крупные ярко-красные «овалы» читались мультяшно) + короткое облачко кровавой взвеси
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed * 1.05f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.038f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.26f, 0.0f, 0.01f), new Color(0.55f, 0.02f, 0.03f));
            main.gravityModifier = 1.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission; em.rateOverTime = 0f;
            int drops = Mathf.Min(500, Mathf.RoundToInt(count * 1.6f));
            em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)drops) });
            var sp = ps.shape; sp.shapeType = ParticleSystemShapeType.Cone; sp.angle = angle; sp.radius = 0.03f;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.5f)));
            var rend = g.GetComponent<ParticleSystemRenderer>();
            rend.material = softMat;
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.022f; rend.lengthScale = 1.3f;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();

            GameObject mg = new GameObject("BloodMist");
            mg.transform.SetParent(g.transform, false);
            ParticleSystem mist = mg.AddComponent<ParticleSystem>();
            mist.Stop();
            var mm = mist.main;
            mm.loop = false; mm.duration = 0.1f;
            mm.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.4f);
            mm.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.08f, speed * 0.25f);
            mm.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
            mm.startColor = new Color(0.32f, 0.02f, 0.03f, 0.32f);
            mm.simulationSpace = ParticleSystemSimulationSpace.World;
            mm.stopAction = ParticleSystemStopAction.None;
            var me = mist.emission; me.rateOverTime = 0f;
            me.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(count / 6, 3, 14)) });
            var ms = mist.shape; ms.shapeType = ParticleSystemShapeType.Cone; ms.angle = angle * 1.3f; ms.radius = 0.05f;
            var msz = mist.sizeOverLifetime; msz.enabled = true;
            msz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.6f)));
            var mcol = mist.colorOverLifetime; mcol.enabled = true;
            Gradient mgr = new Gradient();
            mgr.SetKeys(new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            mcol.color = mgr;
            var mr = mg.GetComponent<ParticleSystemRenderer>();
            mr.material = softMat; mr.renderMode = ParticleSystemRenderMode.Billboard;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mist.Play();
        }

        /// <summary>
        /// Стопа встала на землю (из походки HeroRigDriver): глухой шаг по камню, в дождь — шлепок и брызги из-под подошвы,
        /// на сухом — лёгкая пыль. k ~0.3…1.5: длина шага / притоп в ударе. Частицы из одной общей системы (без мусора на шаг).
        /// </summary>
        public void Footstep(Vector3 pos, float k, Vector3 toe = default(Vector3), int side = 0)
        {
            bool wet = Weather.Instance != null && Weather.Instance.Raining;
            bool snowy = !wet && Shader.GetGlobalFloat(SnowId) > 0.3f;   // снег уже лёг — хруст и белая пыль
            Prints(pos, toe, side);
            if (stepVoice != null)
            {
                float sp = Random.Range(0.85f, 1.12f);
                AudioClip sc = Pick(step, ref sp);
                stepVoice.pitch = sp;
                stepVoice.PlayOneShot(sc, Mathf.Clamp(0.12f + 0.16f * k, 0.1f, 0.45f) * (snowy ? 0.55f : 1f));
                if (wet && stepWet != null) stepVoice.PlayOneShot(stepWet, Mathf.Clamp(0.08f + 0.12f * k, 0.06f, 0.35f));
                float sp2 = 1f;
                if (snowy && stepSnow != null) stepVoice.PlayOneShot(Pick(stepSnow, ref sp2), Mathf.Clamp(0.14f + 0.2f * k, 0.12f, 0.5f));
            }
            if (footFx == null) footFx = MakeFootFx();
            if (footFx == null) return;
            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
            int n = wet ? Mathf.RoundToInt(4f + 6f * k) : Mathf.RoundToInt(2f + 3f * k);
            for (int i = 0; i < n; i++)
            {
                Vector2 c = Random.insideUnitCircle * 0.09f;
                ep.position = pos + new Vector3(c.x, 0.015f, c.y);
                Vector3 v = new Vector3(c.x, 0f, c.y) * Random.Range(4f, 9f);
                if (wet)
                {
                    v.y = Random.Range(0.6f, 1.4f) * (0.7f + 0.4f * k);
                    ep.velocity = v; ep.startSize = Random.Range(0.012f, 0.026f); ep.startLifetime = Random.Range(0.25f, 0.45f);
                    ep.startColor = new Color(0.62f, 0.68f, 0.78f, 0.55f);
                }
                else if (snowy)
                {
                    // снежная пыль: мелкая, светлая, подлетает выше пыли и быстро оседает
                    v.y = Random.Range(0.3f, 0.8f) * (0.6f + 0.5f * k);
                    ep.velocity = v * 0.35f; ep.startSize = Random.Range(0.025f, 0.06f); ep.startLifetime = Random.Range(0.35f, 0.7f);
                    ep.startColor = new Color(0.82f, 0.86f, 0.94f, 0.45f);
                }
                else
                {
                    v.y = Random.Range(0.05f, 0.25f);
                    ep.velocity = v * 0.25f; ep.startSize = Random.Range(0.05f, 0.11f); ep.startLifetime = Random.Range(0.5f, 0.9f);
                    ep.startColor = new Color(0.38f, 0.34f, 0.3f, 0.22f);
                }
                footFx.Emit(ep, 1);
            }
        }

        static readonly int SnowId = Shader.PropertyToID("_BP_Snow");

        /// <summary>Следы: в снегу — каждый шаг; после лужи крови — 6 кровавых отпечатков, всё бледнее.</summary>
        void Prints(Vector3 pos, Vector3 toe, int side)
        {
            Tracked tf = null; float best = 1.2f * 1.2f;
            for (int i = 0; i < tracked.Count; i++)
            {
                if (tracked[i].f == null) continue;
                Vector3 d = tracked[i].f.transform.position - pos; d.y = 0f;
                if (d.sqrMagnitude < best) { best = d.sqrMagnitude; tf = tracked[i]; }
            }
            if (tf == null) return;
            Transform ft = tf.f.transform;
            if (toe.sqrMagnitude < 1e-6f) toe = ft.forward;
            bool left = side != 0 ? side < 0 : Vector3.Dot(pos - ft.position, ft.right) < 0f;
            bool inBlood = InBlood(pos);
            if (inBlood) { tf.bloodSteps = 6; return; }
            if (tf.bloodSteps > 0) { Footprints.Ensure().Add(pos, toe, left, true, tf.bloodSteps / 6f); tf.bloodSteps--; return; }
            if (Shader.GetGlobalFloat(SnowId) > 0.25f) Footprints.Ensure().Add(pos, toe, left, false, 1f);
        }

        /// <summary>Точка внутри растёкшейся лужи (крупные пятна крови на земле).</summary>
        bool InBlood(Vector3 pos)
        {
            for (int i = 0; i < decals.Count; i++)
            {
                Decal d = decals[i];
                if (d.t == null || !d.t.gameObject.activeSelf) continue;
                float r = d.t.localScale.x * 0.42f;
                if (r < 0.12f) continue;
                Vector3 dd = d.t.position - pos; dd.y = 0f;
                if (dd.sqrMagnitude < r * r) return true;
            }
            return false;
        }

        ParticleSystem MakeFootFx()
        {
            if (softMat == null) return null;
            GameObject g = new GameObject("FootFx");
            g.transform.SetParent(transform, false);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = true; main.playOnAwake = false;   // своей эмиссии нет — частицы только из Footstep (система всегда «играет»)
            main.gravityModifier = 0.9f;   // брызги падают; пыль почти не летит (малая скорость)
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            ParticleSystemRenderer r = g.GetComponent<ParticleSystemRenderer>();
            r.material = softMat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // Scene-local, capped reuse of the exact old soft dust/snow effect.
        // No new look or atlas: it retains the current dustMat/fogMat palette.
        readonly List<DustBurstPool> dustPuffs = new List<DustBurstPool>(10);

        /// <summary>Пыль из-под ног: рывок, отбрасывание, падение.</summary>
        public void Dust(Vector3 pos, Vector3 dir, int count)
        {
            if (softMat == null || count <= 0) return;
            DustBurstPool puff = null;
            for (int i = 0; i < dustPuffs.Count; i++)
            {
                DustBurstPool p = dustPuffs[i];
                if (p != null && !p.Busy) { puff = p; break; }
            }
            if (puff == null)
            {
                // A hard budget avoids overlapping clouds hiding the fighters.
                if (dustPuffs.Count >= 10) return;
                puff = DustBurstPool.Create(transform,
                    dustMat != null ? dustMat : (fogMat != null ? fogMat : softMat));
                if (puff == null) return;
                dustPuffs.Add(puff);
            }
            bool snowDust = Shader.GetGlobalFloat(SnowId) > 0.3f &&
                            !(Weather.Instance != null && Weather.Instance.Raining);
            int density = Mathf.RoundToInt(count * MobileVfxBudget.ParticleScale);
            puff.Burst(pos, dir, Mathf.Clamp(density, 1, 64), snowDust);
        }

        // трещины в камне под сокрушительным ударом (добивание сверху, удар духа): тёмная паутина трещин, держится до конца раунда
        readonly List<Transform> cracks = new List<Transform>();
        int crackIdx;
        public void GroundCrack(Vector3 pos, float size)
        {
            if (crackMat == null) return;
            Transform t;
            if (cracks.Count < 3)
            {
                GameObject q = new GameObject("GroundCrack");
                q.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
                MeshRenderer rr = q.AddComponent<MeshRenderer>();
                rr.sharedMaterial = crackMat;
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rr.receiveShadows = false;
                t = q.transform; cracks.Add(t);
            }
            else { crackIdx = (crackIdx + 1) % cracks.Count; t = cracks[crackIdx]; }
            t.gameObject.SetActive(true);
            t.position = new Vector3(pos.x, 0.0105f + cracks.IndexOf(t) * 0.0004f, pos.z);
            t.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
            t.localScale = Vector3.one * size;
            Renderer r = t.GetComponent<Renderer>();
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_Color", new Color(0.06f, 0.05f, 0.045f, 0.8f));
            r.SetPropertyBlock(mpb);
        }

        // ---- веер брызг при попадании: 4 кадра (лист 2×2) за 0,26 с, плоскость к камере, вылет по экранной проекции удара
        class Burst { public Transform t; public Renderer r; public float born, size; public Vector3 pos, dir; }
        readonly List<Burst> bursts = new List<Burst>();
        static Mesh[] burstFrames;
        const float BurstLife4 = 0.26f, BurstLife16 = 0.46f;
        bool spray16;
        float BurstLife { get { return spray16 ? BurstLife16 : BurstLife4; } }

        void BloodBurst(Vector3 pos, Vector3 dir, float size)
        {
            if (burstMat == null) return;
            if (burstFrames == null)
            {
                int n = spray16 ? 4 : 2;
                burstFrames = new Mesh[n * n];
                for (int i = 0; i < n * n; i++)
                {
                    float du = 1f / n, u0 = (i % n) * du, v0 = 1f - (i / n + 1) * du;
                    Mesh m = new Mesh { name = "BurstFrame" + i };
                    m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
                    m.uv = new[] { new Vector2(u0, v0), new Vector2(u0 + du, v0), new Vector2(u0 + du, v0 + du), new Vector2(u0, v0 + du) };
                    m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                    m.RecalculateBounds();
                    burstFrames[i] = m;
                }
            }
            Burst b = null;
            for (int i = 0; i < bursts.Count; i++) if (!bursts[i].t.gameObject.activeSelf) { b = bursts[i]; break; }
            if (b == null && bursts.Count < 4)
            {
                GameObject g = new GameObject("BloodBurst");
                g.AddComponent<MeshFilter>();
                MeshRenderer mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = burstMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                b = new Burst { t = g.transform, r = mr };
                bursts.Add(b);
            }
            if (b == null) { b = bursts[0]; float oldest = b.born; foreach (Burst x in bursts) if (x.born < oldest) { oldest = x.born; b = x; } }
            b.born = Time.time; b.size = size; b.pos = pos; b.dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.right;
            b.t.gameObject.SetActive(true);
            PoseBurst(b);
        }

        void PoseBurst(Burst b)
        {
            float age = (Time.time - b.born) / BurstLife;
            if (age >= 1f) { b.t.gameObject.SetActive(false); return; }
            int fr = Mathf.Min(burstFrames.Length - 1, (int)(age * burstFrames.Length));
            b.t.GetComponent<MeshFilter>().sharedMesh = burstFrames[fr];
            Camera cam = Camera.main;
            Vector3 f = cam != null ? cam.transform.forward : Vector3.forward;
            Vector3 r = b.dir - f * Vector3.Dot(b.dir, f);
            if (r.sqrMagnitude < 1e-4f) r = cam != null ? cam.transform.right : Vector3.right;
            r.Normalize();
            Vector3 up = Vector3.Cross(f, r);
            b.t.rotation = Quaternion.LookRotation(f, up);
            float s = b.size * (spray16 ? 1.25f : 1f) * (1f + 0.15f * age);
            b.t.localScale = new Vector3(s, s, 1f);
            // начало брызг — в точке удара: у 16-кадровых исток у левого края по центру, у старых — в левом нижнем углу
            b.t.position = spray16 ? b.pos + r * (0.47f * s) + up * (0.02f * s) : b.pos + r * (0.42f * s) + up * (0.35f * s);
            mpb.SetColor("_Color", new Color(0.42f, 0.015f, 0.02f, 0.95f * (age < (spray16 ? 0.85f : 0.75f) ? 1f : (1f - age) / (spray16 ? 0.15f : 0.25f))));
            b.r.SetPropertyBlock(mpb);
        }

        /// <summary>Удар они по земле: волна пыли и воздуха, трещины, барабан, тряска.</summary>
        public void Slam(Vector3 pos)
        {
            pos.y = 0.02f;
            // камень лопается: обломки и пыль взлетают и оседают (8 кадров)
            FxSprite rb = FxSprite.SpawnAlpha("rock_burst", pos + Vector3.up * (2.6f * 0.42f), 2.6f, 4, 2, 8, 12f, false, 0.85f);
            if (rb != null) rb.tint = new Color(0.62f, 0.6f, 0.58f, 1f);
            Shockwave(pos + Vector3.up * 0.05f, 2.6f, new Color(1f, 0.6f, 0.4f, 0.35f), 0.3f);
            Dust(pos, Vector3.up, 34);
            GroundCrack(pos, 1.6f);
            Play(boom, 0.8f, 0.75f); Taiko(1f, 0.7f);
            if (CameraOrbit.Instance != null) { CameraOrbit.Instance.Shake(0.12f); CameraOrbit.Instance.Punch(3f); }
            Haptics.Pulse(90);
        }

        /// <summary>Уход в дым (Тень клана): клуб тёмного дыма на месте бойца и шорох.</summary>
        public void SmokeVanish(Vector3 pos)
        {
            // клуб чёрного дыма с перьями в рост человека (8 кадров), поверх — прежние частицы
            FxSprite sm = FxSprite.SpawnAlpha("smoke_teleport", new Vector3(pos.x, 0f, pos.z) + Vector3.up * (2.3f * 0.43f), 2.3f, 4, 2, 8, 14f, false, 0.75f);
            if (sm != null) sm.tint = new Color(0.55f, 0.5f, 0.62f, 0.95f);
            if (dustMat == null) return;
            GameObject g = new GameObject("SmokeVanish");
            g.transform.position = pos + Vector3.up * 0.9f;
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = false; main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.12f, 0.12f, 0.15f, 0.8f);
            main.gravityModifier = -0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)26) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.45f;
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.6f)));
            g.GetComponent<ParticleSystemRenderer>().material = dustMat;
            ps.Play();
            Play(whoosh, 0.7f, 0.8f);
        }

        /// <summary>Новый раунд: трещины заделаны.</summary>
        public void ClearCracks() { for (int i = 0; i < cracks.Count; i++) if (cracks[i] != null) cracks[i].gameObject.SetActive(false); }

        // Blood belongs to one duel, not to the persistent arena scene.
        // Invalidate pooled marks so LateUpdate cannot re-enable stale decals.
        public void ClearBattleDecals()
        {
            // Invalidate coroutines (especially FountainDecals) before clearing
            // marks: otherwise the old execution can repaint the clean arena.
            battleFxGeneration++;
            for (int i = 0; i < transientBloodFx.Count; i++)
            {
                GameObject effect = transientBloodFx[i];
                if (effect == null) continue;
                ParticleSystem emitter = effect.GetComponent<ParticleSystem>();
                if (emitter != null)
                    emitter.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                effect.SetActive(false);
                Destroy(effect);
            }
            transientBloodFx.Clear();
            for (int i = 0; i < decals.Count; i++)
            {
                Decal d = decals[i];
                if (d.t == null) continue;
                d.born = float.PositiveInfinity;
                d.life = 0f;
                d.t.gameObject.SetActive(false);
                decals[i] = d;
            }
            for (int i = 0; i < tracked.Count; i++)
                tracked[i].bloodSteps = 0;
        }

        void Mist(Vector3 pos, Vector3 dir, int count, float life)
        {
            GameObject g = new GameObject("BloodMist");
            TrackBloodFx(g);
            g.transform.position = pos;
            g.transform.rotation = Quaternion.LookRotation(dir);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = false; main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.5f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.32f);
            main.startColor = new Color(0.42f, 0.0f, 0.02f, 0.45f);
            main.gravityModifier = 0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)count) });
            var sp = ps.shape; sp.shapeType = ParticleSystemShapeType.Cone; sp.angle = 45f; sp.radius = 0.04f;
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.6f)));
            g.GetComponent<ParticleSystemRenderer>().material = softMat;
            ps.Play();
        }

        /// <summary>Рана кровоточит: капли стекают с места попадания несколько секунд (эмиттер едет вместе с телом).</summary>
        void Bleed(Transform at, float seconds, float strength)
        {
            if (softMat == null || at == null) return;
            GameObject g = new GameObject("Bleed");
            TrackBloodFx(g);
            g.transform.SetParent(at, false);
            g.transform.localPosition = Vector3.zero;
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = true; main.duration = seconds;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
            // мелкие тёмные капли: крупные ярко-красные с сильным растяжением падали «иглами» вокруг раненого
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.028f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.22f, 0f, 0.01f), new Color(0.45f, 0.02f, 0.03f));
            main.gravityModifier = 1.3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = Mathf.Lerp(8f, 22f, Mathf.Clamp01(strength - 0.4f));
            var sp = ps.shape; sp.shapeType = ParticleSystemShapeType.Sphere; sp.radius = 0.06f;
            var rend = g.GetComponent<ParticleSystemRenderer>();
            rend.material = softMat; rend.renderMode = ParticleSystemRenderMode.Stretch; rend.velocityScale = 0.018f; rend.lengthScale = 1.1f;
            ps.Play();
            StartCoroutine(StopBleed(ps, g, seconds));
        }

        /// <summary>Фонтан крови из шеи после обезглавливания: мощная струя вверх, затухающая за несколько секунд.</summary>
        public void Fountain(Transform neck, float seconds)
        {
            if (softMat == null || neck == null) return;
            GameObject g = new GameObject("Fountain");
            TrackBloodFx(g);
            g.transform.SetParent(neck, false);
            g.transform.localPosition = Vector3.zero;
            g.transform.rotation = Quaternion.LookRotation(Vector3.up);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = true; main.duration = seconds;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 3.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.045f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.3f, 0f, 0.015f), new Color(0.55f, 0.03f, 0.04f));
            main.gravityModifier = 1.4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            var em = ps.emission;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 160f), new Keyframe(0.3f, 90f), new Keyframe(1f, 10f)));
            var sp = ps.shape; sp.shapeType = ParticleSystemShapeType.Cone; sp.angle = 14f; sp.radius = 0.03f;
            var rend = g.GetComponent<ParticleSystemRenderer>();
            rend.material = softMat; rend.renderMode = ParticleSystemRenderMode.Stretch; rend.velocityScale = 0.025f; rend.lengthScale = 1.3f;
            ps.Play();
            Play(hitHeavy, 1f, 0.55f);
            StartCoroutine(FountainDecals(neck, seconds, battleFxGeneration));
            StartCoroutine(StopBleed(ps, g, seconds));
        }

        System.Collections.IEnumerator FountainDecals(Transform neck, float seconds, int generation)
        {
            float end = Time.time + seconds;
            while (generation == battleFxGeneration && Time.time < end && neck != null)
            {
                Vector3 p = neck.position + new Vector3(Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f));
                AddDecal(new Vector3(p.x, 0.012f, p.z), Random.Range(0.08f, 0.2f), Random.insideUnitCircle, 60f, 0.3f, 0.2f);
                yield return new WaitForSeconds(0.12f);
            }
        }

        System.Collections.IEnumerator StopBleed(ParticleSystem ps, GameObject g, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            yield return new WaitForSeconds(1.5f);
            if (g != null) Destroy(g);
        }

        /// <summary>Вспышка разреза: короткая яркая дуга вдоль хода клинка через точку попадания.</summary>
        static bool packSlashChecked;
        static bool packSlashAvailable;

        void SlashFlash(Vector3 p, Vector3 dir, float strength)
        {
            if (fxMat == null) return;
            if (!packSlashChecked)
            {
                packSlashChecked = true;
                packSlashAvailable = Resources.Load<Texture2D>("FX/slash_red") != null || EmbeddedFxArt.Get("slash_red") != null;
            }
            // Optional six-frame painted slash, layered over the existing physical blade flash.
            // Low graphics tier keeps the lightweight line only.
            if (packSlashAvailable && DuelManager.Gfx == 1)
            {
                FxSprite art = FxSprite.Spawn("slash_red", p, 1.1f + strength * 0.28f, 6, 1, 6, 24f, false, 0.25f);
                if (art != null) art.tint = new Color(1f, 0.58f, 0.58f, 0.85f);
            }
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.right;
            dir.Normalize();
            Camera cam = Camera.main;
            Vector3 side = cam != null ? Vector3.Cross(dir, cam.transform.forward).normalized : Vector3.up;
            float len = 0.5f + 0.35f * strength;
            GameObject g = new GameObject("SlashFlash");
            LineRenderer lr = g.AddComponent<LineRenderer>();
            lr.material = fxMat;
            lr.positionCount = 5;
            for (int i = 0; i < 5; i++)
            {
                float u = i / 4f - 0.5f;
                lr.SetPosition(i, p + dir * (u * len) + side * ((0.25f - u * u) * 0.18f * len));   // лёгкий изгиб дугой
            }
            lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 0.05f + 0.03f * strength), new Keyframe(1f, 0f));
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g.AddComponent<FadeLine>().Init(lr, new Color(1f, 0.9f, 0.85f), 0.16f);
            // Animated gold sparks for strong contacts; no external PNG required.
            if (DuelManager.Gfx == 1 && strength >= 1.1f)
            {
                FxSprite burst = FxSprite.Spawn("sparks_gold", p, 0.45f, 6, 1, 6, 24f, false, 0.25f);
                if (burst != null) burst.tint = new Color(1f, 0.7f, 0.35f, 0.7f);
            }
        }

        /// <summary>Заряд супер-удара: воронка светящихся частиц втягивается в бойца.</summary>
        public void SuperCharge(Vector3 pos)
        {
            // Authored six-frame sigil atlas: expands on the ground during the charge.
            // Keep the existing seal, so a missing material/shader never removes the effect.
            if (DuelManager.Gfx == 1)
                GroundFlash.SpawnSheet("FX/sigil_red", pos, 0.8f, 2.8f,
                    new Color(1f, 0.23f, 0.14f, 0.65f), 0.8f, 6, 1, 6);
            // печать духа под ногами (крутится и гаснет) и воронка света, втягивающаяся в бойца
            Texture2D st = Resources.Load<Texture2D>("FX/seal_circle");
            Shader ash = Shader.Find("Legacy Shaders/Particles/Additive");
            if (st != null && ash != null)
            {
                GameObject sg = new GameObject("SuperSeal");
                sg.transform.position = new Vector3(pos.x, 0.02f, pos.z);
                sg.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                sg.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
                MeshRenderer smr = sg.AddComponent<MeshRenderer>();
                Shader gsh = Shader.Find("BladePath/GroundAdditive");
                Material sm = new Material(gsh != null ? gsh : ash) { mainTexture = st }; sm.SetColor("_TintColor", new Color(0.5f, 0.42f, 0.3f, 0.5f));
                smr.sharedMaterial = sm; smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sg.AddComponent<SealSpin>();
            }
            FxSprite vx = FxSprite.Spawn("vortex", pos + Vector3.up * 1.0f, 1.7f, 3, 1, 3, 4f, false, 0.9f);
            if (vx != null) vx.grow = -0.4f;
            if (softMat == null) return;
            GameObject g = new GameObject("SuperCharge");
            g.transform.position = pos + Vector3.up * 1.0f;
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = false; main.duration = 0.5f;
            main.startLifetime = 0.45f;
            main.startSpeed = -3.2f;                       // к центру
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startColor = new Color(0.6f, 0.9f, 1f, 0.9f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.useUnscaledTime = true;
            var em = ps.emission; em.rateOverTime = 160f;
            var sp = ps.shape; sp.shapeType = ParticleSystemShapeType.Sphere; sp.radius = 1.4f; sp.radiusThickness = 0.1f;
            var rend = g.GetComponent<ParticleSystemRenderer>();
            rend.material = softMat; rend.renderMode = ParticleSystemRenderMode.Stretch; rend.velocityScale = 0.05f; rend.lengthScale = 2f;
            ps.Play();
            Play(swish, 0.8f, 0.45f);
        }

        /// <summary>Светящийся след рывка супер-удара (затухающая полоса между двумя точками).</summary>
        public void Streak(Vector3 a, Vector3 b, bool player)
        {
            if (fxMat == null) return;
            GameObject g = new GameObject("SuperStreak");
            LineRenderer lr = g.AddComponent<LineRenderer>();
            lr.material = fxMat;
            lr.positionCount = 2;
            lr.SetPosition(0, a); lr.SetPosition(1, b);
            lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.02f), new Keyframe(0.7f, 0.22f), new Keyframe(1f, 0.05f));
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Color c = player ? new Color(0.6f, 0.9f, 1f) : new Color(0.85f, 0.45f, 1f);
            g.AddComponent<FadeLine>().Init(lr, c, 0.45f);
            Play(swish, 1f, 0.7f);
            Play(hitHeavy, 1f, 0.8f);
            Sparks(b, (b - a).normalized, 30);
        }

        /// <summary>Шар духа: светящийся хвост цвета владельца.</summary>
        public void DecorateBall(GameObject ball, FighterController owner)
        {
            if (fxMat == null) return;
            TrailRenderer tr = ball.AddComponent<TrailRenderer>();
            tr.material = fxMat;
            tr.time = 0.3f;
            tr.minVertexDistance = 0.03f;
            tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.26f), new Keyframe(1f, 0f));
            bool p = DuelManager.Instance != null && DuelManager.Instance.player == owner;
            Color c = p ? new Color(0.5f, 0.85f, 1f) : new Color(0.8f, 0.4f, 1f);
            Gradient g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(c, 0.3f), new GradientColorKey(c, 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // ореол вокруг шара (аддитивный, всегда к камере)
            Shader ash = Shader.Find("Legacy Shaders/Particles/Additive");
            if (ash != null)
            {
                Material hm = new Material(ash);
                hm.mainTexture = SoftSprites.Fog;
                hm.SetColor("_TintColor", new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, 0.5f));
                GameObject halo = new GameObject("BallHalo");
                halo.transform.SetParent(ball.transform, false);
                halo.transform.localScale = Vector3.one * 4.5f;   // шар ~0,28 м → ореол ~1,2 м
                halo.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
                MeshRenderer hr = halo.AddComponent<MeshRenderer>();
                hr.sharedMaterial = hm;
                hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                halo.AddComponent<GlowSprite>().flicker = 0.2f;
            }
            // живое пламя духа (6 кадров из атласа) вместо гладкого шара; хвост пламени тянется за полётом
            EnergyProjectile ep = ball.GetComponent<EnergyProjectile>();
            FxSprite orb = FxSprite.Spawn(p ? "orb_blue" : "orb_red", ball.transform.position, 0.72f, 3, 2, 6, 16f, true);
            if (orb != null)
            {
                orb.follow = ball.transform;
                orb.dir = () => ep != null ? ep.velocity : Vector3.right;
                Renderer br = ball.GetComponent<Renderer>(); if (br != null) br.enabled = false;
                Transform halo2 = ball.transform.Find("BallHalo"); if (halo2 != null) halo2.localScale = Vector3.one * 3.2f;
            }
            Play(whoosh, 0.5f, 1.4f);
        }

        /// <summary>Шар погас (попал, отбит или долетел): взрыв духа — вспышка, кольцо пламени, искры, дымка (6 кадров).</summary>
        public void OrbBurst(Vector3 pos, FighterController owner)
        {
            bool p = DuelManager.Instance != null && DuelManager.Instance.player == owner;
            FxSprite b = FxSprite.Spawn("burst_blue", pos, 1.1f, 3, 2, 6, 18f, false, 0.38f);
            if (b != null) { b.grow = 0.35f; if (!p) b.tint = new Color(1f, 0.55f, 1f, 1f); }
            SparkLight(pos, p ? new Color(0.55f, 0.8f, 1f) : new Color(0.95f, 0.45f, 1f), 2.4f, 3f, 0.15f);
        }

        /// <summary>Крестовая вспышка (контратака, добивание).</summary>
        public void XSlash(Vector3 pos)
        {
            FxSprite x = FxSprite.Spawn("x_slash", pos, 0.9f, 1, 1, 1, 1f, false, 0.22f);
            if (x != null) x.grow = 0.5f;
        }

        /// <summary>Шар отбит клинком.</summary>
        public void Deflect(Vector3 pos, FighterController by)
        {
            Play(clang, 0.9f, 1.3f);
            Sparks(pos, Vector3.up, 24);
            Flash(pos, 0.23f, new Color(0.68f, 0.87f, 1f, 0.88f), 0.11f, true);
            SparkLight(pos, new Color(0.6f, 0.8f, 1f), 2.2f, 3f, 0.12f);
            TrailRenderer[] trs = null;
            foreach (EnergyProjectile e in FindObjectsByType<EnergyProjectile>(FindObjectsSortMode.None))
                if (e.owner == by) { trs = e.GetComponents<TrailRenderer>(); foreach (TrailRenderer tr in trs) tr.Clear(); }
        }

        // вспышка света от искр: на миг освещает лица и доспехи бойцов, камень под ними (один источник, переиспользуется)
        Light sparkLight; float sparkLightAt = -10f, sparkLightDur = 0.1f, sparkLightI;
        void SparkLight(Vector3 pos, Color c, float intensity, float range, float dur)
        {
            if (sparkLight == null)
            {
                GameObject g = new GameObject("SparkLight");
                g.transform.SetParent(transform, false);
                sparkLight = g.AddComponent<Light>();
                sparkLight.type = LightType.Point; sparkLight.shadows = LightShadows.None;
                sparkLight.renderMode = LightRenderMode.ForcePixel;   // короткая — пусть будет попиксельной, не отнимая место у фонарей надолго
            }
            sparkLight.transform.position = pos;
            sparkLight.color = c; sparkLight.range = range;
            sparkLightI = intensity; sparkLightDur = dur; sparkLightAt = Time.unscaledTime;
            sparkLight.intensity = intensity; sparkLight.enabled = true;
        }
        void UpdateSparkLight()
        {
            if (sparkLight == null || !sparkLight.enabled) return;
            float u = (Time.unscaledTime - sparkLightAt) / sparkLightDur;
            if (u >= 1f) { sparkLight.enabled = false; return; }
            sparkLight.intensity = sparkLightI * (1f - u) * (1f - u);
        }

        /// <summary>Клинки столкнулись: вспышка с бликом, сноп раскалённых искр (отскакивают от земли) и звонкий лязг.</summary>
        public void Clash(Vector3 pos)
        {
            Play(clang, 1f, Random.Range(0.75f, 0.9f));
            Sparks(pos, Vector3.up, 34, 1.1f);
            Sparks(pos, Random.onUnitSphere, 22, 1f);
            Flash(pos, 0.22f, new Color(1f, 0.8f, 0.5f, 0.85f), 0.09f, true);   // компактная: ядро не заливает полкадра белым (bloom и так расширит)
            SparkLight(pos, new Color(1f, 0.72f, 0.4f), 2.6f, 3.2f, 0.12f);
            Sheet("fx_clash", pos, 1.0f, 0.3f, new Color(1f, 1f, 1f, 0.6f));   // звезда удара сталь о сталь → веер искр
        }

        // ---- покадровые эффекты (атласы 19–20): лист 4×1, плоскость к камере
        FxSprite Sheet(string tex, Vector3 pos, float size, float life, Color tint, System.Func<Vector3> dir = null)
        {
            FxSprite f = FxSprite.Spawn(tex, pos, size, 4, 1, 4, 4f / Mathf.Max(0.05f, life), false, life);
            if (f != null) { f.tint = tint; f.dir = dir; }
            return f;
        }

        /// <summary>
        /// Живой огонь в жаровнях: нарисованный цикл из 8 кадров (атлас 24) вместо частиц-языков; сердцевина, искры и дым остаются.
        /// </summary>
        void LightBraziers()
        {
            if (Resources.Load<Texture2D>("FX/brazier_fire") == null) return;
            foreach (ArenaObstacle o in FindObjectsByType<ArenaObstacle>(FindObjectsSortMode.None))
            {
                if (o == null || !o.name.StartsWith("Brazier") || o.transform.Find("FireSprite") != null) continue;
                Transform an = new GameObject("FireSprite").transform;
                an.SetParent(o.transform, false); an.localPosition = new Vector3(0f, 1.36f, 0f);
                FxSprite f = FxSprite.Spawn("brazier_fire", an.position, 0.88f, 4, 2, 8, 12f, true);
                if (f == null) { Destroy(an.gameObject); continue; }
                f.follow = an; f.tint = new Color(1f, 0.92f, 0.82f, 0.85f);
                Transform old = o.transform.Find("Fire");
                if (old != null) { ParticleSystemRenderer pr = old.GetComponent<ParticleSystemRenderer>(); if (pr != null) pr.enabled = false; }
            }
        }

        /// <summary>Супер-удар попал: молния с неба в противника и золотой полумесяц по ходу удара.</summary>
        public void SuperStrike(Vector3 pos, Vector3 dir)
        {
            FxSprite b = FxSprite.Spawn("super_strike", new Vector3(pos.x, 0f, pos.z) + Vector3.up * (3.6f * 0.47f), 3.6f, 4, 3, 12, 14f, false, 0.95f);
            if (b != null) b.tint = Color.white;
            Vector3 d = dir; d.y = 0f;
            FxSprite c = FxSprite.Spawn("super_crescent", pos + Vector3.up * 1.0f - d.normalized * 0.9f, 2.6f, 4, 1, 4, 14f, false, 0.3f);
            if (c != null) { c.dir = () => d; c.tint = new Color(1f, 1f, 1f, 0.9f); }
            if (DuelManager.Gfx == 1)
            {
                FxSprite flame = FxSprite.Spawn("flame_red", pos + Vector3.up * 1.1f,
                    1.65f, 6, 1, 6, 19f, false, 0.32f);
                if (flame != null) flame.tint = new Color(1f, 0.6f, 0.55f, 0.55f);
                // A fleeting spectral beast follows the finishing crescent.
                FxSprite spirit = FxSprite.Spawn("spirit_wolf", pos + Vector3.up * 1.1f,
                    2.25f, 6, 1, 6, 14f, false, 0.42f);
                if (spirit != null) { spirit.tint = new Color(0.95f, 0.58f, 0.65f, 0.6f); spirit.vel = dir.normalized * 1.1f; }
            }
            SparkLight(pos + Vector3.up * 2f, new Color(0.75f, 0.6f, 1f), 6f, 6f, 0.25f);
        }

        /// <summary>Слом равновесия: золотое кольцо защиты трескается и разлетается осколками.</summary>
        public void GuardBreak(Vector3 pos) { Sheet("fx_guard_break", pos, 1.6f, 0.5f, new Color(1f, 1f, 1f, 0.7f)); }

        /// <summary>Рывок: порыв ветра по ходу и пыль из-под ног.</summary>
        public void DashFx(Vector3 feet, Vector3 dir)
        {
            Vector3 d = dir; d.y = 0f;
            Sheet("fx_dash_streak", feet + Vector3.up * 0.95f, 2.4f, 0.32f, new Color(0.9f, 0.95f, 1f, 0.65f), () => d);
            Sheet("fx_dash_dust", feet + Vector3.up * 0.52f, 1.5f, 0.5f, new Color(0.75f, 0.74f, 0.72f, 0.6f));
        }

        /// <summary>
        /// Поверженный в конце поединка (не в каждом раунде — враг ещё встанет): от тела поднимается дух. Время без замедления
        /// концовки, три наложенных друг на друга волны со сдвигом и подъёмом — 4 кадра атласа не дёргаются.
        /// </summary>
        public void SpiritRise(Vector3 at) { StartCoroutine(SpiritLater(at)); }

        System.Collections.IEnumerator SpiritLater(Vector3 at)
        {
            yield return new WaitForSecondsRealtime(0.6f);
            // 16 кадров: дух встаёт столбом, крутится, рассыпается пеплом; вторая, слабее и шире — следом, для глубины
            for (int i = 0; i < 2; i++)
            {
                float sz = 2.4f + 0.4f * i;
                FxSprite s = FxSprite.Spawn("fx_spirit", new Vector3(at.x, 0f, at.z) + Vector3.up * (sz * 0.43f), sz, 4, 4, 16, 8f, false, 2.1f);
                if (s != null) { s.unscaled = true; s.vel = Vector3.up * 0.12f; s.grow = 0.1f; s.tint = new Color(0.85f, 0.9f, 1f, i == 0 ? 0.75f : 0.35f); }
                yield return new WaitForSecondsRealtime(0.3f);
            }
        }

        /// <summary>Отражение (парирование): самая яркая вспышка в игре — блик, кольцо волны, веер искр от клинка.</summary>
        public void Parry(Vector3 pos, Vector3 normal)
        {
            Play(clang, 1f, Random.Range(1.05f, 1.2f));
            Play(clang, 0.6f, Random.Range(1.6f, 1.75f));   // высокий призвук «дзинь»
            Sparks(pos, normal.sqrMagnitude > 1e-4f ? normal : Vector3.up, 46, 1.35f);
            Sparks(pos, Vector3.up, 20, 0.9f);
            Flash(pos, 0.28f, new Color(1f, 0.92f, 0.78f, 0.9f), 0.12f, true);   // искра, а не белое пятно: мельче ядро, лучи-блик остаются
            SparkLight(pos, new Color(1f, 0.86f, 0.62f), 3.6f, 3.8f, 0.16f);
            Shockwave(pos, 0.7f, new Color(0.6f, 0.85f, 1f, 0.22f), 0.16f);   // едва заметная волна воздуха, не «магическое кольцо»
            Sheet("fx_perfect_parry", pos, 1.15f, 0.38f, new Color(0.8f, 0.9f, 1f, 0.5f));   // ледяное кольцо и осколки
        }

        Material addMat, glowMat, starMat, ringMat;
        Texture2D sparkTex, ringTex;
        Transform sparkFloor;

        Material Additive(ref Material m, Texture2D tex)
        {
            if (m != null) return m;
            Shader a = Shader.Find("Legacy Shaders/Particles/Additive");
            if (a == null) return softMat;
            m = new Material(a); m.mainTexture = tex;
            m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            return m;
        }

        // одна частица-вспышка (ядро) и, если glint, крестовой блик поверх: короткие, аддитивные — их подхватывает bloom
        void Flash(Vector3 pos, float size, Color c, float life, bool glint)
        {
            // ядро — гауссово пятно без края (диск с краем читается как «белая тарелка»), блик — тонкие лучи крупнее ядра
            // Two-stage highlight: a tight white-hot center and a broader colored halo.
            // Only the stronger glint uses an extra particle, keeping ordinary hits cheap.
            Burst1(pos, size, Color.Lerp(c, Color.white, 0.55f), life * 0.75f,
                Additive(ref glowMat, SoftSprites.Fog), Random.Range(0f, 360f));
            if (glint)
            {
                Burst1(pos, size * 1.8f, new Color(c.r, c.g, c.b, c.a * 0.55f),
                    life * 1.25f, Additive(ref glowMat, SoftSprites.Fog), 0f);
                // Irregular atlas rays avoid a static cross-shaped flare.
                if (sparkTex == null) { sparkTex = Resources.Load<Texture2D>("FX/spark_burst"); if (sparkTex == null) sparkTex = SoftSprites.Star; }
                Burst1(pos, size * 2.7f, Color.Lerp(c, Color.white, 0.35f),
                    life * 1.2f, Additive(ref starMat, sparkTex), Random.Range(0f, 360f));
            }
        }

        /// <summary>Вспышка ступени накопления: кольцо по полу и ядро света.</summary>
        public void ChargePulse(Vector3 pos, Color c, float size)
        {
            Shockwave(pos + Vector3.up * 0.15f, size * 2.2f, c, 0.45f);
            Flash(pos + Vector3.up * 0.9f, size * 0.7f, Color.Lerp(c, Color.white, 0.4f), 0.22f, true);
        }

        void Shockwave(Vector3 pos, float size, Color c, float life)
        {
            if (ringTex == null) { ringTex = Resources.Load<Texture2D>("FX/ring_glow"); if (ringTex == null) ringTex = SoftSprites.Ring; }   // светящееся кольцо из атласа
            Burst1(pos, size, c, life, Additive(ref ringMat, ringTex), 0f, true);
        }

        // A pool shared by all bursts in this arena; cleared automatically on scene unload.
        CinematicFlashPool flashPool;

        void Burst1(Vector3 pos, float size, Color c, float life, Material m, float rotDeg, bool grow = false)
        {
            if (m == null || size <= 0f) return;
            if (flashPool == null)
            {
                GameObject owner = new GameObject("CinematicFlashPool");
                owner.transform.SetParent(transform, false);
                flashPool = owner.AddComponent<CinematicFlashPool>();
            }
            flashPool.Emit(pos, size, c, life, m, rotDeg, grow);
        }

        // искры: раскалённые (белое → оранжевое → тёмно-красное), тают и тормозят в воздухе, отскакивают от земли
        // Katana impacts reuse particle systems instead of allocating/destroying
        // a GameObject, gradient and particle renderer for every sword clash.
        void Sparks(Vector3 pos, Vector3 normal, int count, float power = 1f)
        {
            if (softMat == null) return;
            int budget = Mathf.RoundToInt((DuelManager.Gfx == 1 ? 28f : 14f) *
                MobileVfxBudget.ParticleScale);
            count = Mathf.Clamp(count, 1, Mathf.Max(6, budget));
            ImpactSparkPool.Emit(pos, normal, count, power,
                Additive(ref addMat, SoftSprites.Disc), transform);
        }

        static Mesh quad;
        static Mesh QuadMesh()
        {
            if (quad != null) return quad;
            quad = new Mesh();
            quad.vertices = new Vector3[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            quad.uv = new Vector2[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            quad.triangles = new int[] { 0, 2, 1, 0, 3, 2 };   // нормаль -Z, как у стандартного Quad
            quad.RecalculateNormals();
            return quad;
        }

        // пятна крови на земле (пул плоских квадов)
        void AddDecal(Vector3 pos, float size, Vector2 dir, float life, float delay, float grow = 0f)
        {
            if (splatMat == null) return;
            Decal d;
            if (decals.Count < MaxDecals)
            {
                // CreatePrimitive нельзя: он добавляет MeshCollider, а модуль физики для мешей в сборке вырезан
                GameObject q = new GameObject("BloodDecal");
                q.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
                Renderer rr = q.AddComponent<MeshRenderer>();
                rr.sharedMaterial = splatMat;
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rr.receiveShadows = false;
                d = new Decal(); d.t = q.transform; d.r = rr;
                decals.Add(d);
                decalIdx = decals.Count - 1;
            }
            else { decalIdx = (decalIdx + 1) % decals.Count; d = decals[decalIdx]; }
            float yaw = dir.sqrMagnitude > 1e-4f ? Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg : Random.Range(0f, 360f);
            d.t.position = new Vector3(pos.x, 0.012f + (decalIdx % 7) * 0.0006f, pos.z);
            d.t.rotation = Quaternion.Euler(90f, yaw, 0f);
            // лужа под павшим, вытянутые полосы по ходу брызг (капли улетают дальше), остальное — круглые брызги
            bool streak = grow <= 0f && dir.sqrMagnitude > 1e-4f && Random.value < 0.5f;
            Material dm = grow > 0f ? poolMat : streak ? streakMat : splatMat;
            if (dm != null && d.r.sharedMaterial != dm) d.r.sharedMaterial = dm;
            float stretch = streak ? Random.Range(1.0f, 1.3f) : dir.sqrMagnitude > 1e-4f ? Random.Range(1.0f, 1.6f) : 1f;
            if (streak) size *= 1.35f;
            d.size = size; d.grow = grow;
            d.t.localScale = new Vector3(size, size * stretch, 1f) * (grow > 0f ? 0.1f : 1f);
            d.born = Time.unscaledTime + delay; d.life = life;
            d.t.gameObject.SetActive(false);
            decals[decalIdx] = d;
        }

        void LateUpdate()
        {
            for (int i = 0; i < bursts.Count; i++) if (bursts[i].t != null && bursts[i].t.gameObject.activeSelf) PoseBurst(bursts[i]);
            for (int i = 0; i < decals.Count; i++)
            {
                Decal d = decals[i];
                if (d.t == null) continue;
                float age = Time.unscaledTime - d.born;
                if (age < 0f) continue;
                if (age > d.life)
                {
                    if (d.t.gameObject.activeSelf) d.t.gameObject.SetActive(false);
                    continue;
                }
                if (!d.t.gameObject.activeSelf) d.t.gameObject.SetActive(true);
                float a = Mathf.Clamp01((d.life - age) / 6f);       // последние 6 с — растворяется
                if (d.grow > 0f)
                {
                    float k = Mathf.Clamp01(age / d.grow);
                    float sc = d.size * Mathf.Lerp(0.1f, 2.6f, 1f - (1f - k) * (1f - k));
                    d.t.localScale = new Vector3(sc, sc, 1f);
                }
                mpb.SetColor("_Color", new Color(0.30f, 0.0f, 0.015f, 0.88f * a));
                d.r.SetPropertyBlock(mpb);
                if (a <= 0f) d.t.gameObject.SetActive(false);
            }
        }

        // ---------------------------------------------------------------- синтез

        const int Rate = 22050;

        static AudioClip Make(string name, float[] d)
        {
            AudioClip c = AudioClip.Create(name, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        /// <summary>
        /// Киай — короткий выдох-выкрик на тяжёлом ударе («Хаа!»): голосовой тон 150→175→105 Гц с дрожанием и хрипом
        /// (субгармоника — напряжённые связки), придыхание «х» в начале, гласная «а» тремя формантами (750/1150/2500 Гц),
        /// мягкое насыщение. variant меняет высоту и окраску (у второго — ближе к «эй»).
        /// </summary>
        static AudioClip Kiai(int seed, float pitchK, float f2, float f1 = 760f, float T = 0.46f)
        {
            int n = (int)(Rate * T);
            float[] d = new float[n];
            System.Random rnd = new System.Random(seed);
            float ph = 0f, jit = 0f, peak = 1e-4f;
            float[] fF = { f1, f2, 2550f }, bw = { 110f, 140f, 220f }, gF = { 1f, 0.55f, 0.22f };
            float[] y1 = new float[3], y2 = new float[3];
            int pulse = 0; float prevPh = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                // высота: рывок вверх на ударе и спад на выдохе
                float f0 = (t < 0.06f ? Mathf.Lerp(150f, 178f, t / 0.06f) : Mathf.Lerp(178f, 105f, Mathf.Pow((t - 0.06f) / (T - 0.06f), 0.8f))) * pitchK;
                if ((i & 63) == 0) jit = ((float)rnd.NextDouble() - 0.5f) * 0.04f;
                ph += f0 * (1f + jit) / Rate;
                if (ph >= 1f) { ph -= 1f; pulse++; }
                // импульс связок (Розенберг): плавное открытие, резкое закрытие; каждый второй слабее — хрип
                float g = ph < 0.55f ? 0.5f * (1f - Mathf.Cos(Mathf.PI * ph / 0.55f)) : Mathf.Max(0f, Mathf.Cos(Mathf.PI * (ph - 0.55f) / 0.5f));
                float src = (g - prevPh) * 30f; prevPh = g;   // производная — спектр голоса
                float rough = (pulse & 1) == 0 ? 1f : 0.62f;
                float voiceEnv = Mathf.SmoothStep(0f, 1f, (t - 0.025f) / 0.03f) * Mathf.Clamp01((T - t) / 0.2f);
                float x = (float)(rnd.NextDouble() * 2.0 - 1.0);
                float breath = Mathf.Exp(-t * 28f) * 1.4f + 0.22f * Mathf.Clamp01((T - t) / 0.25f);   // «х» и придых на выдохе
                float exc = src * rough * voiceEnv + x * breath * 0.6f;
                float v = 0f;
                for (int k = 0; k < 3; k++)
                {
                    float fk = fF[k] * (t > 0.3f ? Mathf.Lerp(1f, 0.92f, (t - 0.3f) / (T - 0.3f)) : 1f);   // к концу рот прикрывается
                    float r = Mathf.Exp(-Mathf.PI * bw[k] / Rate), w = 2f * Mathf.PI * fk / Rate;
                    float y = (1f - r) * exc + 2f * r * Mathf.Cos(w) * y1[k] - r * r * y2[k];
                    y2[k] = y1[k]; y1[k] = y; v += y * gF[k];
                }
                float env = Mathf.Min(1f, t * 120f) * Mathf.Clamp01((T - t) / 0.12f);
                v = (float)System.Math.Tanh(v * 1.8f * env);
                d[i] = v; peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            for (int i = 0; i < n; i++) d[i] *= 0.85f / peak;
            return Make("kiai_" + seed, d);
        }

        static AudioClip Growl()
        {
            int n = (int)(Rate * 1.4f);
            float[] d = new float[n];
            System.Random rnd = new System.Random(23);
            float lp = 0f, lp2 = 0f, ph = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Min(1f, t * 5f) * Mathf.Clamp01((1.4f - t) * 2.5f);
                float f = 48f + 10f * Mathf.Sin(t * 7f) + 6f * Mathf.Sin(t * 23f);
                ph += 2f * Mathf.PI * f / Rate;
                float buzz = Mathf.Sign(Mathf.Sin(ph)) * 0.5f + Mathf.Sin(ph * 2f) * 0.3f;   // хриплая пила
                float x = (float)(rnd.NextDouble() * 2.0 - 1.0);
                lp += 0.06f * (x - lp);
                lp2 += 0.08f * ((buzz * (0.6f + 0.4f * lp * 4f)) - lp2);                   // рычание «сквозь» шум
                d[i] = Mathf.Clamp(lp2 * 1.6f * env * (0.75f + 0.25f * Mathf.Sin(t * 31f)), -1f, 1f);
            }
            return Make("growl", d);
        }

        static AudioClip Step()
        {
            int n = (int)(Rate * 0.09f);
            float[] d = new float[n];
            System.Random rnd = new System.Random(17);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += 0.3f * ((float)(rnd.NextDouble() * 2.0 - 1.0) - lp);
                d[i] = (lp * 0.8f + Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.4f) * Mathf.Exp(-t * 55f);
            }
            return Make("step", d);
        }

        static AudioClip Danger_()
        {
            int n = (int)(Rate * 1.1f);
            float[] d = new float[n];
            System.Random rnd = new System.Random(41);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Min(1f, t * 300f) * Mathf.Exp(-t * 3.2f);
                float gong = Mathf.Sin(2f * Mathf.PI * 92f * t) * 0.55f + Mathf.Sin(2f * Mathf.PI * 92f * 2.76f * t) * 0.25f * Mathf.Exp(-t * 5f) + Mathf.Sin(2f * Mathf.PI * 92f * 5.4f * t) * 0.12f * Mathf.Exp(-t * 9f);
                lp += 0.2f * ((float)(rnd.NextDouble() * 2.0 - 1.0) - lp);
                float hit = lp * Mathf.Exp(-t * 40f) * 0.6f;
                d[i] = Mathf.Clamp((gong + hit) * env, -1f, 1f);
            }
            return Make("danger", d);
        }

        /// <summary>Мокрый шлепок подошвы: короткий светлый шум (брызги), без низа.</summary>
        static AudioClip StepWet()
        {
            int n = (int)(Rate * 0.12f);
            float[] d = new float[n];
            System.Random rnd = new System.Random(29);
            float prev = 0f, hp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = (float)(rnd.NextDouble() * 2.0 - 1.0);
                hp = 0.82f * (hp + x - prev); prev = x;   // верхние частоты
                float env = Mathf.Min(1f, t * 400f) * Mathf.Exp(-t * 38f) * (1f + 0.6f * Mathf.Exp(-Mathf.Pow((t - 0.035f) * 90f, 2f)));
                d[i] = Mathf.Clamp(hp * 0.55f * env, -1f, 1f);
            }
            return Make("step_wet", d);
        }

        /// <summary>
        /// Хруст снега: 6–9 коротких сухих щелчков-зёрен (шум в полосе 2–5 кГц) за ~0,16 с с глухим низом сминаемого наста.
        /// </summary>
        static AudioClip StepSnow()
        {
            int n = (int)(Rate * 0.2f);
            float[] d = new float[n];
            System.Random rnd = new System.Random(41);
            float prev = 0f, hp = 0f, lp = 0f;
            int grains = 8;
            float[] gt = new float[grains], ga = new float[grains];
            for (int g = 0; g < grains; g++) { gt[g] = 0.006f + (float)rnd.NextDouble() * 0.15f; ga[g] = 0.5f + (float)rnd.NextDouble() * 0.5f; }
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = (float)(rnd.NextDouble() * 2.0 - 1.0);
                hp = 0.7f * (hp + x - prev); prev = x;
                lp += 0.06f * (x - lp);
                float crk = 0f;
                for (int g = 0; g < grains; g++) { float u = (t - gt[g]) * 900f; if (u > 0f && u < 6f) crk += ga[g] * Mathf.Exp(-u); }
                float body = Mathf.Min(1f, t * 300f) * Mathf.Exp(-t * 22f);
                d[i] = Mathf.Clamp(hp * crk * 0.7f + lp * body * 1.6f + hp * body * 0.15f, -1f, 1f);
            }
            return Make("step_snow", d);
        }

        static AudioClip Drum()
        {
            int n = (int)(Rate * 1.1f);
            float[] d = new float[n];
            System.Random rnd = new System.Random(5);
            float ph = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = 62f + 50f * Mathf.Exp(-t * 18f);
                ph += 2f * Mathf.PI * f / Rate;
                float body = Mathf.Sin(ph) * Mathf.Exp(-t * 3.2f) + 0.35f * Mathf.Sin(ph * 1.52f) * Mathf.Exp(-t * 6f);
                float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
                lp += 0.12f * (noise - lp);
                d[i] = Mathf.Clamp((body + lp * Mathf.Exp(-t * 30f) * 1.4f) * Mathf.Min(1f, t * 600f), -1f, 1f);
            }
            return Make("taiko", d);
        }

        static AudioClip Heart()
        {
            int n = (int)(Rate * 0.6f);
            float[] d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, v = 0f;
                float t1 = t, t2 = t - 0.22f;
                v += Mathf.Sin(2f * Mathf.PI * 55f * t1) * Mathf.Exp(-t1 * 22f);
                if (t2 > 0f) v += 0.75f * Mathf.Sin(2f * Mathf.PI * 50f * t2) * Mathf.Exp(-t2 * 22f);
                d[i] = v * Mathf.Min(1f, t * 300f);
            }
            return Make("heart", d);
        }

        /// <summary>Ночной ветер: бесшовная петля отфильтрованного шума с медленными порывами.</summary>
        static AudioClip Wind()
        {
            int n = Rate * 8, m = Rate / 2;
            float[] raw = new float[n + m];
            System.Random rnd = new System.Random(3);
            float lp1 = 0f, lp2 = 0f;
            for (int i = 0; i < raw.Length; i++)
            {
                float t = i / (float)Rate;
                float gust = 0.55f + 0.3f * Mathf.Sin(t * 0.7f) + 0.15f * Mathf.Sin(t * 1.9f + 1f);
                float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
                lp1 += (0.02f + 0.03f * gust) * (noise - lp1);
                lp2 += 0.004f * (noise - lp2);
                raw[i] = (lp1 * 3.2f + lp2 * 6f) * gust;
            }
            float[] d = new float[n];
            for (int i = 0; i < n; i++) d[i] = raw[i];
            for (int i = 0; i < m; i++) { float k = i / (float)m; d[i] = raw[i] * k + raw[n + i] * (1f - k); }
            return Make("wind", d);
        }

        /// <summary>
        /// Сверчки (бесшовная петля 8 с): четыре сверчка на 3,9–5,1 кГц, у каждого свой ритм — трели из 3–4 импульсов по 12 мс,
        /// паузы плавают; лёгкое дрожание амплитуды. Тихо, фоном.
        /// </summary>
        static AudioClip Crickets()
        {
            int n = Rate * 8, m = Rate / 2;
            float[] raw = new float[n + m];
            System.Random rnd = new System.Random(19);
            float[] f0 = { 4300f, 4720f, 5080f, 3920f };
            float[] amp = { 1f, 0.7f, 0.55f, 0.8f };
            for (int c = 0; c < f0.Length; c++)
            {
                float t0 = (float)rnd.NextDouble() * 0.6f;
                float total = (n + m) / (float)Rate;
                while (t0 < total)
                {
                    int pulses = 3 + rnd.Next(2);
                    for (int p = 0; p < pulses; p++)
                    {
                        float ps = t0 + p * 0.03f;
                        int i0 = (int)(ps * Rate), len = (int)(0.012f * Rate);
                        for (int k = 0; k < len && i0 + k < raw.Length; k++)
                        {
                            float tt = k / (float)Rate;
                            float env = Mathf.Sin(Mathf.PI * k / len);
                            raw[i0 + k] += Mathf.Sin(2f * Mathf.PI * f0[c] * (ps + tt)) * env * env * amp[c] * 0.25f;
                        }
                    }
                    t0 += 0.42f + 0.35f * (float)rnd.NextDouble() + (rnd.NextDouble() < 0.12 ? 1.2f : 0f);   // иногда замолкает
                }
            }
            float[] d = new float[n];
            for (int i = 0; i < n; i++) d[i] = raw[i];
            for (int i = 0; i < m; i++) { float k = i / (float)m; d[i] = raw[i] * k + raw[n + i] * (1f - k); }
            return Make("crickets", d);
        }

        static AudioClip Swish()
        {
            int n = (int)(Rate * 0.30f);
            float[] d = new float[n];
            System.Random rnd = new System.Random(7);
            float lp1 = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
                float a = Mathf.Lerp(0.05f, 0.55f, t);
                lp1 += a * (noise - lp1);
                lp2 += (a * 0.35f) * (noise - lp2);
                float band = lp1 - lp2;
                float env = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.7f)), 1.5f);
                d[i] = band * env * 2.4f;
            }
            return Make("swish", d);
        }

        /// <summary>
        /// Пронзание: рваная ткань и плоть (шум через резонансный полосовой фильтр, частота сползает 3,2 → 1,4 кГц, «треск» модуляцией)
        /// и глухой удар в грудь (синус 100 → 55 Гц).
        /// </summary>
        static AudioClip Stab()
        {
            int n = (int)(Rate * 0.5f);
            float[] d = new float[n];
            System.Random rnd = new System.Random(31);
            float low = 0f, band = 0f, ph = 0f, peak = 1e-4f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = (float)(rnd.NextDouble() * 2.0 - 1.0);
                float fc = 3200f - 1800f * Mathf.Clamp01(t / 0.08f);
                float f = 2f * Mathf.Sin(Mathf.PI * fc / Rate);
                low += f * band; float high = x - low - 0.55f * band; band += f * high;
                float crackle = 0.55f + 0.45f * Mathf.Sign(Mathf.Sin(2f * Mathf.PI * (70f + 140f * t) * t + (float)rnd.NextDouble() * 0.6f));
                float tear = band * Mathf.Exp(-t * 26f) * crackle * Mathf.Min(1f, t * 2500f);
                ph += 2f * Mathf.PI * (55f + 45f * Mathf.Exp(-t * 20f)) / Rate;
                float body = Mathf.Sin(ph) * Mathf.Exp(-t * 9f) * Mathf.Min(1f, t * 400f);
                float v = tear * 0.9f + body;
                d[i] = v; peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            for (int i = 0; i < n; i++) d[i] *= 0.9f / peak;
            return Make("stab", d);
        }

        /// <summary>Глоток из тыквы: два-три глухих «глюк» (низкий тон с падающей частотой и бульканьем) и тихий звон в конце.</summary>
        static AudioClip Gulp()
        {
            int n = (int)(Rate * 0.9f);
            float[] d = new float[n];
            System.Random rnd = new System.Random(41);
            float ph = 0f, peak = 1e-4f, lp = 0f;
            float[] at = { 0.02f, 0.26f, 0.5f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, v = 0f;
                for (int k = 0; k < at.Length; k++)
                {
                    float u = t - at[k];
                    if (u < 0f || u > 0.22f) continue;
                    float env = Mathf.Sin(Mathf.PI * u / 0.22f);
                    float f = 230f - 120f * (u / 0.22f) + 30f * k;
                    ph += 2f * Mathf.PI * f / Rate;
                    float x = (float)(rnd.NextDouble() * 2.0 - 1.0);
                    lp += 0.08f * (x - lp);
                    v += (Mathf.Sin(ph) * 0.8f + lp * 2.2f) * env * env;
                }
                float c = t - 0.72f;   // тёплый звон — лечение подействовало
                if (c > 0f) v += 0.25f * Mathf.Exp(-c * 6f) * (Mathf.Sin(2f * Mathf.PI * 880f * c) + 0.5f * Mathf.Sin(2f * Mathf.PI * 1320f * c));
                d[i] = v; peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            for (int i = 0; i < n; i++) d[i] *= 0.8f / peak;
            return Make("gulp", d);
        }

        AudioClip regrip;
        /// <summary>Враг перехватил меч (смена стойки): шорох рукава и сухой щелчок цубы — тихо, но слышно, что он что-то задумал.</summary>
        public void Regrip()
        {
            if (regrip == null) regrip = Regrip_();
            Play(regrip, 0.35f, Random.Range(0.92f, 1.08f));
        }
        static AudioClip Regrip_()
        {
            int n = (int)(Rate * 0.45f);
            float[] d = new float[n];
            System.Random rnd = new System.Random(61);
            float lp = 0f, bp = 0f, lo = 0f, peak = 1e-4f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = (float)(rnd.NextDouble() * 2.0 - 1.0);
                lp += 0.25f * (x - lp);
                float swish = (x - lp) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.28f)) * 0.35f;   // рукав
                float c = t - 0.24f, click = 0f;
                if (c > 0f) click = (Mathf.Sin(2f * Mathf.PI * 2350f * c) + 0.6f * Mathf.Sin(2f * Mathf.PI * 3720f * c)) * Mathf.Exp(-c * 140f) + x * Mathf.Exp(-c * 900f) * 0.8f;
                float v = swish + click;
                d[i] = v; peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            for (int i = 0; i < n; i++) d[i] *= 0.85f / peak;
            return Make("regrip", d);
        }

        AudioClip gulp;
        /// <summary>Тыква подействовала: тёплые искры поднимаются от тела, мягкое золотое сияние, глоток и звон.</summary>
        public void HealFx(FighterController f)
        {
            if (gulp == null) gulp = Gulp();
            Play(gulp, 0.7f, 1f);
            Vector3 c = f.transform.position + Vector3.up * 1.0f;
            // зелёно-золотые огоньки и листья спиралью вокруг пьющего (цикл, ~1,5 с)
            Transform ha = new GameObject("HealFx").transform;
            ha.SetParent(f.transform, false); ha.localPosition = Vector3.up * 0.78f;
            // 24 unique frames on a 6x4 atlas, played once to avoid looping.
            FxSprite hs = FxSprite.Spawn("fx_heal_hd", ha.position,
                1.55f, 6, 4, 24, 24f, false, 1.03f);
            if (hs != null)
            {
                hs.follow = ha;
                hs.tint = new Color(0.90f, 1f, 0.83f, 0.92f);
                hs.grow = 0.24f;
            }
            Destroy(ha.gameObject, 1.12f);
            Flash(c, 0.9f, new Color(1f, 0.72f, 0.3f, 0.35f), 0.5f, false);
            SparkLight(c, new Color(1f, 0.75f, 0.4f), 1.6f, 3f, 0.5f);
            if (softMat == null) return;
            GameObject g = new GameObject("HealMotes");
            g.transform.position = f.transform.position + Vector3.up * 0.3f;
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = false; main.duration = 0.4f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.startColor = new Color(1f, 0.78f, 0.38f, 1f);
            main.gravityModifier = -0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)26) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 10f; sh.radius = 0.32f;
            g.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.6f, 0.3f), 1f) },
                       new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            ParticleSystemRenderer r = g.GetComponent<ParticleSystemRenderer>();
            r.material = Additive(ref glowMat, SoftSprites.Fog);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        /// <summary>Клинок выдернут: влажное скольжение (резонансный шум, частота уходит вниз) с бульканьем и короткий «туп».</summary>
        static AudioClip Squelch()
        {
            int n = (int)(Rate * 0.5f);
            float[] d = new float[n];
            System.Random rnd = new System.Random(37);
            float low = 0f, band = 0f, peak = 1e-4f, jitter = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = (float)(rnd.NextDouble() * 2.0 - 1.0);
                float fc = 500f + 2200f * Mathf.Exp(-t * 6f);
                float f = 2f * Mathf.Sin(Mathf.PI * fc / Rate);
                low += f * band; float high = x - low - 0.22f * band; band += f * high;
                jitter += 0.002f * ((float)(rnd.NextDouble() * 2.0 - 1.0) - jitter * 0.1f);
                float bub = 0.45f + 0.55f * Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * (19f + 30f * t) * t + jitter * 40f));
                float env = Mathf.Min(1f, t * 90f) * Mathf.Exp(-t * 6.5f);
                float thup = Mathf.Sin(2f * Mathf.PI * 72f * t) * Mathf.Exp(-t * 24f) * Mathf.Min(1f, t * 600f);
                float v = band * bub * env * 0.8f + thup * 0.7f;
                d[i] = v; peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            for (int i = 0; i < n; i++) d[i] *= 0.85f / peak;
            return Make("squelch", d);
        }

        static AudioClip Thump(float len, float f0, float f1, float noiseAmt)
        {
            int n = (int)(Rate * len);
            float[] d = new float[n];
            System.Random rnd = new System.Random(11);
            float phase = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(f0, f1, Mathf.Clamp01(t / len));
                phase += 2f * Mathf.PI * f / Rate;
                float body = Mathf.Sin(phase) * Mathf.Exp(-t * (len > 0.5f ? 3f : 16f));
                float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
                lp += 0.25f * (noise - lp);
                float crack = lp * Mathf.Exp(-t * 38f) * noiseAmt;
                d[i] = Mathf.Clamp(body * 0.9f + crack, -1f, 1f);
            }
            return Make("thump", d);
        }

        /// <summary>
        /// Лязг клинков: моды стальной полосы (1 : 2,756 : 5,404 — свободные концы) для двух клинков с разной основной частотой,
        /// у каждой моды пара чуть расстроенных тонов (биения — «звенит»), щелчок удара (шум с ВЧ) и короткий «тук» корпуса.
        /// </summary>
        static AudioClip Clang()
        {
            int n = (int)(Rate * 1.1f);
            float[] d = new float[n];
            float[] f0 = { 1180f, 1415f };
            float[] ratio = { 1f, 2.756f, 5.404f };
            float[] dec = { 2.6f, 5.5f, 10f };
            float[] amp = { 1f, 0.55f, 0.3f };
            System.Random rnd = new System.Random(5);
            float lp = 0f, peak = 1e-4f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, v = 0f;
                for (int b = 0; b < f0.Length; b++)
                    for (int k = 0; k < ratio.Length; k++)
                    {
                        float f = f0[b] * ratio[k];
                        if (f > Rate * 0.45f) continue;
                        float e = Mathf.Exp(-t * dec[k] * (b == 0 ? 1f : 1.3f)) * amp[k] * (b == 0 ? 1f : 0.7f);
                        v += e * (Mathf.Sin(2f * Mathf.PI * (f - 1.3f) * t) + Mathf.Sin(2f * Mathf.PI * (f + 1.3f) * t + 0.7f)) * 0.5f;
                    }
                float nz = (float)(rnd.NextDouble() * 2.0 - 1.0);
                lp += 0.35f * (nz - lp);
                v += (nz - lp) * Mathf.Exp(-t * 600f) * 1.4f;                       // щелчок
                v += Mathf.Sin(2f * Mathf.PI * 310f * t) * Mathf.Exp(-t * 45f) * 0.5f;   // «тук»
                v *= Mathf.Min(1f, t * 3000f);
                d[i] = v; peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            for (int i = 0; i < n; i++) d[i] *= 0.9f / peak;
            return Make("clang", d);
        }
    }
}
