using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Накопление удара: боец замирает, под ногами разворачивается светящийся узор, вокруг поднимается энергия; узор и выплеск
    /// растут по трём ступеням (0,5 / 1,1 / 1,7 с), каждая — вспышка, гул и дрожь. Отпустил — рывок к врагу и сокрушительный
    /// удар: урон ×1,5 / ×2 / ×2,6, по равновесию сильнее; на третьей ступени — ещё и волна по площадке (блок не спасает,
    /// только прыжок или рывок). Ступень стоит духа (20 / 35 / 50). Удар по накапливающему срывает накопление.
    /// Игрок — удержанием кнопки «ДУХ» (короткое нажатие — по-прежнему шар), враги — они и сёгун в поздних фазах.
    /// </summary>
    public class ChargeSystem : MonoBehaviour
    {
        public static ChargeSystem Instance { get; private set; }
        public static readonly float[] LevelAt = { 0.5f, 1.1f, 1.7f };
        public static readonly float[] KiCost = { 20f, 35f, 50f };
        public static readonly float[] DmgMul = { 1.5f, 2.0f, 2.6f };

        class Charge
        {
            public FighterController f; public float start; public int level; public Color col;
            public Transform sigil, anchor, early, pillar, pool; public Material sigilMat, earlyMat, pillarMat, poolMat; public ParticleSystem rise; public FxSprite flame, inhale, embers; public Light light; public float pillarAspect = 1.3f; public int pillarFrames = 1;
            public Transform draw, emberAnchor; public Material drawMat;
        }
        readonly Dictionary<FighterController, Charge> active = new Dictionary<FighterController, Charge>();
        readonly Dictionary<FighterController, int> strikeSwing = new Dictionary<FighterController, int>();
        readonly Dictionary<FighterController, int> strikeLevel = new Dictionary<FighterController, int>();
        readonly Dictionary<FighterController, float> waveAt = new Dictionary<FighterController, float>();

        void Awake() { Instance = this; }

        public static void Ensure(GameObject host) { if (Instance == null) host.AddComponent<ChargeSystem>(); }

        public bool IsCharging(FighterController f) { return f != null && active.ContainsKey(f); }
        public int Level(FighterController f) { Charge c; return active.TryGetValue(f, out c) ? c.level : -1; }
        public float Progress(FighterController f) { Charge c; return active.TryGetValue(f, out c) ? Mathf.Clamp01((Time.time - c.start) / LevelAt[2]) : 0f; }

        public Color ColorOf(FighterController f)
        {
            DuelManager dm = DuelManager.Instance;
            if (dm != null && dm.player == f) return new Color(0.45f, 0.85f, 1f);
            return Ladder.Current.aura;
        }

        public bool Begin(FighterController f)
        {
            if (f == null || active.ContainsKey(f) || !f.CanAct || f.Ki < KiCost[0]) return false;
            if (f.weapon != null && f.weapon.Phase != SwingPhase.Idle) return false;
            Charge c = new Charge { f = f, start = Time.time, level = -1, col = ColorOf(f) };
            Build(c);
            active[f] = c;
            f.Charging = true;
            if (Juice.Instance != null) Juice.Instance.Regrip();
            return true;
        }

        public void Cancel(FighterController f)
        {
            Charge c;
            if (f == null || !active.TryGetValue(f, out c)) return;
            Cleanup(c); active.Remove(f); f.Charging = false;
        }

        /// <summary>Отпустить: если набрана хоть первая ступень — рывок и сокрушительный удар; иначе ничего (дух не тратится).</summary>
        public bool Release(FighterController f, AttackType type = AttackType.Overhead)
        {
            Charge c;
            if (f == null || !active.TryGetValue(f, out c)) return false;
            int lvl = c.level;
            Cleanup(c); active.Remove(f); f.Charging = false;
            if (lvl < 0) return false;
            while (lvl > 0 && f.Ki < KiCost[lvl]) lvl--;
            if (f.Ki < KiCost[lvl]) return false;
            f.AddKi(-KiCost[lvl]);
            FighterController t = f.opponent;
            if (t != null)
            {
                Vector3 d = t.transform.position - f.transform.position; d.y = 0f;
                if (d.magnitude > 1.15f && d.magnitude < 4.5f) f.Teleport(t.transform.position - d.normalized * 1.05f);   // рывок к врагу
            }
            if (!f.Attack(type, lvl >= 1)) return false;   // со второй ступени блок не держит
            strikeSwing[f] = f.weapon != null ? f.weapon.SwingId : -1;
            strikeLevel[f] = lvl;
            if (lvl >= 2) waveAt[f] = Time.time + 0.18f;
            Vector3 hitAt = f.transform.position + f.transform.forward * 0.9f;
            GroundFlash.Spawn("FX/crack_glow", hitAt, 1.4f + 0.9f * lvl, 1.6f + 1.1f * lvl, c.col, 1.3f + 0.3f * lvl, 0f, true);
            GroundFlash.SpawnSheet("FX/shock_dust", hitAt, 2.0f + 1.0f * lvl, 3.2f + 1.6f * lvl, c.col, 0.55f, 4, 1, 4);
            // выплеск: вспышка → купол → столб → угли (16 кадров), низ кадра — по земле
            float es = 2.2f + 0.8f * lvl;
            FxSprite er = FxSprite.Spawn("eruption", hitAt + Vector3.up * (es * 0.31f), es, 4, 4, 16, 22f, false, 0.8f + 0.1f * lvl);
            if (er != null) er.tint = Color.Lerp(c.col, Color.white, 0.35f);
            if (lvl >= 2)
            {
                FxSprite lb = FxSprite.Spawn("lightning_burst", hitAt + Vector3.up * 1.0f, 3.2f, 4, 1, 4, 12f, false, 0.36f);
                if (lb != null) lb.tint = Color.Lerp(c.col, Color.white, 0.5f);
            }
            if (Juice.Instance != null) { Juice.Instance.Taiko(1f, 0.75f + 0.1f * lvl); Juice.Instance.Dust(f.transform.position + Vector3.up * 0.05f, f.transform.forward, 14 + 8 * lvl); }
            if (CameraOrbit.Instance != null) { CameraOrbit.Instance.Punch(2f + 1.5f * lvl); CameraOrbit.Instance.Shake(0.03f + 0.03f * lvl); }
            Haptics.Pulse(50 + 30 * lvl);
            return true;
        }

        /// <summary>Множитель урона накопленного удара (DuelManager при попадании этого замаха; срабатывает один раз).</summary>
        public float ConsumeMul(FighterController a, out int level)
        {
            level = -1;
            int sw, lv;
            if (a == null || a.weapon == null || !strikeSwing.TryGetValue(a, out sw) || sw != a.weapon.SwingId || !strikeLevel.TryGetValue(a, out lv)) return 1f;
            strikeSwing.Remove(a); level = lv;
            if (Juice.Instance != null) Juice.Instance.XSlash(a.transform.position + a.transform.forward * 0.8f + Vector3.up * 1.1f);
            return DmgMul[lv];
        }

        // ---------------- визуал ----------------

        void Build(Charge c)
        {
            Shader add = Shader.Find("Legacy Shaders/Particles/Additive");
            // узор на полу: свой у героя (кристальный компас) и у врагов; пока нет атласа — золотая печать
            Texture2D sig = Resources.Load<Texture2D>(SigilFor(c.f));
            if (sig == null) sig = Resources.Load<Texture2D>("FX/seal_circle");
            if (sig != null && add != null)
            {
                GameObject g = new GameObject("ChargeSigil");
                g.AddComponent<MeshFilter>().sharedMesh = Quad();
                MeshRenderer r = g.AddComponent<MeshRenderer>();
                c.sigilMat = new Material(add) { mainTexture = sig };
                r.sharedMaterial = c.sigilMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                c.sigil = g.transform;
                c.sigil.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
            // ранняя ступень узора (горит только ядро) — гаснет по мере того, как раскрывается полный
            c.early = GroundQuad("ChargeSigilEarly", Resources.Load<Texture2D>(SigilFor(c.f) + "_early"), add, out c.earlyMat);
            // пятно света под ногами
            c.pool = GroundQuad("ChargePool", Resources.Load<Texture2D>("FX/glow_pool"), add, out c.poolMat);
            // столб ауры: вертикальная плоскость, всегда повёрнута к камере
            // анимированный столб (4 кадра цикла), запасной — статичный
            Texture2D pt = Resources.Load<Texture2D>(PillarFor(c.f).Replace("pillar_", "pillar_loop_"));
            if (pt != null) c.pillarFrames = 4; else pt = Resources.Load<Texture2D>(PillarFor(c.f));
            if (pt != null && add != null)
            {
                GameObject g = new GameObject("ChargePillar");
                g.AddComponent<MeshFilter>().sharedMesh = PillarQuad();
                MeshRenderer r = g.AddComponent<MeshRenderer>();
                c.pillarMat = new Material(add) { mainTexture = pt };
                r.sharedMaterial = c.pillarMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                c.pillar = g.transform;
                c.pillarAspect = pt.height / Mathf.Max(1f, pt.width / (float)c.pillarFrames);
                if (c.pillarFrames > 1) c.pillarMat.mainTextureScale = new Vector2(1f / c.pillarFrames, 1f);
            }
            // светом рисуется узор: точка → кольца → спицы → мандала (12 кадров), потом разряды по ободу
            c.draw = GroundQuad("ChargeSigilDraw", Resources.Load<Texture2D>("FX/sigil_draw"), add, out c.drawMat);
            if (c.drawMat != null) c.drawMat.mainTextureScale = new Vector2(0.25f, 0.25f);
            // поднимающаяся энергия: искры и струйки вверх по кругу узора
            GameObject pg = new GameObject("ChargeRise");
            c.rise = pg.AddComponent<ParticleSystem>();
            c.rise.Stop();
            var main = c.rise.main;
            main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(c.col, Color.white, 0.5f), c.col);
            main.maxParticles = 300; main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = -0.15f;
            var em = c.rise.emission; em.rateOverTime = 30f;
            var sh = c.rise.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6f; sh.radius = 0.6f; sh.radiusThickness = 0.35f;
            pg.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);   // основание конуса — кольцо на земле, частицы бьют вверх
            var col = c.rise.colorOverLifetime; col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            var pr = pg.GetComponent<ParticleSystemRenderer>();
            if (add != null) { Material pm = new Material(add) { mainTexture = SoftSprites.Disc }; pm.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f)); pr.sharedMaterial = pm; }
            pr.renderMode = ParticleSystemRenderMode.Stretch; pr.velocityScale = 0.08f; pr.lengthScale = 1.5f;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            c.rise.Play();
            // пламя ауры вокруг бойца (3 кадра из атласа)
            c.flame = FxSprite.Spawn("aura_flame", c.f.transform.position + Vector3.up * 0.8f, 1.4f, 3, 1, 3, 10f, true);
            c.anchor = new GameObject("ChargeAnchor").transform;
            c.anchor.SetParent(c.f.transform, false); c.anchor.localPosition = Vector3.up * 0.95f;
            if (c.flame != null) { c.flame.follow = c.anchor; c.flame.tint = new Color(c.col.r, c.col.g, c.col.b, 0.6f); }
            // энергия втягивается в бойца (спираль) и угли поднимаются от земли
            c.inhale = FxSprite.Spawn("charge_inhale", c.anchor.position, 1.3f, 4, 1, 4, 12f, true);
            if (c.inhale != null) { c.inhale.follow = c.anchor; c.inhale.tint = new Color(c.col.r, c.col.g, c.col.b, 0.3f); }
            c.emberAnchor = new GameObject("ChargeEmbers").transform;
            c.emberAnchor.SetParent(c.f.transform, false); c.emberAnchor.localPosition = Vector3.up * 0.7f;
            c.embers = FxSprite.Spawn("embers_loop", c.emberAnchor.position, 1.4f, 4, 1, 4, 10f, true);
            if (c.embers != null) { c.embers.follow = c.emberAnchor; c.embers.tint = Color.Lerp(c.col, Color.white, 0.3f); }
            // свет растёт со ступенями
            GameObject lg = new GameObject("ChargeLight");
            c.light = lg.AddComponent<Light>(); c.light.type = LightType.Point; c.light.color = c.col; c.light.range = 3f; c.light.intensity = 0f; c.light.shadows = LightShadows.None;
        }

        string SigilFor(FighterController f)
        {
            DuelManager dm = DuelManager.Instance;
            if (dm != null && dm.player == f) return "FX/sigil_ice";
            int s = Ladder.Index;
            return s == 2 ? "FX/sigil_flame" : s == 3 ? "FX/sigil_sun" : "FX/sigil_storm";
        }

        string PillarFor(FighterController f) { return SigilFor(f).Replace("sigil_", "pillar_"); }

        Transform GroundQuad(string name, Texture2D tex, Shader add, out Material mat)
        {
            mat = null;
            if (tex == null || add == null) return null;
            GameObject g = new GameObject(name);
            g.AddComponent<MeshFilter>().sharedMesh = Quad();
            MeshRenderer r = g.AddComponent<MeshRenderer>();
            mat = new Material(add) { mainTexture = tex };
            r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            return g.transform;
        }

        static Mesh pillarQuad;
        static Mesh PillarQuad()
        {
            if (pillarQuad != null) return pillarQuad;
            pillarQuad = new Mesh { name = "PillarQuad" };
            pillarQuad.vertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, 1f, 0f), new Vector3(-0.5f, 1f, 0f) };
            pillarQuad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            pillarQuad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            pillarQuad.RecalculateBounds();
            return pillarQuad;
        }

        static Mesh quad;
        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "SigilQuad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateBounds();
            return quad;
        }

        void Cleanup(Charge c)
        {
            if (c.sigil != null) Destroy(c.sigil.gameObject);
            if (c.sigilMat != null) Destroy(c.sigilMat);
            if (c.rise != null) { var m = c.rise.main; m.loop = false; m.stopAction = ParticleSystemStopAction.Destroy; c.rise.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
            if (c.flame != null) Destroy(c.flame.gameObject);
            if (c.light != null) Destroy(c.light.gameObject);
            if (c.anchor != null) Destroy(c.anchor.gameObject);
            if (c.draw != null) Destroy(c.draw.gameObject);
            if (c.drawMat != null) Destroy(c.drawMat);
            if (c.inhale != null) Destroy(c.inhale.gameObject);
            if (c.embers != null) Destroy(c.embers.gameObject);
            if (c.emberAnchor != null) Destroy(c.emberAnchor.gameObject);
            if (c.early != null) Destroy(c.early.gameObject);
            if (c.earlyMat != null) Destroy(c.earlyMat);
            if (c.pool != null) Destroy(c.pool.gameObject);
            if (c.poolMat != null) Destroy(c.poolMat);
            if (c.pillar != null) Destroy(c.pillar.gameObject);
            if (c.pillarMat != null) Destroy(c.pillarMat);
        }

        float hintCheck;
        void Update()
        {
            DuelManager dm = DuelManager.Instance;
            // подсказка игроку (первые разы): духа хватает на накопление
            if (dm != null && Time.time > hintCheck && dm.State == DuelState.Fighting && dm.player != null && dm.player.Ki >= KiCost[1] && PlayerPrefs.GetInt("hint_charge", 0) < 2)
            {
                hintCheck = Time.time + 60f;
                PlayerPrefs.SetInt("hint_charge", PlayerPrefs.GetInt("hint_charge", 0) + 1);
                dm.Announce("ДУХ НАКОПЛЕН", new Color(0.45f, 0.85f, 1f), "держи «ШАР» — узор растёт, удар крепнет");
            }
            List<FighterController> drop = null;
            foreach (KeyValuePair<FighterController, Charge> kv in active)
            {
                Charge c = kv.Value; FighterController f = c.f;
                if (f == null || f.Dead || f.Stunned || (dm != null && dm.State != DuelState.Fighting)) { (drop ?? (drop = new List<FighterController>())).Add(kv.Key); continue; }
                float t = Time.time - c.start;
                int lvl = t >= LevelAt[2] ? 2 : t >= LevelAt[1] ? 1 : t >= LevelAt[0] ? 0 : -1;
                while (lvl >= 0 && f.Ki < KiCost[lvl]) lvl--;   // духа не хватает — выше не поднимется
                if (lvl > c.level)
                {
                    c.level = lvl;
                    // вспышка ступени: кольцо, барабан, дрожь
                    if (Juice.Instance != null) { Juice.Instance.Taiko(0.7f + 0.15f * lvl, 1.2f + 0.15f * lvl); Juice.Instance.ChargePulse(f.transform.position, c.col, 1.2f + 0.8f * lvl); }
                    GroundFlash.Spawn("FX/shock_ring", f.transform.position, 1.0f, 3.2f + 1.4f * lvl, c.col, 0.5f, 0f);
                    FxSprite fl = FxSprite.Spawn("charge_flecks", f.transform.position + Vector3.up * 1.0f, 1.6f + 0.6f * lvl, 1, 1, 1, 1f, false, 0.45f);
                    if (fl != null) { fl.tint = Color.Lerp(c.col, Color.white, 0.4f); fl.grow = 1.5f; }
                    if (lvl >= 2) GroundFlash.SpawnSheet("FX/lightning_burst", f.transform.position, 3.4f, 4.2f, c.col, 0.4f, 4, 1, 4);   // третья ступень — разряд по земле
                    if (CameraOrbit.Instance != null) CameraOrbit.Instance.Shake(0.02f + 0.02f * lvl);
                    Haptics.Pulse(25 + 15 * lvl);
                }
                float k = Mathf.Clamp01(t / LevelAt[2]);
                float pulse = 1f + 0.06f * Mathf.Sin(Time.time * (8f + 6f * Mathf.Max(0, c.level)));
                if (c.sigil != null)
                {
                    float s = Mathf.Lerp(0.9f, 3.4f, 1f - (1f - k) * (1f - k)) * pulse;
                    c.sigil.position = new Vector3(f.transform.position.x, 0.022f, f.transform.position.z);
                    c.sigil.localScale = new Vector3(s, s, 1f);
                    c.sigil.Rotate(0f, 0f, (40f + 140f * k) * Time.deltaTime, Space.Self);
                    // свой цветной узор — без подкраски; запасная печать — в цвет бойца
                    float a = Mathf.Clamp01(t / 0.25f) * (c.early != null ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.75f, k)) : 0.55f + 0.45f * k);
                    Color tc = c.early != null ? new Color(0.5f, 0.5f, 0.5f, 0.5f) : new Color(c.col.r * 0.5f, c.col.g * 0.5f, c.col.b * 0.5f, 0.5f);
                    c.sigilMat.SetColor("_TintColor", new Color(tc.r, tc.g, tc.b, tc.a * a * (0.8f + 0.2f * pulse)));
                    if (c.early != null)
                    {
                        c.early.position = c.sigil.position + Vector3.up * 0.002f;
                        float se = Mathf.Lerp(1.1f, s * 0.62f, k);
                        c.early.localScale = new Vector3(se, se, 1f);
                        c.early.Rotate(0f, 0f, -(60f + 90f * k) * Time.deltaTime, Space.Self);   // против хода полного узора
                        float ea = Mathf.Clamp01(t / 0.2f) * (1f - Mathf.InverseLerp(0.45f, 0.95f, k));
                        c.earlyMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f * ea));
                    }
                }
                if (c.draw != null)
                {
                    int fr; float da;
                    if (t < 0.6f) { fr = Mathf.Min(11, (int)(t / 0.6f * 12f)); da = 1f; }
                    else if (c.level >= 2) { fr = 12 + (int)(Time.time * 14f) % 4; da = 0.9f; }
                    else { fr = 11; da = Mathf.Lerp(1f, 0.4f, Mathf.Clamp01((t - 0.6f) / 0.5f)); }
                    c.drawMat.mainTextureOffset = new Vector2((fr % 4) * 0.25f, 0.75f - (fr / 4) * 0.25f);
                    float sd = Mathf.Lerp(0.9f, 3.4f, 1f - (1f - k) * (1f - k)) * pulse * 1.16f;
                    c.draw.position = new Vector3(f.transform.position.x, 0.026f, f.transform.position.z);
                    c.draw.localScale = new Vector3(sd, sd, 1f);
                    if (c.sigil != null) c.draw.rotation = c.sigil.rotation;
                    Color dc = Color.Lerp(c.col, Color.white, 0.35f);
                    c.drawMat.SetColor("_TintColor", new Color(dc.r * 0.5f, dc.g * 0.5f, dc.b * 0.5f, 0.5f * da));
                }
                if (c.inhale != null) { c.inhale.size = Mathf.Lerp(1.3f, 2.8f, k) * pulse; c.inhale.tint = new Color(c.col.r, c.col.g, c.col.b, 0.25f + 0.45f * k); }
                if (c.embers != null)
                {
                    c.embers.size = Mathf.Lerp(1.2f, 2.8f, k);
                    if (c.emberAnchor != null) c.emberAnchor.localPosition = Vector3.up * (c.embers.size * 0.5f);
                    Color ec = Color.Lerp(c.col, Color.white, 0.3f); c.embers.tint = new Color(ec.r, ec.g, ec.b, 0.4f + 0.6f * k);
                }
                if (c.pool != null)
                {
                    float sp = Mathf.Lerp(1.6f, 4.6f, k);
                    c.pool.position = new Vector3(f.transform.position.x, 0.018f, f.transform.position.z);
                    c.pool.localScale = new Vector3(sp, sp, 1f);
                    c.poolMat.SetColor("_TintColor", new Color(c.col.r * 0.5f, c.col.g * 0.5f, c.col.b * 0.5f, (0.12f + 0.25f * k) * Mathf.Clamp01(t / 0.3f)));
                }
                if (c.pillar != null)
                {
                    float w = Mathf.Lerp(0.9f, 2.2f, k) * pulse;
                    c.pillar.position = new Vector3(f.transform.position.x, 0.0f, f.transform.position.z);
                    c.pillar.localScale = new Vector3(w, w * c.pillarAspect * Mathf.Lerp(0.7f, 1.25f, k), 1f);
                    Camera cam = Camera.main;
                    if (cam != null)
                    {
                        Vector3 look = c.pillar.position - cam.transform.position; look.y = 0f;
                        if (look.sqrMagnitude > 1e-4f) c.pillar.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
                    }
                    float pa = Mathf.Clamp01((t - 0.15f) / 0.4f) * (0.2f + 0.7f * k);
                    c.pillarMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f * pa));
                    if (c.pillarFrames > 1) c.pillarMat.mainTextureOffset = new Vector2(((int)(Time.time * 12f) % c.pillarFrames) / (float)c.pillarFrames, 0f);
                }
                if (c.rise != null)
                {
                    c.rise.transform.position = new Vector3(f.transform.position.x, 0.03f, f.transform.position.z);
                    var em = c.rise.emission; em.rateOverTime = 30f + 160f * k;
                    var sh = c.rise.shape; sh.radius = Mathf.Lerp(0.45f, 1.5f, k);
                    var main = c.rise.main; main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f + 2f * k, 3.2f + 4f * k);
                }
                if (c.flame != null) { c.flame.size = Mathf.Lerp(1.1f, 2.6f, k) * pulse; c.flame.tint = new Color(c.col.r, c.col.g, c.col.b, 0.35f + 0.55f * k); }
                if (c.light != null) { c.light.transform.position = f.transform.position + Vector3.up * 0.6f; c.light.intensity = (0.6f + 2.4f * k) * pulse; c.light.range = 2.5f + 2.5f * k; }
                f.SetMoveVector(Vector2.zero);
            }
            if (drop != null) foreach (FighterController f in drop) Cancel(f);
            // волна третьей ступени — по площадке вокруг удара
            if (waveAt.Count > 0)
            {
                List<FighterController> done = null;
                foreach (KeyValuePair<FighterController, float> kv in waveAt)
                {
                    if (Time.time < kv.Value) continue;
                    (done ?? (done = new List<FighterController>())).Add(kv.Key);
                    FighterController a = kv.Key;
                    if (a == null || dm == null) continue;
                    Vector3 at = a.transform.position + a.transform.forward * 0.9f;
                    if (Juice.Instance != null) Juice.Instance.Slam(at);
                    GroundFlash.SpawnSheet("FX/shock_dust", at, 5.0f, 7.0f, ColorOf(a), 0.7f, 4, 1, 4);
                    GroundFlash.Spawn("FX/shock_ring", at, 1.0f, 6.2f, ColorOf(a), 0.6f, 0f);
                    GroundFlash.Spawn("FX/glow_pool", at, 2.0f, 5.0f, ColorOf(a), 0.5f, 0f);
                    FighterController t = a.opponent;
                    if (t != null && !t.Dead)
                    {
                        Vector3 d = t.transform.position - at; d.y = 0f;
                        if (d.magnitude < 2.8f && t.Grounded && !t.Dashing) dm.ForceHit(t, a, 12f, 40f, at);
                    }
                }
                if (done != null) foreach (FighterController f in done) waveAt.Remove(f);
            }
        }
    }
}
