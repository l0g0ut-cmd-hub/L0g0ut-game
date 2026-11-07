using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Жаровня, которую сбили (боец отброшен в неё тяжёлым ударом): тренога валится в сторону удара с отскоком,
    /// огонь в чаше гаснет, угли рассыпаются по камням и горят ещё ~14 с — кто наступит, получит ожог.
    /// Свет жаровни ложится к углям на землю. В начале раунда всё возвращается на место (ResetAll из Juice.ClearBattleDecals).
    /// Идея — «окружение в бою» как в Onimusha: Way of the Sword (врага вбивают в жаровню).
    /// </summary>
    public class BrazierTopple : MonoBehaviour
    {
        static readonly List<BrazierTopple> all = new List<BrazierTopple>();

        Vector3 restPos; Quaternion restRot; bool hasRest;
        bool down; float downAt; Vector3 dir, axis, pivot;
        readonly List<FxSprite> coals = new List<FxSprite>();
        readonly List<Vector3> coalPos = new List<Vector3>();
        readonly Dictionary<FighterController, float> burnAt = new Dictionary<FighterController, float>();
        Light fireLight; FlickerLight flicker; Vector3 lightLocal; float lightBase = -1f;
        FxSprite bowlFire;
        const float FallTime = 0.42f, BurnTime = 14f;

        public bool Down { get { return down; } }

        void Awake() { all.Add(this); }
        void OnDestroy() { all.Remove(this); }

        void Remember()
        {
            if (hasRest) return;
            restPos = transform.position; restRot = transform.rotation; hasRest = true;
            Transform lo = transform.Find("FireLight");
            if (lo != null)
            {
                fireLight = lo.GetComponent<Light>(); flicker = lo.GetComponent<FlickerLight>(); lightLocal = lo.localPosition;
                lightBase = flicker != null ? flicker.baseIntensity : (fireLight != null ? fireLight.intensity : 1f);
            }
        }

        /// <summary>Сбить жаровню толчком push (скорость отброшенного бойца).</summary>
        public void Topple(Vector3 push)
        {
            if (down) return;
            Remember();
            push.y = 0f;
            dir = push.sqrMagnitude > 1e-4f ? push.normalized : Vector3.forward;
            axis = Vector3.Cross(Vector3.up, dir);
            pivot = restPos + dir * 0.34f;   // валится через ногу треноги со стороны удара
            down = true; downAt = Time.time;
            burnAt.Clear();
            Wobble wb = GetComponent<Wobble>(); if (wb != null) wb.enabled = false;
            ArenaObstacle ob = GetComponent<ArenaObstacle>(); if (ob != null) ob.enabled = false;   // лежащая тренога не держит бойцов невидимым столбом
            // огонь в чаше гаснет (нарисованный огонь, ореол, частицы пламени)
            Transform anchor = transform.Find("FireSprite");
            if (anchor != null)
                foreach (FxSprite f in FindObjectsByType<FxSprite>(FindObjectsSortMode.None))
                    if (f != null && f.follow == anchor) { bowlFire = f; f.gameObject.SetActive(false); }
            SetChild("Halo", false); SetChild("Fire", false); SetChild("FireCore", false);
            foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>())
                if (ps.name == "Embers") { EmitBurst(ps, 40, 1.2f); ParticleSystem.EmissionModule em = ps.emission; em.enabled = false; }
        }

        // свет мерцает своим FlickerLight от baseIntensity — менять надо её, иначе мерцание перебьёт
        void SetLight(float v) { if (flicker != null) flicker.baseIntensity = v; else if (fireLight != null) fireLight.intensity = v; }

        void SetChild(string n, bool on) { Transform c = transform.Find(n); if (c != null) c.gameObject.SetActive(on); }

        void EmitBurst(ParticleSystem ps, int n, float spread)
        {
            ParticleSystem.EmitParams e = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                e.position = ps.transform.position + Random.insideUnitSphere * 0.15f;
                e.velocity = dir * Random.Range(1f, 3f) * spread + new Vector3(Random.Range(-1f, 1f), Random.Range(1.2f, 3.4f), Random.Range(-1f, 1f));
                e.startLifetime = Random.Range(0.7f, 1.6f);
                ps.Emit(e, 1);
            }
        }

        void Land()
        {
            // угли рассыпаются полосой по направлению падения
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = restPos + dir * (0.85f + i * 0.28f) + axis * Random.Range(-0.22f, 0.22f);
                p.y = 0.02f;
                FxSprite f = FxSprite.Spawn("brazier_fire", p + Vector3.up * (0.2f + 0.05f * (i % 2)), 0.42f - i * 0.05f, 4, 2, 8, 12f + i, true);
                if (f != null) { f.tint = new Color(1f, 0.85f, 0.7f, 0.85f); coals.Add(f); }
                coalPos.Add(p);
            }
            if (Juice.Instance != null)
            {
                Juice.Instance.Dust(restPos + dir * 1.1f, dir, 8);
                Juice.Instance.ObstacleThud();
            }
            if (CameraOrbit.Instance != null) CameraOrbit.Instance.Shake(0.05f);
            if (fireLight != null)
            {
                fireLight.transform.position = restPos + dir * 1.2f + Vector3.up * 0.35f;   // свет — от углей на земле
                SetLight(lightBase * 0.8f);
            }
        }

        void Update()
        {
            if (!down) return;
            float t = Time.time - downAt;
            // падение с ускорением и два затухающих отскока
            float ang;
            if (t < FallTime) { float u = t / FallTime; ang = 86f * u * u; }
            else { float b = t - FallTime; ang = 86f - 9f * Mathf.Exp(-b * 7f) * Mathf.Abs(Mathf.Sin(b * 14f)); }
            Quaternion r = Quaternion.AngleAxis(ang, axis);
            transform.position = pivot + r * (restPos - pivot);
            transform.rotation = r * restRot;
            if (t >= FallTime && coalPos.Count == 0) Land();
            if (fireLight != null && coalPos.Count > 0) fireLight.transform.position = restPos + dir * 1.2f + Vector3.up * 0.35f;   // держим у углей (лампа — дочерняя у падающей треноги)

            if (coalPos.Count == 0) return;
            float burn = t - FallTime;
            // угли догорают: последние 2 с пламя тает
            float fade = Mathf.Clamp01((BurnTime - burn) / 2f);
            for (int i = 0; i < coals.Count; i++) if (coals[i] != null) coals[i].tint = new Color(1f, 0.85f, 0.7f, 0.85f * fade);
            SetLight(lightBase * 0.8f * fade);
            if (fade <= 0f) { KillCoals(); return; }
            // ожог тому, кто стоит в углях (не чаще раза в секунду)
            DuelManager dm = DuelManager.Instance;
            if (dm == null || dm.State != DuelState.Fighting) return;
            BurnIfIn(dm.player); BurnIfIn(dm.enemy);
        }

        void BurnIfIn(FighterController f)
        {
            if (f == null || f.Dead) return;
            Vector3 fp = f.transform.position;
            for (int i = 0; i < coalPos.Count; i++)
            {
                Vector3 d = fp - coalPos[i]; d.y = 0f;
                if (d.sqrMagnitude > 0.42f * 0.42f) continue;
                float at;
                if (burnAt.TryGetValue(f, out at) && Time.time < at) return;
                burnAt[f] = Time.time + 1f;
                DuelManager.Instance.Burn(f, coalPos[i]);
                return;
            }
        }

        void KillCoals()
        {
            for (int i = 0; i < coals.Count; i++) if (coals[i] != null) Destroy(coals[i].gameObject);
            coals.Clear();
        }

        void Restore()
        {
            if (!hasRest) return;
            KillCoals(); coalPos.Clear(); burnAt.Clear();
            down = false;
            transform.position = restPos; transform.rotation = restRot;
            Wobble wb = GetComponent<Wobble>(); if (wb != null) wb.enabled = true;
            ArenaObstacle ob = GetComponent<ArenaObstacle>(); if (ob != null) ob.enabled = true;
            if (bowlFire != null) bowlFire.gameObject.SetActive(true);
            SetChild("Halo", true); SetChild("Fire", true); SetChild("FireCore", true);
            foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>(true))
                if (ps.name == "Embers") { ParticleSystem.EmissionModule em = ps.emission; em.enabled = true; }
            if (fireLight != null) { fireLight.transform.localPosition = lightLocal; SetLight(lightBase); }
        }

        /// <summary>Новый раунд: все сбитые жаровни снова стоят и горят.</summary>
        public static void ResetAll()
        {
            for (int i = 0; i < all.Count; i++) if (all[i] != null) all[i].Restore();
        }
    }
}
