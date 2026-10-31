#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Кадры сцены боя из CI (build/shots/*.jpg → ветка apk-latest): общий план как в игре, бойцы крупно, вспышка парирования,
    /// панорама арены. Нужны, чтобы оценивать картинку без телефона. Бойцы стоят в стойке (Animator + HeroRigDriver, как в игре),
    /// частицы окружения прокручены на несколько секунд. Без видеокарты (Null device) — только строка в probe.txt.
    /// </summary>
    public static class SceneShots
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static void Call(object o, string m) { MethodInfo mi = o.GetType().GetMethod(m, F, null, Type.EmptyTypes, null); if (mi != null) mi.Invoke(o, null); }

        public static void Capture(StringBuilder log)
        {
            log.Append("PROBE|shots|gfx " + SystemInfo.graphicsDeviceType + "|" + SystemInfo.graphicsDeviceName + "|" + SystemInfo.graphicsDeviceVersion + "\n");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { log.Append("PROBE|shots|нет графического устройства — кадров не будет\n"); return; }
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../build/shots"));
            Directory.CreateDirectory(dir);
            DuelManager.ApplyQuality();
            QualitySettings.shadowDistance = Mathf.Max(QualitySettings.shadowDistance, 25f);

            FighterController[] fs = UnityEngine.Object.FindObjectsByType<FighterController>(FindObjectsSortMode.None);
            FighterController player = null, enemy = null;
            DuelManager dm = UnityEngine.Object.FindFirstObjectByType<DuelManager>();
            if (dm != null) { player = dm.player; enemy = dm.enemy; }
            if (player == null || enemy == null) foreach (FighterController f in fs) { if (f.transform.position.x < 0f) player = f; else enemy = f; }
            const float dt = 1f / 30f;
            HeroRigDriver.DtOverride = dt;
            try
            {
                // стойка: 40 кадров «как в игре»
                foreach (FighterController f in fs)
                {
                    HeroRigDriver d = HeroRigDriver.Of(f);
                    if (f.weapon != null) f.weapon.SnapRest();
                    if (d == null || d.animator == null) continue;
                    d.animator.Rebind(); d.animator.Update(0f);
                }
                // кожа пересчитывается на каждый рендер: иначе все кадры одного «редакторского кадра» рисуют тело в позе первого
                foreach (SkinnedMeshRenderer smr in UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
                { smr.forceMatrixRecalculationPerRender = true; smr.updateWhenOffscreen = true; }
                for (int i = 0; i < 40; i++)
                    foreach (FighterController f in fs)
                    {
                        HeroRigDriver d = HeroRigDriver.Of(f);
                        if (d == null || d.animator == null) continue;
                        if (f.weapon != null) Call(f.weapon, "Update");
                        Tick(f, d, dt, dt);
                    }

                // стойка в цифрах: угол в локтях (180 — прямая рука) и вынос кистей вперёд от плеч (в системе бойца)
                foreach (FighterController f in fs)
                {
                    HeroRigDriver d = HeroRigDriver.Of(f);
                    if (d == null || d.upperArmR == null || d.forearmR == null || d.handR == null || d.upperArmL == null) continue;
                    float eR = Vector3.Angle(d.upperArmR.position - d.forearmR.position, d.handR.position - d.forearmR.position);
                    float eL = Vector3.Angle(d.upperArmL.position - d.forearmL.position, d.handL.position - d.forearmL.position);
                    Vector3 sh = (d.upperArmR.position + d.upperArmL.position) * 0.5f, hm = (d.handR.position + d.handL.position) * 0.5f;
                    Vector3 rel = f.transform.InverseTransformDirection(hm - sh);
                    log.Append(string.Format("PROBE|shots|stance|{0}|elbow R {1:F0} L {2:F0}|hands from shoulders x {3:F2} y {4:F2} z {5:F2}\n", f.name, eR, eL, rel.x, rel.y, rel.z));
                }

                // портрет героя для полосы здоровья (в сборку: снимок попадает в Resources до сборки APK)
                if (player != null) HeroPortrait(player, log);

                // компоненты, которые в игре готовятся в Awake (текстуры листьев/лепестков, ореолы) — в редакторе вызываем сами
                foreach (MonoBehaviour mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    if (mb is PetalTexture || mb is GlowSprite || mb is FlickerLight || mb is FloorReflection || mb is LightShaft) Call(mb, "Awake");
                foreach (ContactShadow cs in UnityEngine.Object.FindObjectsByType<ContactShadow>(FindObjectsSortMode.None)) { Call(cs, "Start"); Call(cs, "LateUpdate"); }
                // окружение: огонь, туман, светлячки, лепестки — уже «живут» несколько секунд
                foreach (ParticleSystem ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                    if (ps.transform.parent == null || ps.GetComponentInParent<FighterController>() == null) ps.Simulate(4f, true, true);

                Camera cam = Camera.main;
                if (cam == null) { log.Append("PROBE|shots|нет основной камеры\n"); return; }
                Vector3 pp = player != null ? player.transform.position : new Vector3(-1.2f, 0f, 0f);
                Vector3 ep = enemy != null ? enemy.transform.position : new Vector3(1.2f, 0f, 0f);
                Vector3 mid = (pp + ep) * 0.5f;
                float sep = Vector3.Distance(new Vector3(pp.x, 0f, pp.z), new Vector3(ep.x, 0f, ep.z));
                float dist = 5.2f + Mathf.Max(0f, 2.4f + sep * 1.35f - 5.2f);
                Vector3 focus = new Vector3(mid.x * 0.5f, 1.15f, mid.z * 0.5f);

                Shot(cam, dir, "1_game", focus + new Vector3(0f, 0f, -dist), focus, cam.fieldOfView, log);   // как в игре (FOV камеры боя)
                if (player != null) Shot(cam, dir, "2_player", pp + new Vector3(0f, 1.2f, -3.0f) + player.transform.forward * 0.9f, pp + Vector3.up * 0.95f, 36f, log);   // со стороны камеры боя, чуть спереди
                if (enemy != null) YawProbe(enemy, HeroRigDriver.Of(enemy), "samurai", log);
                if (enemy != null) Shot(cam, dir, "3_enemy", ep + new Vector3(0f, 1.2f, -3.0f) + enemy.transform.forward * 0.9f, ep + Vector3.up * 0.95f, 36f, log);
                // раны: брызги и потёки крови на телах у почти убитых (в игре — по потере здоровья)
                if (player != null && enemy != null)
                {
                    SetWound(player, 0.3f); SetWound(enemy, 0.3f);
                    Shot(cam, dir, "31_wounds", mid + new Vector3(0f, 1.25f, -3.6f), mid + Vector3.up * 1.0f, 44f, log);
                    SetWound(player, 0f); SetWound(enemy, 0f);
                }
                // хват: руки на рукояти крупно (с камеры спереди-сбоку и сверху-сбоку)
                if (player != null && player.weapon != null && player.weapon.pivot != null)
                {
                    Vector3 hp = player.weapon.pivot.position;
                    Transform pt = player.transform;
                    // лунный блик на середине пробега по клинку (в игре его ставит Juice)
                    BladeGlint glint = player.weapon.gameObject.AddComponent<BladeGlint>(); glint.weapon = player.weapon;
                    Call(glint, "Start");
                    FieldInfo grt = typeof(BladeGlint).GetField("runT", F), grd = typeof(BladeGlint).GetField("runDur", F), grp = typeof(BladeGlint).GetField("runPow", F);
                    if (grt != null) { grt.SetValue(glint, 0.2f); grd.SetValue(glint, 0.45f); grp.SetValue(glint, 1f); }
                    Shot(cam, dir, "7_grip", hp + pt.forward * 0.75f - pt.right * 0.55f + Vector3.up * 0.15f, hp, 34f, log);
                    Shot(cam, dir, "8_grip_top", hp + pt.forward * 0.35f + pt.right * 0.45f + Vector3.up * 0.65f, hp, 34f, log);
                    UnityEngine.Object.DestroyImmediate(glint);
                    GameObject gq = GameObject.Find("BladeGlint"); if (gq != null) UnityEngine.Object.DestroyImmediate(gq);   // квад блика (в редакторе OnDestroy не зовётся)
                    BladeBloodShot(cam, dir, player, log);
                }
                // раскадровка ударов: 8 фаз сбоку (как камера боя) — видно замах, удар, проводку и возврат
                if (player != null)
                {
                    SwingStrip(cam, dir, "9_swing_overhead", player, AttackType.Overhead, log);
                    SwingStrip(cam, dir, "10_swing_slash", player, AttackType.Slash, log);
                    SwingStrip(cam, dir, "11_swing_thrust", player, AttackType.Thrust, log);
                    SwingStrip(cam, dir, "24_swing_rising", player, AttackType.Rising, log);
                    SwingStrip(cam, dir, "25_swing_legs", player, AttackType.LegSweep, log);
                    if (enemy != null) SwingStrip(cam, dir, "26_enemy_charge", enemy, AttackType.Charge, log);
                    SwingStrip(cam, dir, "44_salute_bow", player, AttackType.Salute, log);   // поклон перед боем и клинок у лица
                }
                Shot(cam, dir, "4_arena", new Vector3(0f, 3.6f, -6.4f), new Vector3(0f, 1.2f, 2.5f), 50f, log);
                // тени за сёдзи: сидящий монах и идущий (поставлен на середину пути), крупно на храм
                ShojiShadows shoji = UnityEngine.Object.FindFirstObjectByType<ShojiShadows>();
                if (shoji != null)
                {
                    shoji.dir = 1f; shoji.Pose(0.555f);
                    Shot(cam, dir, "36_shoji", new Vector3(-0.8f, 1.5f, 0.6f), new Vector3(-0.8f, 1.3f, 5.2f), 50f, log);
                    if (shoji.walker != null) shoji.walker.enabled = false;
                }
                ReflectShot(cam, dir, "37_reflect_night", log);
                foreach (ParticleSystem ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                    if (ps.name == "Fire" || ps.name == "FireCore" || ps.name == "Embers" || ps.name == "Smoke") ps.Simulate(3f, false, true);
                Shot(cam, dir, "43_brazier", new Vector3(-2.3f, 1.35f, 0.9f), new Vector3(-3.2f, 1.2f, 2.2f), 40f, log);   // огонь жаровни крупно
                Shot(cam, dir, "38_gate", new Vector3(0.4f, 2.0f, 3.4f), new Vector3(0f, 1.1f, -8f), 55f, log);   // назад: передняя стена, ворота, дорожка, лес
                Shot(cam, dir, "39_garden", new Vector3(0f, 3.0f, -2.0f), new Vector3(0f, 0f, -5.8f), 62f, log);   // пруд слева, карэсансуй справа
                Shot(cam, dir, "40_backcourt", new Vector3(5.2f, 2.4f, 11.5f), new Vector3(-1.2f, 0.9f, 9.2f), 62f, log);   // за храмом: колокольня, кура, задняя стена
                WeatherShots(cam, dir, focus + new Vector3(0f, 0f, -dist), focus, log);
                DayShots(cam, dir, focus + new Vector3(0f, 0f, -dist), focus, log);
                // второй облик врага (ронин) — как 3_enemy
                EnemySkins skins = enemy != null ? enemy.GetComponent<EnemySkins>() : null;
                if (skins != null && skins.models.Length > 1)
                {
                    try
                    {
                        skins.Apply(1);
                        HeroRigDriver d = skins.models[1].GetComponent<HeroRigDriver>();
                        if (d != null)
                        {
                            Call(d, "Start");
                            if (d.animator != null) { d.animator.Rebind(); d.animator.Update(0f); }
                            if (enemy.weapon != null) enemy.weapon.SnapRest();
                            for (int i = 0; i < 40; i++) Tick(enemy, d, dt, dt);
                            YawProbe(enemy, d, "ronin", log);
                            Shot(cam, dir, "22_enemy_ronin", ep + new Vector3(0f, 1.2f, -3.0f) + enemy.transform.forward * 0.9f, ep + Vector3.up * 0.95f, 36f, log);
                            Shot(cam, dir, "23_ronin_duel", focus + new Vector3(0f, 0f, -dist), focus, cam.fieldOfView, log);
                            // ронин в ранах крупно и в ударе — проверка, что сетка не «рвётся» (раны по 3D-шуму, руки на вытянутом ударе)
                            SetWound(enemy, 0.3f);
                            Shot(cam, dir, "22c_ronin_wounds", ep + new Vector3(0f, 1.3f, -2.2f) + enemy.transform.forward * 0.8f, ep + Vector3.up * 1.1f, 34f, log);
                            SetWound(enemy, 0f);
                            SwingStrip(cam, dir, "22b_ronin_overhead", enemy, AttackType.Overhead, log);
                            SwingStrip(cam, dir, "22d_ronin_thrust", enemy, AttackType.Thrust, log);
                        }
                    }
                    catch (Exception e) { log.Append("PROBE|shots|ronin|error " + e.Message + "\n"); }
                    finally { skins.Apply(0); }
                }
                // походка: шаг вперёд и приставной вбок (мокап + замок стоп), камера едет рядом
                if (player != null)
                {
                    WalkStrip(cam, dir, "12_walk_fwd", player, enemy, Vector3.forward, 1.2f, false, log);
                    WalkStrip(cam, dir, "13_walk_side", player, enemy, Vector3.right, 0.72f, true, log);
                    if (enemy != null)
                    {
                        DeathblowStrip(cam, dir, "14_deathblow", player, enemy, AttackType.Deathblow, 1.38f, new[] { 0f, 0.32f, 0.4f, 0.46f, 0.8f, 1.1f, 1.26f, 1.36f }, log);
                        DeathblowStrip(cam, dir, "16_deathblow_kesa", player, enemy, AttackType.DeathblowKesa, 1.22f, new[] { 0f, 0.2f, 0.36f, 0.42f, 0.45f, 0.48f, 0.9f, 1.22f }, log);
                        DeathblowStrip(cam, dir, "17_deathblow_neck", player, enemy, AttackType.DeathblowNeck, 1.2f, new[] { 0f, 0.2f, 0.38f, 0.44f, 0.46f, 0.5f, 0.6f, 1.22f }, log);
                        DeathblowStrip(cam, dir, "18_deathblow_rising", player, enemy, AttackType.DeathblowRising, 1.22f, new[] { 0f, 0.2f, 0.38f, 0.44f, 0.46f, 0.5f, 0.6f, 1.18f }, log);
                        DeathblowStrip(cam, dir, "30_deathblow_cross", player, enemy, AttackType.DeathblowCross, 1.22f, new[] { 0.16f, 0.36f, 0.45f, 0.6f, 0.68f, 0.77f, 1.1f, 1.4f }, log);
                        DeathblowStrip(cam, dir, "28_deathblow_plunge", player, enemy, AttackType.DeathblowPlunge, 1.0f, new[] { 0.06f, 0.18f, 0.3f, 0.38f, 0.44f, 0.8f, 1.12f, 1.32f }, log, 0.44f);
                    }
                    if (enemy != null) GuardStrip(cam, dir, "15_guard", player, enemy, log);
                    if (enemy != null) StanceShots(cam, dir, enemy, log);
                    DrinkStrip(cam, dir, player, log);
                    MoveStrip(cam, dir, "19_dash_jump", player, enemy, log);
                }

                // парирование: вспышка, блик, волна и искры через 50 мс после удара
                Juice juice = UnityEngine.Object.FindFirstObjectByType<Juice>();
                if (juice == null) juice = new GameObject("Juice").AddComponent<Juice>();
                if (juice != null)
                {
                    Call(juice, "Awake");
                    HashSet<ParticleSystem> before = new HashSet<ParticleSystem>(UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None));
                    Vector3 hit = new Vector3(mid.x, 1.25f, mid.z);
                    juice.Parry(hit, (pp - ep).normalized);
                    List<ParticleSystem> fresh = new List<ParticleSystem>();
                    foreach (ParticleSystem ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                        if (!before.Contains(ps)) fresh.Add(ps);
                    foreach (ParticleSystem ps in fresh) ps.Simulate(0.035f, true, true);
                    Shot(cam, dir, "5_parry", hit + new Vector3(0f, 0.15f, -2.6f), hit, 34f, log);
                    foreach (ParticleSystem ps in fresh) if (ps != null) ps.Simulate(0.12f, true, true);
                    Shot(cam, dir, "6_parry_late", hit + new Vector3(0f, 0.15f, -2.6f), hit, 34f, log);
                }
            }
            catch (Exception e) { log.Append("PROBE|shots|error " + e + "\n"); }
            finally { HeroRigDriver.DtOverride = -1f; }
        }

        static void SwingStrip(Camera cam, string dir, string name, FighterController f, AttackType type, StringBuilder log)
        {
            WeaponController w = f.weapon; HeroRigDriver d = HeroRigDriver.Of(f);
            if (w == null || d == null) return;
            w.SnapRest();
            if (!w.TryStartSwing(type)) return;
            SwingProfile p = w.CurrentProfile;
            float total = p.windupTime + p.strikeTime + p.followTime + p.recoveryTime;
            PropertyInfo phase = typeof(WeaponController).GetProperty("Phase", F);
            FieldInfo pt = typeof(WeaponController).GetField("phaseTime", F);
            FieldInfo bl = typeof(WeaponController).GetField("blendLeft", F);
            const int n = 8, cw = 480, chh = 420;
            Texture2D sheet = new Texture2D(cw * 4, chh * 2, TextureFormat.RGB24, false);
            Vector3 fp = f.transform.position;
            Vector3 camPos = fp + new Vector3(0f, 1.15f, -3.3f) + f.transform.forward * 0.45f, look = fp + Vector3.up * 1.0f + f.transform.forward * 0.45f;
            Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation; float f0 = cam.fieldOfView;
            RenderTexture rt = new RenderTexture(cw, chh, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            RenderTexture prevT = cam.targetTexture, prevA = RenderTexture.active;
            try
            {
                for (int k = 0; k < n; k++)
                {
                    float tau = total * 0.98f * k / (n - 1);
                    float rem = tau; SwingPhase ph = SwingPhase.Windup;
                    if (rem >= p.windupTime) { rem -= p.windupTime; ph = SwingPhase.Strike; if (rem >= p.strikeTime) { rem -= p.strikeTime; ph = SwingPhase.FollowThrough; if (rem >= p.followTime) { rem -= p.followTime; ph = SwingPhase.Recovery; } } }
                    phase.GetSetMethod(true).Invoke(w, new object[] { ph });
                    pt.SetValue(w, rem); bl.SetValue(w, 0f);
                    Call(w, "ApplyPose");
                    // поза Animator заново на каждой итерации (драйвер крутит кости относительно неё), сглаживания драйвера сходятся
                    for (int i = 0; i < 8; i++) Tick(f, d, 0f, 1f / 30f);
                    cam.transform.position = camPos; cam.transform.rotation = Quaternion.LookRotation(look - camPos, Vector3.up); cam.fieldOfView = 42f;
                    cam.targetTexture = rt; cam.Render();
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, cw, chh), (k % 4) * cw, (1 - k / 4) * chh);
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(dir, name + ".jpg"), sheet.EncodeToJPG(85));
                log.Append("PROBE|shots|" + name + "|" + n + " кадров, " + total.ToString("F2") + " с\n");
            }
            catch (Exception e) { log.Append("PROBE|shots|" + name + "|error " + e.Message + "\n"); }
            finally
            {
                cam.targetTexture = prevT; RenderTexture.active = prevA;
                cam.transform.position = p0; cam.transform.rotation = r0; cam.fieldOfView = f0;
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(sheet);
                w.SnapRest();
                for (int i = 0; i < 8; i++) Tick(f, d, 0f, 1f / 30f);
            }
        }

        /// <summary>Раскадровка добивания: враг «поплыл» (слом равновесия), игрок на дистанции укола, 8 моментов приёма.</summary>
        static void DeathblowStrip(Camera cam, string dir, string name, FighterController a, FighterController t, AttackType type, float distance, float[] taus, StringBuilder log, float plungeContact = -1f)
        {
            WeaponController w = a.weapon; HeroRigDriver da = HeroRigDriver.Of(a), dt_ = HeroRigDriver.Of(t);
            if (w == null || da == null || dt_ == null || t.weapon == null) return;
            Vector3 pa0 = a.transform.position, pt0 = t.transform.position; Quaternion ra0 = a.transform.rotation, rt0 = t.transform.rotation;
            PropertyInfo phase = typeof(WeaponController).GetProperty("Phase", F);
            FieldInfo pt = typeof(WeaponController).GetField("phaseTime", F), bl = typeof(WeaponController).GetField("blendLeft", F), sk = typeof(WeaponController).GetField("stagK", F);
            const int n = 8, cw = 480, chh = 420;
            Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation; float f0 = cam.fieldOfView;
            Texture2D sheet = new Texture2D(cw * 4, chh * 2, TextureFormat.RGB24, false);
            RenderTexture rt = new RenderTexture(cw, chh, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            RenderTexture prevT = cam.targetTexture, prevA = RenderTexture.active;
            try
            {
                Vector3 d = pt0 - pa0; d.y = 0f; d.Normalize();
                a.transform.position = pt0 - d * distance;   // дистанция приёма (DuelManager.DbKinds)
                a.transform.rotation = Quaternion.LookRotation(d, Vector3.up); t.transform.rotation = Quaternion.LookRotation(-d, Vector3.up);
                t.AddPosture(10000f);   // слом равновесия: клинок повис, корпус осел
                t.weapon.SnapRest(); if (sk != null) sk.SetValue(t.weapon, 1f); Call(t.weapon, "ApplyRestPose");
                w.SnapRest();
                if (!w.TryStartSwing(type)) return;
                SwingProfile p = w.CurrentProfile;
                Vector3 mid = (a.transform.position + t.transform.position) * 0.5f;
                Vector3 camPos = mid + Quaternion.Euler(0f, -20f * Mathf.Sign(d.x + 1e-4f), 0f) * new Vector3(0f, 0f, -3.3f) + Vector3.up * 1.15f, look = mid + Vector3.up * 1.05f;
                for (int i = 0; i < 20; i++) Tick(t, dt_, 0f, 1f / 30f);
                for (int k = 0; k < n; k++)
                {
                    float rem = taus[k]; SwingPhase ph = SwingPhase.Windup;
                    if (rem >= p.windupTime) { rem -= p.windupTime; ph = SwingPhase.Strike; if (rem >= p.strikeTime) { rem -= p.strikeTime; ph = SwingPhase.FollowThrough; if (rem >= p.followTime) { rem -= p.followTime; ph = SwingPhase.Recovery; } } }
                    phase.GetSetMethod(true).Invoke(w, new object[] { ph });
                    pt.SetValue(w, rem); bl.SetValue(w, 0f);
                    Call(w, "ApplyPose");
                    if (plungeContact > 0f)
                    {
                        // с прыжка: траектория DuelManager.PlungePos от точки прыжка (1,3 м до приёма, на высоте 0,9 м) к месту приёма
                        Vector3 to = pt0 - d * distance, from = to - d * 1.2f + Vector3.up * 0.75f;
                        float s = Mathf.Clamp01(taus[k] / plungeContact);
                        a.Carry(DuelManager.PlungePos(from, to, s), s >= 1f);
                    }
                    for (int i = 0; i < 8; i++) { Tick(a, da, 0f, 1f / 30f); Tick(t, dt_, 0f, 1f / 30f); }
                    cam.transform.position = camPos; cam.transform.rotation = Quaternion.LookRotation(look - camPos, Vector3.up); cam.fieldOfView = 40f;
                    cam.targetTexture = rt; cam.Render();
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, cw, chh), (k % 4) * cw, (1 - k / 4) * chh);
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(dir, name + ".jpg"), sheet.EncodeToJPG(85));
                log.Append("PROBE|shots|" + name + "|" + n + " кадров\n");
            }
            catch (Exception e) { log.Append("PROBE|shots|" + name + "|error " + e.Message + "\n"); }
            finally
            {
                cam.targetTexture = prevT; RenderTexture.active = prevA;
                cam.transform.position = p0; cam.transform.rotation = r0; cam.fieldOfView = f0;
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(sheet);
                a.ResetFighter(); t.ResetFighter();
                a.transform.position = pa0; a.transform.rotation = ra0; t.transform.position = pt0; t.transform.rotation = rt0;
                w.SnapRest(); t.weapon.SnapRest();
                for (int i = 0; i < 20; i++) { Tick(a, da, 1f / 30f, 1f / 30f); Tick(t, dt_, 1f / 30f, 1f / 30f); }
            }
        }

        /// <summary>Блок по направлению удара: враг бьёт сверху / рез / укол / по ногам, игрок в блоке; момент встречи клинков сбоку и из-за спины.</summary>
        static void GuardStrip(Camera cam, string dir, string name, FighterController def, FighterController att, StringBuilder log)
        {
            WeaponController wd = def.weapon, wa = att.weapon;
            HeroRigDriver dd = HeroRigDriver.Of(def), da = HeroRigDriver.Of(att);
            if (wd == null || wa == null || dd == null || da == null) return;
            PropertyInfo phase = typeof(WeaponController).GetProperty("Phase", F), blocking = typeof(FighterController).GetProperty("Blocking", F);
            FieldInfo pt = typeof(WeaponController).GetField("phaseTime", F), bl = typeof(WeaponController).GetField("blendLeft", F), bk = typeof(WeaponController).GetField("blockK", F);
            AttackType[] types = { AttackType.Overhead, AttackType.Slash, AttackType.Thrust, AttackType.LegSweep };
            const int cw = 480, chh = 420;
            Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation; float f0 = cam.fieldOfView;
            Texture2D sheet = new Texture2D(cw * 4, chh * 2, TextureFormat.RGB24, false);
            RenderTexture rt = new RenderTexture(cw, chh, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            RenderTexture prevT = cam.targetTexture, prevA = RenderTexture.active;
            try
            {
                Vector3 pd = def.transform.position, pa = att.transform.position, mid = (pd + pa) * 0.5f;
                Vector3 fwd = pa - pd; fwd.y = 0f; fwd.Normalize();
                for (int k = 0; k < types.Length; k++)
                {
                    wa.SnapRest(); wd.SnapRest();
                    if (!wa.TryStartSwing(types[k])) continue;
                    SwingProfile p = wa.CurrentProfile;
                    blocking.GetSetMethod(true).Invoke(def, new object[] { true }); bk.SetValue(wd, 1f);
                    // замах (60 %) — блок читает угрозу, затем середина удара — клинки встречаются
                    float[] at = { p.windupTime * 0.6f, p.windupTime + p.strikeTime * 0.6f };
                    foreach (float tau in at)
                    {
                        float rem = tau; SwingPhase ph = SwingPhase.Windup;
                        if (rem >= p.windupTime) { rem -= p.windupTime; ph = SwingPhase.Strike; }
                        phase.GetSetMethod(true).Invoke(wa, new object[] { ph });
                        pt.SetValue(wa, rem); bl.SetValue(wa, 0f);
                        Call(wa, "ApplyPose");
                        for (int i = 0; i < 6; i++) { Tick(att, da, 0f, 1f / 30f); Call(wd, "ApplyRestPose"); Tick(def, dd, 0f, 1f / 30f); }
                    }
                    for (int v = 0; v < 2; v++)
                    {
                        Vector3 camPos = v == 0 ? mid + new Vector3(0f, 1.2f, -3.4f) : pd - fwd * 1.9f + Vector3.Cross(Vector3.up, fwd) * -0.9f + Vector3.up * 1.75f;
                        Vector3 look = v == 0 ? mid + Vector3.up * 1.05f : pd + fwd * 0.9f + Vector3.up * 1.15f;
                        cam.transform.position = camPos; cam.transform.rotation = Quaternion.LookRotation(look - camPos, Vector3.up); cam.fieldOfView = 40f;
                        cam.targetTexture = rt; cam.Render();
                        RenderTexture.active = rt;
                        sheet.ReadPixels(new Rect(0, 0, cw, chh), k * cw, (1 - v) * chh);
                    }
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(dir, name + ".jpg"), sheet.EncodeToJPG(85));
                log.Append("PROBE|shots|" + name + "|ok\n");
            }
            catch (Exception e) { log.Append("PROBE|shots|" + name + "|error " + e.Message + "\n"); }
            finally
            {
                cam.targetTexture = prevT; RenderTexture.active = prevA;
                cam.transform.position = p0; cam.transform.rotation = r0; cam.fieldOfView = f0;
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(sheet);
                blocking.GetSetMethod(true).Invoke(def, new object[] { false }); bk.SetValue(wd, 0f);
                wa.SnapRest(); wd.SnapRest();
                for (int i = 0; i < 20; i++) { Tick(def, dd, 1f / 30f, 1f / 30f); Tick(att, da, 1f / 30f, 1f / 30f); }
            }
        }

        /// <summary>Рывок вперёд (верхний ряд, 4 кадра через 0,05 с) и прыжок (нижний ряд, через 0,12 с) — сбоку.</summary>
        static void MoveStrip(Camera cam, string dir, string name, FighterController f, FighterController other, StringBuilder log)
        {
            HeroRigDriver d = HeroRigDriver.Of(f);
            if (d == null) return;
            const float dt = 1f / 30f;
            const int cw = 480, chh = 420;
            Transform ft = f.transform; Vector3 start = ft.position; Quaternion startRot = ft.rotation;
            Vector3 otherPos = other != null ? other.transform.position : Vector3.zero;
            FieldInfo dl = typeof(FighterController).GetField("dashLeft", F), dd = typeof(FighterController).GetField("dashDir", F), vyF = typeof(FighterController).GetField("vy", F);
            PropertyInfo gr = typeof(FighterController).GetProperty("Grounded", F);
            Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation; float f0 = cam.fieldOfView;
            Texture2D sheet = new Texture2D(cw * 4, chh * 2, TextureFormat.RGB24, false);
            RenderTexture rt = new RenderTexture(cw, chh, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            RenderTexture prevT = cam.targetTexture, prevA = RenderTexture.active;
            if (f.weapon != null) f.weapon.SnapRest();
            try
            {
                Vector3 face = ft.forward; face.y = 0f; face.Normalize();
                if (other != null) other.transform.position = start + face * 7f;
                for (int row = 0; row < 2; row++)
                {
                    ft.position = start;
                    for (int i = 0; i < 20; i++) Tick(f, d, dt, dt);
                    Vector3 camPos = start + face * 0.7f + new Vector3(0f, row == 0 ? 1.05f : 1.55f, row == 0 ? -3.4f : -4.0f), look = start + face * 0.7f + Vector3.up * (row == 0 ? 0.9f : 1.35f);
                    if (row == 0) { dl.SetValue(f, f.dashTime); dd.SetValue(f, face); }
                    else { vyF.SetValue(f, f.jumpVelocity); gr.GetSetMethod(true).Invoke(f, new object[] { false }); }
                    int every = row == 0 ? 2 : 4, k = 0;
                    for (int i = 0; i < 4 * every && k < 4; i++)
                    {
                        if (row == 0)
                        {
                            float left = (float)dl.GetValue(f);
                            if (left > 0f) { ft.position += face * (f.dashSpeed * dt); dl.SetValue(f, left - dt); }
                        }
                        else
                        {
                            float vy = (float)vyF.GetValue(f) - f.gravity * dt;
                            Vector3 p = ft.position; p.y += vy * dt;
                            if (p.y <= 0f) { p.y = 0f; vy = 0f; gr.GetSetMethod(true).Invoke(f, new object[] { true }); }
                            ft.position = p; vyF.SetValue(f, vy);
                        }
                        Tick(f, d, dt, dt);
                        if (i % every == every - 1)
                        {
                            cam.transform.position = camPos; cam.transform.rotation = Quaternion.LookRotation(look - camPos, Vector3.up); cam.fieldOfView = 40f;
                            cam.targetTexture = rt; cam.Render();
                            RenderTexture.active = rt;
                            sheet.ReadPixels(new Rect(0, 0, cw, chh), k * cw, (1 - row) * chh);
                            k++;
                        }
                    }
                    dl.SetValue(f, 0f); vyF.SetValue(f, 0f); gr.GetSetMethod(true).Invoke(f, new object[] { true });
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(dir, name + ".jpg"), sheet.EncodeToJPG(85));
                log.Append("PROBE|shots|" + name + "|ok\n");
            }
            catch (Exception e) { log.Append("PROBE|shots|" + name + "|error " + e.Message + "\n"); }
            finally
            {
                cam.targetTexture = prevT; RenderTexture.active = prevA;
                cam.transform.position = p0; cam.transform.rotation = r0; cam.fieldOfView = f0;
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(sheet);
                ft.position = start; ft.rotation = startRot;
                if (other != null) other.transform.position = otherPos;
                for (int i = 0; i < 20; i++) Tick(f, d, dt, dt);
            }
        }

        /// <summary>Погода: снег (хлопья, побелевший камень, снег на плечах) и ливень (мокрые бойцы, отражения) — общий план как в игре.</summary>
        /// <summary>Солнечный день (DayNight): кадр как в игре и общий план двора; после — ночь как была.</summary>
        static void DayShots(Camera cam, string dir, Vector3 pos, Vector3 look, StringBuilder log)
        {
            DayNight dn = UnityEngine.Object.FindFirstObjectByType<DayNight>();
            if (dn == null) { log.Append("PROBE|shots|day|нет DayNight\n"); return; }
            try
            {
                Call(dn, "Awake");
                dn.Apply(true);
                foreach (FlickerLight fl in UnityEngine.Object.FindObjectsByType<FlickerLight>(FindObjectsSortMode.None)) Call(fl, "Update");
                GameObject dfly = GameObject.Find("Dragonflies");
                ParticleSystem dps = dfly != null ? dfly.GetComponent<ParticleSystem>() : null;
                if (dps != null) { Call(dfly.GetComponent<PetalTexture>(), "Awake"); var dem = dps.emission; dem.enabled = true; dps.Simulate(8f, true, true); }
                Shot(cam, dir, "33_day", pos, look, cam.fieldOfView, log);
                Shot(cam, dir, "33b_day_arena", new Vector3(0f, 3.6f, -6.4f), new Vector3(0f, 1.2f, 2.5f), 50f, log);
                ReflectShot(cam, dir, "37b_reflect_day", log);
                Shot(cam, dir, "39b_garden_day", new Vector3(0f, 3.0f, -2.0f), new Vector3(0f, 0f, -5.8f), 62f, log);
                Shot(cam, dir, "40b_backcourt_day", new Vector3(5.2f, 2.4f, 11.5f), new Vector3(-1.2f, 0.9f, 9.2f), 62f, log);
                dn.Apply(true, false, true);   // закат: солнце за храмом
                foreach (FlickerLight fl in UnityEngine.Object.FindObjectsByType<FlickerLight>(FindObjectsSortMode.None)) Call(fl, "Update");
                Shot(cam, dir, "41_dusk", pos, look, cam.fieldOfView, log);
                Shot(cam, dir, "41b_dusk_arena", new Vector3(0f, 3.6f, -6.4f), new Vector3(0f, 1.2f, 2.5f), 50f, log);
                dn.Apply(true, true);   // рассвет в тумане
                foreach (FlickerLight fl in UnityEngine.Object.FindObjectsByType<FlickerLight>(FindObjectsSortMode.None)) Call(fl, "Update");
                Shot(cam, dir, "34_dawn", pos, look, cam.fieldOfView, log);
                Shot(cam, dir, "34b_dawn_arena", new Vector3(0f, 3.6f, -6.4f), new Vector3(0f, 1.2f, 2.5f), 50f, log);
                dn.Apply(true);
                if (dps != null)
                {
                    // стрекоза крупно: ближайшая частица
                    ParticleSystem.Particle[] pp = new ParticleSystem.Particle[8];
                    int cnt = dps.GetParticles(pp);
                    if (cnt > 0) Shot(cam, dir, "33c_dragonfly", pp[0].position + new Vector3(0.25f, 0.1f, -0.6f), pp[0].position, 30f, log);
                    var dem2 = dps.emission; dem2.enabled = false; dps.Clear();
                }
            }
            catch (Exception e) { log.Append("PROBE|shots|day|error " + e.Message + "\n"); }
            finally
            {
                dn.Apply(false);
                foreach (FlickerLight fl in UnityEngine.Object.FindObjectsByType<FlickerLight>(FindObjectsSortMode.None)) Call(fl, "Update");
                PropertyInfo ip = typeof(DayNight).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (ip != null) ip.GetSetMethod(true).Invoke(null, new object[] { null });   // иначе следующий бросок погоды в редакторе может включить день
            }
        }

        static void WeatherShots(Camera cam, string dir, Vector3 pos, Vector3 look, StringBuilder log)
        {
            // Weather в игре добавляет DuelManager при запуске — здесь временный, со своими частицами; после кадров всё убираем
            GameObject wg = new GameObject("WeatherShots");
            Weather w = wg.AddComponent<Weather>();
            PropertyInfo snowP = typeof(Weather).GetProperty("Snowing", F), rainP = typeof(Weather).GetProperty("Raining", F);
            float fogD = RenderSettings.fogDensity; Color fogC = RenderSettings.fogColor;
            try
            {
                Call(w, "Awake");
                Call(w, "Start");   // запоминает луну, туман, небо (и бросает случайную погоду — сбрасываем)
                Call(w, "StopRain"); Call(w, "StopSnow");
                // снег
                snowP.GetSetMethod(true).Invoke(w, new object[] { true }); rainP.GetSetMethod(true).Invoke(w, new object[] { false });
                Call(w, "StartSnow");
                Shader.SetGlobalFloat("_BP_Snow", 0.85f); Shader.SetGlobalFloat("_BP_Wetness", 0f);
                SnowDrifts drifts = wg.GetComponent<SnowDrifts>(); if (drifts != null) Call(drifts, "Update");   // сугробы у стен и жаровен
                foreach (ParticleSystem ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.name == "Snow") ps.Simulate(8f, true, true);
                // следы в снегу: бойцы пришли к месту поединка — цепочка отпечатков за каждым (и пара кровавых у врага)
                Footprints fp = Footprints.Ensure();
                foreach (FighterController f in UnityEngine.Object.FindObjectsByType<FighterController>(FindObjectsSortMode.None))
                {
                    Transform ft = f.transform;
                    for (int i = 0; i < 9; i++)
                    {
                        bool left = (i & 1) == 0;
                        Vector3 p = ft.position - ft.forward * (0.45f + i * 0.4f) + ft.right * (left ? -0.13f : 0.13f) + ft.right * Mathf.Sin(i * 0.7f) * 0.05f;
                        fp.Add(p, Quaternion.AngleAxis(left ? -8f : 8f, Vector3.up) * ft.forward, left, false, 1f);
                    }
                }
                Call(fp, "LateUpdate");
                Shot(cam, dir, "20_snow", pos, look, cam.fieldOfView, log);
                Shot(cam, dir, "20b_snow_prints", look + new Vector3(0f, 2.4f, -3.2f), look - Vector3.up * 1.0f, 46f, log);
                Shot(cam, dir, "20c_snow_arena", new Vector3(0f, 3.6f, -6.4f), new Vector3(0f, 1.2f, 2.5f), 50f, log);   // снег на крышах, стенах, земле
                Shader.SetGlobalFloat("_BP_Snow", 0.4f);
                if (drifts != null) Call(drifts, "Update");
                Shot(cam, dir, "20d_snow_early", new Vector3(0f, 3.6f, -6.4f), new Vector3(0f, 1.2f, 2.5f), 50f, log);   // снег только начал ложиться — пятнами
                Shader.SetGlobalFloat("_BP_Snow", 0.85f);
                UnityEngine.Object.DestroyImmediate(fp.gameObject);
                Call(w, "StopSnow");
                snowP.GetSetMethod(true).Invoke(w, new object[] { false });
                Shader.SetGlobalFloat("_BP_Snow", 0f);
                GameObject driftRoot = GameObject.Find("SnowDrifts"); if (driftRoot != null) UnityEngine.Object.DestroyImmediate(driftRoot);
                foreach (ParticleSystem ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.name == "Snow") ps.Clear();
                // ливень
                rainP.GetSetMethod(true).Invoke(w, new object[] { true });
                Call(w, "StartRain");
                Shader.SetGlobalFloat("_BP_Wetness", 1f);
                foreach (ParticleSystem ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                    if (ps.name == "Rain" || ps.name == "RainSplash" || ps.name == "RainRipples" || ps.name == "EaveDrips") ps.Simulate(2f, true, true);
                Shot(cam, dir, "21_rain", pos, look, cam.fieldOfView, log);
                Shot(cam, dir, "21b_rain_arena", new Vector3(0f, 3.6f, -6.4f), new Vector3(0f, 1.2f, 2.5f), 50f, log);   // мокрое дерево и камень блестят
                Call(w, "StopRain"); rainP.GetSetMethod(true).Invoke(w, new object[] { false });
                Shader.SetGlobalFloat("_BP_Wetness", 0f);
                foreach (ParticleSystem ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                    if (ps.name == "Rain" || ps.name == "RainSplash" || ps.name == "RainRipples" || ps.name == "EaveDrips" || ps.name == "EaveSplash") ps.Clear();
                Call(w, "StartFog");
                Shot(cam, dir, "42_fog", pos, look, cam.fieldOfView, log);
                Shot(cam, dir, "42b_fog_arena", new Vector3(0f, 3.6f, -6.4f), new Vector3(0f, 1.2f, 2.5f), 50f, log);
                Call(w, "StopFog");
                rainP.GetSetMethod(true).Invoke(w, new object[] { true });
                Call(w, "StopRain");
                rainP.GetSetMethod(true).Invoke(w, new object[] { false });
                Shader.SetGlobalFloat("_BP_Wetness", 0f);
                foreach (ParticleSystem ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                    if (ps.name == "Rain" || ps.name == "RainSplash" || ps.name == "RainRipples" || ps.name == "EaveDrips" || ps.name == "EaveSplash") ps.Clear();
            }
            catch (Exception e) { log.Append("PROBE|shots|weather|error " + e.Message + "\n"); }
            finally
            {
                foreach (string n in new[] { "Rain", "RainSplash", "RainRipples", "Snow", "SnowFloor" })
                {
                    GameObject g = GameObject.Find(n);
                    if (g != null) UnityEngine.Object.DestroyImmediate(g);
                }
                foreach (HeroRigDriver d in UnityEngine.Object.FindObjectsByType<HeroRigDriver>(FindObjectsSortMode.None))
                    if (d.head != null) { Transform b = d.head.Find("Breath"); if (b != null) UnityEngine.Object.DestroyImmediate(b.gameObject); }
                UnityEngine.Object.DestroyImmediate(wg);
                RenderSettings.fogDensity = fogD; RenderSettings.fogColor = fogC;
                Shader.SetGlobalFloat("_BP_Snow", 0f); Shader.SetGlobalFloat("_BP_Wetness", 0f);
            }
        }

        /// <summary>Один кадр бойца как в игре: Update драйвера → Animator → LateUpdate драйвера → полы плаща.</summary>
        /// <summary>
        /// Портрет героя для HUD в стиле нарисованных (UI/portrait_*): лицо крупно, тёплый ключевой свет и холодный контур,
        /// тёмный фон, виньетка, рамка — пиксели кольца берутся из portrait_ronin. Сохраняется в Resources/UI/portrait_hero.png
        /// и импортируется до сборки APK. Раньше у игрока был портрет бородатого ронина, а тот теперь — облик врага.
        /// </summary>
        static void HeroPortrait(FighterController f, StringBuilder log)
        {
            HeroRigDriver d = HeroRigDriver.Of(f);
            string framePath = Path.Combine(Application.dataPath, "_Project/Resources/UI/portrait_ronin.png");
            if (d == null || d.head == null || !File.Exists(framePath)) return;
            const int S = 320, O = 160;
            GameObject cg = new GameObject("PortraitShotCam");
            Camera c = cg.AddComponent<Camera>();
            GameObject k1 = new GameObject("PortraitKey"), k2 = new GameObject("PortraitRim");
            RenderTexture rt = new RenderTexture(S, S, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            try
            {
                Transform ft = f.transform;
                Vector3 face = d.head.position + ft.forward * 0.06f - Vector3.up * 0.01f;
                c.transform.position = face + ft.forward * 0.62f + ft.right * 0.2f + Vector3.up * 0.03f;
                c.transform.rotation = Quaternion.LookRotation(face - c.transform.position, Vector3.up);
                c.fieldOfView = 24f; c.nearClipPlane = 0.05f; c.farClipPlane = 1.6f;
                c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0.05f, 0.035f, 0.035f, 1f);
                c.targetTexture = rt;
                Light kl = k1.AddComponent<Light>(); kl.type = LightType.Point; kl.range = 2.2f; kl.intensity = 1.6f; kl.color = new Color(1f, 0.78f, 0.55f);
                k1.transform.position = face + ft.forward * 0.55f - ft.right * 0.35f + Vector3.up * 0.3f;
                Light rl = k2.AddComponent<Light>(); rl.type = LightType.Point; rl.range = 1.6f; rl.intensity = 1.2f; rl.color = new Color(0.55f, 0.65f, 1f);
                k2.transform.position = face - ft.forward * 0.4f + ft.right * 0.45f + Vector3.up * 0.25f;
                c.Render();
                RenderTexture.active = rt;
                Texture2D shot = new Texture2D(S, S, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, S, S), 0, 0); shot.Apply();
                RenderTexture.active = null;
                Texture2D frame = new Texture2D(2, 2); frame.LoadImage(File.ReadAllBytes(framePath));
                Texture2D outT = new Texture2D(O, O, TextureFormat.RGBA32, false);
                Color[] fpx = frame.width == O ? frame.GetPixels() : null;
                Color[] spx = shot.GetPixels();
                Color[] opx = new Color[O * O];
                for (int y = 0; y < O; y++)
                    for (int x = 0; x < O; x++)
                    {
                        float u = (x + 0.5f) / O * 2f - 1f, v = (y + 0.5f) / O * 2f - 1f, r = Mathf.Sqrt(u * u + v * v);
                        Color fc = fpx != null ? fpx[y * O + x] : Color.clear;
                        if (r > 0.86f && fpx != null) { opx[y * O + x] = fc; continue; }   // кольцо рамки — как у нарисованных портретов
                        // лицо: 2×2 отсчёта рендера, виньетка, лёгкое тёплое тонирование и контраст «под живопись»
                        Color sc = (spx[(y * 2) * S + x * 2] + spx[(y * 2) * S + x * 2 + 1] + spx[(y * 2 + 1) * S + x * 2] + spx[(y * 2 + 1) * S + x * 2 + 1]) * 0.25f;
                        float vig = Mathf.Clamp01(1.1f - r * r * 0.9f);
                        float l = sc.r * 0.3f + sc.g * 0.55f + sc.b * 0.15f;
                        Color g = Color.Lerp(new Color(l, l, l), sc, 0.85f);
                        g = new Color(Mathf.Pow(g.r, 1.08f) * 1.06f, Mathf.Pow(g.g, 1.08f) * 1.0f, Mathf.Pow(g.b, 1.08f) * 0.94f);
                        g *= vig;
                        float edge = Mathf.Clamp01((0.88f - r) / 0.03f);
                        Color o = new Color(g.r, g.g, g.b, 1f);
                        opx[y * O + x] = fpx != null && r > 0.84f ? Color.Lerp(fc, o, edge) : o;
                    }
                outT.SetPixels(opx); outT.Apply();
                string outPath = "Assets/_Project/Resources/UI/portrait_hero.png";
                File.WriteAllBytes(Path.Combine(Application.dataPath, "_Project/Resources/UI/portrait_hero.png"), outT.EncodeToPNG());
                UnityEditor.AssetDatabase.ImportAsset(outPath);
                File.WriteAllBytes(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "../build/shots")), "35_portrait_hero.png"), outT.EncodeToPNG());
                log.Append("PROBE|shots|35_portrait_hero|ok\n");
                UnityEngine.Object.DestroyImmediate(shot); UnityEngine.Object.DestroyImmediate(frame); UnityEngine.Object.DestroyImmediate(outT);
            }
            catch (Exception e) { log.Append("PROBE|shots|portrait|error " + e.Message + "\n"); }
            finally
            {
                c.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(cg); UnityEngine.Object.DestroyImmediate(k1); UnityEngine.Object.DestroyImmediate(k2);
            }
        }

        /// <summary>Глоток из тыквы-горлянки: 4 фазы (подъём, у рта, глоток с запрокинутой головой, опускание).</summary>
        static void DrinkStrip(Camera cam, string dir, FighterController f, StringBuilder log)
        {
            WeaponController w = f.weapon; HeroRigDriver d = HeroRigDriver.Of(f);
            if (w == null || d == null) return;
            FieldInfo ds = typeof(FighterController).GetField("drinkStart", F), du = typeof(FighterController).GetField("drinkUntil", F), dk = typeof(WeaponController).GetField("drinkK", F);
            if (ds == null || du == null || dk == null) { log.Append("PROBE|shots|32_drink|нет полей\n"); return; }
            const int cw = 480, chh = 560;
            float[] taus = { 0.15f, 0.36f, 0.6f, 1.02f };
            Texture2D sheet = new Texture2D(cw * 4, chh, TextureFormat.RGB24, false);
            RenderTexture rt = new RenderTexture(cw, chh, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            RenderTexture prevT = cam.targetTexture; Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation; float f0 = cam.fieldOfView;
            try
            {
                w.SnapRest();
                Transform ft = f.transform;
                Vector3 eye = ft.position + ft.forward * 1.3f + Vector3.back * 1.6f + Vector3.up * 1.55f, look = ft.position + ft.forward * 0.15f + Vector3.up * 1.25f;
                for (int k = 0; k < taus.Length; k++)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        ds.SetValue(f, Time.time - taus[k]); du.SetValue(f, Time.time + 5f);
                        dk.SetValue(w, 1f); Call(w, "ApplyRestPose");
                        Tick(f, d, 1f / 30f, 1f / 30f);
                    }
                    cam.transform.position = eye; cam.transform.rotation = Quaternion.LookRotation(look - eye, Vector3.up); cam.fieldOfView = 34f;
                    cam.targetTexture = rt; cam.Render();
                    RenderTexture.active = rt; sheet.ReadPixels(new Rect(0, 0, cw, chh), cw * k, 0); RenderTexture.active = null;
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(dir, "32_drink.jpg"), sheet.EncodeToJPG(86));
                log.Append("PROBE|shots|32_drink|4 фазы\n");
            }
            catch (Exception e) { log.Append("PROBE|shots|32_drink|error " + e.Message + "\n"); }
            finally
            {
                ds.SetValue(f, -10f); du.SetValue(f, 0f); dk.SetValue(w, 0f); w.SnapRest();
                cam.targetTexture = prevT; cam.transform.SetPositionAndRotation(p0, r0); cam.fieldOfView = f0;
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(sheet);
                for (int i = 0; i < 12; i++) Tick(f, d, 1f / 30f, 1f / 30f);
                GameObject gq = GameObject.Find("Gourd"); if (gq != null) gq.SetActive(false);
            }
        }

        /// <summary>Стойки врага: тюдан, хассо, вакигамаэ — спереди-сбоку, склейка в один кадр.</summary>
        static void StanceShots(Camera cam, string dir, FighterController f, StringBuilder log)
        {
            WeaponController w = f.weapon; HeroRigDriver d = HeroRigDriver.Of(f);
            if (w == null || d == null) return;
            const int cw = 480, chh = 600;
            Texture2D sheet = new Texture2D(cw * 3, chh, TextureFormat.RGB24, false);
            RenderTexture rt = new RenderTexture(cw, chh, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            RenderTexture prevT = cam.targetTexture; Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation; float f0 = cam.fieldOfView;
            try
            {
                Transform ft = f.transform;
                Vector3 eye = ft.position + Vector3.back * 3.0f + ft.forward * 1.1f + Vector3.up * 1.3f, look = ft.position + Vector3.up * 1.05f;   // как камера боя, чуть спереди
                for (int i = 0; i < 3; i++)
                {
                    w.stance = i; w.SnapRest();
                    for (int k = 0; k < 12; k++) { Call(w, "Update"); Tick(f, d, 1f / 30f, 1f / 30f); }
                    cam.transform.position = eye; cam.transform.rotation = Quaternion.LookRotation(look - eye, Vector3.up); cam.fieldOfView = 38f;
                    cam.targetTexture = rt; cam.Render();
                    RenderTexture.active = rt; sheet.ReadPixels(new Rect(0, 0, cw, chh), cw * i, 0); RenderTexture.active = null;
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(dir, "29_enemy_stances.jpg"), sheet.EncodeToJPG(86));
                log.Append("PROBE|shots|29_enemy_stances|3 стойки\n");
            }
            catch (Exception e) { log.Append("PROBE|shots|29_enemy_stances|error " + e.Message + "\n"); }
            finally
            {
                w.stance = 0; w.SnapRest();
                cam.targetTexture = prevT; cam.transform.SetPositionAndRotation(p0, r0); cam.fieldOfView = f0;
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(sheet);
                for (int k = 0; k < 12; k++) Tick(f, d, 1f / 30f, 1f / 30f);
            }
        }

        /// <summary>Кровь на клинке крупно: три уровня (после удара, после трёх, после добивания) и подсохшая — склейка в один кадр.</summary>
        static void BladeBloodShot(Camera cam, string dir, FighterController f, StringBuilder log)
        {
            WeaponController w = f.weapon;
            if (w == null || w.bladeRoot == null) return;
            List<Renderer> steel = new List<Renderer>();
            foreach (Renderer r in w.bladeRoot.GetComponentsInChildren<Renderer>(true))
                if (r is MeshRenderer && r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Blood")) steel.Add(r);
            if (steel.Count == 0) { log.Append("PROBE|shots|27_blade_blood|нет шейдера крови\n"); return; }
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            Bounds bb = steel[0].bounds;   // кадр по самому мешу клинка
            Vector3 mid = bb.center;
            Vector3 ax = w.bladeRoot.forward;
            Vector3 side = Vector3.Cross(ax, Vector3.up).normalized;
            if (Vector3.Dot(side, cam.transform.position - mid) < 0f) side = -side;
            Vector3 eye = mid + side * (bb.extents.magnitude * 2.3f + 0.3f) + Vector3.up * 0.2f;
            float[] lv = { 0.22f, 0.6f, 0.95f, 0.6f }, dry = { 0f, 0f, 0f, 1f };
            int W = 960, H = 360;   // клинок длинный — кадры широкие, 2×2
            Texture2D sheet = new Texture2D(W * 2, H * 2, TextureFormat.RGB24, false);
            RenderTexture rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            RenderTexture prevT = cam.targetTexture; Vector3 pp = cam.transform.position; Quaternion pr = cam.transform.rotation; float pf = cam.fieldOfView;
            for (int i = 0; i < lv.Length; i++)
            {
                foreach (Renderer r in steel) { r.GetPropertyBlock(mpb); mpb.SetFloat("_Blood", lv[i]); mpb.SetFloat("_BloodDry", dry[i]); r.SetPropertyBlock(mpb); }
                cam.transform.position = eye; cam.transform.LookAt(mid); cam.fieldOfView = 34f;
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt; sheet.ReadPixels(new Rect(0, 0, W, H), W * (i % 2), H * (1 - i / 2)); RenderTexture.active = null;
            }
            foreach (Renderer r in steel) { r.GetPropertyBlock(mpb); mpb.SetFloat("_Blood", 0f); mpb.SetFloat("_BloodDry", 0f); r.SetPropertyBlock(mpb); }
            cam.targetTexture = prevT; cam.transform.SetPositionAndRotation(pp, pr); cam.fieldOfView = pf;
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(dir, "27_blade_blood.jpg"), sheet.EncodeToJPG(88));
            UnityEngine.Object.DestroyImmediate(sheet); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            log.Append("PROBE|shots|27_blade_blood|4 уровня\n");
        }

        /// <summary>Зеркальный шар перед бойцами — проверка кубкарты отражений: небо сверху, земля и дымка снизу, без перевёрнутых граней.</summary>
        static void ReflectShot(Camera cam, string dir, string name, StringBuilder log)
        {
            GameObject s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = "ReflectProbeBall";
            UnityEngine.Object.DestroyImmediate(s.GetComponent<Collider>());
            s.transform.position = new Vector3(0f, 1.1f, -1.7f);
            s.transform.localScale = Vector3.one * 0.7f;
            Material m = new Material(Shader.Find("Standard"));
            m.SetFloat("_Metallic", 1f); m.SetFloat("_Glossiness", 0.96f); m.color = Color.white;
            s.GetComponent<Renderer>().sharedMaterial = m;
            try { Shot(cam, dir, name, new Vector3(0f, 1.25f, -2.75f), new Vector3(0f, 1.1f, -1.7f), 40f, log); }
            finally { UnityEngine.Object.DestroyImmediate(s); UnityEngine.Object.DestroyImmediate(m); }
        }

        /// <summary>Разворот таза и груди относительно бойца: поза Animator и после драйвера (строка диагностики драйвера).</summary>
        static void YawProbe(FighterController f, HeroRigDriver d, string tag, StringBuilder log)
        {
            if (f == null || d == null) return;
            HeroRigDriver.Diag = true;
            try
            {
                Tick(f, d, 1f / 30f, 1f / 30f);
                FieldInfo fi = typeof(HeroRigDriver).GetField("diag", BindingFlags.NonPublic | BindingFlags.Instance);
                log.Append("PROBE|yaw|" + tag + "|" + (fi != null ? fi.GetValue(d) : "-") + "\n");
            }
            finally { HeroRigDriver.Diag = false; }
        }

        static void SetWound(FighterController f, float w)
        {
            foreach (Renderer r in f.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && m.HasProperty("_Wound")) m.SetFloat("_Wound", w);
        }

        static void Tick(FighterController f, HeroRigDriver d, float animDt, float dt)
        {
            HeroRigDriver.DtOverride = dt;
            Call(d, "Update");
            if (d.animator != null) d.animator.Update(animDt);
            Call(d, "LateUpdate");
            CoatSpring cs = d.GetComponent<CoatSpring>();   // на модели облика (у врага их несколько)
            if (cs != null) Call(cs, "LateUpdate");
            ContactShadow sh = d.GetComponent<ContactShadow>();
            if (sh != null) Call(sh, "LateUpdate");
        }

        /// <summary>Раскадровка походки: боец идёт dirLocal со скоростью speed (как PoseProbe.WalkSim), 8 кадров через 0,1 с после разгона.</summary>
        static void WalkStrip(Camera cam, string dir, string name, FighterController f, FighterController other, Vector3 dirLocal, float speed, bool camBehind, StringBuilder log)
        {
            HeroRigDriver d = HeroRigDriver.Of(f);
            if (d == null || d.animator == null) return;
            const float dt = 1f / 30f;
            const int n = 8, cw = 480, chh = 420, warm = 30, every = 3;
            Transform ft = f.transform; Vector3 start = ft.position; Quaternion startRot = ft.rotation;
            MethodInfo fu = typeof(FighterController).GetMethod("Update", F);
            Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation; float f0 = cam.fieldOfView;
            Texture2D sheet = new Texture2D(cw * 4, chh * 2, TextureFormat.RGB24, false);
            RenderTexture rt = new RenderTexture(cw, chh, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            RenderTexture prevT = cam.targetTexture, prevA = RenderTexture.active;
            if (f.weapon != null) f.weapon.SnapRest();
            Vector3 otherPos = other != null ? other.transform.position : Vector3.zero;
            try
            {
                Vector3 wdir = ft.TransformDirection(dirLocal);
                // противник отходит далеко вперёд по линии взгляда: боец смотрит туда же, но не упирается в него
                Vector3 face = ft.forward; face.y = 0f; face.Normalize();
                if (other != null) other.transform.position = start + face * 7f;
                // камера стоит на месте (видно, едет ли стопа по земле), смотрит на середину пройденного пути:
                // шаг вперёд — сбоку, приставной вбок — со спины (движение поперёк кадра)
                Vector3 midPath = start + wdir * speed * dt * (warm + n * every * 0.5f);
                Vector3 camPos = camBehind ? midPath - face * 3.4f + Vector3.up * 1.05f : midPath + new Vector3(0f, 1.0f, -3.6f);
                Vector3 look = midPath + Vector3.up * 0.75f;
                int k = 0;
                for (int i = 0; i < warm + n * every && k < n; i++)
                {
                    f.SetMoveVector(new Vector2(wdir.x, wdir.z));
                    if (fu != null) fu.Invoke(f, null);
                    ft.position += wdir * (speed * dt);
                    Tick(f, d, dt, dt);
                    if (i >= warm && (i - warm) % every == 0)
                    {
                        cam.transform.position = camPos; cam.transform.rotation = Quaternion.LookRotation(look - camPos, Vector3.up); cam.fieldOfView = 40f;
                        cam.targetTexture = rt; cam.Render();
                        RenderTexture.active = rt;
                        sheet.ReadPixels(new Rect(0, 0, cw, chh), (k % 4) * cw, (1 - k / 4) * chh);
                        k++;
                    }
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(dir, name + ".jpg"), sheet.EncodeToJPG(85));
                log.Append("PROBE|shots|" + name + "|" + k + " кадров\n");
            }
            catch (Exception e) { log.Append("PROBE|shots|" + name + "|error " + e.Message + "\n"); }
            finally
            {
                cam.targetTexture = prevT; RenderTexture.active = prevA;
                cam.transform.position = p0; cam.transform.rotation = r0; cam.fieldOfView = f0;
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(sheet);
                f.SetMoveVector(Vector2.zero);
                ft.position = start; ft.rotation = startRot;
                if (other != null) other.transform.position = otherPos;
                for (int i = 0; i < 30; i++) { if (fu != null) fu.Invoke(f, null); Tick(f, d, dt, dt); }
            }
        }

        static void Shot(Camera cam, string dir, string name, Vector3 pos, Vector3 look, float fov, StringBuilder log)
        {
            const int W = 1280, H = 720;
            Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation; float f0 = cam.fieldOfView;
            RenderTexture rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            RenderTexture prevT = cam.targetTexture, prevA = RenderTexture.active;
            try
            {
                cam.transform.position = pos;
                cam.transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
                cam.fieldOfView = fov;
                foreach (GlowSprite gs in UnityEngine.Object.FindObjectsByType<GlowSprite>(FindObjectsSortMode.None)) Call(gs, "LateUpdate");   // ореолы — лицом к камере
                foreach (FloorReflection fr in UnityEngine.Object.FindObjectsByType<FloorReflection>(FindObjectsSortMode.None)) Call(fr, "LateUpdate");   // отражения — под эту камеру
                foreach (LightShaft ls in UnityEngine.Object.FindObjectsByType<LightShaft>(FindObjectsSortMode.None)) Call(ls, "LateUpdate");          // лучи — лицом к этой камере
                foreach (BladeGlint bg in UnityEngine.Object.FindObjectsByType<BladeGlint>(FindObjectsSortMode.None)) Call(bg, "LateUpdate");          // блик — лицом к этой камере
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                Color32[] px = tex.GetPixels32();
                double lum = 0; for (int i = 0; i < px.Length; i += 97) lum += (px[i].r + px[i].g + px[i].b) / 3.0;
                File.WriteAllBytes(Path.Combine(dir, name + ".jpg"), tex.EncodeToJPG(88));
                log.Append("PROBE|shots|" + name + "|mean " + (lum / (px.Length / 97.0)).ToString("F1") + "\n");
                UnityEngine.Object.DestroyImmediate(tex);
            }
            catch (Exception e) { log.Append("PROBE|shots|" + name + "|error " + e.Message + "\n"); }
            finally
            {
                cam.targetTexture = prevT; RenderTexture.active = prevA;
                cam.transform.position = p0; cam.transform.rotation = r0; cam.fieldOfView = f0;
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            }
        }
    }
}
#endif
