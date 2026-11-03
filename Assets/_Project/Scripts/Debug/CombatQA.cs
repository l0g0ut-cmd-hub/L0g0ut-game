#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Автотест боя в CI (Play Mode в редакторе, запускает EditorTools.QARunner): настоящая игра — те же Update, время и физика,
    /// что на телефоне. Враг стоит (ИИ выключен), бессмертны оба. Результат — build/qa/qa.txt (строки QA|…, PASS/FAIL) и кадры.
    ///  1. каждый облик врага и героя: кости найдены, обе кисти на рукояти (расстояние кулака до рукояти), портрет;
    ///  2. каждый наземный удар героя на двух дистанциях: был ли контакт клинка, засчитано ли попадание, «фантом» —
    ///     насколько клинок (хитбокс и след) уходит от правой кисти в фазе удара;
    ///  3. удар в прыжке: прыжок, удар через разное время после отрыва, две дистанции, плюс раскадровка;
    ///  4. серии (удар → следующий из восстановления) — там и вылезали фантомные взмахи;
    ///  5. удары каждого облика врага по стоящему герою: хват и фантом у новых моделей.
    /// Только для редактора: в APK этого кода нет.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // замеры и кадры — после драйвера скелета (0), сэмплера клинка (100) и следа (200): то, что видно на экране
    public class CombatQA : MonoBehaviour
    {
        public static bool Finished;
        public static int Fails;
        public static string OutDir;

        const float Dt = 1f / 60f;               // шаг игрового времени — как телефон на 60 к/с, не зависит от скорости рендера в CI
        const float GripMaxPoint = 0.07f;        // кулак (точка хвата из Blender) до рукояти, м
        const float GripMaxBone = 0.15f;         // кость кисти до рукояти (модели без точки хвата), м
        const float PhantomMax = 0.10f;          // клинок от правой кисти в фазе удара, м
        static readonly AttackType[] Ground = { AttackType.Overhead, AttackType.Rising, AttackType.Slash, AttackType.SlashBack, AttackType.Thrust, AttackType.LegSweep, AttackType.Charge };

        readonly StringBuilder log = new StringBuilder();
        DuelManager dm; FighterController player, enemy;
        Vector3 home, fwd;
        Camera cam; RenderTexture rt;

        // контакты текущего опыта
        int contacts, hits; string firstZone = "-"; float dmg;
        FighterController attackerNow;

        // замер текущего удара и съёмка — в LateUpdate (см. порядок выполнения выше)
        FighterController swingOf; SwingStat swingStat; Strip swingStrip; int lateFrame;
        bool traceSwing; Vector3 tracePrevTip; bool traceHasPrev;

        /// <summary>Покадрово: фаза, окно попадания, острие (вперёд/вверх от бойца), скорость острия, ближайшая зона врага до клинка.</summary>
        void Trace(FighterController a, WeaponController w)
        {
            FighterController target = a == player ? enemy : player;
            Vector3 root = w.bladeRoot.position, ax = w.bladeRoot.forward, tip = root + ax * w.bladeLength;
            float best = 99f; string zone = "-";
            foreach (BodyZone z in target.zones)
            {
                if (z == null) continue;
                foreach (Collider col in z.GetComponentsInChildren<Collider>())
                {
                    if (col == null || !col.enabled) continue;
                    for (int k = 0; k <= 6; k++)
                    {
                        Vector3 p = root + ax * (w.bladeLength * k / 6f);
                        float d = Vector3.Distance(p, col.ClosestPoint(p));
                        if (d < best) { best = d; zone = z.name; }
                    }
                }
            }
            float v = traceHasPrev ? (tip - tracePrevTip).magnitude / Mathf.Max(1e-5f, Time.deltaTime) : 0f;
            tracePrevTip = tip; traceHasPrev = true;
            Vector3 rel = a.transform.InverseTransformPoint(tip);
            HeroRigDriver drv = HeroRigDriver.Of(a);
            Vector3 hr = drv != null && drv.handR != null ? a.transform.InverseTransformPoint(drv.handR.position) : Vector3.zero;
            Vector3 sh = drv != null && drv.upperArmR != null ? a.transform.InverseTransformPoint(drv.upperArmR.position) : Vector3.zero;
            Line(string.Format("trace|{0}|{1:F2}|окно {2}|остриё вбок {10:F2} вперёд {3:F2} вверх {4:F2} м над землёй {5:F2}|v {6:F1}|до врага {7:F3} ({8})|боец y {9:F2}|кисть R {11:F2} {12:F2} {13:F2} плечо R {14:F2} {15:F2} {16:F2}|до цели {17:F2}",
                w.Phase, w.PhaseProgress, w.ContactWindowOpen ? "ДА" : "нет", rel.z, rel.y, tip.y, v, best, zone, a.transform.position.y, rel.x, hr.x, hr.y, hr.z, sh.x, sh.y, sh.z, Vector3.Distance(Flat(a.transform.position), Flat(target.transform.position))));
        }

        FighterController shotOf; string shotName; float shotGripR = -1f, shotGripL = -1f; bool shotPR, shotPL, shotDone;

        void LateUpdate()
        {
            lateFrame++;
            if (swingOf != null && swingStat != null)
            {
                WeaponController w = swingOf.weapon;
                if (w != null && w.Phase != SwingPhase.Idle)
                {
                    if (w.CurrentProfile != null && w.CurrentProfile.type != swingStat.last) { swingStat.last = w.CurrentProfile.type; swingStat.types += (swingStat.types.Length > 0 ? ">" : "") + swingStat.last; }
                    bool bp; float e = GripErr(swingOf, true, out bp);
                    if (e >= 0f)
                    {
                        swingStat.phantomAll = Mathf.Max(swingStat.phantomAll, e);
                        if (w.Phase == SwingPhase.Strike || w.Phase == SwingPhase.FollowThrough) swingStat.phantomStrike = Mathf.Max(swingStat.phantomStrike, e);
                    }
                    swingStat.frames++;
                }
                if (swingStrip != null) swingStrip.Tick(lateFrame);
                if (traceSwing && w != null && w.Phase != SwingPhase.Idle) Trace(swingOf, w);
            }
            if (shotOf != null && !shotDone)
            {
                shotGripR = GripErr(shotOf, true, out shotPR); shotGripL = GripErr(shotOf, false, out shotPL);
                Portrait(shotOf, shotName);
                shotDone = true;
            }
        }

        void Line(string s) { log.Append("QA|").Append(s).Append('\n'); Debug.Log("QA|" + s); }
        void Fail(string s) { Fails++; Line(s + "|FAIL"); }

        IEnumerator Start()
        {
            Time.captureDeltaTime = Dt;
            Finished = false; Fails = 0;
            if (string.IsNullOrEmpty(OutDir)) OutDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../build/qa"));
            Directory.CreateDirectory(OutDir);
            Line("start|gfx " + SystemInfo.graphicsDeviceType + "|unity " + Application.unityVersion);
            float t0 = Time.time;
            while ((dm = DuelManager.Instance) == null && Time.time - t0 < 5f) yield return null;
            if (dm == null) { Fail("setup|нет DuelManager"); Done(); yield break; }
            player = dm.player; enemy = dm.enemy;
            if (player == null || enemy == null) { Fail("setup|нет бойцов"); Done(); yield break; }

            // карта похода закрыта, враг стоит, оба бессмертны, автобой выключен
            MethodInfo hide = typeof(MapScreen).GetMethod("Hide", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (hide != null && MapScreen.Open) hide.Invoke(null, null);
            DuelManager.AutoBattle = false; DuelManager.Immortal = true; DuelManager.EnemyImmortal = true;
            foreach (EnemyAI ai in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) ai.aiEnabled = false;
            t0 = Time.time;
            while (dm.State != DuelState.Fighting && Time.time - t0 < 30f) yield return null;
            Line("state|" + dm.State + " за " + (Time.time - t0).ToString("F1") + " с");
            if (dm.State != DuelState.Fighting) { Fail("setup|бой не начался"); Done(); yield break; }

            foreach (BladeTrajectorySampler s in FindObjectsByType<BladeTrajectorySampler>(FindObjectsSortMode.None)) s.ContactDetected += OnContact;
            if (CombatResolver.Instance != null) CombatResolver.Instance.HitRegistered += OnHit;
            dm.Damaged += OnDamaged;

            home = player.transform.position;
            fwd = enemy.transform.position - home; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            SetupCamera();

            yield return Skins();
            yield return PlayerAttacks();
            yield return JumpStrikes();
            yield return Chains();
            yield return EnemyAttacks();
            yield return ArenaCheck();
            yield return DiceThrows();

            Line("summary|fails " + Fails);
            Done();
        }

        void Done()
        {
            try { File.WriteAllText(Path.Combine(OutDir, "qa.txt"), log.ToString()); } catch (Exception e) { Debug.Log("QA: не записано: " + e.Message); }
            Time.captureDeltaTime = 0f;
            Finished = true;
        }

        void OnContact(BladeContact c)
        {
            if (attackerNow == null || c.weapon == null || c.weapon.owner != attackerNow || c.zone == null) return;
            contacts++;
            if (firstZone == "-") firstZone = c.zone.name;
        }
        void OnHit(HitResult r) { if (r.attacker == attackerNow) hits++; }
        void OnDamaged(FighterController f, float a) { if (attackerNow != null && f != attackerNow) dmg += a; }

        // ------------------------------------------------------------------ расстановка
        IEnumerator Place(float dist, float settle = 0.5f)
        {
            foreach (FighterController f in new[] { player, enemy })
            {
                if (f.weapon != null) f.weapon.SnapRest();
                f.ResetPosture(); f.SetMoveVector(Vector2.zero);
            }
            // здоровье — заново: бессмертный враг застревает на 1 ед., и урон следующих ударов читался как 0
            Dictionary<FighterController, float> hp = typeof(DuelManager).GetField("hp", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dm) as Dictionary<FighterController, float>;
            if (hp != null) { hp[player] = dm.MaxHp(player); hp[enemy] = dm.MaxHp(enemy); }
            Vector3 c = new Vector3(0f, home.y, 0f);   // центр арены: у края Teleport упирался в границу и ставил дальше нужного
            enemy.Teleport(c + fwd * (dist * 0.5f));
            player.Teleport(c - fwd * (dist * 0.5f));
            enemy.Teleport(c + fwd * (dist * 0.5f));   // повернуться друг к другу
            float t = 0f;
            while (t < settle || ((!player.CanAct || !enemy.CanAct) && t < settle + 3f)) { t += Time.deltaTime; yield return null; }
            float real = Vector3.Distance(Flat(player.transform.position), Flat(enemy.transform.position));
            if (Mathf.Abs(real - dist) > 0.15f) Line("place|дистанция " + real.ToString("F2") + " вместо " + dist.ToString("F2"));
        }
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        // ------------------------------------------------------------------ хват
        static Transform FindDeep(Transform root, string lowerKey)
        {
            if (root == null) return null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.ToLowerInvariant().Replace(".", "").Replace("_", "").Contains(lowerKey)) return t;
            return null;
        }

        /// <summary>Расстояние кулака до рукояти (отрезок от основания клинка назад на 0,30 м); точка хвата из Blender, иначе кость кисти.</summary>
        static float GripErr(FighterController f, bool right, out bool byPoint)
        {
            byPoint = false;
            HeroRigDriver d = HeroRigDriver.Of(f);
            WeaponController w = f.weapon;
            if (d == null || w == null || w.bladeRoot == null) return -1f;
            Transform hand = right ? d.handR : d.handL;
            if (hand == null) return -1f;
            Transform gp = FindDeep(hand, right ? "grippointr" : "grippointl");
            byPoint = gp != null;
            Vector3 p = gp != null ? gp.position : hand.position;
            Vector3 a = w.bladeRoot.position - w.bladeRoot.forward * 0.30f, b = w.bladeRoot.position + w.bladeRoot.forward * 0.02f;
            Vector3 ab = b - a; float k = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * k);
        }

        string BonesReport(FighterController f, out bool ok)
        {
            HeroRigDriver d = HeroRigDriver.Of(f);
            if (d == null) { ok = false; return "нет HeroRigDriver"; }
            List<string> miss = new List<string>();
            if (d.hips == null) miss.Add("Hips"); if (d.spine == null) miss.Add("Spine"); if (d.chest == null) miss.Add("Chest"); if (d.head == null) miss.Add("Head");
            if (d.upperArmR == null) miss.Add("UpperArm.R"); if (d.forearmR == null) miss.Add("Forearm.R"); if (d.handR == null) miss.Add("Hand.R");
            if (d.upperArmL == null) miss.Add("UpperArm.L"); if (d.forearmL == null) miss.Add("Forearm.L"); if (d.handL == null) miss.Add("Hand.L");
            if (d.footL == null) miss.Add("Foot.L"); if (d.footR == null) miss.Add("Foot.R");
            ok = miss.Count == 0;
            return ok ? "кости ok" : "нет костей: " + string.Join(",", miss.ToArray());
        }

        IEnumerator CheckSkin(string side, FighterController f, int idx, string name)
        {
            yield return Place(3.2f, 1.0f);   // противник далеко — не загораживает кадр
            bool bonesOk; string bones = BonesReport(f, out bonesOk);
            shotOf = f; shotName = "skin_" + side + "_" + idx + "_" + name; shotDone = false;
            while (!shotDone) yield return null;
            shotOf = null;
            bool pr = shotPR, pl = shotPL;
            float gr = shotGripR, gl = shotGripL;
            float lim = pr ? GripMaxPoint : GripMaxBone;
            bool gripOk = gr >= 0f && gl >= 0f && gr <= lim && gl <= (pl ? GripMaxPoint : GripMaxBone);
            string s = string.Format("skin|{0}|{1}|{2}|{3}|кулак R {4:F3} L {5:F3} м ({6}, порог {7:F2})", side, idx, name, bones, gr, gl, pr ? "точка хвата" : "кость кисти", lim);
            if (bonesOk && gripOk) Line(s + "|PASS"); else Fail(s);
        }

        IEnumerator Skins()
        {
            EnemySkins es = enemy.GetComponent<EnemySkins>();
            if (es != null && es.models != null)
            {
                int was = es.Active;
                HashSet<GameObject> seen = new HashSet<GameObject>();
                for (int i = 0; i < es.models.Length; i++)
                {
                    if (es.models[i] == null || !seen.Add(es.models[i])) continue;
                    es.Apply(i);
                    yield return CheckSkin("enemy", enemy, i, es.models[i].name);
                }
                es.Apply(was);
            }
            else yield return CheckSkin("enemy", enemy, 0, "single");
            PlayerSkins ps = player.GetComponent<PlayerSkins>();
            if (ps != null && ps.models != null)
            {
                int was = ps.Active;
                HashSet<GameObject> seen = new HashSet<GameObject>();
                for (int i = 0; i < ps.models.Length; i++)
                {
                    if (ps.models[i] == null || !seen.Add(ps.models[i])) continue;
                    ps.Apply(i);
                    yield return CheckSkin("player", player, i, ps.models[i].name);
                }
                ps.Apply(was);
            }
            else yield return CheckSkin("player", player, 0, "single");
        }

        // ------------------------------------------------------------------ удар целиком
        class SwingStat { public float phantomStrike, phantomAll; public int frames; public bool started; public string types = ""; public AttackType last = (AttackType)(-1); }

        /// <summary>Крутит игру, пока оружие атакующего не вернётся в стойку (и боец не на земле); меряет фантом по кадрам.</summary>
        IEnumerator RunSwing(FighterController a, SwingStat st, Func<int, bool> each, Strip strip)
        {
            float t = 0f; int frame = 0; bool seenSwing = false;
            swingOf = a; swingStat = st; swingStrip = strip;
            while (t < 4f)
            {
                yield return null;
                t += Time.deltaTime; frame++;
                if (each != null) each(frame);
                if (a.weapon.Phase != SwingPhase.Idle) seenSwing = true;
                if (seenSwing && a.weapon.Phase == SwingPhase.Idle && a.Grounded) break;
            }
            yield return null;   // последний LateUpdate
            swingOf = null; swingStat = null; swingStrip = null;
            st.started = seenSwing;
        }

        void ResetCounters(FighterController a) { attackerNow = a; contacts = 0; hits = 0; dmg = 0f; firstZone = "-"; }

        string Outcome(SwingStat st)
        {
            return string.Format("контакт {0} ({1}) попаданий {2} урон {3:F0}|фантом удар {4:F3} весь {5:F3} м|{6}", contacts > 0 ? "да" : "НЕТ", firstZone, hits, dmg, st.phantomStrike, st.phantomAll, st.types);
        }

        IEnumerator PlayerAttacks()
        {
            foreach (float dist in new[] { 1.3f, 1.7f })
                foreach (AttackType t in Ground)
                {
                    yield return Place(dist);
                    ResetCounters(player);
                    SwingStat st = new SwingStat();
                    bool ok = player.Attack(t);
                    if (!ok) { Fail("attack|player|" + t + "|d" + dist.ToString("F1") + "|удар не начался"); continue; }
                    Strip strip = dist < 1.5f && (t == AttackType.Overhead || t == AttackType.SlashBack || t == AttackType.Charge) ? new Strip(this, player, "attack_" + t + "_d" + dist.ToString("F1"), 3) : null;
                    traceSwing = dist < 1.5f && t == AttackType.SlashBack; traceHasPrev = false;
                    yield return RunSwing(player, st, null, strip);
                    traceSwing = false;
                    if (strip != null) strip.Save();
                    string s = "attack|player|" + t + "|d" + dist.ToString("F1") + "|" + Outcome(st);
                    // ближняя дистанция обязана попадать; дальняя — для сведения. Натиск бьёт телом (клинок не проверяется) — по урону
                    bool landed = contacts > 0 || (SwingProfile.Get(t).scripted && dmg > 0f);
                    bool bad = st.phantomStrike > PhantomMax || (dist < 1.5f && !landed);
                    if (bad) Fail(s); else Line(s + "|PASS");
                }
            attackerNow = null;
        }

        IEnumerator JumpStrikes()
        {
            foreach (float dist in new[] { 1.3f, 1.7f })
            {
                int okCount = 0;
                foreach (float delay in new[] { 0.05f, 0.15f, 0.25f, 0.35f })
                {
                    yield return Place(dist);
                    ResetCounters(player);
                    SwingStat st = new SwingStat();
                    if (!player.Jump()) { Fail("jump|d" + dist.ToString("F1") + "|прыжок не начался"); continue; }
                    float t = 0f; float apex = 0f;
                    while (t < delay) { yield return null; t += Time.deltaTime; apex = Mathf.Max(apex, player.transform.position.y); }
                    bool started = player.Attack(AttackType.Overhead);
                    bool shoot = Mathf.Approximately(delay, 0.15f) && dist < 1.5f;
                    Strip strip = shoot ? new Strip(this, player, "jump_strike_d" + dist.ToString("F1"), 3) : null;
                    if (!started) { Line("jump|d" + dist.ToString("F1") + "|t" + delay.ToString("F2") + "|удар не начался (на земле? " + player.Grounded + ")"); continue; }
                    traceSwing = shoot; traceHasPrev = false;
                    yield return RunSwing(player, st, f => { apex = Mathf.Max(apex, player.transform.position.y); return true; }, strip);
                    traceSwing = false;
                    if (strip != null) strip.Save();
                    if (contacts > 0) okCount++;
                    string s = "jump|d" + dist.ToString("F1") + "|t" + delay.ToString("F2") + "|высота " + apex.ToString("F2") + "|" + Outcome(st);
                    if (st.phantomStrike > PhantomMax) Fail(s); else Line(s + (contacts > 0 ? "|PASS" : "|MISS"));
                }
                string sum = "jump_summary|d" + dist.ToString("F1") + "|попаданий " + okCount + " из 4";
                if (dist < 1.5f && okCount == 0) Fail(sum); else Line(sum);
            }
            attackerNow = null;
        }

        IEnumerator Chains()
        {
            AttackType[][] seqs = {
                new[] { AttackType.Slash, AttackType.SlashBack, AttackType.Overhead },
                new[] { AttackType.Overhead, AttackType.Thrust },
                new[] { AttackType.Rising, AttackType.Overhead },
                new[] { AttackType.LegSweep, AttackType.Slash },
                new[] { AttackType.Thrust, AttackType.Rising },
            };
            foreach (AttackType[] seq in seqs)
            {
                yield return Place(1.3f);
                ResetCounters(player);
                SwingStat st = new SwingStat();
                int next = 1;
                player.Attack(seq[0]);
                yield return RunSwing(player, st, f =>
                {
                    WeaponController w = player.weapon;
                    if (next < seq.Length && w.Phase == SwingPhase.Recovery && player.Chain(seq[next])) next++;
                    return true;
                }, null);
                string s = "chain|" + string.Join(">", Array.ConvertAll(seq, x => x.ToString())) + "|прошло " + next + " из " + seq.Length + "|" + Outcome(st);
                if (st.phantomStrike > PhantomMax) Fail(s); else Line(s + "|PASS");
            }
            attackerNow = null;
        }

        IEnumerator EnemyAttacks()
        {
            EnemySkins es = enemy.GetComponent<EnemySkins>();
            int was = es != null ? es.Active : 0;
            HashSet<GameObject> seen = new HashSet<GameObject>();
            int n = es != null && es.models != null ? es.models.Length : 1;
            for (int i = 0; i < n; i++)
            {
                string name = "single";
                if (es != null) { if (es.models[i] == null || !seen.Add(es.models[i])) continue; es.Apply(i); name = es.models[i].name; }
                foreach (AttackType t in new[] { AttackType.Overhead, AttackType.Slash, AttackType.Thrust })
                {
                    yield return Place(1.4f, 0.8f);
                    ResetCounters(enemy);
                    SwingStat st = new SwingStat();
                    if (!enemy.Attack(t)) { Fail("enemy|" + i + "|" + name + "|" + t + "|удар не начался"); continue; }
                    bool shogun = name.Contains("Shogun") || name.Contains("Shadow");   // Тень — для сравнения: тот же клип, но попадает
                    Strip strip = t == AttackType.Overhead || (shogun && t == AttackType.Slash) ? new Strip(this, enemy, "enemy_" + i + "_" + name + "_" + t, 4) : null;
                    traceSwing = shogun && t != AttackType.Thrust; traceHasPrev = false;
                    if (traceSwing) Line("trace_start|" + name + "|" + t + "|дистанция " + Vector3.Distance(Flat(player.transform.position), Flat(enemy.transform.position)).ToString("F2"));
                    yield return RunSwing(enemy, st, null, strip);
                    traceSwing = false;
                    if (strip != null) strip.Save();
                    string s = "enemy|" + i + "|" + name + "|" + t + "|" + Outcome(st);
                    if (st.phantomStrike > PhantomMax || contacts == 0) Fail(s); else Line(s + "|PASS");
                }
            }
            if (es != null) es.Apply(was);
            attackerNow = null;
        }

        // ------------------------------------------------------------------ тестовая арена (ArenaVariants, облик 1)
        IEnumerator ArenaCheck()
        {
            if (ArenaVariants.Instance == null || ArenaVariants.Count < 2) { Line("arena|нет второго облика арены"); yield break; }
            for (int a = 1; a >= 0; a--)
            {
                ArenaVariants.Instance.Apply(a);
                yield return Place(1.3f, 1.0f);   // на 1,6 удар сверху не достаёт и на старой арене
                string nm = "arena_" + a;
                Camera mc = Camera.main;
                if (mc != null && cam != null)
                {
                    RenderTexture big = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
                    Vector3 mid = (player.transform.position + enemy.transform.position) * 0.5f;
                    Vector3 side = Vector3.Cross(Vector3.up, fwd);
                    Vector3[] eyes = { mc.transform.position, mid + side * -5.5f + Vector3.up * 2.2f, mid + Vector3.up * 9f + side * -7f, mid + fwd * -6f + Vector3.up * 3f };
                    for (int k = 0; k < eyes.Length; k++)
                    {
                        cam.transform.position = eyes[k];
                        cam.transform.rotation = k == 0 ? mc.transform.rotation : Quaternion.LookRotation(mid + Vector3.up * 0.9f - eyes[k], Vector3.up);
                        cam.fieldOfView = k == 0 ? mc.fieldOfView : 50f;
                        cam.targetTexture = big; cam.Render();
                        RenderTexture prev = RenderTexture.active; RenderTexture.active = big;
                        Texture2D tx = new Texture2D(1280, 720, TextureFormat.RGB24, false); tx.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tx.Apply();
                        RenderTexture.active = prev; cam.targetTexture = null;
                        File.WriteAllBytes(Path.Combine(OutDir, nm + "_view" + k + ".jpg"), tx.EncodeToJPG(85));
                        Destroy(tx);
                    }
                    big.Release(); Destroy(big);
                }
                // бойцы стоят на полу: стопы на высоте 0, удар по-прежнему попадает
                HeroRigDriver d = HeroRigDriver.Of(player);
                float foot = d != null && d.footL != null ? Mathf.Min(d.footL.position.y, d.footR.position.y) : -1f;
                ResetCounters(player);
                SwingStat st = new SwingStat();
                player.Attack(AttackType.Overhead);
                yield return RunSwing(player, st, null, null);
                string s = "arena|" + ArenaVariants.Names[a] + "|стопа y " + foot.ToString("F3") + "|удар сверху: " + Outcome(st);
                if (contacts == 0) Fail(s); else Line(s + "|PASS");
            }
            attackerNow = null;
        }

        // ------------------------------------------------------------------ кости в трактире (TavernDiceTable)
        IEnumerator DiceThrows()
        {
            TavernDiceTable t = TavernDiceTable.Ensure();
            yield return null;
            for (int n = 0; n < 3; n++)
            {
                t.Throw();
                List<Texture2D> fr = new List<Texture2D>();
                float t0 = Time.unscaledTime; int f = 0;
                while (!(t.Settled && t.StillFor > 0.35f) && Time.unscaledTime - t0 < 8f)
                {
                    yield return null;
                    if (f++ % 5 == 0 && fr.Count < 12 && t.RT != null)
                    {
                        t.RenderNow();   // в CI без окна камера сама может не рисовать
                        RenderTexture prev = RenderTexture.active; RenderTexture.active = t.RT;
                        Texture2D tx = new Texture2D(t.RT.width, t.RT.height, TextureFormat.RGBA32, false);
                        tx.ReadPixels(new Rect(0, 0, t.RT.width, t.RT.height), 0, 0); tx.Apply();
                        RenderTexture.active = prev; fr.Add(tx);
                    }
                }
                float took = Time.unscaledTime - t0;
                // последний кадр целиком, с прозрачностью — для наложения на арт стола
                if (t.RT != null)
                {
                    t.RenderNow();
                    RenderTexture prev = RenderTexture.active; RenderTexture.active = t.RT;
                    Texture2D last = new Texture2D(t.RT.width, t.RT.height, TextureFormat.RGBA32, false);
                    last.ReadPixels(new Rect(0, 0, t.RT.width, t.RT.height), 0, 0); last.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(OutDir, "dice_final_" + (n + 1) + ".png"), last.EncodeToPNG());
                    Destroy(last);
                }
                bool ok = t.Settled && t.D1 >= 1 && t.D1 <= 6 && t.D2 >= 1 && t.D2 <= 6 && took < 6f;
                string s = "dice|бросок " + (n + 1) + "|выпало " + t.D1 + " и " + t.D2 + "|остановились за " + took.ToString("F2") + " с|кадров " + f;
                if (ok) Line(s + "|PASS"); else Fail(s);
                if (fr.Count > 0)
                {
                    int w = fr[0].width / 2, h = fr[0].height / 2;
                    Texture2D sheet = new Texture2D(w * 4, h * 3, TextureFormat.RGB24, false);
                    Color[] bgc = new Color[w * 4 * h * 3]; for (int i = 0; i < bgc.Length; i++) bgc[i] = new Color(0.30f, 0.19f, 0.10f);   // «стол» под прозрачным фоном
                    sheet.SetPixels(bgc);
                    for (int i = 0; i < fr.Count; i++)
                    {
                        Color[] src = fr[i].GetPixels(0, 0, fr[i].width, fr[i].height);
                        int ox = (i % 4) * w, oy = (2 - i / 4) * h;
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++)
                            {
                                Color c = src[(y * 2) * fr[i].width + x * 2];
                                Color b = new Color(0.30f, 0.19f, 0.10f);
                                sheet.SetPixel(ox + x, oy + y, Color.Lerp(b, c, c.a));
                            }
                        Destroy(fr[i]);
                    }
                    sheet.Apply();
                    File.WriteAllBytes(Path.Combine(OutDir, "dice_throw_" + (n + 1) + ".jpg"), sheet.EncodeToJPG(85));
                    Destroy(sheet);
                }
            }
            TavernDiceTable.Release();
        }

        // ------------------------------------------------------------------ кадры
        void SetupCamera()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { Line("shots|нет графического устройства — без кадров"); return; }
            GameObject go = new GameObject("QA Camera");
            cam = go.AddComponent<Camera>();
            if (Camera.main != null) cam.CopyFrom(Camera.main);
            cam.enabled = false;
            rt = new RenderTexture(480, 560, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
        }

        Texture2D Grab(FighterController f, Vector3 side, float back)
        {
            if (cam == null) return null;
            Vector3 c = f.transform.position + Vector3.up * 1.0f;
            Vector3 pos = c + side * back + Vector3.up * 0.25f;
            cam.transform.position = pos; cam.transform.rotation = Quaternion.LookRotation(c - pos, Vector3.up); cam.fieldOfView = 45f;
            cam.targetTexture = rt; cam.Render();
            RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null;
            return tex;
        }

        void Portrait(FighterController f, string name)
        {
            Texture2D a = Grab(f, (f.transform.forward + Vector3.Cross(Vector3.up, f.transform.forward) * 0.8f).normalized, 2.6f);
            if (a == null) return;
            Texture2D b = Grab(f, Vector3.Cross(Vector3.up, f.transform.forward), 2.6f);
            Texture2D sheet = new Texture2D(a.width * 2, a.height, TextureFormat.RGB24, false);
            sheet.SetPixels(0, 0, a.width, a.height, a.GetPixels()); sheet.SetPixels(a.width, 0, b.width, b.height, b.GetPixels()); sheet.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, Safe(name) + ".jpg"), sheet.EncodeToJPG(85));
            Destroy(a); Destroy(b); Destroy(sheet);
        }
        static string Safe(string s) { foreach (char ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_'); return s.Replace(' ', '_'); }

        /// <summary>Раскадровка удара сбоку: 8 кадров через каждые every кадров игры.</summary>
        class Strip
        {
            readonly CombatQA q; readonly FighterController f; readonly string name; readonly int every;
            readonly List<Texture2D> frames = new List<Texture2D>();
            public Strip(CombatQA q, FighterController f, string name, int every) { this.q = q; this.f = f; this.name = name; this.every = every; }
            public void Tick(int frame)
            {
                if (q.cam == null || frames.Count >= 8 || frame % every != 0) return;
                Vector3 side = Vector3.Cross(Vector3.up, q.fwd);
                Texture2D t = q.Grab(f, side, 3.4f);
                if (t != null) frames.Add(t);
            }
            public void Save()
            {
                if (frames.Count == 0) return;
                int w = frames[0].width, h = frames[0].height;
                Texture2D sheet = new Texture2D(w * 4, h * 2, TextureFormat.RGB24, false);
                for (int i = 0; i < frames.Count; i++) sheet.SetPixels((i % 4) * w, (1 - i / 4) * h, w, h, frames[i].GetPixels());
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(OutDir, Safe(name) + ".jpg"), sheet.EncodeToJPG(85));
                foreach (Texture2D t in frames) UnityEngine.Object.Destroy(t);
                UnityEngine.Object.Destroy(sheet);
            }
        }
    }
}
#endif
