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
        AudioClip swish, hit, hitHeavy, clang, boom, drum, heart, whoosh, step, growl, stepWet;
        AudioSource stepVoice;
        ParticleSystem footFx;
        ParticleSystem petals;
        AudioSource ambient;
        Material fxMat, softMat, splatMat, fogMat;
        const int MaxDecals = 220;
        readonly List<Decal> decals = new List<Decal>();
        int decalIdx;
        struct Decal { public Transform t; public Renderer r; public float born, life, size, grow; }
        MaterialPropertyBlock mpb;

        class Tracked
        {
            public WeaponController w; public SwingPhase last; public TrailRenderer trail;
            public FighterController f; public ParticleSystem aura; public Renderer[] blade; public float blood;
            public float stepT; public bool wasGrounded = true;
        }
        readonly List<Tracked> tracked = new List<Tracked>();

        void Awake()
        {
            Instance = this;
            voices = new AudioSource[6];
            for (int i = 0; i < voices.Length; i++) { voices[i] = gameObject.AddComponent<AudioSource>(); voices[i].spatialBlend = 0f; voices[i].playOnAwake = false; }
            swish = Swish(); hit = Thump(0.22f, 140f, 55f, 0.55f); hitHeavy = Thump(0.34f, 100f, 38f, 0.9f); clang = Clang(); boom = Thump(1.0f, 70f, 28f, 0.8f);
            drum = Drum(); heart = Heart(); whoosh = Swish(); step = Step(); growl = Growl(); stepWet = StepWet();
            stepVoice = gameObject.AddComponent<AudioSource>(); stepVoice.spatialBlend = 0f; stepVoice.playOnAwake = false;   // шаги не отнимают голоса у ударов
            ambient = gameObject.AddComponent<AudioSource>();
            ambient.clip = Wind(); ambient.loop = true; ambient.volume = 0.22f; ambient.spatialBlend = 0f; ambient.Play();
            Shader sh = Shader.Find("Sprites/Default");
            if (sh != null)
            {
                fxMat = new Material(sh);
                softMat = new Material(sh); softMat.mainTexture = SoftSprites.Disc;
                splatMat = new Material(sh); Texture2D bs = Resources.Load<Texture2D>("FX/blood_splat"); splatMat.mainTexture = bs != null ? bs : SoftSprites.Splat;
                fogMat = new Material(sh); fogMat.mainTexture = SoftSprites.Fog;
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
                WeaponTrail.Attach(w, isPlayer ? new Color(0.55f, 0.8f, 1f) : new Color(0.85f, 0.35f, 1f));
                if (!isPlayer && owner != null) MakeEyes(owner);
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
            m.SetColor("_TintColor", new Color(0.9f, 0.3f, 1f, 0.9f));
            eyeMats.Add(m);
            Mesh q = QuadMesh();
            for (int s = -1; s <= 1; s += 2)
            {
                GameObject g = new GameObject("Eye");
                g.transform.SetParent(rig.head, true);
                g.transform.position = rig.head.position + f.transform.forward * 0.1f + f.transform.right * (0.034f * s) + Vector3.up * rig.eyeHeight;
                g.transform.localScale = Vector3.one * 0.07f / Mathf.Max(0.01f, rig.head.lossyScale.x);
                g.AddComponent<MeshFilter>().sharedMesh = q;
                MeshRenderer r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                g.AddComponent<GlowSprite>().flicker = 0.25f;
            }
        }

        public void SetAuraColor(FighterController f, Color c)
        {
            for (int i = 0; i < eyeMats.Count; i++) eyeMats[i].SetColor("_TintColor", new Color(c.r, c.g, c.b, 0.9f));
            for (int i = 0; i < tracked.Count; i++)
                if (tracked[i].f == f && tracked[i].aura != null) { var m = tracked[i].aura.main; c.a = 0.7f; m.startColor = c; }
        }

        /// <summary>Клинок в крови: каждое попадание окрашивает лезвие, к новому раунду оно снова чистое.</summary>
        public void BloodyBlade(WeaponController w, float amount)
        {
            for (int i = 0; i < tracked.Count; i++) if (tracked[i].w == w) tracked[i].blood = Mathf.Clamp01(tracked[i].blood + amount);
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

        void Update()
        {
            for (int i = 0; i < tracked.Count; i++)
            {
                Tracked t = tracked[i];
                if (t.w == null) continue;
                SwingPhase ph = t.w.Phase;
                if (ph != t.last)
                {
                    if (ph == SwingPhase.Strike) { Play(swish, 0.55f, Random.Range(0.9f, 1.15f)); if (t.trail != null) { t.trail.Clear(); t.trail.emitting = true; } }
                    if ((ph == SwingPhase.Recovery || ph == SwingPhase.Idle) && t.trail != null) t.trail.emitting = false;
                    t.last = ph;
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
                if (t.aura != null && t.f != null)
                {
                    var em = t.aura.emission;
                    bool rage = DuelManager.Instance != null && DuelManager.Instance.Enraged && t.f == DuelManager.Instance.enemy;
                    em.rateOverTime = t.f.Dead ? 0f : (rage ? 45f : (t.f.KiFull ? 18f : 0f));
                }
                if (t.blade != null && mpb != null)
                {
                    Color c = Color.Lerp(Color.white, new Color(0.5f, 0.06f, 0.06f), t.blood * 0.85f);
                    for (int k = 0; k < t.blade.Length; k++)
                    {
                        Renderer r = t.blade[k];
                        if (r == null) continue;
                        r.GetPropertyBlock(mpb);
                        mpb.SetColor("_Color", c);
                        r.SetPropertyBlock(mpb);
                    }
                }
            }
        }

        /// <summary>Удар тайко: начало раунда, «БОЙ!».</summary>
        public void Taiko(float vol = 1f, float pitch = 1f) { Play(drum, vol, pitch); }
        /// <summary>Рык нежити перед боем.</summary>
        public void Growl(float pitch = 1f) { Play(growl, 0.85f, pitch); }
        /// <summary>Порыв лепестков сакуры (сильный удар, KO).</summary>
        public void PetalBurst(int n)
        {
            if (petals == null) { GameObject g = GameObject.Find("Petals"); if (g != null) petals = g.GetComponent<ParticleSystem>(); }
            if (petals != null) petals.Emit(n);
        }
        /// <summary>Стук сердца на низком здоровье.</summary>
        public void Heartbeat() { Play(heart, 0.7f, 1f); }
        /// <summary>Рывок.</summary>
        public void DashSound() { Play(whoosh, 0.45f, 0.6f); }

        void Play(AudioClip c, float vol, float pitch)
        {
            if (c == null || voices == null) return;
            AudioSource a = voices[voiceIdx]; voiceIdx = (voiceIdx + 1) % voices.Length;
            a.pitch = pitch; a.volume = vol; a.clip = c; a.Play();
        }

        /// <summary>Попадание. dir — направление удара (скорость клинка), strength ~0.4…2 (урон/крит/убийство).</summary>
        public void OnHit(Vector3 point, Vector3 normal, Vector3 dir, float strength, bool blocked, bool kill, Transform bleedAt)
        {
            if (blocked) { Play(clang, 0.8f, Random.Range(0.9f, 1.2f)); Sparks(point, normal, 16); return; }
            Play(strength > 1f ? hitHeavy : hit, 0.95f, Random.Range(0.92f, 1.08f));
            SlashFlash(point, dir, strength);
            Blood(point, normal, dir, strength, kill);
            if (bleedAt != null) Bleed(bleedAt, Mathf.Lerp(1.2f, 3.5f, Mathf.Clamp01(strength - 0.4f)), strength);
            if (kill) { Play(boom, 1f, 1f); PetalBurst(60); }
            else if (strength > 1.2f) PetalBurst(12);
        }

        // ---------------------------------------------------------------- кровь

        void Blood(Vector3 pos, Vector3 normal, Vector3 dir, float strength, bool kill)
        {
            if (softMat == null) return;
            if (dir.sqrMagnitude < 1e-4f) dir = -normal;
            dir.Normalize();
            Vector3 cone = (dir * 0.75f + normal * 0.45f + Vector3.up * 0.35f).normalized;
            int count = Mathf.RoundToInt(Mathf.Lerp(50f, 150f, Mathf.Clamp01(strength - 0.3f)) * (kill ? 1.6f : 1f));

            // 1) струя брызг: вытянутые капли по ходу полёта
            Spray(pos, cone, count, 3.5f * Mathf.Lerp(0.9f, 1.4f, Mathf.Clamp01(strength - 0.5f)), 36f);
            // 2) веер вперёд по ходу клинка (длинные тонкие капли)
            Spray(pos, dir, Mathf.RoundToInt(count * 0.4f), 6.5f * Mathf.Lerp(0.9f, 1.3f, Mathf.Clamp01(strength - 0.5f)), 14f);
            // 3) кровавая взвесь
            Mist(pos, cone, 10 + Mathf.RoundToInt(10 * strength), 0.9f);
            // 4) пятна на земле там, куда долетели капли
            int splats = Mathf.RoundToInt(14 * strength) + (kill ? 18 : 0);
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
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.35f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 0.0f, 0.02f), new Color(0.85f, 0.04f, 0.05f));
            main.gravityModifier = 1.1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)count) });
            var sp = ps.shape; sp.shapeType = ParticleSystemShapeType.Cone; sp.angle = angle; sp.radius = 0.03f;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.4f)));
            var rend = g.GetComponent<ParticleSystemRenderer>();
            rend.material = softMat;
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.05f; rend.lengthScale = 2.4f;
            ps.Play();
        }

        /// <summary>
        /// Стопа встала на землю (из походки HeroRigDriver): глухой шаг по камню, в дождь — шлепок и брызги из-под подошвы,
        /// на сухом — лёгкая пыль. k ~0.3…1.5: длина шага / притоп в ударе. Частицы из одной общей системы (без мусора на шаг).
        /// </summary>
        public void Footstep(Vector3 pos, float k)
        {
            bool wet = Weather.Instance != null && Weather.Instance.Raining;
            if (stepVoice != null)
            {
                stepVoice.pitch = Random.Range(0.85f, 1.12f);
                stepVoice.PlayOneShot(step, Mathf.Clamp(0.12f + 0.16f * k, 0.1f, 0.45f));
                if (wet && stepWet != null) stepVoice.PlayOneShot(stepWet, Mathf.Clamp(0.08f + 0.12f * k, 0.06f, 0.35f));
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
                else
                {
                    v.y = Random.Range(0.05f, 0.25f);
                    ep.velocity = v * 0.25f; ep.startSize = Random.Range(0.05f, 0.11f); ep.startLifetime = Random.Range(0.5f, 0.9f);
                    ep.startColor = new Color(0.38f, 0.34f, 0.3f, 0.22f);
                }
                footFx.Emit(ep, 1);
            }
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

        /// <summary>Пыль из-под ног: рывок, отбрасывание, падение.</summary>
        public void Dust(Vector3 pos, Vector3 dir, int count)
        {
            if (softMat == null) return;
            GameObject g = new GameObject("Dust");
            g.transform.position = pos;
            Vector3 d = (dir.normalized * 0.8f + Vector3.up * 0.6f);
            g.transform.rotation = Quaternion.LookRotation(d.sqrMagnitude > 1e-4f ? d : Vector3.up);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = false; main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startColor = new Color(0.38f, 0.34f, 0.3f, 0.3f);   // тёплая серо-бурая пыль
            main.gravityModifier = -0.02f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)count) });
            var sp = ps.shape; sp.shapeType = ParticleSystemShapeType.Cone; sp.angle = 70f; sp.radius = 0.12f;
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 1.3f)));
            g.GetComponent<ParticleSystemRenderer>().material = fogMat != null ? fogMat : softMat;
            ps.Play();
        }

        void Mist(Vector3 pos, Vector3 dir, int count, float life)
        {
            GameObject g = new GameObject("BloodMist");
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
            g.transform.SetParent(at, false);
            g.transform.localPosition = Vector3.zero;
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = true; main.duration = seconds;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.055f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.4f, 0f, 0.02f), new Color(0.8f, 0.03f, 0.04f));
            main.gravityModifier = 1.3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = Mathf.Lerp(18f, 55f, Mathf.Clamp01(strength - 0.4f));
            var sp = ps.shape; sp.shapeType = ParticleSystemShapeType.Sphere; sp.radius = 0.08f;
            var rend = g.GetComponent<ParticleSystemRenderer>();
            rend.material = softMat; rend.renderMode = ParticleSystemRenderMode.Stretch; rend.velocityScale = 0.04f; rend.lengthScale = 2f;
            ps.Play();
            StartCoroutine(StopBleed(ps, g, seconds));
        }

        /// <summary>Фонтан крови из шеи после обезглавливания: мощная струя вверх, затухающая за несколько секунд.</summary>
        public void Fountain(Transform neck, float seconds)
        {
            if (softMat == null || neck == null) return;
            GameObject g = new GameObject("Fountain");
            g.transform.SetParent(neck, false);
            g.transform.localPosition = Vector3.zero;
            g.transform.rotation = Quaternion.LookRotation(Vector3.up);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = true; main.duration = seconds;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 3.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 0f, 0.02f), new Color(0.85f, 0.04f, 0.05f));
            main.gravityModifier = 1.4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            var em = ps.emission;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 160f), new Keyframe(0.3f, 90f), new Keyframe(1f, 10f)));
            var sp = ps.shape; sp.shapeType = ParticleSystemShapeType.Cone; sp.angle = 14f; sp.radius = 0.03f;
            var rend = g.GetComponent<ParticleSystemRenderer>();
            rend.material = softMat; rend.renderMode = ParticleSystemRenderMode.Stretch; rend.velocityScale = 0.05f; rend.lengthScale = 2f;
            ps.Play();
            Play(hitHeavy, 1f, 0.55f);
            StartCoroutine(FountainDecals(neck, seconds));
            StartCoroutine(StopBleed(ps, g, seconds));
        }

        System.Collections.IEnumerator FountainDecals(Transform neck, float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end && neck != null)
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
        void SlashFlash(Vector3 p, Vector3 dir, float strength)
        {
            if (fxMat == null) return;
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
        }

        /// <summary>Заряд супер-удара: воронка светящихся частиц втягивается в бойца.</summary>
        public void SuperCharge(Vector3 pos)
        {
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
            Play(whoosh, 0.5f, 1.4f);
        }

        /// <summary>Шар отбит клинком.</summary>
        public void Deflect(Vector3 pos, FighterController by)
        {
            Play(clang, 0.9f, 1.3f);
            Sparks(pos, Vector3.up, 24);
            TrailRenderer[] trs = null;
            foreach (EnergyProjectile e in FindObjectsByType<EnergyProjectile>(FindObjectsSortMode.None))
                if (e.owner == by) { trs = e.GetComponents<TrailRenderer>(); foreach (TrailRenderer tr in trs) tr.Clear(); }
        }

        /// <summary>Клинки столкнулись: сноп искр во все стороны и звонкий лязг.</summary>
        public void Clash(Vector3 pos)
        {
            Play(clang, 1f, Random.Range(0.75f, 0.9f));
            Sparks(pos, Vector3.up, 28);
            Sparks(pos, Random.onUnitSphere, 18);
        }

        void Sparks(Vector3 pos, Vector3 normal, int count)
        {
            if (softMat == null) return;
            GameObject g = new GameObject("Sparks");
            g.transform.position = pos;
            g.transform.rotation = Quaternion.LookRotation(normal.sqrMagnitude > 1e-4f ? normal : Vector3.up);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = false; main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
            main.startColor = new Color(1f, 0.85f, 0.45f);
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)count) });
            var sp = ps.shape; sp.shapeType = ParticleSystemShapeType.Cone; sp.angle = 50f; sp.radius = 0.02f;
            var rend = g.GetComponent<ParticleSystemRenderer>();
            rend.material = softMat; rend.renderMode = ParticleSystemRenderMode.Stretch; rend.velocityScale = 0.06f; rend.lengthScale = 2.5f;
            ps.Play();
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
            float stretch = dir.sqrMagnitude > 1e-4f ? Random.Range(1.0f, 1.8f) : 1f;
            d.size = size; d.grow = grow;
            d.t.localScale = new Vector3(size, size * stretch, 1f) * (grow > 0f ? 0.1f : 1f);
            d.born = Time.unscaledTime + delay; d.life = life;
            d.t.gameObject.SetActive(false);
            decals[decalIdx] = d;
        }

        void LateUpdate()
        {
            for (int i = 0; i < decals.Count; i++)
            {
                Decal d = decals[i];
                if (d.t == null) continue;
                float age = Time.unscaledTime - d.born;
                if (age < 0f) continue;
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

        static AudioClip Clang()
        {
            int n = (int)(Rate * 0.7f);
            float[] d = new float[n];
            float[] fr = { 880f, 1320f, 1870f, 2650f, 3400f };
            float[] dc = { 6f, 8f, 10f, 13f, 18f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, v = 0f;
                for (int k = 0; k < fr.Length; k++) v += Mathf.Sin(2f * Mathf.PI * fr[k] * t) * Mathf.Exp(-t * dc[k]) / (1f + k * 0.4f);
                d[i] = v * 0.5f * Mathf.Min(1f, t * 400f);
            }
            return Make("clang", d);
        }
    }
}
