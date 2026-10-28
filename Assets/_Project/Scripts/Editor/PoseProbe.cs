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
            all.Append(sb);
        }
    }
}
#endif
