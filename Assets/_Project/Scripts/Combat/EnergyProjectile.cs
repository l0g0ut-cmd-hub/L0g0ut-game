using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Дальняя атака — энергетический шар. Летит по прямой; каждый кадр делает SphereCast между прошлой
    /// и новой позицией (поэтому не «пролетает сквозь» тонкие зоны), плюс OverlapSphere на случай появления
    /// внутри тела. Первое попадание в чужую зону тела гасит шар и сообщает CombatResolver.
    /// </summary>
    public class EnergyProjectile : MonoBehaviour
    {
        public FighterController owner;
        public Vector3 velocity;
        public float radius = 0.14f;
        public float lifeSeconds = 2.5f;

        static readonly List<EnergyProjectile> active = new List<EnergyProjectile>();
        static readonly RaycastHit[] castBuf = new RaycastHit[8];
        static readonly Collider[] overlapBuf = new Collider[8];
        float born;

        public static EnergyProjectile Spawn(FighterController owner, Vector3 position, Vector3 velocity, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "EnergyBall";
            Collider c = go.GetComponent<Collider>();
            if (c != null) Object.Destroy(c);   // попадания считаем сами, Unity-коллайдер не нужен
            EnergyProjectile p = go.AddComponent<EnergyProjectile>();
            p.owner = owner;
            p.velocity = velocity;
            go.transform.position = position;
            go.transform.localScale = Vector3.one * (p.radius * 2f);
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
            return p;
        }

        public static void DestroyAll()
        {
            for (int i = active.Count - 1; i >= 0; i--) if (active[i] != null) Object.Destroy(active[i].gameObject);
            active.Clear();
        }

        void OnEnable() { born = Time.time; active.Add(this); }
        void OnDisable() { active.Remove(this); }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 pos = transform.position;
            Vector3 step = velocity * dt;
            float dist = step.magnitude;

            Physics.SyncTransforms();

            // 1) уже внутри чужой зоны (шар появился в теле / противник шагнул навстречу)
            int oc = Physics.OverlapSphereNonAlloc(pos, radius, overlapBuf, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < oc; i++)
            {
                BodyZone z = overlapBuf[i].GetComponentInParent<BodyZone>();
                if (z != null && z.owner != owner) { Hit(z, overlapBuf[i].ClosestPoint(pos)); return; }
            }

            // 2) путь за кадр
            if (dist > 1e-5f)
            {
                int n = Physics.SphereCastNonAlloc(pos, radius, step / dist, castBuf, dist, ~0, QueryTriggerInteraction.Ignore);
                BodyZone best = null;
                float bestDist = float.MaxValue;
                Vector3 bestPoint = pos;
                for (int i = 0; i < n; i++)
                {
                    BodyZone z = castBuf[i].collider.GetComponentInParent<BodyZone>();
                    if (z == null || z.owner == owner) continue;
                    if (castBuf[i].distance < bestDist) { bestDist = castBuf[i].distance; best = z; bestPoint = castBuf[i].point; }
                }
                if (best != null) { Hit(best, bestPoint); return; }
            }

            transform.position = pos + step;

            if (Time.time - born > lifeSeconds)
            {
                if (CombatResolver.Instance != null) CombatResolver.Instance.ReportProjectileMiss(owner);
                Destroy(gameObject);
            }
        }

        void Hit(BodyZone zone, Vector3 point)
        {
            if (CombatResolver.Instance != null) CombatResolver.Instance.ReportProjectileHit(owner, zone, point, velocity);
            Destroy(gameObject);
        }
    }
}
