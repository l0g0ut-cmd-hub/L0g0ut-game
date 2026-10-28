using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Физическое падение побеждённого бойца (ragdoll) на костях модели: 11 тел (таз, корпус, голова, плечи, предплечья, бёдра, голени),
    /// суставы CharacterJoint с анатомическими пределами. Пока боец жив — тела кинематические, коллайдеры выключены (ничего не стоят).
    /// Нулевая поза суставов задаётся «средней» позой (колени и локти согнуты, бедро чуть вперёд), а пределы симметричны вокруг неё —
    /// поэтому не зависят от знаковых соглашений осей и не дают переразгибания.
    /// Модель на время падения отцепляется от бойца, чтобы отброс и разворот корня не таскали тело.
    /// </summary>
    public class FighterRagdoll
    {
        public const int Layer = 9;   // свой слой: клинок и снаряды его не видят (BladeTrajectorySampler/EnergyProjectile)

        class Part { public Transform t; public Rigidbody rb; public Collider[] cols; public Quaternion bindRot; public Vector3 bindPos; }
        readonly List<Part> parts = new List<Part>();
        readonly Transform model, modelParent;
        readonly Vector3 modelLocalPos; readonly Quaternion modelLocalRot;
        public bool Active { get; private set; }
        public bool Ready { get { return parts.Count >= 11; } }

        static GameObject ground;

        public FighterRagdoll(HeroRigDriver d, Transform fighter)
        {
            model = d.transform; modelParent = model.parent; modelLocalPos = model.localPosition; modelLocalRot = model.localRotation;
            Transform neck = Find(d.chest, "Neck");
            if (d.hips == null || d.spine == null || d.chest == null || d.head == null || d.upperLegL == null || d.lowerLegL == null || d.footL == null ||
                d.upperLegR == null || d.lowerLegR == null || d.footR == null || d.upperArmL == null || d.forearmL == null || d.handL == null ||
                d.upperArmR == null || d.forearmR == null || d.handR == null) return;
            Vector3 fwd = fighter.forward, up = Vector3.up;

            // запомнить позу (вызывается из Start — поза покоя)
            Transform[] all = { d.hips, d.spine, d.chest, d.head, d.upperArmL, d.forearmL, d.upperArmR, d.forearmR, d.upperLegL, d.lowerLegL, d.upperLegR, d.lowerLegR };
            Dictionary<Transform, Quaternion> save = new Dictionary<Transform, Quaternion>();
            foreach (Transform t in all) save[t] = t.localRotation;

            // «средняя» поза: колени согнуты на 50°, бедро вперёд на 35°, локти на 60° — нули суставов
            Bend(d.upperLegL, d.lowerLegL, fwd, 35f, true); Bend(d.lowerLegL, d.footL, -fwd, 50f, false);
            Bend(d.upperLegR, d.lowerLegR, fwd, 35f, true); Bend(d.lowerLegR, d.footR, -fwd, 50f, false);
            Bend(d.forearmL, d.handL, fwd, 60f, false); Bend(d.forearmR, d.handR, fwd, 60f, false);

            float hipW = Vector3.Distance(d.upperLegL.position, d.upperLegR.position);
            Part hips = Add(d.hips, null, 10f);
            BoxCol(hips, (d.hips.position + d.spine.position) * 0.5f, new Vector3(hipW + 0.14f, Mathf.Max(0.16f, Vector3.Distance(d.hips.position, d.spine.position) + 0.08f), 0.24f), fighter);
            Part spine = Add(d.spine, hips, 16f);
            Vector3 neckP = neck != null ? neck.position : d.head.position;
            Capsule(spine, d.spine.position, neckP, 0.15f);
            Part head = Add(d.head, spine, 5f);
            SphereCol(head, d.head.position + up * 0.09f, 0.12f);
            Part ua = Add(d.upperArmL, spine, 2.5f); Capsule(ua, d.upperArmL.position, d.forearmL.position, 0.055f);
            Part fa = Add(d.forearmL, ua, 2f); Capsule(fa, d.forearmL.position, d.handL.position + (d.handL.position - d.forearmL.position).normalized * 0.08f, 0.045f);
            Part ub = Add(d.upperArmR, spine, 2.5f); Capsule(ub, d.upperArmR.position, d.forearmR.position, 0.055f);
            Part fb = Add(d.forearmR, ub, 2f); Capsule(fb, d.forearmR.position, d.handR.position + (d.handR.position - d.forearmR.position).normalized * 0.08f, 0.045f);
            Part tl = Add(d.upperLegL, hips, 7f); Capsule(tl, d.upperLegL.position, d.lowerLegL.position, 0.08f);
            Part sl = Add(d.lowerLegL, tl, 4.5f); Capsule(sl, d.lowerLegL.position, d.footL.position, 0.06f); FootBox(sl, d.footL, fighter);
            Part tr = Add(d.upperLegR, hips, 7f); Capsule(tr, d.upperLegR.position, d.lowerLegR.position, 0.08f);
            Part sr = Add(d.lowerLegR, tr, 4.5f); Capsule(sr, d.lowerLegR.position, d.footR.position, 0.06f); FootBox(sr, d.footR, fighter);

            Vector3 right = fighter.right;
            // суставы: ось «скручивания» = ось сгиба (поперёк кости), пределы симметричны вокруг средней позы
            Joint(spine, hips, right, 25f, 15f, 20f);
            Joint(head, spine, right, 35f, 30f, 25f);
            Joint(ua, spine, right, 70f, 30f, 50f);
            Joint(ub, spine, right, 70f, 30f, 50f);
            Joint(fa, ua, Hinge(d.upperArmL, d.forearmL, d.handL, right), 55f, 5f, 5f);
            Joint(fb, ub, Hinge(d.upperArmR, d.forearmR, d.handR, right), 55f, 5f, 5f);
            Joint(tl, hips, right, 70f, 20f, 35f);
            Joint(tr, hips, right, 70f, 20f, 35f);
            Joint(sl, tl, Hinge(d.upperLegL, d.lowerLegL, d.footL, right), 48f, 5f, 5f);
            Joint(sr, tr, Hinge(d.upperLegR, d.lowerLegR, d.footR, right), 48f, 5f, 5f);

            foreach (Transform t in all) t.localRotation = save[t];
            foreach (Part p in parts) { p.bindRot = p.t.localRotation; p.bindPos = p.t.localPosition; }
            SetPhysics(false);
        }

        static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        /// <summary>Повернуть кость a так, чтобы направление a→b отклонилось на deg к dir (без знаковых соглашений).</summary>
        static void Bend(Transform a, Transform b, Vector3 dir, float deg, bool hipFlex)
        {
            Vector3 v = (b.position - a.position).normalized;
            Vector3 d = Vector3.ProjectOnPlane(dir, v).normalized;
            if (d.sqrMagnitude < 1e-6f) return;
            Vector3 bent = v * Mathf.Cos(deg * Mathf.Deg2Rad) + d * Mathf.Sin(deg * Mathf.Deg2Rad);
            a.rotation = Quaternion.FromToRotation(v, bent) * a.rotation;
        }

        static Vector3 Hinge(Transform a, Transform b, Transform c, Vector3 fallback)
        {
            Vector3 h = Vector3.Cross(b.position - a.position, c.position - b.position);
            return h.sqrMagnitude > 1e-8f ? h.normalized : fallback;
        }

        Part Add(Transform t, Part parent, float mass)
        {
            Part p = new Part { t = t };
            p.rb = t.gameObject.AddComponent<Rigidbody>();
            p.rb.mass = mass; p.rb.linearDamping = 0.05f; p.rb.angularDamping = 0.6f;
            p.rb.solverIterations = 12; p.rb.solverVelocityIterations = 4;
            p.rb.interpolation = RigidbodyInterpolation.None;
            p.rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            p.cols = new Collider[0];
            parts.Add(p);
            return p;
        }

        static PhysicsMaterial mat;
        static PhysicsMaterial Mat()
        {
            if (mat == null) { mat = new PhysicsMaterial("Ragdoll") { dynamicFriction = 0.8f, staticFriction = 0.9f, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Maximum }; }
            return mat;
        }

        GameObject ColGO(Part p, Vector3 center, Quaternion rot)
        {
            GameObject g = new GameObject("rg_col");
            g.layer = Layer;
            g.transform.SetParent(p.t, false);
            g.transform.position = center; g.transform.rotation = rot;
            Vector3 ls = p.t.lossyScale;
            g.transform.localScale = new Vector3(1f / Mathf.Max(1e-4f, ls.x), 1f / Mathf.Max(1e-4f, ls.y), 1f / Mathf.Max(1e-4f, ls.z));   // размеры в метрах мира
            return g;
        }

        void AddCol(Part p, Collider c) { c.sharedMaterial = Mat(); List<Collider> l = new List<Collider>(p.cols); l.Add(c); p.cols = l.ToArray(); }

        void Capsule(Part p, Vector3 a, Vector3 b, float r)
        {
            Vector3 v = b - a; float len = v.magnitude;
            if (len < 1e-3f) return;
            GameObject g = ColGO(p, (a + b) * 0.5f, Quaternion.LookRotation(v / len, Mathf.Abs(Vector3.Dot(v / len, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up));
            CapsuleCollider c = g.AddComponent<CapsuleCollider>();
            c.direction = 2; c.radius = r; c.height = len + 2f * r * 0.6f;
            AddCol(p, c);
        }

        void SphereCol(Part p, Vector3 c0, float r)
        {
            GameObject g = ColGO(p, c0, Quaternion.identity);
            SphereCollider c = g.AddComponent<SphereCollider>(); c.radius = r;
            AddCol(p, c);
        }

        void BoxCol(Part p, Vector3 center, Vector3 size, Transform fighter)
        {
            GameObject g = ColGO(p, center, fighter.rotation);
            BoxCollider c = g.AddComponent<BoxCollider>(); c.size = size;
            AddCol(p, c);
        }

        void FootBox(Part shin, Transform foot, Transform fighter)
        {
            // стопа без своего тела: коробка на кости стопы, но принадлежит телу голени
            Vector3 f = Vector3.ProjectOnPlane(fighter.forward, Vector3.up).normalized;
            GameObject g = ColGO(shin, foot.position + Vector3.down * 0.07f + f * 0.05f, Quaternion.LookRotation(f, Vector3.up));
            g.transform.SetParent(foot, true);
            BoxCollider c = g.AddComponent<BoxCollider>(); c.size = new Vector3(0.10f, 0.07f, 0.24f);
            AddCol(shin, c);
        }

        static void Joint(Part child, Part parent, Vector3 twistAxisWorld, float twist, float swing1, float swing2)
        {
            CharacterJoint j = child.t.gameObject.AddComponent<CharacterJoint>();
            j.connectedBody = parent.rb;
            j.autoConfigureConnectedAnchor = true;
            j.anchor = Vector3.zero;
            Vector3 ax = child.t.InverseTransformDirection(twistAxisWorld).normalized;
            Vector3 sw = Vector3.ProjectOnPlane(child.t.InverseTransformDirection(Vector3.up), ax);
            if (sw.sqrMagnitude < 1e-4f) sw = Vector3.ProjectOnPlane(child.t.InverseTransformDirection(Vector3.forward), ax);
            j.axis = ax; j.swingAxis = sw.normalized;
            j.lowTwistLimit = new SoftJointLimit { limit = -twist };
            j.highTwistLimit = new SoftJointLimit { limit = twist };
            j.swing1Limit = new SoftJointLimit { limit = swing1 };
            j.swing2Limit = new SoftJointLimit { limit = swing2 };
            j.enableProjection = true; j.projectionDistance = 0.04f; j.projectionAngle = 8f;
            j.enablePreprocessing = false;
        }

        void SetPhysics(bool on)
        {
            foreach (Part p in parts)
            {
                p.rb.isKinematic = !on;
                p.rb.detectCollisions = on;
                p.rb.interpolation = on ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
                foreach (Collider c in p.cols) c.enabled = on;
            }
        }

        /// <summary>Пол для тел: статический коллайдер на слое тел (у арены своих коллайдеров нет — бойцы ходят без физики).</summary>
        public static void EnsureGround()
        {
            if (ground != null) return;
            ground = new GameObject("RagdollGround");
            ground.layer = Layer;
            BoxCollider b = ground.AddComponent<BoxCollider>();
            b.size = new Vector3(60f, 1f, 60f); b.center = new Vector3(0f, -0.5f, 0f);
            b.sharedMaterial = Mat();
            for (int i = 0; i < ArenaObstacle.All.Count; i++)
            {
                ArenaObstacle o = ArenaObstacle.All[i];
                if (o == null) continue;
                GameObject g = new GameObject("RagdollObstacle"); g.layer = Layer;
                g.transform.SetParent(ground.transform, false);
                g.transform.position = new Vector3(o.transform.position.x, 0.6f, o.transform.position.z);
                CapsuleCollider c = g.AddComponent<CapsuleCollider>(); c.radius = o.radius; c.height = 1.2f; c.direction = 1; c.sharedMaterial = Mat();
            }
            for (int a = 0; a < 32; a++) if (a != Layer) Physics.IgnoreLayerCollision(Layer, a, true);
        }

        /// <summary>Упасть: тело отцепляется от бойца, все кости получают скорость бойца, удар толкает задетую часть.</summary>
        public void Activate(Vector3 bodyVel, Vector3 hitImpulse, Vector3 hitPoint)
        {
            if (!Ready || Active) return;
            EnsureGround();
            Active = true;
            model.SetParent(null, true);
            SetPhysics(true);
            Part best = parts[0]; float bd = float.MaxValue;
            foreach (Part p in parts)
            {
                p.rb.linearVelocity = bodyVel; p.rb.angularVelocity = Vector3.zero;
                float dd = (p.rb.worldCenterOfMass - hitPoint).sqrMagnitude;
                if (dd < bd) { bd = dd; best = p; }
            }
            best.rb.AddForceAtPosition(hitImpulse, hitPoint, ForceMode.VelocityChange);
            parts[1].rb.AddForce(hitImpulse * 0.35f, ForceMode.VelocityChange);   // корпус уводит за ударом
        }

        /// <summary>Новый раунд: тела снова кинематические, кости в позе покоя, модель на своём месте у бойца.</summary>
        public void Deactivate()
        {
            if (!Active) return;
            Active = false;
            SetPhysics(false);
            foreach (Part p in parts) { p.t.localRotation = p.bindRot; p.t.localPosition = p.bindPos; }
            model.SetParent(modelParent, false);
            model.localPosition = modelLocalPos; model.localRotation = modelLocalRot;
        }

        /// <summary>Для диагностики (PoseProbe): положения тел.</summary>
        public void Dump(System.Text.StringBuilder sb, string who, string stage, Transform fighter)
        {
            float minY = float.MaxValue, maxV = 0f; Vector3 c = Vector3.zero;
            foreach (Part p in parts)
            {
                Vector3 w = p.rb.worldCenterOfMass;
                minY = Mathf.Min(minY, w.y); maxV = Mathf.Max(maxV, p.rb.linearVelocity.magnitude); c += w;
                if (float.IsNaN(w.x)) { sb.Append("PROBE|" + who + "|rag|" + stage + "|NaN\n"); return; }
            }
            c /= Mathf.Max(1, parts.Count);
            Vector3 hp = parts[0].rb.worldCenterOfMass, hd = parts[2].rb.worldCenterOfMass;
            sb.AppendFormat("PROBE|{0}|rag|{1}|minY {2:F3}|hips {3:F2} {4:F2} {5:F2}|head {6:F2} {7:F2} {8:F2}|center {9:F2} {10:F2} {11:F2}|maxV {12:F2}\n",
                who, stage, minY, hp.x, hp.y, hp.z, hd.x, hd.y, hd.z, c.x, c.y, c.z, maxV);
        }
    }
}
