#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BladePath
{
    /// <summary>
    /// Удары мечом из мокапа Mixamo (tools/mocap/mixamo, Great Sword Pack) → SwordAnimSet под скелет конкретной модели.
    /// Поворот каждой кости источника относительно его Т-позы (X Bot) переносится на нашу кость относительно её «выровненной»
    /// позы покоя: кости корпуса и рук сначала доворачиваются так, чтобы смотреть туда же, куда кости X Bot в Т-позе
    /// (у моделей Meshy руки в покое опущены — без выравнивания весь мокап был бы с руками на 40° ниже).
    /// Руки и меч в игре тогда ведёт клип: меч лежит в правой кисти, левая ставится на рукоять.
    /// </summary>
    public static class SwordAnimBaker
    {
        // наша кость ← кость Mixamo; направление кости — к «ребёнку» (для выравнивания Т-позы)
        static readonly string[] Src = { "Spine", "Spine2", "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand", "RightShoulder", "RightArm", "RightForeArm", "RightHand" };
        static readonly string[] SrcChild = { "Spine2", "Neck", "LeftArm", "LeftForeArm", "LeftHand", "LeftHandMiddle1", "RightArm", "RightForeArm", "RightHand", "RightHandMiddle1" };
        static readonly string[] DstChild = { "Chest", "Neck", "UpperArm.L", "Forearm.L", "Hand.L", null, "UpperArm.R", "Forearm.R", "Hand.R", null };

        struct Spec
        {
            public string name, file; public int type; public float[] marks;
            public Spec(string n, string f, int t, params float[] m) { name = n; file = f; type = t; marks = m; }
        }

        // кадры (30 к/с): начало замаха, начало удара, конец удара, конец проводки, конец восстановления
        static readonly Spec[] Specs =
        {
            new Spec("Idle", "great sword idle", -1),
            new Spec("Block", "great sword blocking", -2),
            new Spec("Overhead", "great sword slash", (int)AttackType.Overhead, 11, 17, 21, 24, 38),
            new Spec("Slash", "great sword slash (3)", (int)AttackType.Slash, 0, 25, 30, 33, 55),
            // по выгрузке клипа: кадры 0–10 — руки к правому плечу (замах), 11–16 — рез вперёд; было 3–6 — окно удара шло на замахе назад
            new Spec("SlashBack", "great sword attack", (int)AttackType.SlashBack, 0, 11, 16, 20, 36),
            new Spec("LegSweep", "great sword slash (5)", (int)AttackType.LegSweep, 0, 11, 16, 19, 43),
            new Spec("Rising", "great sword slash (2)", (int)AttackType.Rising, 62, 75, 83, 86, 106),
            // в воздухе — клип удара сверху: «great sword jump attack» по CI-логу ведёт клинок кругом вокруг самого бойца
            // (над целью на 2,2 м, потом к себе и за спину, ближе 0,44 м к цели не подходит) — 0 попаданий из 8.
            // Боец и так поднят прыжком на 0,7 м, поэтому рубящий сверху приходит в голову и плечи цели
            new Spec("JumpStrike", "great sword slash", (int)AttackType.JumpStrike, 11, 17, 21, 24, 38),
            new Spec("Charge", "great sword high spin attack", (int)AttackType.Charge, 0, 26, 33, 37, 56),
        };

        const string ImportDir = "Assets/_Project/Animation/Mixamo";

        public static SwordAnimSet Bake(Transform modelRoot, Transform fighter, Func<string, Transform> find, string folder, string prefix)
        {
            string srcDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../tools/mocap/mixamo"));
            if (!Directory.Exists(srcDir)) { Debug.LogWarning("SwordAnimBaker: нет папки " + srcDir); return null; }
            int n = SwordAnimSet.BoneNames.Length;
            Transform[] dst = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                dst[i] = find(SwordAnimSet.BoneNames[i]);
                bool optional = SwordAnimSet.BoneNames[i].StartsWith("Shoulder");
                if (dst[i] == null && !optional) { Debug.LogWarning("SwordAnimBaker: у модели " + modelRoot.name + " нет кости " + SwordAnimSet.BoneNames[i]); return null; }
            }
            Transform neck = find("Neck");

            GameObject rest = LoadMixamo(srcDir, "X Bot");
            if (rest == null) { Debug.LogWarning("SwordAnimBaker: нет X Bot.fbx"); return null; }

            // все кости модели — запомнить позу и вернуть после (сцена сохраняется после сборки)
            Transform[] all = modelRoot.GetComponentsInChildren<Transform>(true);
            Quaternion[] saveR = new Quaternion[all.Length]; Vector3[] saveP = new Vector3[all.Length];
            for (int j = 0; j < all.Length; j++) { saveR[j] = all[j].localRotation; saveP[j] = all[j].localPosition; }

            Vector3 fwd = Vector3.ProjectOnPlane(fighter.forward, Vector3.up).normalized;
            Quaternion Q = Quaternion.LookRotation(fwd, Vector3.up), Qi = Quaternion.Inverse(Q);
            GameObject restGo = Object.Instantiate(rest);
            List<SwordAnimSet.Clip> clips = new List<SwordAnimSet.Clip>();
            SwordAnimSet set = ScriptableObject.CreateInstance<SwordAnimSet>();
            try
            {
                restGo.transform.position = Vector3.zero; restGo.transform.rotation = Quaternion.identity;
                Transform[] s0 = new Transform[n], s0c = new Transform[n];
                for (int i = 0; i < n; i++) { s0[i] = FindMixamo(restGo.transform, Src[i]); s0c[i] = FindMixamo(restGo.transform, SrcChild[i]); }
                for (int i = 0; i < n; i++) if (s0[i] == null) { Debug.LogWarning("SwordAnimBaker: у X Bot нет " + Src[i]); return null; }
                Quaternion[] s0R = new Quaternion[n];
                for (int i = 0; i < n; i++) s0R[i] = s0[i].rotation;
                // таз: высота в Т-позе и масштаб по длине ноги (наша модель / X Bot)
                Transform sHip = FindMixamo(restGo.transform, "Hips"), sUL = FindMixamo(restGo.transform, "LeftUpLeg"), sLL = FindMixamo(restGo.transform, "LeftLeg"), sFt = FindMixamo(restGo.transform, "LeftFoot");
                Transform dUL = find("UpperLeg.L"), dLL = find("LowerLeg.L"), dFt = find("Foot.L");
                float hipRestY = sHip != null ? sHip.position.y : 0f, legRatio = 1f;
                if (sUL != null && sLL != null && sFt != null && dUL != null && dLL != null && dFt != null)
                {
                    float ls = Vector3.Distance(sUL.position, sLL.position) + Vector3.Distance(sLL.position, sFt.position);
                    float ld = Vector3.Distance(dUL.position, dLL.position) + Vector3.Distance(dLL.position, dFt.position);
                    if (ls > 1e-4f) legRatio = ld / ls;
                }

                // ось «вдоль кости» в системе нашей кости (у кисти нет кости-ребёнка — берём ту же ось, что у предплечья)
                Vector3 alongFore = Quaternion.Inverse(dst[SwordAnimSet.ForearmR].rotation) * (dst[SwordAnimSet.HandR].position - dst[SwordAnimSet.ForearmR].position).normalized;
                Vector3 alongForeL = Quaternion.Inverse(dst[SwordAnimSet.ForearmL].rotation) * (dst[SwordAnimSet.HandL].position - dst[SwordAnimSet.ForearmL].position).normalized;

                // выравнивание позы покоя: каждая кость (родитель раньше) смотрит туда же, куда кость X Bot в Т-позе
                Quaternion[] aligned = new Quaternion[n];
                for (int i = 0; i < n; i++)
                {
                    if (dst[i] == null) continue;
                    Transform dc = DstChild[i] != null ? (DstChild[i] == "Neck" ? neck : find(DstChild[i])) : null;
                    Vector3 dDir;
                    if (dc != null) dDir = dc.position - dst[i].position;
                    else if (i == SwordAnimSet.HandR) dDir = dst[i].rotation * alongFore;
                    else if (i == SwordAnimSet.HandL) dDir = dst[i].rotation * alongForeL;
                    else dDir = Vector3.zero;
                    if (s0c[i] != null && dDir.sqrMagnitude > 1e-8f)
                    {
                        Vector3 sDir = Q * (s0c[i].position - s0[i].position);
                        dst[i].rotation = Quaternion.FromToRotation(dDir, sDir) * dst[i].rotation;
                    }
                    aligned[i] = dst[i].rotation;
                }

                set.bonePaths = new string[n];
                for (int i = 0; i < n; i++) set.bonePaths[i] = dst[i] != null ? AnimationUtility.CalculateTransformPath(dst[i], modelRoot) : "";

                foreach (Spec sp in Specs)
                {
                    GameObject asset = LoadMixamo(srcDir, sp.file);
                    AnimationClip ac = asset != null ? ClipOf(AssetDatabase.GetAssetPath(asset)) : null;
                    if (ac == null) { Debug.LogWarning("SwordAnimBaker: нет клипа " + sp.file); continue; }
                    GameObject go = Object.Instantiate(asset);
                    try
                    {
                        go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
                        Transform[] sb = new Transform[n];
                        for (int i = 0; i < n; i++) sb[i] = FindMixamo(go.transform, Src[i]);
                        int frames = Mathf.Max(2, Mathf.RoundToInt(ac.length * 30f) + 1);
                        SwordAnimSet.Clip c = new SwordAnimSet.Clip { name = sp.name, type = sp.type >= 0 ? sp.type : -1, frames = frames, marks = sp.marks, rot = new Quaternion[frames * n], hipY = new float[frames] };
                        Transform aHip = FindMixamo(go.transform, "Hips");
                        for (int k = 0; k < frames; k++)
                        {
                            ac.SampleAnimation(go, Mathf.Min(ac.length, k / 30f));
                            if (aHip != null) c.hipY[k] = (aHip.position.y - hipRestY) * legRatio;
                            for (int i = 0; i < n; i++)
                            {
                                Quaternion q = aligned[i];
                                if (sb[i] != null && dst[i] != null)
                                    q = Q * (sb[i].rotation * Quaternion.Inverse(s0R[i])) * Qi * aligned[i];
                                c.rot[k * n + i] = Qi * q;   // в системе бойца (Q — его поворот при сборке)
                            }
                        }
                        // непрерывность кватернионов (без «перескока» знака между кадрами)
                        for (int i = 0; i < n; i++)
                            for (int k = 1; k < frames; k++)
                                if (Quaternion.Dot(c.rot[(k - 1) * n + i], c.rot[k * n + i]) < 0f)
                                { Quaternion q = c.rot[k * n + i]; c.rot[k * n + i] = new Quaternion(-q.x, -q.y, -q.z, -q.w); }
                        if (sp.type == -1) set.idle = clips.Count;
                        if (sp.type == -2) { set.block = clips.Count; set.blockHold = Mathf.Min(12, frames - 1); }
                        clips.Add(c);
                    }
                    finally { Object.DestroyImmediate(go); }
                }
                set.clips = clips.ToArray();
                if (set.idle < 0) { Debug.LogWarning("SwordAnimBaker: нет стойки — мокап рук выключен"); return null; }

                // хват: по стойке — где кулаки и как лежит рукоять в правой кисти
                Transform hR = dst[SwordAnimSet.HandR], hL = dst[SwordAnimSet.HandL], fR = dst[SwordAnimSet.ForearmR], fL = dst[SwordAnimSet.ForearmL];
                float palmD = 0.32f * Vector3.Distance(fR.position, hR.position);
                SwordAnimSet.Clip idle = set.clips[set.idle];
                Vector3 axS = Vector3.zero, edS = Vector3.zero, pR = Vector3.zero, pL = Vector3.zero;
                for (int k = 0; k < idle.frames; k++)
                {
                    for (int i = 0; i < n; i++) if (dst[i] != null) dst[i].rotation = Q * idle.rot[k * n + i];
                    Vector3 dirR = hR.rotation * alongFore, dirL = hL.rotation * alongForeL;
                    Vector3 cR = hR.position + dirR * palmD, cL = hL.position + dirL * palmD;
                    Vector3 ax = (cR - cL).normalized;
                    Vector3 ed = Vector3.ProjectOnPlane(dirR, ax).normalized;
                    axS += Quaternion.Inverse(hR.rotation) * ax;
                    edS += Quaternion.Inverse(hR.rotation) * ed;
                    pR += hR.InverseTransformPoint(cR);
                    pL += hL.InverseTransformPoint(cL);
                }
                set.bladeAxisR = axS.normalized;
                set.bladeEdgeR = Vector3.ProjectOnPlane(edS, set.bladeAxisR).normalized;
                set.palmR = pR / idle.frames; set.palmL = pL / idle.frames;

                string ap = folder + "/" + prefix + "SwordAnim.asset";
                AssetDatabase.DeleteAsset(ap);
                AssetDatabase.CreateAsset(set, ap);
                AssetDatabase.SaveAssets();
                Debug.Log("SwordAnimBaker: " + modelRoot.name + " — " + clips.Count + " клипов, хват ось " + set.bladeAxisR + " кромка " + set.bladeEdgeR);
                return set;
            }
            catch (Exception e) { Debug.LogError("SwordAnimBaker: " + e); return null; }
            finally
            {
                Object.DestroyImmediate(restGo);
                for (int j = 0; j < all.Length; j++) if (all[j] != null) { all[j].localRotation = saveR[j]; all[j].localPosition = saveP[j]; }
            }
        }

        /// <summary>FBX из tools/mocap/mixamo копируется в проект (только для сборки: в игру не попадает — на него никто не ссылается).</summary>
        static GameObject LoadMixamo(string srcDir, string file)
        {
            string src = Path.Combine(srcDir, file + ".fbx");
            if (!File.Exists(src)) return null;
            string ap = ImportDir + "/" + file + ".fbx";
            string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ap));
            if (!File.Exists(full))
            {
                if (!AssetDatabase.IsValidFolder("Assets/_Project/Animation")) AssetDatabase.CreateFolder("Assets/_Project", "Animation");
                if (!AssetDatabase.IsValidFolder(ImportDir)) AssetDatabase.CreateFolder("Assets/_Project/Animation", "Mixamo");
                File.Copy(src, full, true);
                AssetDatabase.ImportAsset(ap, ImportAssetOptions.ForceSynchronousImport);
                ModelImporter mi = AssetImporter.GetAtPath(ap) as ModelImporter;
                if (mi != null)
                {
                    mi.animationType = ModelImporterAnimationType.Generic;
                    mi.importAnimation = true;
                    mi.importCameras = false; mi.importLights = false;
                    mi.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(ap);
        }

        static AnimationClip ClipOf(string path)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                AnimationClip c = o as AnimationClip;
                if (c != null && !c.name.StartsWith("__preview")) return c;
            }
            return null;
        }

        static Transform FindMixamo(Transform root, string bone)
        {
            if (string.IsNullOrEmpty(bone)) return null;
            string suf = ":" + bone;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == bone || t.name.EndsWith(suf)) return t;
            return null;
        }
    }
}
#endif
