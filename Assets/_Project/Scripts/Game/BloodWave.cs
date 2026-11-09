using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Кровавая волна (2026-11-09, по просьбе владельца): накопление как у ступеней, только красное (кнопка «ВОЛНА»),
    /// на отпускании — взмах, и по площадке к врагу летит красный полумесяц. Листы — владельца из ChatGPT
    /// (tools/fx/prep_blood_wave.py): выпуск 24 кадра, полёт 32 кадра по кругу, попадание 24 кадра; все с наплывом кадров.
    ///
    /// Урон по ступени (I…IV): 13 / 18 / 24 / 32, по равновесию 25 / 35 / 50 / 70. Блок гасит две трети урона.
    /// Рывок или прыжок в миг встречи — волна проходит насквозь. Летит ~11 м/с, гаснет за краем двора.
    /// </summary>
    public class BloodWave : MonoBehaviour
    {
        public static readonly float[] Dmg = { 13f, 18f, 24f, 32f };
        public static readonly float[] Posture = { 25f, 35f, 50f, 70f };
        public static readonly Color Red = new Color(1f, 0.16f, 0.1f);
        const float Speed = 11f, MaxDist = 11f, HitRadius = 0.7f;

        FighterController owner, target;
        int level;
        Vector3 dir, pos;
        float travelled, born, fadeAt = -1f, glowAt;
        bool passed, flip;
        SheetFx fly; Light lt;

        public static void Launch(FighterController a, int level)
        {
            if (a == null) return;
            level = Mathf.Clamp(level, 0, 3);
            FighterController t = a.opponent;
            Vector3 d = t != null ? t.transform.position - a.transform.position : a.transform.forward;
            d.y = 0f; d = d.sqrMagnitude > 1e-4f ? d.normalized : a.transform.forward;
            GameObject g = new GameObject("BloodWave");
            BloodWave w = g.AddComponent<BloodWave>();
            w.owner = a; w.target = t; w.level = level; w.dir = d; w.born = Time.time;
            w.pos = a.transform.position + d * 0.8f;
            Camera cam = Camera.main;
            w.flip = cam != null && Vector3.Dot(d, cam.transform.right) < 0f;   // летит влево по экрану — лист зеркально
            float hgt = 1.8f + 0.3f * level;   // высота полумесяца, м
            float cell = hgt / 0.85f;            // полумесяц занимает ~85% высоты кадра
            w.fly = SheetFx.Spawn("bwave_fly", w.pos + Vector3.up * (hgt * 0.5f + 0.05f), 8, 4, 0, 32, 26f, true, cell);
            if (w.fly != null) { w.fly.flipX = w.flip; w.fly.tint = new Color(1f, 1f, 1f, 0f); }
            // выпуск у клинка: дуга режет воздух, полумесяц отрывается
            SheetFx l = SheetFx.Spawn("bwave_launch", a.transform.position + d * 0.9f + Vector3.up * 1.15f, 8, 4, 0, 24, 34f, false, 2.4f + 0.3f * level);
            if (l != null) l.flipX = w.flip;
            GameObject lg = new GameObject("BloodWaveLight");
            lg.transform.SetParent(g.transform, false);
            w.lt = lg.AddComponent<Light>(); w.lt.type = LightType.Point; w.lt.color = Red; w.lt.range = 3.5f + 0.5f * level; w.lt.intensity = 0f; w.lt.shadows = LightShadows.None;
            if (Juice.Instance != null) Juice.Instance.WaveLaunch(a.transform.position + d * 0.9f + Vector3.up * 1.1f);
            if (CameraOrbit.Instance != null) { CameraOrbit.Instance.Punch(1.5f + 0.8f * level); CameraOrbit.Instance.Shake(0.02f + 0.015f * level); }
            GroundFlash.Spawn("FX/glow_pool", a.transform.position + d * 0.9f, 1.2f, 2.6f + 0.5f * level, Red, 0.45f, 0f);
            Haptics.Pulse(40 + 15 * level);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float age = Time.time - born;
            DuelManager dm = DuelManager.Instance;
            if (dm == null || dm.State != DuelState.Fighting) { if (fadeAt < 0f) fadeAt = Time.time; }
            if (fadeAt < 0f)
            {
                Vector3 prev = pos;
                float step = Speed * dt;
                pos += dir * step; travelled += step;
                if (travelled > MaxDist || Mathf.Abs(pos.x) > 6.5f || Mathf.Abs(pos.z) > 5.5f) fadeAt = Time.time;
                else if (!passed && target != null && !target.Dead) CheckHit(prev);
                // по камням под волной — красный отсвет
                if (Time.time >= glowAt) { glowAt = Time.time + 0.05f; GroundFlash.Spawn("FX/glow_pool", new Vector3(pos.x, 0.02f, pos.z), 0.8f, 1.6f + 0.3f * level, Red, 0.35f, 0f); }
            }
            float k = Mathf.Clamp01(age / 0.12f);                         // проступает вслед за выпуском
            float fade = fadeAt < 0f ? 1f : 1f - Mathf.Clamp01((Time.time - fadeAt) / 0.22f);
            if (fly != null)
            {
                float hgt = fly.width * 0.85f;
                fly.transform.position = pos + Vector3.up * (hgt * 0.5f + 0.05f);
                fly.tint = new Color(1f, 1f, 1f, 0.95f * k * fade);
                fly.width *= fadeAt < 0f ? 1f : 1f + 0.8f * dt;              // гаснет — чуть расходится
            }
            if (lt != null) { lt.transform.position = pos + Vector3.up * 1.0f; lt.intensity = (1.6f + 0.4f * level) * k * fade; }
            if (fadeAt >= 0f && Time.time - fadeAt > 0.25f) Destroy(gameObject);
        }

        void CheckHit(Vector3 prev)
        {
            Vector3 tp = target.transform.position; tp.y = 0f;
            Vector3 a = new Vector3(prev.x, 0f, prev.z), b = new Vector3(pos.x, 0f, pos.z);
            Vector3 ab = b - a;
            float u = Mathf.Clamp01(Vector3.Dot(tp - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            if ((a + ab * u - tp).magnitude > HitRadius) return;
            // рывок или прыжок в миг встречи — волна проходит насквозь
            if (target.Dashing || !target.Grounded)
            {
                passed = true;
                DuelManager.Popup p = new DuelManager.Popup();
                p.world = target.transform.position + Vector3.up * 2.1f; p.text = "МИМО"; p.color = new Color(0.9f, 0.9f, 0.9f);
                p.born = Time.unscaledTime; p.size = 20f;
                DuelManager.Instance.Popups.Add(p);
                return;
            }
            bool blocked = target.Blocking;
            float dmg = Dmg[level] * (blocked ? 0.35f : 1f), post = Posture[level] * (blocked ? 1.3f : 1f);
            Vector3 at = target.transform.position + Vector3.up * 1.1f;
            DuelManager.Instance.ForceHit(target, owner, dmg, post, at);
            if (!target.Dead) target.Knockback(dir, blocked ? 1.2f : 1.8f + 0.4f * level);
            SheetFx h = SheetFx.Spawn("bwave_hit", at, 8, 4, 0, 24, 32f, false, 2.4f + 0.35f * level);
            if (h != null) h.flipX = flip;
            if (Juice.Instance != null) Juice.Instance.WaveHit(at, blocked);
            if (CameraOrbit.Instance != null) { CameraOrbit.Instance.Shake(0.05f + 0.025f * level); CameraOrbit.Instance.Punch(2f + level); }
            GroundFlash.Spawn("FX/shock_ring", new Vector3(at.x, 0.02f, at.z), 0.8f, 2.6f + 0.6f * level, Red, 0.45f, 0f);
            PostFX.Kick(0.006f + 0.002f * level);
            Haptics.Pulse(60 + 20 * level);
            fadeAt = Time.time;   // волна разбилась о врага
        }
    }
}
