#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Мокап ходьбы (CMU Graphics Lab Motion Capture Database, mocap.cs.cmu.edu; BVH в tools/mocap) → клипы под скелет конкретной модели.
    /// Перенос по мировым поворотам костей относительно Т-позы актёра на позу покоя модели — без зависимости от осей костей FBX:
    ///   W_модель(t) = Q · Cy · W_актёр(t) · W_актёр(Т-поза)⁻¹ · Q⁻¹ · W_модель(покой),
    /// Q — курс бойца, Cy — снимает разворот актёра (он идёт не по +Z). Таз: смещение вокруг линии хода × (нога модели / нога актёра).
    /// Один цикл шага (две постановки стопы), петля без шва (остаток ошибки цикла разносится по кадрам), клип на месте —
    /// скорость клипа уходит в HeroRigDriver, темп в игре подгоняется под настоящую скорость бойца.
    /// Руки и плечи клип держит в позе стойки (клип Idle) — их всё равно ведёт IK к рукояти.
    /// </summary>
    public static class MocapBaker
    {
        public struct Spec
        {
            public string name, file; public int a, b; public float fps, legScale;
            public Spec(string n, string f, int a_, int b_, float fps_, float legScale_ = 1f) { name = n; file = f; a = a_; b = b_; fps = fps_; legScale = legScale_; }
        }

        // циклы выбраны по сходству позы начала и конца (tools/mocap/README.md); реальная частота съёмки 60 к/с
        public static readonly Spec[] Specs =
        {
            new Spec("LocoF", "cmu_143_32.bvh", 612, 679, 60f),
            new Spec("LocoB", "cmu_141_31.bvh", 414, 472, 60f),
            // приставной шаг у актёра очень широкий (стопы расходятся на ~0.8 м) — размах ног и таза уменьшен
            new Spec("LocoS1", "cmu_141_33.bvh", 718, 786, 60f, 0.75f),
            new Spec("LocoS2", "cmu_141_33.bvh", 296, 369, 60f, 0.75f),
        };

        public class Result { public AnimationClip clip; public Vector2 dir; public float speed; public string name; }

        static readonly string[] SrcNames = { "Hips", "LowerBack", "Spine1", "Neck", "Head", "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot" };
        static readonly string[] DstNames = { "Hips", "Spine", "Chest", "Neck", "Head", "UpperLeg.L", "LowerLeg.L", "Foot.L", "UpperLeg.R", "LowerLeg.R", "Foot.R" };

        // ------------------------------------------------------------------ BVH
        class Bvh
        {
            public readonly List<string> names = new List<string>();
            public readonly List<int> parent = new List<int>();
            public readonly List<double[]> offset = new List<double[]>();
            public readonly List<string[]> chans = new List<string[]>();
            public double[][] data;

            public Bvh(string path)
            {
                string txt = File.ReadAllText(path);
                int mi = txt.IndexOf("MOTION", StringComparison.Ordinal);
                string[] tok = txt.Substring(0, mi).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                int cur = -1;
                for (int i = 0; i < tok.Length; i++)
                {
                    string t = tok[i];
                    if (t == "ROOT" || t == "JOINT" || t == "End")
                    {
                        names.Add(t == "End" ? names[cur] + "_end" : tok[i + 1]); parent.Add(cur); offset.Add(new double[3]); chans.Add(new string[0]);
                        cur = names.Count - 1; i++;
                    }
                    else if (t == "}") cur = cur >= 0 ? parent[cur] : -1;
                    else if (t == "OFFSET")
                    {
                        offset[cur] = new[] { D(tok[i + 1]), D(tok[i + 2]), D(tok[i + 3]) }; i += 3;
                    }
                    else if (t == "CHANNELS")
                    {
                        int n = int.Parse(tok[i + 1], CultureInfo.InvariantCulture);
                        string[] c = new string[n]; Array.Copy(tok, i + 2, c, 0, n); chans[cur] = c; i += 1 + n;
                    }
                }
                string[] lines = txt.Substring(mi).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                List<double[]> rows = new List<double[]>();
                for (int l = 3; l < lines.Length; l++)
                {
                    string[] p = lines[l].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length == 0) continue;
                    double[] r = new double[p.Length];
                    for (int k = 0; k < p.Length; k++) r[k] = D(p[k]);
                    rows.Add(r);
                }
                data = rows.ToArray();
            }

            static double D(string s) { return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture); }
            public int Idx(string n) { int i = names.IndexOf(n); if (i < 0) throw new Exception("BVH: нет сустава " + n); return i; }

            /// <summary>Прямая кинематика кадра f: мировые матрицы 3×3 (по строкам) и позиции, система BVH (правая, Y вверх).</summary>
            public void FK(int f, double[][] R, double[][] P)
            {
                double[] row = data[Mathf.Clamp(f, 0, data.Length - 1)]; int k = 0;
                for (int j = 0; j < names.Count; j++)
                {
                    double[] loc = (double[])offset[j].Clone(); double[] Rl = Eye();
                    foreach (string ch in chans[j])
                    {
                        double v = row[k++];
                        if (ch.EndsWith("position")) loc["XYZ".IndexOf(ch[0])] = v;
                        else Rl = Mul(Rl, Rot(ch[0], v));
                    }
                    int p = parent[j];
                    if (p < 0) { R[j] = Rl; P[j] = loc; }
                    else { R[j] = Mul(R[p], Rl); double[] o = MulV(R[p], loc); P[j] = new[] { P[p][0] + o[0], P[p][1] + o[1], P[p][2] + o[2] }; }
                }
            }
        }

        static double[] Eye() { return new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }; }
        static double[] Rot(char axis, double deg)
        {
            double a = deg * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
            if (axis == 'X') return new double[] { 1, 0, 0, 0, c, -s, 0, s, c };
            if (axis == 'Y') return new double[] { c, 0, s, 0, 1, 0, -s, 0, c };
            return new double[] { c, -s, 0, s, c, 0, 0, 0, 1 };
        }
        static double[] Mul(double[] A, double[] B)
        {
            double[] C = new double[9];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) C[i * 3 + j] = A[i * 3] * B[j] + A[i * 3 + 1] * B[3 + j] + A[i * 3 + 2] * B[6 + j];
            return C;
        }
        static double[] MulV(double[] A, double[] v) { return new[] { A[0] * v[0] + A[1] * v[1] + A[2] * v[2], A[3] * v[0] + A[4] * v[1] + A[5] * v[2], A[6] * v[0] + A[7] * v[1] + A[8] * v[2] }; }

        /// <summary>BVH (правая система) → Unity (левая): зеркало по X. Поворот: S·R·S, точка: (-x, y, z).</summary>
        static Quaternion QU(double[] R)
        {
            // S·R·S с S = diag(-1, 1, 1): меняют знак элементы, где ровно один индекс = 0
            Vector3 c1 = new Vector3((float)(-R[1]), (float)R[4], (float)R[7]);     // столбец Y
            Vector3 c2 = new Vector3((float)(-R[2]), (float)R[5], (float)R[8]);     // столбец Z
            return Quaternion.LookRotation(c2, c1);
        }
        static Vector3 PU(double[] p) { return new Vector3((float)(-p[0]), (float)p[1], (float)p[2]); }

        // ------------------------------------------------------------------ запекание
        public static List<Result> Bake(Transform modelRoot, Transform fighter, Func<string, Transform> find, AnimationClip basePose, string folder, string prefix)
        {
            List<Result> res = new List<Result>();
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../tools/mocap"));
            if (!Directory.Exists(dir)) { Debug.LogWarning("MocapBaker: нет папки " + dir); return res; }

            Transform[] dst = new Transform[DstNames.Length];
            for (int i = 0; i < DstNames.Length; i++) { dst[i] = find(DstNames[i]); if (dst[i] == null) { Debug.LogWarning("MocapBaker: нет кости " + DstNames[i]); return res; } }
            // все кости модели, которые пишет клип, включая корневую кость над тазом: клип стойки может её сдвигать,
            // и если её не записать, в игре она встанет в позу покоя, а таз клипа уедет (вниз/вперёд) вместе с ней
            List<Transform> bones = new List<Transform>();
            Transform top = dst[0].parent != null && dst[0].parent != modelRoot ? dst[0].parent : dst[0];
            foreach (Transform t in top.GetComponentsInChildren<Transform>(true))
                if (t.GetComponent<Renderer>() == null && !t.name.StartsWith("rg_") && !t.name.Contains("Eye") && !t.name.Contains("Marker")) bones.Add(t);
            // запоминаем и потом возвращаем позу (сцена сохраняется после сборки)
            Dictionary<Transform, Quaternion> saveR = new Dictionary<Transform, Quaternion>(); Dictionary<Transform, Vector3> saveP = new Dictionary<Transform, Vector3>();
            foreach (Transform t in bones) { saveR[t] = t.localRotation; saveP[t] = t.localPosition; }

            // поза-основа: покой модели (как в FBX), руки/плечи — из клипа стойки
            if (basePose != null) basePose.SampleAnimation(modelRoot.gameObject, 0f);
            Dictionary<Transform, Quaternion> baseR = new Dictionary<Transform, Quaternion>(); Dictionary<Transform, Vector3> baseP = new Dictionary<Transform, Vector3>();
            foreach (Transform t in bones) { baseR[t] = t.localRotation; baseP[t] = t.localPosition; }
            // покой ног и корпуса — поза из FBX (ноги прямые, как Т-поза актёра)
            foreach (Transform t in bones) { t.localRotation = saveR[t]; t.localPosition = saveP[t]; }
            Quaternion[] rest = new Quaternion[dst.Length];
            for (int i = 0; i < dst.Length; i++) rest[i] = dst[i].rotation;
            Vector3 hipRest = dst[0].position;
            float legT = Vector3.Distance(find("UpperLeg.L").position, find("LowerLeg.L").position) + Vector3.Distance(find("LowerLeg.L").position, find("Foot.L").position);
            Vector3 fwd = Vector3.ProjectOnPlane(fighter.forward, Vector3.up).normalized;
            Quaternion Q = Quaternion.LookRotation(fwd, Vector3.up);
            Dictionary<Transform, Quaternion> restLocal = new Dictionary<Transform, Quaternion>();
            for (int i = 0; i < dst.Length; i++) restLocal[dst[i]] = dst[i].localRotation;

            Dictionary<string, Bvh> cache = new Dictionary<string, Bvh>();
            try
            {
                foreach (Spec sp in Specs)
                {
                    string path = Path.Combine(dir, sp.file);
                    if (!File.Exists(path)) { Debug.LogWarning("MocapBaker: нет " + path); continue; }
                    Bvh b;
                    if (!cache.TryGetValue(sp.file, out b)) { b = new Bvh(path); cache[sp.file] = b; }
                    Result r = BakeOne(b, sp, modelRoot, dst, bones, baseR, baseP, rest, hipRest, legT, Q, folder, prefix);
                    if (r != null) res.Add(r);
                }
            }
            catch (Exception e) { Debug.LogError("MocapBaker: " + e); }
            finally { foreach (Transform t in bones) { t.localRotation = saveR[t]; t.localPosition = saveP[t]; } }
            return res;
        }

        static Result BakeOne(Bvh b, Spec sp, Transform modelRoot, Transform[] dst, List<Transform> bones, Dictionary<Transform, Quaternion> baseR, Dictionary<Transform, Vector3> baseP,
            Quaternion[] rest, Vector3 hipRest, float legT, Quaternion Q, string folder, string prefix)
        {
            int n = b.names.Count;
            int[] si = new int[SrcNames.Length];
            for (int i = 0; i < SrcNames.Length; i++) si[i] = b.Idx(SrcNames[i]);
            double[][] R = new double[n][], P = new double[n][];
            // Т-поза — первый кадр
            b.FK(0, R, P);
            Quaternion[] s0 = new Quaternion[si.Length];
            for (int i = 0; i < si.Length; i++) s0[i] = QU(R[si[i]]);
            Vector3 hipT = PU(P[si[0]]);
            float legS = Vector3.Distance(PU(P[b.Idx("LeftUpLeg")]), PU(P[b.Idx("LeftLeg")])) + Vector3.Distance(PU(P[b.Idx("LeftLeg")]), PU(P[b.Idx("LeftFoot")]));
            float ratio = legT / Mathf.Max(1e-4f, legS);

            // кадры цикла (с интерполяцией) в системе Unity
            int K = Mathf.Max(8, Mathf.RoundToInt((sp.b - sp.a) / sp.fps * 30f));
            Quaternion[][] W = new Quaternion[K + 1][]; Vector3[] H = new Vector3[K + 1];
            for (int k = 0; k <= K; k++)
            {
                double fr = sp.a + (sp.b - sp.a) * (double)k / K;
                int f0 = (int)Math.Floor(fr); float u = (float)(fr - f0);
                b.FK(f0, R, P);
                Quaternion[] q0 = new Quaternion[si.Length]; for (int i = 0; i < si.Length; i++) q0[i] = QU(R[si[i]]);
                Vector3 h0 = PU(P[si[0]]);
                b.FK(f0 + 1, R, P);
                W[k] = new Quaternion[si.Length];
                for (int i = 0; i < si.Length; i++) W[k][i] = Quaternion.Slerp(q0[i], QU(R[si[i]]), u);
                H[k] = Vector3.Lerp(h0, PU(P[si[0]]), u);
            }
            // курс актёра за цикл (перед таза относительно Т-позы) → Cy, чтобы клип шёл «лицом» по +Z
            Vector3 face = Vector3.zero;
            for (int k = 0; k < K; k++) face += Vector3.ProjectOnPlane((W[k][0] * Quaternion.Inverse(s0[0])) * Vector3.forward, Vector3.up);
            Quaternion Cy = Quaternion.FromToRotation(face.normalized, Vector3.forward);
            Cy = Quaternion.Euler(0f, Cy.eulerAngles.y, 0f);
            Vector3 travel = H[K] - H[0]; travel.y = 0f;
            float secs = (sp.b - sp.a) / sp.fps;
            float speed = travel.magnitude * ratio / secs * sp.legScale;
            Vector3 tl = Cy * travel; Vector2 dirv = new Vector2(tl.x, tl.z).normalized;

            // таз: колебания вокруг линии хода (на месте), по высоте — относительно Т-позы
            Vector3[] off = new Vector3[K + 1]; Vector3 mean = Vector3.zero;
            for (int k = 0; k <= K; k++)
            {
                Vector3 line = Vector3.Lerp(H[0], H[K], (float)k / K);
                Vector3 d = H[k] - line; d.y = H[k].y - hipT.y;
                off[k] = Cy * d; if (k < K) mean += off[k];
            }
            mean /= K; mean.y = 0f;

            // поза по кадрам → локальные повороты всех костей
            Quaternion[][] LR = new Quaternion[K + 1][]; Vector3[][] LP = new Vector3[K + 1][];
            for (int k = 0; k <= K; k++)
            {
                foreach (Transform t in bones) { t.localRotation = baseR[t]; t.localPosition = baseP[t]; }
                Vector3 ok = (off[k] - mean) * ratio; ok.x *= sp.legScale; ok.z *= sp.legScale;
                Vector3 hp = hipRest + Q * ok;
                dst[0].position = hp;
                for (int i = 0; i < dst.Length; i++)
                {
                    Quaternion delta = Cy * W[k][i] * Quaternion.Inverse(s0[i]);
                    if (i >= 5 && sp.legScale != 1f) delta = Quaternion.Slerp(Quaternion.identity, delta, sp.legScale);   // ноги (DstNames[5..10])
                    dst[i].rotation = Q * delta * Quaternion.Inverse(Q) * rest[i];
                }
                LR[k] = new Quaternion[bones.Count]; LP[k] = new Vector3[bones.Count];
                for (int j = 0; j < bones.Count; j++) { LR[k][j] = bones[j].localRotation; LP[k][j] = bones[j].localPosition; }
            }
            // петля без шва: остаток (кадр K против кадра 0) разносим по циклу
            for (int j = 0; j < bones.Count; j++)
            {
                Quaternion err = LR[0][j] * Quaternion.Inverse(LR[K][j]);
                for (int k = 1; k <= K; k++) LR[k][j] = Quaternion.Slerp(Quaternion.identity, err, (float)k / K) * LR[k][j];
            }
            for (int j = 0; j < bones.Count; j++)
            {
                Vector3 perr = LP[0][j] - LP[K][j];
                for (int k = 1; k <= K; k++) LP[k][j] += perr * ((float)k / K);
            }

            // клип
            AnimationClip clip = new AnimationClip { name = prefix + sp.name, frameRate = 30f };
            float T = secs;
            float[] times = new float[K + 1];
            for (int k = 0; k <= K; k++) times[k] = T * k / K;
            for (int j = 0; j < bones.Count; j++)
            {
                string tp = AnimationUtility.CalculateTransformPath(bones[j], modelRoot);
                Quaternion prev = LR[0][j];
                float[][] v = { new float[K + 1], new float[K + 1], new float[K + 1], new float[K + 1] };
                for (int k = 0; k <= K; k++)
                {
                    Quaternion q = LR[k][j];
                    if (Quaternion.Dot(q, prev) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    prev = q;
                    v[0][k] = q.x; v[1][k] = q.y; v[2][k] = q.z; v[3][k] = q.w;
                }
                string[] comp = { "x", "y", "z", "w" };
                for (int c = 0; c < 4; c++)
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(tp, typeof(Transform), "m_LocalRotation." + comp[c]), LoopCurve(times, v[c]));
                {
                    float[][] pv = { new float[K + 1], new float[K + 1], new float[K + 1] };
                    for (int k = 0; k <= K; k++) { pv[0][k] = LP[k][j].x; pv[1][k] = LP[k][j].y; pv[2][k] = LP[k][j].z; }
                    for (int c = 0; c < 3; c++)
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(tp, typeof(Transform), "m_LocalPosition." + comp[c]), LoopCurve(times, pv[c]));
                }
            }
            clip.EnsureQuaternionContinuity();
            AnimationClipSettings st = AnimationUtility.GetAnimationClipSettings(clip);
            st.loopTime = true; st.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, st);
            string ap = folder + "/" + clip.name + ".anim";
            AssetDatabase.DeleteAsset(ap);
            AssetDatabase.CreateAsset(clip, ap);
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "MocapBaker: {0} — {1} кадров, {2:F2} с, скорость {3:F2} м/с, направление ({4:F2}, {5:F2}), масштаб {6:F4}", clip.name, K, T, speed, dirv.x, dirv.y, ratio));
            return new Result { clip = clip, dir = dirv, speed = speed, name = sp.name };
        }

        /// <summary>Кривая петли с гладкими касательными (по соседям, через шов цикла), без «ступенек» на ключах.</summary>
        static AnimationCurve LoopCurve(float[] t, float[] v)
        {
            int n = v.Length;   // последний ключ = первый (шов)
            Keyframe[] k = new Keyframe[n];
            float T = t[n - 1];
            for (int i = 0; i < n; i++)
            {
                int ip = i == 0 ? n - 2 : i - 1, inx = i == n - 1 ? 1 : i + 1;
                float tp = i == 0 ? t[ip] - T : t[ip], tn = i == n - 1 ? t[inx] + T : t[inx];
                float slope = (v[inx] - v[ip]) / Mathf.Max(1e-5f, tn - tp);
                k[i] = new Keyframe(t[i], v[i], slope, slope);
            }
            AnimationCurve c = new AnimationCurve(k);
            c.preWrapMode = WrapMode.Loop; c.postWrapMode = WrapMode.Loop;
            return c;
        }
    }
}
#endif
