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
                    HeroRigDriver d = f.GetComponentInChildren<HeroRigDriver>(true);
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
                        HeroRigDriver d = f.GetComponentInChildren<HeroRigDriver>(true);
                        if (d == null || d.animator == null) continue;
                        if (f.weapon != null) Call(f.weapon, "Update");
                        Tick(f, d, dt, dt);
                    }

                // компоненты, которые в игре готовятся в Awake (текстуры листьев/лепестков, ореолы) — в редакторе вызываем сами
                foreach (MonoBehaviour mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    if (mb is PetalTexture || mb is GlowSprite || mb is FlickerLight || mb is FloorReflection) Call(mb, "Awake");
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
                if (enemy != null) Shot(cam, dir, "3_enemy", ep + new Vector3(0f, 1.2f, -3.0f) + enemy.transform.forward * 0.9f, ep + Vector3.up * 0.95f, 36f, log);
                // хват: руки на рукояти крупно (с камеры спереди-сбоку и сверху-сбоку)
                if (player != null && player.weapon != null && player.weapon.pivot != null)
                {
                    Vector3 hp = player.weapon.pivot.position;
                    Transform pt = player.transform;
                    Shot(cam, dir, "7_grip", hp + pt.forward * 0.75f - pt.right * 0.55f + Vector3.up * 0.15f, hp, 34f, log);
                    Shot(cam, dir, "8_grip_top", hp + pt.forward * 0.35f + pt.right * 0.45f + Vector3.up * 0.65f, hp, 34f, log);
                }
                // раскадровка ударов: 8 фаз сбоку (как камера боя) — видно замах, удар, проводку и возврат
                if (player != null)
                {
                    SwingStrip(cam, dir, "9_swing_overhead", player, AttackType.Overhead, log);
                    SwingStrip(cam, dir, "10_swing_slash", player, AttackType.Slash, log);
                    SwingStrip(cam, dir, "11_swing_thrust", player, AttackType.Thrust, log);
                }
                Shot(cam, dir, "4_arena", new Vector3(0f, 3.6f, -6.4f), new Vector3(0f, 1.2f, 2.5f), 50f, log);
                // походка: шаг вперёд и приставной вбок (мокап + замок стоп), камера едет рядом
                if (player != null)
                {
                    WalkStrip(cam, dir, "12_walk_fwd", player, Vector3.forward, 1.2f, log);
                    WalkStrip(cam, dir, "13_walk_side", player, Vector3.right, 0.72f, log);
                    if (enemy != null) DeathblowStrip(cam, dir, "14_deathblow", player, enemy, log);
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
            WeaponController w = f.weapon; HeroRigDriver d = f.GetComponentInChildren<HeroRigDriver>(true);
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
        static void DeathblowStrip(Camera cam, string dir, string name, FighterController a, FighterController t, StringBuilder log)
        {
            WeaponController w = a.weapon; HeroRigDriver da = a.GetComponentInChildren<HeroRigDriver>(true), dt_ = t.GetComponentInChildren<HeroRigDriver>(true);
            if (w == null || da == null || dt_ == null || t.weapon == null) return;
            Vector3 pa0 = a.transform.position, pt0 = t.transform.position; Quaternion ra0 = a.transform.rotation, rt0 = t.transform.rotation;
            PropertyInfo phase = typeof(WeaponController).GetProperty("Phase", F);
            FieldInfo pt = typeof(WeaponController).GetField("phaseTime", F), bl = typeof(WeaponController).GetField("blendLeft", F), sk = typeof(WeaponController).GetField("stagK", F);
            const int n = 8, cw = 480, chh = 420;
            float[] taus = { 0f, 0.32f, 0.4f, 0.46f, 0.8f, 1.1f, 1.26f, 1.36f };
            Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation; float f0 = cam.fieldOfView;
            Texture2D sheet = new Texture2D(cw * 4, chh * 2, TextureFormat.RGB24, false);
            RenderTexture rt = new RenderTexture(cw, chh, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            RenderTexture prevT = cam.targetTexture, prevA = RenderTexture.active;
            try
            {
                Vector3 d = pt0 - pa0; d.y = 0f; d.Normalize();
                a.transform.position = pt0 - d * 1.38f;   // = DuelManager.DbDistance
                a.transform.rotation = Quaternion.LookRotation(d, Vector3.up); t.transform.rotation = Quaternion.LookRotation(-d, Vector3.up);
                t.AddPosture(10000f);   // слом равновесия: клинок повис, корпус осел
                t.weapon.SnapRest(); if (sk != null) sk.SetValue(t.weapon, 1f); Call(t.weapon, "ApplyRestPose");
                w.SnapRest();
                if (!w.TryStartSwing(AttackType.Deathblow)) return;
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

        /// <summary>Один кадр бойца как в игре: Update драйвера → Animator → LateUpdate драйвера → полы плаща.</summary>
        static void Tick(FighterController f, HeroRigDriver d, float animDt, float dt)
        {
            HeroRigDriver.DtOverride = dt;
            Call(d, "Update");
            if (d.animator != null) d.animator.Update(animDt);
            Call(d, "LateUpdate");
            CoatSpring cs = f.GetComponentInChildren<CoatSpring>(true);
            if (cs != null) Call(cs, "LateUpdate");
        }

        /// <summary>Раскадровка походки: боец идёт dirLocal со скоростью speed (как PoseProbe.WalkSim), 8 кадров через 0,1 с после разгона.</summary>
        static void WalkStrip(Camera cam, string dir, string name, FighterController f, Vector3 dirLocal, float speed, StringBuilder log)
        {
            HeroRigDriver d = f.GetComponentInChildren<HeroRigDriver>(true);
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
            try
            {
                Vector3 wdir = ft.TransformDirection(dirLocal);
                // камера стоит на месте (видно, едет ли стопа по земле), смотрит на середину пройденного пути
                Vector3 midPath = start + wdir * speed * dt * (warm + n * every * 0.5f);
                Vector3 camPos = midPath + new Vector3(0f, 1.0f, -3.6f), look = midPath + Vector3.up * 0.75f;
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
