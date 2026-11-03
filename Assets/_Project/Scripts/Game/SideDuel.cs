using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Соратник на арене: если у героя есть живой соратник, у врага выходит свой — и эта вторая пара бьётся в стороне
    /// от основного боя (у дальнего края площадки, на стороне, противоположной главной схватке), не вмешиваясь в него.
    /// Удары второй пары считаются здесь (своё здоровье), клинки задевают только своего противника.
    ///
    /// Кто раньше победил в своей паре — отходит к краю арены и ждёт. Когда обе пары решены, выжившие сходятся:
    ///  • герой и соратник живы — победа; враг и его боец живы — поражение;
    ///  • герой победил, а соратник пал — боец врага вступает вместо павшего врага (облик и остаток здоровья — его);
    ///  • герой пал, а соратник жив — бой за соратником (ИИ): одолеет врага — победа, падёт — поражение.
    /// Павший соратник лежит до хижины травницы (Profile.AllyDown).
    /// </summary>
    public class SideDuel : MonoBehaviour
    {
        public static SideDuel Instance { get; private set; }
        /// <summary>Нововведение целиком (меню разработчика): выключено — ни найма в трактире, ни второй пары на арене.</summary>
        /// <summary>Сложность ИИ второй пары (0 ученик, 1 воин, 2 мастер) — меню разработчика.</summary>
        public static int AiLevel
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt("allies_lvl", 1), 0, 2); }
            set { PlayerPrefs.SetInt("allies_lvl", Mathf.Clamp(value, 0, 2)); PlayerPrefs.Save(); }
        }
        /// <summary>Отладка: бессмертие спутников — 0 нет, 1 соратник, 2 приспешник, 3 оба (здоровье не ниже 1).</summary>
        public static int ImmortalMode;
        public static readonly string[] ImmortalNames = { "НЕТ", "СОРАТНИК", "ПРИСПЕШНИК", "ОБА" };
        public static bool Enabled
        {
            get { return PlayerPrefs.GetInt("allies", 1) == 1; }
            set { PlayerPrefs.SetInt("allies", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public FighterController ally, foe;
        public EnemyAI allyAi, foeAi;
        /// <summary>Облик бойца врага в EnemySkins основного врага (для подмены, когда он вступает).</summary>
        public int foeModel = 4;

        public const string AllyName = "СОРАТНИК", FoeName = "ПРИСПЕШНИК";
        const float AnchorX = 2.0f, AnchorZ = 1.15f, EdgeX = 2.75f, EdgeZ = 1.6f;

        public bool On { get; private set; }
        /// <summary>Герой пал — бой продолжает соратник.</summary>
        public bool Takeover { get; private set; }
        public float AllyHp01 { get { return allyMax > 0f ? Mathf.Clamp01(allyHp / allyMax) : 0f; } }
        public float FoeHp01 { get { return foeMax > 0f ? Mathf.Clamp01(foeHp / foeMax) : 0f; } }

        float allyHp, foeHp, allyMax, foeMax, anchorX = AnchorX;
        FighterController sideWinner, mainLoser;
        float foeEnterAt = -1f;
        readonly List<FighterController> watchers = new List<FighterController>();
        readonly Dictionary<FighterController, Vector3> watchAt = new Dictionary<FighterController, Vector3>();
        readonly Dictionary<FighterController, int> lastSwing = new Dictionary<FighterController, int>();
        readonly List<DuelManager.Popup> popups = new List<DuelManager.Popup>();
        CombatResolver resolver;
        EnemyAI mainAi;
        bool trails;

        DuelManager Dm { get { return DuelManager.Instance; } }

        /// <summary>Этот боец сейчас стоит в стороне и ждёт (ввод игрока и ИИ его не ведут).</summary>
        public static bool Held(FighterController f) { return Instance != null && f != null && Instance.watchers.Contains(f); }

        void Awake()
        {
            Instance = this;
            foreach (FighterController f in new[] { ally, foe }) if (f != null) f.gameObject.SetActive(false);   // выходят в Setup, если соратник нанят
        }
        void OnDestroy() { if (resolver != null) resolver.HitRegistered -= OnHit; if (Instance == this) Instance = null; }

        EnemyAI MainAi()
        {
            if (mainAi == null && Dm != null)
                foreach (EnemyAI e in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) if (!e.autoPlayer && !e.sideFighter && e.self == Dm.enemy) mainAi = e;
            return mainAi;
        }

        /// <summary>Начало раунда (DuelManager.ResetDuel): связи бойцов — как обычно, вторая пара выходит, если соратник жив.</summary>
        public void Setup()
        {
            DuelManager dm = Dm;
            if (dm == null || dm.player == null || dm.enemy == null) return;
            ClearWatch();
            Takeover = false; sideWinner = null; mainLoser = null; foeEnterAt = -1f;
            dm.player.opponent = dm.enemy; dm.enemy.opponent = dm.player;
            EnemyAI ea = MainAi(); if (ea != null) ea.target = dm.player;
            On = Enabled && ally != null && foe != null && Profile.Ally >= 0 && !Profile.AllyDown;
            if (ally != null) ally.gameObject.SetActive(On);
            if (foe != null) foe.gameObject.SetActive(On);
            FighterController[] all = On ? new[] { dm.player, dm.enemy, ally, foe } : null;
            dm.player.avoid = all; dm.enemy.avoid = all;
            if (CameraOrbit.Instance != null) CameraOrbit.Instance.SetFocus(dm.player, dm.enemy);
            if (!On) return;
            if (resolver == null && CombatResolver.Instance != null) { resolver = CombatResolver.Instance; resolver.HitRegistered += OnHit; }
            if (!trails)
            {
                trails = true;
                WeaponTrail.Attach(ally.weapon, new Color(0.6f, 1f, 0.75f), "trail_ice");
                WeaponTrail.Attach(foe.weapon, new Color(0.85f, 0.35f, 1f), "trail_violet");
            }
            ally.avoid = all; foe.avoid = all;
            ally.opponent = foe; foe.opponent = ally;
            anchorX = AnchorX;
            Vector3 a = new Vector3(anchorX, 0f, AnchorZ);
            ally.SetHome(a + Vector3.left * 0.6f, Quaternion.Euler(0f, 90f, 0f));
            foe.SetHome(a + Vector3.right * 0.6f, Quaternion.Euler(0f, -90f, 0f));
            ally.ResetFighter(); foe.ResetFighter();
            allyMax = 110f + 15f * Profile.Cycle; foeMax = (90f + 12f * Ladder.Index) * Profile.CycleHp;
            allyHp = allyMax; foeHp = foeMax;
            lastSwing.Clear(); popups.Clear();
            Ai(allyAi, ally, foe); Ai(foeAi, foe, ally);
        }

        static void Ai(EnemyAI ai, FighterController self, FighterController target)
        {
            if (ai == null) return;
            ai.self = self; ai.target = target; ai.sideFighter = true; ai.hasAnchor = true; ai.hold = false;
            ai.aiEnabled = true; ai.difficultyOverride = AiLevel;
        }

        void Update()
        {
            DuelManager dm = Dm;
            if (!On || dm == null) return;
            if (resolver == null && CombatResolver.Instance != null) { resolver = CombatResolver.Instance; resolver.HitRegistered += OnHit; }
            bool fighting = dm.State == DuelState.Fighting;
            // раунд закончен — вторая пара замирает; во вступлении стоит на местах
            ally.Frozen = !fighting && dm.State != DuelState.Intro; foe.Frozen = ally.Frozen;
            if (!fighting || dm.Paused) return;
            if (allyAi != null) allyAi.difficultyOverride = AiLevel;
            if (foeAi != null) foeAi.difficultyOverride = AiLevel;

            // место второй пары — у дальнего края, на стороне, противоположной основному бою (с запасом, чтобы не металась)
            if (sideWinner == null && mainLoser == null && !Takeover)
            {
                float mx = (dm.player.transform.position.x + dm.enemy.transform.position.x) * 0.5f;
                float want = mx > 0.7f ? -AnchorX : mx < -0.7f ? AnchorX : anchorX;
                anchorX = Mathf.MoveTowards(anchorX, want, 0.5f * Time.deltaTime);
                Vector3 a = new Vector3(anchorX, 0f, AnchorZ);
                if (allyAi != null) allyAi.anchor = a;
                if (foeAi != null) foeAi.anchor = a;
            }

            // ждущие отходят к краю арены и смотрят на бой
            for (int i = 0; i < watchers.Count; i++)
            {
                FighterController w = watchers[i];
                if (w == null || w.Dead) continue;
                w.SetBlock(false);
                Vector3 d = watchAt[w] - w.transform.position; d.y = 0f;
                if (d.magnitude > 0.2f) w.SetMoveVector(new Vector2(d.x, d.z).normalized * 0.75f);
            }

            if (foeEnterAt > 0f && Time.time >= foeEnterAt) FoeEnters();
            for (int i = popups.Count - 1; i >= 0; i--) if (Time.unscaledTime - popups[i].born > 1f) popups.RemoveAt(i);
        }

        // ---------------------------------------------------------------- удары второй пары
        bool Mine(FighterController f) { return f != null && (f == ally || f == foe); }

        void OnHit(HitResult r)
        {
            DuelManager dm = Dm;
            if (!On || dm == null || dm.State != DuelState.Fighting || r.attacker == null || r.target == null) return;
            if (!Mine(r.attacker) && !Mine(r.target)) return;
            if (r.attacker.Dead || r.target.Dead || r.attacker.opponent != r.target) return;
            int id = r.contact.swingId, last;
            if (lastSwing.TryGetValue(r.attacker, out last) && last == id) return;   // один удар — одно попадание
            lastSwing[r.attacker] = id;
            AttackType type = r.attacker.weapon != null && r.attacker.weapon.CurrentProfile != null ? r.attacker.weapon.CurrentProfile.type : AttackType.Overhead;
            if (type == AttackType.LegSweep && !r.target.Grounded && r.target.transform.position.y > 0.12f) return;   // перепрыгнул подсечку
            float dmg = DuelManager.BaseDamage(type) * DuelManager.ZoneMul(r.zone) * (r.glancing ? 0.5f : 1f);
            if (r.attacker == dm.enemy) dmg *= Ladder.Current.dmgMul * Campaign.EnemyDmgMul;
            else if (r.attacker == foe) dmg *= 0.75f;
            else if (r.attacker == ally) dmg *= 0.8f;
            bool perilous = r.attacker.weapon != null && r.attacker.weapon.Perilous;
            Transform zt = r.contact.zone != null ? r.contact.zone.transform : null;

            if (r.target.Blocking && !perilous)
            {
                bool parry = r.target.BlockTime < r.target.ParryWindow;
                if (parry)
                {
                    if (r.attacker.weapon != null) r.attacker.weapon.ResetState();
                    if (r.target.weapon != null) r.target.weapon.Deflect(1f);
                    r.attacker.Stun(0.6f);
                    r.attacker.AddPosture(DuelManager.PostureDamage(type) * 0.9f);
                    r.attacker.Knockback(r.attacker.transform.position - r.target.transform.position, 1.4f);
                    if (Juice.Instance != null) Juice.Instance.Parry(r.contact.point, r.contact.normal);
                    Pop(r.contact.point + Vector3.up * 0.2f, "ПАРИРОВАНИЕ", new Color(0.4f, 0.9f, 1f), 20f);
                    return;
                }
                r.target.AddPosture(DuelManager.PostureDamage(type));
                r.target.Knockback(r.target.transform.position - r.attacker.transform.position, 0.9f);
                if (r.attacker.weapon != null) r.attacker.weapon.Rebound();
                if (r.target.weapon != null) r.target.weapon.Deflect(-0.6f);
                if (r.target == dm.enemy) { dm.AllyStrike(r.attacker, Mathf.Max(1f, dmg * 0.15f), r.contact.point, r.contact.normal, r.contact.bladeVelocity, null, true); return; }
                if (Juice.Instance != null) Juice.Instance.OnHit(r.contact.point, r.contact.normal, r.contact.bladeVelocity, 0.4f, true, false, null);
                Damage(r.target, Mathf.Max(1f, Mathf.Round(dmg * 0.15f)), r.contact.point, true);
                return;
            }
            if (r.target.PostureBroken) dmg *= 1.5f;   // поплыл — удар проходит сполна
            if (r.target == dm.enemy) { dm.AllyStrike(r.attacker, dmg, r.contact.point, r.contact.normal, r.contact.bladeVelocity, zt, false); return; }
            float hpLeft = r.target == ally ? allyHp : foeHp;
            bool kill = dmg >= hpLeft;
            if (Juice.Instance != null) Juice.Instance.OnHit(r.contact.point, r.contact.normal, r.contact.bladeVelocity, Mathf.Clamp(dmg / 20f + (kill ? 0.4f : 0f), 0.3f, 1.4f), false, kill, zt);
            if (!kill)
            {
                if (r.target.weapon != null && r.target.weapon.Phase == SwingPhase.Windup) r.target.weapon.ResetState();
                r.target.Stun(dmg >= 20f ? 0.5f : 0.35f);
                r.target.AddPosture(dmg * 0.6f);
            }
            Damage(r.target, Mathf.Round(dmg), r.contact.point, false);
        }

        /// <summary>Урон по бойцу второй пары не от клинка (удар по земле, волна накопления, ForceHit).</summary>
        public void ExternalHit(FighterController target, FighterController by, float dmg, float posture, Vector3 point)
        {
            if (!On || !Mine(target) || target.Dead) return;
            if (by != null && by.opponent != target) return;   // чужая пара — мимо
            Damage(target, Mathf.Round(dmg), point, true);
            if (!target.Dead) target.AddPosture(posture);
        }

        void Damage(FighterController t, float dmg, Vector3 point, bool chip)
        {
            bool imm = (ImmortalMode & (t == ally ? 1 : 2)) != 0;
            float floor = imm ? 1f : 0f;
            if (t == ally) allyHp = Mathf.Max(floor, allyHp - dmg); else foeHp = Mathf.Max(floor, foeHp - dmg);
            Pop(point + Vector3.up * 0.15f, ((int)dmg).ToString(), t == ally ? new Color(1f, 0.55f, 0.5f) : new Color(0.85f, 0.85f, 0.85f), chip ? 16f : 19f);
            if ((t == ally ? allyHp : foeHp) > 0f) return;
            t.Dead = true;
            if (t.weapon != null) t.weapon.ResetState();
            OnDeath(t);
        }

        void Pop(Vector3 at, string text, Color c, float size)
        {
            popups.Add(new DuelManager.Popup { world = at, text = text, color = c, born = Time.unscaledTime, size = size });
        }

        // ---------------------------------------------------------------- исходы
        void OnDeath(FighterController dead)
        {
            DuelManager dm = Dm;
            if (dead == ally)
            {
                Profile.SetAllyDown(true);   // до хижины травницы
                dm.Announce("СОРАТНИК ПАЛ", new Color(1f, 0.4f, 0.3f), Takeover ? null : "его выходит травница");
            }
            else dm.Announce("ПРИСПЕШНИК ПОВЕРЖЕН", new Color(0.55f, 0.9f, 1f), "соратник ждёт у края арены");
            if (Takeover)
            {
                // соратник продолжал бой за павшего героя — и пал
                Takeover = false;
                dm.FinishRound(dm.player);
                return;
            }
            sideWinner = dead == ally ? foe : ally;
            Resolve(false);
        }

        /// <summary>Основная пара решена (DuelManager.EndRound): true — раунд пока не кончается.</summary>
        public bool InterceptMainEnd(FighterController loser)
        {
            DuelManager dm = Dm;
            if (!On || dm == null || Takeover || mainLoser != null) return false;
            if (loser != dm.player && loser != dm.enemy) return false;
            mainLoser = loser;
            return Resolve(true);
        }

        bool Resolve(bool fromMain)
        {
            DuelManager dm = Dm;
            if (mainLoser == null)
            {
                // вторая пара решилась раньше: победитель отходит к краю и ждёт своей очереди
                Watch(sideWinner, dm.player, dm.enemy);
                return false;
            }
            FighterController mainWinner = mainLoser == dm.enemy ? dm.player : dm.enemy;
            mainWinner.Frozen = false;   // после воскрешения враг стоял замерший
            if (sideWinner == null)
            {
                // соратники ещё бьются: победитель главной пары отходит и смотрит
                Watch(mainWinner, ally, foe);
                if (CameraOrbit.Instance != null) CameraOrbit.Instance.SetFocus(ally, foe);
                dm.Announce(mainWinner == dm.player ? "СОРАТНИК ЕЩЁ БЬЁТСЯ" : "ВСЯ НАДЕЖДА — НА СОРАТНИКА", new Color(0.85f, 0.85f, 0.7f));
                return true;
            }
            bool pW = mainWinner == dm.player, aW = sideWinner == ally;
            if (pW == aW)
            {
                // обе пары за одной стороной — раунд решён
                ClearWatch();
                if (fromMain) return false;
                dm.FinishRound(pW ? dm.enemy : dm.player);
                return true;
            }
            if (pW)
            {
                // герой победил, соратник пал: боец врага вступает
                ClearWatch();
                Watch(foe, dm.player, dm.player);
                Watch(dm.player, foe, foe);
                foeEnterAt = Time.time + 1.1f;
                return true;
            }
            StartTakeover();
            return true;
        }

        void FoeEnters()
        {
            DuelManager dm = Dm;
            foeEnterAt = -1f;
            ClearWatch();
            Vector3 p = foe.transform.position; Quaternion q = foe.transform.rotation;
            foe.gameObject.SetActive(false);
            dm.player.opponent = dm.enemy; dm.enemy.opponent = dm.player;
            EnemyAI ea = MainAi(); if (ea != null) { ea.target = dm.player; ea.hold = false; }
            dm.FoeTakesOver(p, q, foeModel, FoeHp01, FoeName);
            if (CameraOrbit.Instance != null) CameraOrbit.Instance.SetFocus(dm.player, dm.enemy);
        }

        void StartTakeover()
        {
            DuelManager dm = Dm;
            ClearWatch();
            Takeover = true;
            FighterController e = dm.enemy;
            e.Frozen = false; ally.Frozen = false;
            e.opponent = ally; ally.opponent = e;
            EnemyAI ea = MainAi(); if (ea != null) { ea.target = ally; ea.hold = false; }
            if (allyAi != null) { allyAi.target = e; allyAi.hasAnchor = false; allyAi.hold = false; }
            if (CameraOrbit.Instance != null) CameraOrbit.Instance.SetFocus(ally, e);
            dm.Announce("БОЙ ПРОДОЛЖАЕТ СОРАТНИК", new Color(0.55f, 0.9f, 1f), "он ещё может спасти бой");
            if (Juice.Instance != null) Juice.Instance.Taiko(1f, 1f);
        }

        /// <summary>Отойти к краю арены (подальше от идущего боя) и смотреть на него.</summary>
        void Watch(FighterController w, FighterController a, FighterController b)
        {
            if (w == null || w.Dead) return;
            if (!watchers.Contains(w)) watchers.Add(w);
            Vector3 mid = (a.transform.position + b.transform.position) * 0.5f;
            float sx = Mathf.Abs(mid.x) > 0.3f ? -Mathf.Sign(mid.x) : (w.transform.position.x >= 0f ? 1f : -1f);
            watchAt[w] = new Vector3(sx * EdgeX, 0f, EdgeZ);
            if (w.weapon != null) w.weapon.ResetState();
            // смотрит на ближнего из идущего боя (по нему же и разворачивается)
            FighterController look = Vector3.Distance(w.transform.position, a.transform.position) <= Vector3.Distance(w.transform.position, b.transform.position) ? a : b;
            if (look != null && !look.Dead) w.opponent = look;
            EnemyAI ai = w == ally ? allyAi : w == foe ? foeAi : w == Dm.enemy ? MainAi() : null;
            if (ai != null) ai.hold = true;
        }

        void ClearWatch()
        {
            foreach (FighterController w in watchers)
            {
                EnemyAI ai = w == ally ? allyAi : w == foe ? foeAi : (Dm != null && w == Dm.enemy) ? MainAi() : null;
                if (ai != null) ai.hold = false;
            }
            watchers.Clear(); watchAt.Clear();
        }

        // ---------------------------------------------------------------- шкалы над головами
        void OnGUI()
        {
            DuelManager dm = Dm;
            if (!On || dm == null || MapScreen.Open || InventoryScreen.Open) return;
            if (dm.State != DuelState.Fighting && dm.State != DuelState.Intro) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Matrix4x4 old = UiKit.Begin();
            Bar(cam, ally, AllyName, AllyHp01, new Color(0.45f, 0.85f, 0.55f));
            if (foe.gameObject.activeSelf) Bar(cam, foe, FoeName, FoeHp01, new Color(0.85f, 0.3f, 0.3f));
            for (int i = 0; i < popups.Count; i++)
            {
                DuelManager.Popup p = popups[i];
                float age = Time.unscaledTime - p.born;
                Vector3 sp = cam.WorldToScreenPoint(p.world);
                if (sp.z < 0f) continue;
                Vector2 v = new Vector2(sp.x / UiKit.Scale, (Screen.height - sp.y) / UiKit.Scale - age * 40f);
                Color c = p.color; c.a = Mathf.Clamp01(1.6f - age * 1.6f);
                UiKit.Text(new Rect(v.x - 50f, v.y - 16f, 100f, 32f), p.text, p.size, c);
            }
            GUI.matrix = old;
        }

        static void Bar(Camera cam, FighterController f, string name, float k, Color col)
        {
            if (f == null || f.Dead) return;
            Vector3 sp = cam.WorldToScreenPoint(f.transform.position + Vector3.up * 2.0f);
            if (sp.z < 0f) return;
            Vector2 c = new Vector2(sp.x / UiKit.Scale, (Screen.height - sp.y) / UiKit.Scale);
            c.y = Mathf.Max(c.y, 112f);
            const float w = 70f;
            UiKit.Text(new Rect(c.x - 60f, c.y - 17f, 120f, 14f), name, 9f, new Color(col.r, col.g, col.b, 0.9f));
            UiKit.Rect(new Rect(c.x - w * 0.5f - 1f, c.y - 1f, w + 2f, 6f), new Color(0f, 0f, 0f, 0.6f));
            UiKit.Rect(new Rect(c.x - w * 0.5f, c.y, w * k, 4f), col);
        }
    }
}
