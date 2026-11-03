using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Накопление удара: боец замирает, под ногами разворачивается светящийся узор, вокруг поднимается энергия; узор и выплеск
    /// растут по трём ступеням (0,5 / 1,1 / 1,7 с; каждая — свой облик: I — светом рисуется узор, II — раскрывается цветной узор и столб,
    /// III — по ободу и земле бьют молнии, столб в полный рост), каждая — вспышка, гул и дрожь. Отпустил — рывок к врагу и сокрушительный
    /// удар: урон ×1,5 / ×2 / ×2,6, по равновесию сильнее; на третьей ступени — ещё и волна по площадке (блок не спасает,
    /// только прыжок или рывок). Ступень стоит духа (15 / 25 / 40). Удар по накапливающему срывает накопление.
    /// Игрок — удержанием кнопки «ДУХ» (короткое нажатие — по-прежнему шар), враги — они и сёгун в поздних фазах.
    /// </summary>
    public class ChargeSystem : MonoBehaviour
    {
        public static ChargeSystem Instance { get; private set; }
        public static readonly float[] LevelAt = { 0.5f, 1.1f, 1.7f, 2.6f };
        public static readonly float[] KiCost = { 15f, 25f, 40f, 60f };
        public static readonly float[] DmgMul = { 1.5f, 2.0f, 2.6f, 3.4f };
        public const int Max = 3;
        public static float BigReleaseAt = -10f;   // удар с III ступени и выше — для линий скорости на экране   // IV ступень — «ЯРОСТЬ»: аура в багровый, второй узор и столб, гул, молнии и трещины
        static readonly Color Crimson = new Color(1f, 0.07f, 0.05f);

        class Charge
        {
            public FighterController f; public float start; public int level; public Color col;
            public Transform sigil, anchor, early, pillar, pool; public Material sigilMat, earlyMat, pillarMat, poolMat; public ParticleSystem rise; public FxSprite flame, inhale, embers; public Light light; public float pillarAspect = 1.3f; public int pillarFrames = 1, pillarCols = 1, pillarRows = 1;
            public Transform draw, emberAnchor; public Material drawMat;
            public float kv, popAt = -10f, boltAt;
            public float ex, crackAt, beatAt; public Color baseCol;   // ex — ярость (IV ступень) 0…1
            public Transform rage, ragePillar; public Material rageMat, ragePillarMat; public int ragePillarFrames = 1, ragePillarCols = 1, ragePillarRows = 1; public float ragePillarAspect = 1f; public bool ragePulse, bodyAura;   // kv — облик ступени (0…1, догоняет ступень), popAt — вспышка новой ступени
        }
        readonly Dictionary<FighterController, Charge> active = new Dictionary<FighterController, Charge>();
        readonly Dictionary<FighterController, int> strikeSwing = new Dictionary<FighterController, int>();
        readonly Dictionary<FighterController, int> strikeLevel = new Dictionary<FighterController, int>();
        readonly Dictionary<FighterController, float> waveAt = new Dictionary<FighterController, float>();
        readonly Dictionary<FighterController, int> waveLvl = new Dictionary<FighterController, int>();
        readonly Dictionary<FighterController, Vector2Int> slashLvl = new Dictionary<FighterController, Vector2Int>();   // x — ступень, y — замах   // полумесяц энергии — на фазе удара

        void Awake() { Instance = this; }

        public static void Ensure(GameObject host) { if (Instance == null) host.AddComponent<ChargeSystem>(); }

        public bool IsCharging(FighterController f) { return f != null && active.ContainsKey(f); }

        /// <summary>Окно встречи духа: оба выпустили накопленный удар не дальше друг от друга, с.</summary>
        public const float ClashWindow = 0.45f;
        readonly Dictionary<FighterController, float> releaseAt = new Dictionary<FighterController, float>();

        /// <summary>Лёгкий удар по накапливающему игроку не срывает накопление, а отнимает часть прогресса (с).</summary>
        public void Knock(FighterController f, float seconds)
        {
            Charge c;
            if (f == null || !active.TryGetValue(f, out c)) return;
            c.start = Mathf.Min(Time.time, c.start + seconds);
            int lvl = -1; float tt = Time.time - c.start;
            for (int i = 0; i <= Max; i++) if (tt >= LevelAt[i]) lvl = i;
            if (lvl < c.level) c.level = lvl;
        }
        public int Level(FighterController f) { Charge c; return active.TryGetValue(f, out c) ? c.level : -1; }
        public float Progress(FighterController f) { Charge c; return active.TryGetValue(f, out c) ? Mathf.Clamp01((Time.time - c.start) / LevelAt[Max]) : 0f; }

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
            // встреча духа: противник выпустил накопленный удар только что (его удар ещё не дошёл) — клинки сшибаются
            float ta; int tl, tsw;
            if (t != null && releaseAt.TryGetValue(t, out ta) && Time.time - ta < ClashWindow && strikeLevel.TryGetValue(t, out tl)
                && strikeSwing.TryGetValue(t, out tsw) && t.weapon != null && tsw == t.weapon.SwingId && DuelManager.Instance != null)
            {
                strikeSwing.Remove(f); strikeSwing.Remove(t); releaseAt.Remove(t); waveAt.Remove(t); slashLvl.Remove(t);
                DuelManager.Instance.ChargeClash(f, lvl, t, tl);
                return true;
            }
            releaseAt[f] = Time.time;
            if (lvl >= 2) { waveAt[f] = Time.time + 0.18f; waveLvl[f] = lvl; BigReleaseAt = Time.unscaledTime; }
            if (lvl >= 1) slashLvl[f] = new Vector2Int(lvl, f.weapon != null ? f.weapon.SwingId : -1);
            Vector3 hitAt = f.transform.position + f.transform.forward * 0.9f;
            GroundFlash.Spawn("FX/crack_glow", hitAt, 1.4f + 0.9f * lvl, 1.6f + 1.1f * lvl, c.col, 1.3f + 0.3f * lvl, 0f, true);
            GroundFlash.SpawnSheet("FX/shock_dust", hitAt, 2.0f + 1.0f * lvl, 3.2f + 1.6f * lvl, c.col, 0.55f, 4, 1, 4);
            // выплеск: вспышка → купол → столб → угли (16 кадров), низ кадра — по земле
            float es = 2.2f + 0.8f * lvl;
            FxSprite er = FxSprite.Spawn("eruption", hitAt + Vector3.up * (es * 0.31f), es, 4, 4, 16, 22f, false, 0.8f + 0.1f * lvl);
            if (er != null) er.tint = Color.Lerp(c.col, Color.white, 0.35f);
            if (lvl >= 2)
            {
                FxSprite lb = FxSprite.Spawn("lightning_burst", hitAt + Vector3.up * 1.0f, 3.2f + 1.2f * (lvl - 2), 4, 1, 4, 12f, false, 0.36f);
                if (lb != null) lb.tint = Color.Lerp(c.col, Color.white, 0.5f);
            }
            if (lvl >= Max)
            {
                // ярость: второй, багровый выплеск выше первого, удар по экрану
                FxSprite e2 = FxSprite.Spawn("eruption", hitAt + Vector3.up * (es * 1.3f * 0.31f), es * 1.3f, 4, 4, 16, 22f, false, 1.1f);
                if (e2 != null) e2.tint = new Color(1f, 0.15f, 0.1f, 0.9f);
                GroundFlash.Spawn("FX/rage_cracks", hitAt, 4.5f, 5.5f, Color.white, 2.2f, 0f, true);
                FxSprite st = FxSprite.Spawn("rage_star", hitAt + Vector3.up * 1.1f, 3.6f, 1, 1, 1, 1f, false, 0.3f);
                if (st != null) st.grow = 0.6f;
                FxSprite em = FxSprite.Spawn("rage_embers", hitAt + Vector3.up * 1.4f, 3.4f, 1, 1, 1, 1f, false, 1.0f);
                if (em != null) em.grow = 0.6f;
                PostFX.Kick(0.012f);
                if (Juice.Instance != null) Juice.Instance.Growl(0.7f);
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
            // свет на полу — свой шейдер без «мягких частиц» (те гасят плоскость у самой поверхности — узор был невидим)
            Shader add = Shader.Find("BladePath/GroundAdditive");
            if (add == null) add = Shader.Find("Legacy Shaders/Particles/Additive");
            Shader soft = Shader.Find("Legacy Shaders/Particles/Additive");
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
            // ярость (IV): второй узор и столб — багровые; пока нет своего атласа — огненный лотос и огненный столб
            Texture2D rs = Resources.Load<Texture2D>("FX/sigil_rage_pulse");   // 4 кадра пульса
            c.ragePulse = rs != null;
            if (rs == null) rs = Resources.Load<Texture2D>("FX/sigil_rage") ?? Resources.Load<Texture2D>("FX/sigil_flame");
            c.rage = GroundQuad("ChargeRageSigil", rs, add, out c.rageMat);
            if (c.ragePulse && c.rageMat != null) c.rageMat.mainTextureScale = new Vector2(0.25f, 1f);
            Texture2D rp = Resources.Load<Texture2D>("FX/pillar_loop_rage") ?? Resources.Load<Texture2D>("FX/pillar_loop_flame");
            if (rp != null && add != null)
            {
                GameObject g = new GameObject("ChargeRagePillar");
                g.AddComponent<MeshFilter>().sharedMesh = PillarQuad();
                MeshRenderer r = g.AddComponent<MeshRenderer>();
                c.ragePillarMat = new Material(add) { mainTexture = rp };
                r.sharedMaterial = c.ragePillarMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                c.ragePillar = g.transform;
                SheetDims(rp, out c.ragePillarCols, out c.ragePillarRows);
                c.ragePillarFrames = c.ragePillarCols * c.ragePillarRows;
                c.ragePillarAspect = (rp.height / (float)c.ragePillarRows) / Mathf.Max(1f, rp.width / (float)c.ragePillarCols);
                c.ragePillarMat.mainTextureScale = new Vector2(1f / c.ragePillarCols, 1f / c.ragePillarRows);
                c.ragePillarMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0f));
            }
            if (c.rageMat != null) c.rageMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0f));
            // анимированный столб (4 кадра цикла), запасной — статичный
            Texture2D pt = Resources.Load<Texture2D>(PillarFor(c.f).Replace("pillar_", "pillar_loop_"));
            if (pt != null) { SheetDims(pt, out c.pillarCols, out c.pillarRows); c.pillarFrames = c.pillarCols * c.pillarRows; }
            else pt = Resources.Load<Texture2D>(PillarFor(c.f));
            if (pt != null && add != null)
            {
                GameObject g = new GameObject("ChargePillar");
                g.AddComponent<MeshFilter>().sharedMesh = PillarQuad();
                MeshRenderer r = g.AddComponent<MeshRenderer>();
                c.pillarMat = new Material(add) { mainTexture = pt };
                r.sharedMaterial = c.pillarMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                c.pillar = g.transform;
                c.pillarAspect = (pt.height / (float)c.pillarRows) / Mathf.Max(1f, pt.width / (float)c.pillarCols);
                if (c.pillarFrames > 1) c.pillarMat.mainTextureScale = new Vector2(1f / c.pillarCols, 1f / c.pillarRows);
            }
            // светом рисуется узор: точка → кольца → спицы → мандала (12 кадров), потом разряды по ободу
            c.draw = GroundQuad("ChargeSigilDraw", Resources.Load<Texture2D>("FX/sigil_draw"), add, out c.drawMat);
            if (c.drawMat != null) c.drawMat.mainTextureScale = new Vector2(0.25f, 0.25f);
            // поднимающаяся энергия: искры и струйки вверх по кругу узора
            GameObject pg = new GameObject("ChargeRise");
            c.rise = pg.AddComponent<ParticleSystem>();
            c.rise.Stop();
            var main = c.rise.main;
            main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.045f);
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
            if (soft != null) { Material pm = new Material(soft) { mainTexture = SoftSprites.Disc }; pm.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f)); pr.sharedMaterial = pm; }
            pr.renderMode = ParticleSystemRenderMode.Stretch; pr.velocityScale = 0.04f; pr.lengthScale = 1f;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            c.rise.Play();
            // пламя ауры вокруг бойца (3 кадра из атласа)
            // пламя ауры вокруг фигуры (16 кадров цикла); запасное — 3 кадра
            c.flame = Resources.Load<Texture2D>("FX/aura_body") != null
                ? FxSprite.Spawn("aura_body", c.f.transform.position + Vector3.up * 0.95f, 2.1f, 4, 4, 16, 14f, true)
                : FxSprite.Spawn("aura_flame", c.f.transform.position + Vector3.up * 0.8f, 1.4f, 3, 1, 3, 10f, true);
            c.bodyAura = c.flame != null && c.flame.frames == 16;
            c.anchor = new GameObject("ChargeAnchor").transform;
            c.anchor.SetParent(c.f.transform, false); c.anchor.localPosition = Vector3.up * 0.95f;
            if (c.flame != null) { c.flame.follow = c.anchor; c.flame.tint = new Color(c.col.r, c.col.g, c.col.b, 0.6f); }
            // энергия втягивается в бойца (спираль) и угли поднимаются от земли
            c.inhale = FxSprite.Spawn("charge_inhale", c.anchor.position, 1.3f, 4, 2, 8, 14f, true);
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

        // лист кадров: 4 столбца квадратных кадров, строк — сколько поместится (4×1, 4×2, 4×4)
        static void SheetDims(Texture2D t, out int cols, out int rows)
        {
            cols = 4; rows = Mathf.Max(1, Mathf.RoundToInt(4f * t.height / Mathf.Max(1f, t.width)));
            if (t.width < t.height * 2 && rows == 1) cols = 1;   // одиночная картинка
        }
        static Vector2 FrameUV(int f, int cols, int rows) { return new Vector2((f % cols) / (float)cols, 1f - (f / cols + 1) / (float)rows); }

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
            if (c.rage != null) Destroy(c.rage.gameObject);
            if (c.rageMat != null) Destroy(c.rageMat);
            if (c.ragePillar != null) Destroy(c.ragePillar.gameObject);
            if (c.ragePillarMat != null) Destroy(c.ragePillarMat);
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

        void PoseVisuals(Charge c, FighterController f, float t)
        {
            float k = c.kv;
            if (c.baseCol.a <= 0f) c.baseCol = c.col;
            c.col = Color.Lerp(c.baseCol, Crimson, c.ex);   // ярость перекрашивает всю ауру в багровый
            float pulse = (1f + 0.06f * Mathf.Sin(Time.time * (8f + 6f * Mathf.Max(0, c.level)))) * (1f + 0.22f * Mathf.Exp(-(Time.time - c.popAt) * 7f));
            if (c.sigil != null)
            {
                float s = Mathf.Lerp(0.9f, 3.4f, 1f - (1f - k) * (1f - k)) * pulse;
                c.sigil.position = new Vector3(f.transform.position.x, 0.022f, f.transform.position.z);
                c.sigil.localScale = new Vector3(s, s, 1f);
                c.sigil.Rotate(0f, 0f, (40f + 140f * k) * Time.deltaTime, Space.Self);
                // свой цветной узор — без подкраски; запасная печать — в цвет бойца
                float a = Mathf.Clamp01(t / 0.25f) * (c.early != null ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 0.66f, k)) : 0.55f + 0.45f * k);   // II ступень — полный цветной узор
                Color tc = c.early != null ? new Color(0.5f, 0.5f, 0.5f, 0.5f) : new Color(c.col.r * 0.5f, c.col.g * 0.5f, c.col.b * 0.5f, 0.5f);
                c.sigilMat.SetColor("_TintColor", new Color(tc.r, tc.g, tc.b, tc.a * a * 0.62f * (0.8f + 0.2f * pulse) * (1f - 0.7f * c.ex)));   // в ярости прежний узор уступает багровому   // без пересвета в центре
                if (c.early != null)
                {
                    c.early.position = c.sigil.position + Vector3.up * 0.002f;
                    float se = Mathf.Lerp(1.1f, s * 0.62f, k);
                    c.early.localScale = new Vector3(se, se, 1f);
                    c.early.Rotate(0f, 0f, -(60f + 90f * k) * Time.deltaTime, Space.Self);   // против хода полного узора
                    float ea = Mathf.Clamp01(t / 0.2f) * Mathf.InverseLerp(0.12f, 0.33f, k) * (1f - Mathf.InverseLerp(0.5f, 0.72f, k));   // I ступень — ядро узора в цвете
                    c.earlyMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f * ea));
                }
            }
            if (c.draw != null)
            {
                int fr; float da;
                if (t < 0.6f) { fr = Mathf.Min(11, (int)(t / 0.6f * 12f)); da = 1f; }
                else if (c.level >= 2) { fr = 12 + (int)(Time.time * 14f) % 4; da = 0.55f; }
                else { fr = 11; da = Mathf.Lerp(0.8f, 0.12f, Mathf.Clamp01((t - 0.6f) / 0.5f)); }
                c.drawMat.mainTextureOffset = new Vector2((fr % 4) * 0.25f, 0.75f - (fr / 4) * 0.25f);
                float sd = Mathf.Lerp(0.9f, 3.4f, 1f - (1f - k) * (1f - k)) * pulse * 1.16f;
                c.draw.position = new Vector3(f.transform.position.x, 0.026f, f.transform.position.z);
                c.draw.localScale = new Vector3(sd, sd, 1f);
                if (c.sigil != null) c.draw.rotation = c.sigil.rotation;
                Color dc = Color.Lerp(c.col, Color.white, 0.35f);
                c.drawMat.SetColor("_TintColor", new Color(dc.r * 0.5f, dc.g * 0.5f, dc.b * 0.5f, 0.5f * da * (1f - 0.8f * c.ex)));
            }
            if (c.inhale != null) { c.inhale.size = Mathf.Lerp(1.3f, 2.8f, k) * pulse; c.inhale.tint = new Color(c.col.r, c.col.g, c.col.b, 0.2f + 0.3f * k); }
            if (c.embers != null)
            {
                c.embers.size = Mathf.Lerp(1.2f, 2.8f, k);
                if (c.emberAnchor != null) c.emberAnchor.localPosition = Vector3.up * (c.embers.size * 0.5f);
                Color ec = Color.Lerp(c.col, Color.white, 0.3f); c.embers.tint = new Color(ec.r, ec.g, ec.b, (0.3f + 0.4f * k) * Mathf.InverseLerp(0.2f, 0.4f, k));
            }
            if (c.pool != null)
            {
                float sp = Mathf.Lerp(1.6f, 4.6f, k);
                c.pool.position = new Vector3(f.transform.position.x, 0.018f, f.transform.position.z);
                c.pool.localScale = new Vector3(sp, sp, 1f);
                c.poolMat.SetColor("_TintColor", new Color(c.col.r * 0.5f, c.col.g * 0.5f, c.col.b * 0.5f, (0.03f + 0.06f * k) * Mathf.Clamp01(t / 0.3f)));
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
                float pa = Mathf.Clamp01((t - 0.15f) / 0.4f) * (0.15f + 0.45f * k);
                c.pillarMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f * pa * (1f - 0.75f * c.ex)));
                if (c.pillarFrames > 1) c.pillarMat.mainTextureOffset = FrameUV((int)(Time.time * 12f) % c.pillarFrames, c.pillarCols, c.pillarRows);
            }
            if (c.rise != null)
            {
                c.rise.transform.position = new Vector3(f.transform.position.x, 0.03f, f.transform.position.z);
                var em = c.rise.emission; em.rateOverTime = 20f + 70f * k;
                var sh = c.rise.shape; sh.radius = Mathf.Lerp(0.4f, 1.15f, k);
                var main = c.rise.main; main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f + 1f * k, 2.2f + 2f * k);
            }
            if (c.flame != null)
            {
                // аура по фигуре — в рост бойца (размер почти не меняется, растёт яркость); старое пламя — растёт
                c.flame.size = c.bodyAura ? Mathf.Lerp(2.05f, 2.3f, k) * (1f + 0.25f * (pulse - 1f)) : Mathf.Lerp(1.1f, 2.6f, k) * pulse;
                c.flame.tint = new Color(c.col.r, c.col.g, c.col.b, c.bodyAura ? 0.2f + 0.45f * k : 0.25f + 0.3f * k);
            }
            if (c.light != null) { c.light.color = c.col; c.light.transform.position = f.transform.position + Vector3.up * 0.6f; c.light.intensity = (0.4f + 1.2f * k + 1.0f * c.ex) * pulse; c.light.range = 2.5f + 2.5f * k + 2f * c.ex; }
            if (c.ex > 0.001f)
            {
                float jitter = 1f + 0.05f * Mathf.Sin(Time.time * 37f) + 0.03f * Mathf.Sin(Time.time * 61f);   // ярость дрожит
                if (c.rage != null)
                {
                    float sr = Mathf.Lerp(2.6f, 4.6f, c.ex) * pulse * jitter;
                    c.rage.position = new Vector3(f.transform.position.x, 0.03f, f.transform.position.z);
                    c.rage.localScale = new Vector3(sr, sr, 1f);
                    c.rage.Rotate(0f, 0f, -150f * Time.deltaTime, Space.Self);
                    c.rageMat.SetColor("_TintColor", c.ragePulse ? new Color(0.62f, 0.5f, 0.5f, 0.5f * c.ex) : new Color(0.8f, 0.32f, 0.3f, 0.5f * c.ex));
                    if (c.ragePulse) { int pf = (int)(Time.time * 12f) % 6; pf = pf < 4 ? pf : 6 - pf; c.rageMat.mainTextureOffset = new Vector2(pf * 0.25f, 0f); }   // пульс 0-1-2-3-2-1
                }
                if (c.ragePillar != null)
                {
                    float w2 = 3.0f * pulse * jitter;
                    c.ragePillar.position = new Vector3(f.transform.position.x, 0f, f.transform.position.z);
                    c.ragePillar.localScale = new Vector3(w2, w2 * c.ragePillarAspect * 1.35f * Mathf.Lerp(0.5f, 1f, c.ex), 1f);
                    if (c.pillar != null) c.ragePillar.rotation = c.pillar.rotation;
                    c.ragePillarMat.SetColor("_TintColor", new Color(0.62f, 0.5f, 0.5f, 0.5f * 0.75f * c.ex));
                    if (c.ragePillarFrames > 1) c.ragePillarMat.mainTextureOffset = FrameUV((int)(Time.time * 16f + 2) % c.ragePillarFrames, c.ragePillarCols, c.ragePillarRows);
                }
                if (c.flame != null) c.flame.size *= 1f + (c.bodyAura ? 0.12f : 0.5f) * c.ex;
                if (c.rise != null) { var em2 = c.rise.emission; em2.rateOverTime = 20f + 70f * k + 140f * c.ex; var m2 = c.rise.main; m2.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.45f), Crimson); }
            }
        }

        /// <summary>Повернуть столбы ауры к камере (в игре это делает PoseVisuals каждый кадр; здесь — для кадров CI).</summary>
        public void FacePillars(Camera cam)
        {
            if (cam == null) return;
            foreach (KeyValuePair<FighterController, Charge> kv in active)
            {
                Transform p = kv.Value.pillar;
                if (p == null) continue;
                Vector3 look = p.position - cam.transform.position; look.y = 0f;
                if (look.sqrMagnitude > 1e-4f) p.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            }
        }

        /// <summary>Для кадров CI (редактор, без игрового цикла): построить накопление и показать его на момент t.</summary>
        public void Preview(FighterController f, float t, int level)
        {
            Charge c = new Charge { f = f, start = 0f, level = level, col = ColorOf(f), kv = Mathf.Min(1f, (level + 1) / 3f), ex = level >= Max ? 1f : 0f };
            Build(c);
            active[f] = c;
            PoseVisuals(c, f, t);
            if (c.rise != null) c.rise.Simulate(1.2f, true, true);
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
                // враг копит — он раскрылся: дух игрока растёт быстрее (успеть накопить навстречу или ударить)
                if (dm != null && dm.player != null && f != dm.player && !dm.player.Dead) dm.player.AddKi(9f * Time.deltaTime);
                float t = Time.time - c.start;
                int lvl = t >= LevelAt[3] ? 3 : t >= LevelAt[2] ? 2 : t >= LevelAt[1] ? 1 : t >= LevelAt[0] ? 0 : -1;
                while (lvl >= 0 && f.Ki < KiCost[lvl]) lvl--;   // духа не хватает — выше не поднимется
                // облик ступени догоняет ступень; внутри ступени узор чуть подрастает к следующей (если на неё хватит духа)
                float kt = lvl < 0 ? 0.12f * Mathf.Clamp01(t / LevelAt[0]) : Mathf.Min(1f, (lvl + 1) / 3f);
                if (lvl >= 0 && lvl < 2 && f.Ki >= KiCost[lvl + 1]) kt += 0.07f * Mathf.Clamp01((t - LevelAt[lvl]) / (LevelAt[lvl + 1] - LevelAt[lvl]));
                c.kv = Mathf.MoveTowards(c.kv, kt, Time.deltaTime * 1.8f);
                c.ex = Mathf.MoveTowards(c.ex, lvl >= Max ? 1f : 0f, Time.deltaTime * (lvl >= Max ? 1.5f : 3f));   // багровый круг проступает, пока выжигается
                if (lvl > c.level)
                {
                    c.level = lvl; c.popAt = Time.time;
                    if (dm != null)
                    {
                        DuelManager.Popup sp = new DuelManager.Popup();
                        sp.world = f.transform.position + Vector3.up * 2.35f; sp.text = lvl == 0 ? "СТУПЕНЬ I" : lvl == 1 ? "СТУПЕНЬ II" : lvl == 2 ? "СТУПЕНЬ III" : "IV · ЯРОСТЬ";
                        sp.color = lvl >= Max ? new Color(1f, 0.25f, 0.18f) : Color.Lerp(c.col, Color.white, 0.35f); sp.born = Time.unscaledTime; sp.size = lvl >= Max ? 32f : 18f + 4f * lvl;
                        dm.Popups.Add(sp);
                    }
                    // вспышка ступени: кольцо, барабан, дрожь
                    if (Juice.Instance != null) { Juice.Instance.Taiko(0.7f + 0.15f * lvl, 1.2f + 0.15f * lvl); Juice.Instance.ChargePulse(f.transform.position, c.col, 1.2f + 0.8f * lvl); }
                    GroundFlash.Spawn("FX/shock_ring", f.transform.position, 1.0f, 3.2f + 1.4f * lvl, c.col, 0.5f, 0f);
                    FxSprite fl = FxSprite.Spawn("charge_flecks", f.transform.position + Vector3.up * 1.0f, 1.6f + 0.6f * lvl, 1, 1, 1, 1f, false, 0.45f);
                    if (fl != null) { fl.tint = Color.Lerp(c.col, Color.white, 0.4f); fl.grow = 1.5f; }
                    if (lvl >= 2) GroundFlash.SpawnSheet("FX/lightning_burst", f.transform.position, 3.4f, 4.2f, c.col, 0.4f, 4, 1, 4);   // третья ступень — разряд по земле
                    if (CameraOrbit.Instance != null) CameraOrbit.Instance.Shake(0.02f + 0.02f * lvl);
                    Haptics.Pulse(25 + 15 * lvl);
                    if (lvl >= Max)
                    {
                        // взрыв ярости: рёв, багровая волна с пылью, трещины, удар по экрану
                        if (Juice.Instance != null) { Juice.Instance.Growl(0.75f); Juice.Instance.Taiko(1.2f, 0.7f); Juice.Instance.ChargePulse(f.transform.position, Crimson, 4.5f); }
                        GroundFlash.SpawnSheet("FX/shock_dust", f.transform.position, 4f, 6.5f, Crimson, 0.7f, 4, 1, 4);
                        GroundFlash.Spawn("FX/rage_ring", f.transform.position, 1.5f, 7.5f, Color.white, 0.6f, 0f);
                        // круг выжигается в землю: искра → трещины-лучи → кольца и когти → вспышка (16 кадров за 0,7 с)
                        GroundFlash.SpawnSheet("FX/rage_ignite", f.transform.position, 4.4f, 4.6f, Color.white, 0.7f, 4, 4, 16);
                        GroundFlash.Spawn("FX/rage_cracks", f.transform.position, 3.5f, 4.2f, Color.white, 2.5f, 0f, true);
                        FxSprite re = FxSprite.Spawn("rage_embers", f.transform.position + Vector3.up * 1.3f, 3.0f, 1, 1, 1, 1f, false, 0.9f);
                        if (re != null) { re.grow = 0.5f; re.tint = new Color(1f, 1f, 1f, 0.9f); }
                        if (CameraOrbit.Instance != null) { CameraOrbit.Instance.Shake(0.1f); CameraOrbit.Instance.Punch(4f); }
                        PostFX.Kick(0.01f);
                        Haptics.Pulse(140);
                    }
                }
                // ярость держится: багровые молнии чаще, трещины, сердцебиение, земля дрожит
                if (c.level >= Max)
                {
                    if (Time.time >= c.crackAt) { c.crackAt = Time.time + Random.Range(0.6f, 0.9f); GroundFlash.Spawn("FX/rage_cracks", f.transform.position + new Vector3(Random.Range(-1.2f, 1.2f), 0f, Random.Range(-1.2f, 1.2f)), 1.6f, 2.0f, Color.white, 1.2f, 0f, true); }
                    if (Time.time >= c.beatAt) { c.beatAt = Time.time + 0.55f; if (Juice.Instance != null) Juice.Instance.Heartbeat(); if (f == (dm != null ? dm.player : null)) Haptics.Pulse(20); }
                    if (CameraOrbit.Instance != null) CameraOrbit.Instance.Shake(0.012f);
                }
                // третья ступень держится: разряды по земле, свет мигает
                if (c.level >= 2 && Time.time >= c.boltAt)
                {
                    c.boltAt = Time.time + (c.level >= Max ? Random.Range(0.15f, 0.3f) : Random.Range(0.35f, 0.6f));
                    Vector3 bp = f.transform.position + new Vector3(Random.Range(-0.4f, 0.4f), 0f, Random.Range(-0.4f, 0.4f));
                    if (c.level >= Max) GroundFlash.SpawnSheet("FX/rage_lightning", bp, 3.0f, 3.8f, Color.white, 0.3f, 4, 1, 4);
                    else GroundFlash.SpawnSheet("FX/lightning_burst", bp, 2.6f, 3.2f, c.col, 0.3f, 4, 1, 4);
                }
                PoseVisuals(c, f, t);
                f.SetMoveVector(Vector2.zero);
            }
            if (drop != null) foreach (FighterController f in drop) Cancel(f);
            // со II ступени: клинок выпускает огромный полумесяц энергии — в момент удара, по ходу взмаха
            if (slashLvl.Count > 0)
            {
                List<FighterController> sd = null;
                foreach (KeyValuePair<FighterController, Vector2Int> kv in slashLvl)
                {
                    FighterController a = kv.Key;
                    bool gone = a == null || a.weapon == null || kv.Value.y != a.weapon.SwingId;
                    if (!gone && a.weapon.Phase != SwingPhase.Strike) continue;
                    (sd ?? (sd = new List<FighterController>())).Add(a);
                    if (gone) continue;
                    int lv = kv.Value.x;
                    Color sc = lv >= Max ? Crimson : ColorOf(a);
                    float size = 2.4f + 0.7f * lv;
                    FxSprite s = FxSprite.Spawn("charge_slash", a.transform.position + a.transform.forward * 0.9f + Vector3.up * 1.15f, size, 4, 4, 16, 30f, false, 0.55f);
                    if (s != null)
                    {
                        s.tint = new Color(Mathf.Lerp(1f, sc.r, 0.6f), Mathf.Lerp(1f, sc.g, 0.6f), Mathf.Lerp(1f, sc.b, 0.6f), 0.9f);
                        Camera cam = Camera.main;
                        if (cam != null) s.flipX = Vector3.Dot(a.transform.forward, cam.transform.right) < 0f;   // удар влево — полумесяц зеркально
                    }
                    if (lv >= 2)
                    {
                        FxSprite s2 = FxSprite.Spawn("charge_slash", a.transform.position + a.transform.forward * 1.2f + Vector3.up * 1.1f, size * 1.25f, 4, 4, 16, 30f, false, 0.65f);   // эхо, крупнее и тише
                        if (s2 != null) { s2.tint = new Color(sc.r, sc.g, sc.b, 0.45f); if (s != null) s2.flipX = s.flipX; }
                    }
                }
                if (sd != null) foreach (FighterController a in sd) slashLvl.Remove(a);
            }
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
                    int wl; if (!waveLvl.TryGetValue(a, out wl)) wl = 2;
                    bool rage = wl >= Max;
                    Color wc = rage ? Crimson : ColorOf(a);
                    Vector3 at = a.transform.position + a.transform.forward * 0.9f;
                    if (Juice.Instance != null) Juice.Instance.Slam(at);
                    GroundFlash.SpawnSheet("FX/shock_dust", at, rage ? 7f : 5.0f, rage ? 9.5f : 7.0f, wc, 0.7f, 4, 1, 4);
                    if (rage) GroundFlash.Spawn("FX/rage_ring", at, 1.0f, 8.5f, Color.white, 0.6f, 0f);
                    else GroundFlash.Spawn("FX/shock_ring", at, 1.0f, 6.2f, wc, 0.6f, 0f);
                    GroundFlash.Spawn("FX/glow_pool", at, 2.0f, rage ? 7f : 5.0f, wc, 0.5f, 0f);
                    FighterController t = a.opponent;
                    if (t != null && !t.Dead)
                    {
                        Vector3 d = t.transform.position - at; d.y = 0f;
                        if (d.magnitude < (rage ? 3.8f : 2.8f) && t.Grounded && !t.Dashing) dm.ForceHit(t, a, rage ? 18f : 12f, rage ? 60f : 40f, at);
                    }
                }
                if (done != null) foreach (FighterController f in done) { waveAt.Remove(f); waveLvl.Remove(f); }
            }
        }
    }
}
