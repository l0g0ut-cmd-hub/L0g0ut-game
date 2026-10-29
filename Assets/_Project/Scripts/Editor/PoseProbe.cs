#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Диагностика в CI: та же цепочка, что в игре (клип Idle из FBX, как его импортировал Unity → HeroRigDriver.LateUpdate),
    /// без телефона. Пишет в лог положения суставов в системе бойца (x вправо, y вверх, z вперёд):
    /// поза покоя, поза клипа, итог драйвера в стойке и в кадрах удара сверху. Сравнивается с симулятором в Blender.
    /// </summary>
    public static class PoseProbe
    {
        static readonly string[] Bones = { "Hips", "Spine", "Chest", "Neck", "Head", "Shoulder.L", "UpperArm.L", "Forearm.L", "Hand.L",
            "Shoulder.R", "UpperArm.R", "Forearm.R", "Hand.R", "UpperLeg.L", "LowerLeg.L", "Foot.L", "UpperLeg.R", "LowerLeg.R", "Foot.R" };
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        static readonly StringBuilder all = new StringBuilder();

        public static void Run(string scenePath)
        {
            try
            {
                all.Length = 0;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                foreach (FighterController f in UnityEngine.Object.FindObjectsByType<FighterController>(FindObjectsSortMode.None))
                    Probe(f);
                SceneShots.Capture(all);   // кадры сцены (build/shots) — оценка картинки без телефона
            }
            catch (Exception e) { all.Append("PROBE|error|" + e + "\n"); }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }   // изменения сцены не сохраняются
            try
            {
                string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../build/probe.txt"));
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path, all.ToString());
                Debug.Log("PROBE: записано " + path + " (" + all.Length + " символов)");
            }
            catch (Exception e) { Debug.Log("PROBE: не записано: " + e.Message); }
        }

        static void Call(object o, string m) { MethodInfo mi = o.GetType().GetMethod(m, F); if (mi != null) mi.Invoke(o, null); }

        static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        static void Dump(FighterController f, Transform model, string stage, StringBuilder sb)
        {
            Transform ft = f.transform;
            foreach (string b in Bones)
            {
                Transform t = Find(model, b);
                if (t == null) { sb.Append("PROBE|" + f.name + "|" + stage + "|" + b + "|missing\n"); continue; }
                Vector3 p = ft.InverseTransformPoint(t.position);
                Vector3 fw = ft.InverseTransformDirection(t.forward), up = ft.InverseTransformDirection(t.up);
                sb.AppendFormat("PROBE|{0}|{1}|{2}|{3:F3}|{4:F3}|{5:F3}|fw {6:F2} {7:F2} {8:F2}|up {9:F2} {10:F2} {11:F2}\n", f.name, stage, b, p.x, p.y, p.z, fw.x, fw.y, fw.z, up.x, up.y, up.z);
            }
            Transform mt = model;
            sb.AppendFormat("PROBE|{0}|{1}|model|{2:F3}|{3:F3}|{4:F3}|rot {5}\n", f.name, stage, mt.localPosition.x, mt.localPosition.y, mt.localPosition.z, mt.localEulerAngles);
        }

        static void Probe(FighterController f)
        {
            HeroRigDriver d = f.GetComponentInChildren<HeroRigDriver>(true);
            if (d == null) { Debug.Log("PROBE|" + f.name + "|no driver"); return; }
            Animator anim = d.animator;
            WeaponController w = f.weapon;
            StringBuilder sb = new StringBuilder();
            Call(f, "Awake");
            if (w != null) Call(w, "Awake");
            Transform model = d.transform;
            Dump(f, model, "rest", sb);
            Call(d, "Start");
            AnimationClip idle = null;
            if (anim != null && anim.runtimeAnimatorController != null)
                foreach (AnimationClip c in anim.runtimeAnimatorController.animationClips) if (c.name.Contains("Idle")) { idle = c; break; }
            sb.Append("PROBE|" + f.name + "|clip|" + (idle != null ? idle.name + " len " + idle.length : "none") + "\n");
            if (idle != null) idle.SampleAnimation(anim.gameObject, 0.5f);
            Dump(f, model, "anim", sb);
            for (int i = 0; i < 3; i++)
            {
                if (idle != null) idle.SampleAnimation(anim.gameObject, 0.5f);
                Call(d, "LateUpdate");
            }
            Dump(f, model, "idle", sb);
            // как в игре: позу пишет сам Animator (контроллер, аватар, переходы), а не SampleAnimation
            if (anim != null)
            {
                try
                {
                    anim.Rebind();
                    anim.Update(0f);
                    Dump(f, model, "rt_anim0", sb);
                    for (int i = 0; i < 30; i++) { anim.Update(1f / 30f); if (i == 0) Dump(f, model, "rt_anim1", sb); Call(d, "LateUpdate"); }
                    Dump(f, model, "rt_idle", sb);
                    sb.Append("PROBE|" + f.name + "|rt_info|root " + anim.applyRootMotion + " avatar " + (anim.avatar != null ? anim.avatar.name + " valid " + anim.avatar.isValid + " human " + anim.avatar.isHuman : "null")
                        + " state " + (anim.runtimeAnimatorController != null ? anim.GetCurrentAnimatorStateInfo(0).shortNameHash.ToString() : "-") + " cull " + anim.cullingMode + "\n");
                }
                catch (Exception e) { sb.Append("PROBE|" + f.name + "|rt_error|" + e.Message + "\n"); }
            }
            // кадры удара сверху: фаза и время ставятся напрямую, поза — как в игре
            if (w != null && w.TryStartSwing(AttackType.Overhead))
            {
                PropertyInfo phase = typeof(WeaponController).GetProperty("Phase", F);
                FieldInfo pt = typeof(WeaponController).GetField("phaseTime", F);
                FieldInfo bl = typeof(WeaponController).GetField("blendLeft", F);
                SwingProfile p = w.CurrentProfile;
                float[] taus = { 0.26f, 0.36f, 0.49f, 0.64f };
                foreach (float tau in taus)
                {
                    float rem = tau; SwingPhase ph = SwingPhase.Windup;
                    if (rem >= p.windupTime) { rem -= p.windupTime; ph = SwingPhase.Strike; if (rem >= p.strikeTime) { rem -= p.strikeTime; ph = SwingPhase.FollowThrough; if (rem >= p.followTime) { rem -= p.followTime; ph = SwingPhase.Recovery; } } }
                    phase.GetSetMethod(true).Invoke(w, new object[] { ph });
                    pt.SetValue(w, rem); bl.SetValue(w, 0f);
                    Call(w, "ApplyPose");
                    for (int i = 0; i < 3; i++)
                    {
                        if (idle != null) idle.SampleAnimation(anim.gameObject, 0.5f);
                        Call(d, "LateUpdate");
                    }
                    Dump(f, model, "oh" + tau.ToString("0.00"), sb);
                }
            }
            // шейдеры персонажа: не розовый ли (ошибка компиляции → InternalErrorShader)
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null) sb.Append("PROBE|" + f.name + "|shader|" + r.name + "|" + m.name + "|" + m.shader.name + "|supported " + m.shader.isSupported + "\n");
            // мокап-клипы: поза по фазам (таз и стопы в системе бойца)
            if (anim != null && anim.runtimeAnimatorController != null)
                foreach (AnimationClip c in anim.runtimeAnimatorController.animationClips)
                {
                    if (!c.name.Contains("Loco")) continue;
                    sb.Append("PROBE|" + f.name + "|loco|" + c.name + "|len " + c.length.ToString("F2") + " loop " + c.isLooping + "\n");
                    Transform hp = Find(model, "Hips"), fl = Find(model, "Foot.L"), fr = Find(model, "Foot.R");
                    for (int k = 0; k < 8; k++)
                    {
                        c.SampleAnimation(anim.gameObject, c.length * k / 8f);
                        Vector3 a = model.InverseTransformPoint(hp.position), l = model.InverseTransformPoint(fl.position), r = model.InverseTransformPoint(fr.position);   // в системе модели (сдвиг стойки драйвера не мешает)
                        sb.AppendFormat("PROBE|{0}|loco|{1}|ph {2}|hips {3:F3} {4:F3} {5:F3}|L {6:F3} {7:F3} {8:F3}|R {9:F3} {10:F3} {11:F3}\n", f.name, c.name, k, a.x, a.y, a.z, l.x, l.y, l.z, r.x, r.y, r.z);
                    }
                }
            // ходьба целиком (Animator + драйвер, шаг времени 1/30): проскальзывание стоп в опоре, провал в пол, состояние
            if (anim != null && d.mocapLoco)
            {
                WalkSim(f, d, anim, model, "F", Vector3.forward, 1.2f, sb);
                WalkSim(f, d, anim, model, "B", Vector3.back, 1.02f, sb);
                WalkSim(f, d, anim, model, "R", Vector3.right, 0.72f, sb);
                WalkSim(f, d, anim, model, "L", Vector3.left, 0.72f, sb);
                WalkSim(f, d, anim, model, "FR", new Vector3(0.7071f, 0f, 0.7071f), 0.96f, sb);
                WalkSim(f, d, anim, model, "Rcircle", Vector3.right, 0.72f, sb, -30f);   // кружит вокруг противника: стопы не должны ехать от поворота
            }
            // физическое падение: 3 с симуляции — тело не взрывается, ложится на землю, не улетает
            FieldInfo rf = typeof(HeroRigDriver).GetField("rag", F);
            FighterRagdoll rg = rf != null ? rf.GetValue(d) as FighterRagdoll : null;
            if (rg == null) sb.Append("PROBE|" + f.name + "|rag|none\n");
            else
            {
                SimulationMode sm0 = Physics.simulationMode;
                try
                {
                    Physics.simulationMode = SimulationMode.Script;
                    if (anim != null) anim.enabled = false;
                    Transform ft = f.transform; Transform hp = Find(model, "Hips");
                    rg.Activate(-ft.forward * 1.6f + Vector3.up * 0.4f, -ft.forward * 2.4f + Vector3.up * 0.6f, hp.position + Vector3.up * 0.3f);
                    rg.Dump(sb, f.name, "t0.0", ft);
                    for (int i = 1; i <= 150; i++)
                    {
                        Physics.Simulate(0.02f);
                        if (i == 10 || i == 25 || i == 50 || i == 100 || i == 150) rg.Dump(sb, f.name, "t" + (i * 0.02f).ToString("F1"), ft);
                    }
                    rg.Deactivate();
                    Vector3 hl = ft.InverseTransformPoint(hp.position);
                    sb.AppendFormat("PROBE|{0}|rag|restored hips {1:F3} {2:F3} {3:F3}\n", f.name, hl.x, hl.y, hl.z);
                }
                catch (Exception e) { sb.Append("PROBE|" + f.name + "|rag|error " + e.Message + "\n"); }
                finally { Physics.simulationMode = sm0; if (anim != null) anim.enabled = true; }
            }
            all.Append(sb);
        }

        static void WalkSim(FighterController f, HeroRigDriver d, Animator anim, Transform model, string label, Vector3 dirLocal, float speed, StringBuilder sb, float yawDeg = 0f)
        {
            const float dt = 1f / 30f;
            Transform ft = f.transform; Vector3 start = ft.position; Quaternion startRot = ft.rotation;
            MethodInfo fu = typeof(FighterController).GetMethod("Update", F);
            Transform fl = Find(model, "Foot.L"), fr = Find(model, "Foot.R"), hp = Find(model, "Hips");
            HeroRigDriver.DtOverride = dt;
            if (f.weapon != null) f.weapon.SnapRest();   // после кадров удара — снова стойка
            try
            {
                Vector3 wdir = ft.TransformDirection(dirLocal);
                List<Vector3> L = new List<Vector3>(), R = new List<Vector3>(); List<float> H = new List<float>();
                // точки подошвы (пятка/подушечка/носок) каждой стопы — по отдельности: скольжение опорной точки, а не «нижней из трёх»
                Vector3[] solL = typeof(HeroRigDriver).GetField("solL", F).GetValue(d) as Vector3[], solR = typeof(HeroRigDriver).GetField("solR", F).GetValue(d) as Vector3[];
                List<Vector3[]> SP = new List<Vector3[]>();
                int frames = 90, stopAt = 70;
                string st = "";
                for (int i = 0; i < frames; i++)
                {
                    bool go = i < stopAt;
                    if (go && yawDeg != 0f) { ft.Rotate(0f, yawDeg * dt, 0f, Space.World); wdir = ft.TransformDirection(dirLocal); }   // кружит вокруг противника
                    if (go) f.SetMoveVector(new Vector2(wdir.x, wdir.z));
                    if (fu != null) fu.Invoke(f, null);
                    if (go) ft.position += wdir * (speed * dt);
                    Call(d, "Update");
                    anim.Update(dt);
                    Call(d, "LateUpdate");
                    L.Add(fl.position); R.Add(fr.position); H.Add(hp.position.y - ft.position.y);
                    if (solL != null && solR != null)
                    {
                        Vector3[] pp = new Vector3[solL.Length + solR.Length];
                        for (int k = 0; k < solL.Length; k++) pp[k] = fl.TransformPoint(solL[k]);
                        for (int k = 0; k < solR.Length; k++) pp[solL.Length + k] = fr.TransformPoint(solR[k]);
                        SP.Add(pp);
                    }
                    if (i == stopAt - 1)
                    {
                        AnimatorStateInfo si = anim.GetCurrentAnimatorStateInfo(0);
                        st = (si.IsName("Loco") ? "Loco" : si.IsName("Idle") ? "Idle" : "?") + string.Format(" x {0:F2} z {1:F2} tempo {2:F2}", anim.GetFloat("MoveX"), anim.GetFloat("MoveZ"), anim.GetFloat("LocoSpeed"));
                    }
                }
                float minY = float.MaxValue, minAll = float.MaxValue;
                for (int i = 0; i < frames; i++) minAll = Mathf.Min(minAll, Mathf.Min(L[i].y, R[i].y) - ft.position.y);
                for (int i = 25; i < stopAt; i++) minY = Mathf.Min(minY, Mathf.Min(L[i].y, R[i].y) - ft.position.y);   // опора — по установившемуся ходу
                float slideSum = 0f, slideMax = 0f; int slideN = 0; float hMin = float.MaxValue, hMax = float.MinValue;
                for (int i = 25; i < stopAt; i++)
                {
                    hMin = Mathf.Min(hMin, H[i]); hMax = Mathf.Max(hMax, H[i]);
                    for (int s = 0; s < 2; s++)
                    {
                        Vector3 a = s == 0 ? L[i - 1] : R[i - 1], b = s == 0 ? L[i] : R[i];
                        if (Mathf.Min(a.y, b.y) - ft.position.y > minY + 0.02f) continue;   // стопа в воздухе
                        Vector3 dv = b - a; dv.y = 0f; float v = dv.magnitude / dt;
                        slideSum += v; slideN++; slideMax = Mathf.Max(slideMax, v);
                    }
                }
                // по подошве: точка опоры на земле (пятка/подушечка) — настоящее скольжение, без переката голеностопа
                float sSum = 0f, sMax = 0f, sMin = float.MaxValue; int sN = 0;
                for (int i = 25; i < stopAt && i < SP.Count; i++)
                    for (int k = 0; k < SP[i].Length; k++)
                    {
                        Vector3 a = SP[i - 1][k], b = SP[i][k];
                        sMin = Mathf.Min(sMin, b.y - ft.position.y);
                        if (Mathf.Max(a.y, b.y) - ft.position.y > 0.012f) continue;   // точка на земле в обоих кадрах
                        Vector3 dv = b - a; dv.y = 0f; float v = dv.magnitude / dt;
                        sSum += v; sN++; sMax = Mathf.Max(sMax, v);
                    }
                sb.AppendFormat("PROBE|{0}|sole_{1}|slide mean {2:F2} max {3:F2} m/s (n {4})|soleMinY {5:F3}\n", f.name, label, sN > 0 ? sSum / sN : 0f, sMax, sN, sMin);
                Vector3 hl = ft.InverseTransformPoint(hp.position);
                sb.AppendFormat("PROBE|{0}|walk_{1}|v {2:F2}|slide mean {3:F2} max {4:F2} m/s (n {5})|footMinY walk {6:F3} all {7:F3}|hips {8:F3}..{9:F3} end {10:F2} {11:F2} {12:F2}|{13}\n",
                    f.name, label, speed, slideN > 0 ? slideSum / slideN : 0f, slideMax, slideN, minY, minAll, hMin, hMax, hl.x, hl.y, hl.z, st);
            }
            catch (Exception e) { sb.Append("PROBE|" + f.name + "|walk_" + label + "|error " + e.Message + "\n"); }
            finally { HeroRigDriver.DtOverride = -1f; ft.position = start; ft.rotation = startRot; }
        }
    }
}
#endif
