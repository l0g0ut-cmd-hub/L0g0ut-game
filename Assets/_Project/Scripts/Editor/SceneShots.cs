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
                    if (f.weapon != null) f.weapon.ResetState();
                    if (d == null || d.animator == null) continue;
                    d.animator.Rebind(); d.animator.Update(0f);
                }
                for (int i = 0; i < 40; i++)
                    foreach (FighterController f in fs)
                    {
                        HeroRigDriver d = f.GetComponentInChildren<HeroRigDriver>(true);
                        if (d == null || d.animator == null) continue;
                        if (f.weapon != null) Call(f.weapon, "Update");
                        Call(d, "Update");
                        d.animator.Update(dt);
                        Call(d, "LateUpdate");
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
                Shot(cam, dir, "4_arena", new Vector3(0f, 3.6f, -6.4f), new Vector3(0f, 1.2f, 2.5f), 50f, log);

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
