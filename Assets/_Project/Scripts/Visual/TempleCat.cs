using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Храмовый кот на веранде храма (за спинами бойцов, на свету сёдзи): рыжий с белой грудкой и лапками. Сидит, обвив
    /// лапы хвостом, кончик хвоста подёргивается, голова следит за бойцами; время от времени умывается — поднимает лапу
    /// ко рту, вылизывает её, проводит ею по морде и за ухом; иногда встаёт и неспешно переходит по веранде. Когда во дворе
    /// громыхнуло — испуганное «мя!» и бегом на другой конец. В дождь и снег жмётся к сёдзи под свесом крыши.
    /// Тело — сцепленные эллипсоиды на суставах (грудь, круп, шея, голова; передние лапы из двух звеньев, задние — из трёх:
    /// бедро, голень, стопа), позы «сидит» и «идёт» плавно перетекают; звенья заходят друг в друга — без щелей.
    /// </summary>
    public class TempleCat : MonoBehaviour
    {
        public Vector3 a = new Vector3(-4.1f, 0.6f, 4.85f), b = new Vector3(2.5f, 0.6f, 4.85f);   // край веранды
        public float sitYaw = 180f;   // сидя — мордой во двор

        class Leg { public Transform hip, knee, hock; public float l1, l2, l3; }
        Transform root, chest, haunch, neck, head, tailRoot;
        // туловище — одна гладкая «колбаса» вдоль хребта (кольца по сплайну от крупа к шее), а не два шара
        Mesh bodyMesh; Vector3[] bodyV; const int Rings = 16, Sides = 12;
        Leg[] legs = new Leg[4];             // 0, 1 — передние (Л, П); 2, 3 — задние
        Transform[] tail = new Transform[8];
        float z, targetZ, timer, groomT = -1f, walkPh, face = 90f, tailPh, sitK = 1f;
        bool walking;
        FighterController[] fighters;
        AudioSource voice; static AudioClip meow, meowShort; float meowAt; int seenStartle; float rush = 1f;
        const float GroomLen = 7f;

        void Start()
        {
            Build();
            z = targetZ = Random.Range(0f, 1f);
            timer = Random.Range(6f, 14f);
            fighters = FindObjectsByType<FighterController>(FindObjectsSortMode.None);
            voice = gameObject.AddComponent<AudioSource>(); voice.playOnAwake = false; voice.spatialBlend = 0.75f; voice.minDistance = 2f; voice.maxDistance = 25f;
            if (meow == null) { meow = Meow(51, 0.62f, 1f); meowShort = Meow(52, 0.3f, 1.25f); }
            meowAt = Time.time + Random.Range(30f, 70f);
            seenStartle = Birds.StartleCount;
        }

        // ---------- построение ----------

        public void Build()
        {
            if (root != null) return;
            Shader sh = Shader.Find("Standard");
            Material fur = new Material(sh) { name = "CatFur", color = new Color(0.5f, 0.29f, 0.13f) }; fur.SetFloat("_Glossiness", 0.12f);
            Material white = new Material(sh) { name = "CatWhite", color = new Color(0.78f, 0.75f, 0.7f) }; white.SetFloat("_Glossiness", 0.1f);
            Material dark = new Material(sh) { name = "CatDark", color = new Color(0.12f, 0.06f, 0.05f) }; dark.SetFloat("_Glossiness", 0.3f);
            Material pink = new Material(sh) { name = "CatPink", color = new Color(0.55f, 0.32f, 0.3f) };
            Material eye = new Material(sh) { name = "CatEye", color = new Color(0.45f, 0.42f, 0.1f) };
            eye.EnableKeyword("_EMISSION"); eye.SetColor("_EmissionColor", new Color(0.5f, 0.44f, 0.07f));   // ночью глаза ловят свет

            root = new GameObject("TempleCat").transform; root.SetParent(transform, false);
            chest = Ell(root, "Chest", Vector3.zero, new Vector3(0.12f, 0.15f, 0.16f), fur);
            Ell(chest, "Bib", new Vector3(0f, -0.12f, 0.32f), new Vector3(0.75f, 0.7f, 0.45f), white);   // белая грудка (в долях груди)
            haunch = Ell(root, "Haunch", Vector3.zero, new Vector3(0.14f, 0.14f, 0.19f), fur);
            chest.GetComponent<Renderer>().enabled = false; haunch.GetComponent<Renderer>().enabled = false;   // опоры для грудки и лап; видимое тело — bodyMesh
            BuildBody(fur);
            neck = Ell(root, "Neck", Vector3.zero, new Vector3(0.075f, 0.085f, 0.09f), fur);
            head = new GameObject("Head").transform; head.SetParent(root, false);
            Ell(head, "Skull", Vector3.zero, new Vector3(0.1f, 0.085f, 0.092f), fur);
            Ell(head, "Cheeks", new Vector3(0f, -0.012f, 0.012f), new Vector3(0.105f, 0.06f, 0.08f), fur);
            Ell(head, "Muzzle", new Vector3(0f, -0.017f, 0.04f), new Vector3(0.048f, 0.034f, 0.036f), white);
            Ell(head, "Nose", new Vector3(0f, -0.006f, 0.058f), new Vector3(0.012f, 0.008f, 0.008f), pink);
            Ell(head, "EyeL", new Vector3(-0.022f, 0.008f, 0.04f), new Vector3(0.017f, 0.014f, 0.01f), eye);
            Ell(head, "EyeR", new Vector3(0.022f, 0.008f, 0.04f), new Vector3(0.017f, 0.014f, 0.01f), eye);
            for (int s = -1; s <= 1; s += 2)
            {
                // ухо — квадрат, повёрнутый ромбом: нижняя половина утоплена в голову, торчит треугольник
                Transform ear = Box(head, "Ear" + s, new Vector3(s * 0.03f, 0.036f, -0.006f), new Vector3(0.036f, 0.036f, 0.008f), fur);
                ear.localRotation = Quaternion.Euler(-8f, s * 12f, 45f);
                Transform inner = Box(ear, "Inner", new Vector3(0f, 0f, 0.4f), new Vector3(0.62f, 0.62f, 0.5f), pink);
                inner.localRotation = Quaternion.identity;
            }
            // лапы: передние — плечо и предплечье с лапкой; задние — бедро, голень, стопа с лапкой
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2;
                Leg l = new Leg();
                l.l1 = front ? 0.075f : 0.07f; l.l2 = front ? 0.07f : 0.06f; l.l3 = front ? 0f : 0.045f;
                l.hip = new GameObject("Hip" + i).transform; l.hip.SetParent(root, false);
                Ell(l.hip, "Upper", new Vector3(0f, -l.l1 * 0.45f, 0f), front ? new Vector3(0.05f, l.l1 * 1.35f, 0.055f) : new Vector3(0.062f, l.l1 * 1.45f, 0.085f), fur);
                l.knee = new GameObject("Knee").transform; l.knee.SetParent(l.hip, false); l.knee.localPosition = new Vector3(0f, -l.l1, 0f);
                Ell(l.knee, "Lower", new Vector3(0f, -l.l2 * 0.5f, 0f), new Vector3(0.034f, l.l2 * 1.3f, 0.036f), front ? white : fur);
                if (front)
                {
                    Ell(l.knee, "Paw", new Vector3(0f, -l.l2, 0.008f), new Vector3(0.036f, 0.024f, 0.046f), white);
                }
                else
                {
                    l.hock = new GameObject("Hock").transform; l.hock.SetParent(l.knee, false); l.hock.localPosition = new Vector3(0f, -l.l2, 0f);
                    Ell(l.hock, "Foot", new Vector3(0f, -l.l3 * 0.5f, 0f), new Vector3(0.03f, l.l3 * 1.3f, 0.032f), white);
                    Ell(l.hock, "Paw", new Vector3(0f, -l.l3, 0.006f), new Vector3(0.034f, 0.022f, 0.044f), white);
                }
                legs[i] = l;
            }
            tailRoot = new GameObject("Tail").transform; tailRoot.SetParent(root, false);
            Transform parent = tailRoot;
            for (int i = 0; i < tail.Length; i++)
            {
                Transform t = new GameObject("T" + i).transform; t.SetParent(parent, false);
                t.localPosition = i == 0 ? Vector3.zero : new Vector3(0f, 0f, -0.04f);
                float w = Mathf.Lerp(0.034f, 0.022f, i / (float)(tail.Length - 1));
                Ell(t, "Seg", new Vector3(0f, 0f, -0.02f), new Vector3(w, w, 0.056f), i == tail.Length - 1 ? dark : fur);
                tail[i] = t; parent = t;
            }
            Pose(1f, -1f, 0f, 0f, 0f, 0f);
        }

        void BuildBody(Material fur)
        {
            bodyV = new Vector3[Rings * Sides + 2];
            List<int> t = new List<int>();
            for (int r = 0; r < Rings - 1; r++)
                for (int k = 0; k < Sides; k++)
                {
                    int a0 = r * Sides + k, a1 = r * Sides + (k + 1) % Sides, b0 = a0 + Sides, b1 = a1 + Sides;
                    t.Add(a0); t.Add(b1); t.Add(b0); t.Add(a0); t.Add(a1); t.Add(b1);   // по часовой снаружи
                }
            int capA = Rings * Sides, capB = capA + 1;
            for (int k = 0; k < Sides; k++)
            {
                int k2 = (k + 1) % Sides, last = (Rings - 1) * Sides;
                t.Add(capA); t.Add(k2); t.Add(k);
                t.Add(capB); t.Add(last + k); t.Add(last + k2);
            }
            bodyMesh = new Mesh { name = "CatBody" };
            bodyMesh.MarkDynamic();
            bodyMesh.vertices = bodyV;
            bodyMesh.SetTriangles(t, 0);
            GameObject g = new GameObject("Body");
            g.transform.SetParent(root, false);
            g.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = fur;
        }

        static Vector3 CR(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        /// <summary>Хребет по 4 точкам (круп → бёдра → грудь → основание шеи) и радиусы (ширина x, высота y) в них.</summary>
        void UpdateBody(Vector3[] c, Vector2[] rad)
        {
            if (bodyMesh == null) return;
            Vector3[] p = { c[0] * 2f - c[1], c[0], c[1], c[2], c[3], c[3] * 2f - c[2] };
            Vector3 prev = c[0];
            for (int r = 0; r < Rings; r++)
            {
                float u = r / (float)(Rings - 1) * 3f;   // 0…3 по трём отрезкам
                int seg = Mathf.Min(2, (int)u); float f = u - seg;
                Vector3 pos = CR(p[seg], p[seg + 1], p[seg + 2], p[seg + 3], f);
                Vector3 nxt = CR(p[seg], p[seg + 1], p[seg + 2], p[seg + 3], Mathf.Min(1f, f + 0.05f));
                Vector3 tan = (seg == 2 && f > 0.95f ? pos - prev : nxt - pos);
                if (tan.sqrMagnitude < 1e-8f) tan = Vector3.forward;
                tan.Normalize();
                Vector2 rr = Vector2.Lerp(rad[seg], rad[seg + 1], Mathf.SmoothStep(0f, 1f, f));
                float end = Mathf.Min(u, 3f - u);   // скругление концов
                float taper = end < 0.35f ? Mathf.Sqrt(Mathf.Max(0.05f, end / 0.35f)) : 1f;
                rr *= Mathf.Lerp(0.55f, 1f, taper);
                Vector3 side = Vector3.right, nrm = Vector3.Cross(tan, side).normalized;   // «вверх» поперёк хребта
                if (nrm.y < 0f) nrm = -nrm;
                for (int k = 0; k < Sides; k++)
                {
                    float a = Mathf.PI * 2f * k / Sides;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    // живот чуть площе спины
                    float down = sa < 0f ? 0.85f : 1f;
                    bodyV[r * Sides + k] = pos + side * (ca * rr.x) + nrm * (sa * rr.y * down);
                }
                prev = pos;
            }
            Vector3 t0 = (c[0] - c[1]).normalized, t1 = (c[3] - c[2]).normalized;
            bodyV[Rings * Sides] = c[0] + t0 * rad[0].y * 0.35f;
            bodyV[Rings * Sides + 1] = c[3] + t1 * rad[3].y * 0.35f;
            bodyMesh.vertices = bodyV;
            bodyMesh.RecalculateNormals();
            bodyMesh.RecalculateBounds();
        }

        static Transform Ell(Transform parent, string name, Vector3 pos, Vector3 scale, Material m) { return Prim(parent, PrimitiveType.Sphere, name, pos, scale, m); }
        static Transform Box(Transform parent, string name, Vector3 pos, Vector3 scale, Material m) { return Prim(parent, PrimitiveType.Cube, name, pos, scale, m); }

        static Transform Prim(Transform parent, PrimitiveType t, string name, Vector3 pos, Vector3 scale, Material m)
        {
            GameObject g = GameObject.CreatePrimitive(t);
            g.name = name;
            Collider c = g.GetComponent<Collider>(); if (c != null) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            Renderer r = g.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return g.transform;
        }

        // ---------- поза ----------

        static float L(float x, float y, float k) { return x + (y - x) * k; }
        static Vector3 L(Vector3 x, Vector3 y, float k) { return x + (y - x) * k; }
        static Vector2 L(Vector2 x, Vector2 y, float k) { return x + (y - x) * k; }

        /// <summary>
        /// Поза: sit 0 — идёт, 1 — сидит; groom — время умывания (−1 — не умывается); ph — фаза шага;
        /// lookYaw/lookPitch — куда смотрит голова; tph — фаза хвоста.
        /// </summary>
        public void Pose(float sit, float groom, float ph, float lookYaw, float lookPitch, float tph)
        {
            if (root == null) return;
            float w = 1f - sit;
            // туловище: идёт — горизонтально, сидит — грудь поднята, круп на полу
            chest.localPosition = L(new Vector3(0f, 0.165f, 0.06f), new Vector3(0f, 0.165f, 0.035f), sit);
            chest.localRotation = Quaternion.Euler(L(-4f, -58f, sit), 0f, 0f);
            haunch.localPosition = L(new Vector3(0f, 0.165f, -0.07f), new Vector3(0f, 0.075f, -0.06f), sit);   // грудь и круп внахлёст — без «седловины»
            haunch.localRotation = Quaternion.Euler(L(4f, -18f, sit), 0f, 0f);
            haunch.localScale = L(new Vector3(0.13f, 0.135f, 0.19f), new Vector3(0.15f, 0.14f, 0.18f), sit);
            float bob = Mathf.Abs(Mathf.Sin(ph)) * 0.008f * w;
            chest.localPosition += Vector3.up * bob; haunch.localPosition += Vector3.up * bob;
            Vector3 lift = Vector3.up * bob;
            Vector3[] spine = {
                L(new Vector3(0f, 0.165f, -0.165f), new Vector3(0f, 0.045f, -0.13f), sit) + lift,   // круп
                L(new Vector3(0f, 0.168f, -0.07f), new Vector3(0f, 0.085f, -0.06f), sit) + lift,    // бёдра
                L(new Vector3(0f, 0.168f, 0.05f), new Vector3(0f, 0.175f, 0.025f), sit) + lift,     // грудь
                L(new Vector3(0f, 0.19f, 0.135f), new Vector3(0f, 0.245f, 0.055f), sit) + lift };   // основание шеи
            Vector2[] rad = {
                L(new Vector2(0.045f, 0.045f), new Vector2(0.065f, 0.045f), sit),
                L(new Vector2(0.066f, 0.07f), new Vector2(0.078f, 0.075f), sit),
                L(new Vector2(0.06f, 0.072f), new Vector2(0.062f, 0.07f), sit),
                L(new Vector2(0.044f, 0.048f), new Vector2(0.045f, 0.05f), sit) };
            UpdateBody(spine, rad);
            neck.localPosition = L(new Vector3(0f, 0.205f, 0.165f), new Vector3(0f, 0.255f, 0.06f), sit);
            neck.localRotation = Quaternion.Euler(L(-40f, -15f, sit), 0f, 0f);
            Vector3 headPos = L(new Vector3(0f, 0.235f, 0.215f), new Vector3(0f, 0.3f, 0.085f), sit);

            // передние лапы: идёт — шаг, сидит — прямые столбиками; при умывании правая (1) поднята ко рту
            float gPaw = 0f, gWipe = 0f, gLick = 0f;
            if (groom >= 0f)
            {
                float u = groom / GroomLen;
                gPaw = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.08f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - u) / 0.08f));
                gWipe = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - 0.5f) / 0.06f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((0.9f - u) / 0.06f));   // вторая половина — трёт морду и за ухом
                gLick = gPaw * (1f - gWipe);
            }
            for (int i = 0; i < 2; i++)
            {
                Leg l = legs[i];
                float sd = i == 0 ? -1f : 1f;
                l.hip.localPosition = L(new Vector3(sd * 0.042f, 0.152f, 0.12f), new Vector3(sd * 0.034f, 0.162f, 0.07f), sit) + Vector3.up * bob;
                float sw = Mathf.Sin(ph + (i == 0 ? 0f : Mathf.PI)) * 28f * w;
                float a1 = sw + 4f * w, a2 = Mathf.Max(0f, -Mathf.Sin(ph + (i == 0 ? 0f : Mathf.PI))) * 40f * w;   // лапа в переносе сгибается
                float yawIn = 0f;
                if (i == 1 && gPaw > 0f)
                {
                    float lick = Mathf.Sin(groom * 9f) * 6f;
                    float wipe = Mathf.Sin(groom * 5.5f) * 22f;
                    a1 = L(a1, L(-105f + lick, -150f + wipe, gWipe), gPaw);     // плечо вперёд, локоть вперёд-вверх
                    a2 = L(a2, L(-88f, -45f, gWipe), gPaw);                      // предплечье загнуто вверх: подушечка у рта / у щеки и уха
                    yawIn = -18f * gPaw;
                }
                l.hip.localRotation = Quaternion.Euler(a1, yawIn, 0f);
                l.knee.localRotation = Quaternion.Euler(a2, 0f, 0f);
            }
            // задние: идёт — бедро-голень-стопа шагают; сидит — бедро вперёд вдоль пола, голень назад, стопа плашмя вперёд
            for (int i = 2; i < 4; i++)
            {
                Leg l = legs[i];
                float sd = i == 2 ? -1f : 1f;
                l.hip.localPosition = L(new Vector3(sd * 0.05f, 0.165f, -0.115f), new Vector3(sd * 0.062f, 0.075f, -0.065f), sit) + Vector3.up * bob;
                float s = Mathf.Sin(ph + (i == 2 ? Mathf.PI : 0f));
                float k1 = L(-20f + s * 24f, -75f, sit);
                float k2 = L(40f - Mathf.Max(0f, -s) * 25f, 123f, sit);
                float k3 = L(-20f + Mathf.Max(0f, -s) * 20f, -138f, sit);
                l.hip.localRotation = Quaternion.Euler(k1, 0f, sd * 6f * sit);
                l.knee.localRotation = Quaternion.Euler(k2, 0f, 0f);
                l.hock.localRotation = Quaternion.Euler(k3, 0f, 0f);
            }

            // голова: следит за бойцом; при умывании наклоняется к лапе и вылизывает (кивки), потом трётся о лапу щекой
            float hp = lookPitch, hy = lookYaw, hr = 0f;
            if (gPaw > 0f)
            {
                hp = L(hp, L(26f + Mathf.Sin(groom * 9f) * 9f, 8f, gWipe), gPaw);
                hy = L(hy, L(14f, 24f, gWipe), gPaw);
                hr = L(0f, L(-8f, -26f + Mathf.Sin(groom * 5.5f) * 10f, gWipe), gPaw);
                headPos += new Vector3(0.008f, -0.02f * gLick, 0.012f * gLick);
            }
            head.localPosition = headPos;
            head.localRotation = Quaternion.Euler(L(-8f, 0f, sit) + hp, hy, hr);

            // хвост: сидя — лежит на полу и обвивает передние лапы, кончик подёргивается; на ходу — трубой с изгибом
            tailRoot.localPosition = spine[0] + (spine[0] - spine[1]).normalized * 0.01f + Vector3.up * L(0.01f, -0.005f, sit);   // растёт из конца крупа
            for (int i = 0; i < tail.Length; i++)
            {
                float k = i / (float)(tail.Length - 1);
                float upW = i == 0 ? 22f : i < 3 ? 12f : i < 5 ? 4f : 20f;   // дугой назад-вверх, кончик загнут                                      // поднят трубой и загнут вперёд «вопросом»
                float sideW = Mathf.Sin(tph * 2f + i * 0.6f) * 7f;
                float upS = i == 0 ? 8f : 0f;                                                      // лежит на полу
                float sideS = (i == 0 ? 80f : 27f) + Mathf.Sin(tph * (1f + k) + i) * 14f * k * k;  // огибает круп и обвивает лапы, кончик дёргается
                tail[i].localRotation = Quaternion.Euler(L(upW, upS, sit), L(sideW, sideS, sit), 0f);
            }
        }

        // ---------- поведение ----------

        void Update()
        {
            if (root == null) return;
            float dt = Time.deltaTime, t = Time.time;
            // в дождь и снег не исчезает, а жмётся к сёдзи под свесом крыши — там сухо
            Weather wth = Weather.Instance;
            bool wet = wth != null && (wth.Raining || wth.Snowing);
            shelter = Mathf.MoveTowards(shelter, wet ? 0.24f : 0f, dt * 0.15f);
            timer -= dt;
            // во дворе громыхнуло — испуганное «мя!» и бегом на другой конец веранды
            if (Birds.StartleCount != seenStartle)
            {
                seenStartle = Birds.StartleCount;
                walking = true; rush = 3f; groomT = -1f; targetZ = z < 0.5f ? Random.Range(0.85f, 1f) : Random.Range(0f, 0.15f);
                if (voice != null && meowShort != null) { voice.pitch = Random.Range(0.95f, 1.1f); voice.PlayOneShot(meowShort, 0.35f); Ambience.Extend(0.4f); }
            }
            if (!walking && groomT < 0f && t >= meowAt)
            {
                meowAt = t + Random.Range(50f, 120f);
                if (voice != null && meow != null && Ambience.Claim(0.7f, 3f, 7f)) { voice.pitch = Random.Range(0.92f, 1.08f); voice.PlayOneShot(meow, 0.22f); }
            }
            if (!walking && groomT < 0f && timer <= 0f)
            {
                if (Random.value < 0.4f) { walking = true; targetZ = Mathf.Clamp01(z + Random.Range(-0.6f, 0.6f)); }
                else groomT = 0f;   // умывается
                timer = Random.Range(8f, 18f);
            }
            if (groomT >= 0f) { groomT += dt; if (groomT >= GroomLen) groomT = -1f; }
            if (walking)
            {
                float dir = Mathf.Sign(targetZ - z);
                float len = Mathf.Max(0.1f, Vector3.Distance(a, b));
                z += dir * 0.42f * rush / len * dt;
                walkPh += dt * 7.5f * Mathf.Sqrt(rush);
                Vector3 ab = (b - a) * dir;
                face = Mathf.MoveTowardsAngle(face, Mathf.Atan2(ab.x, ab.z) * Mathf.Rad2Deg, dt * 360f);
                if (Mathf.Abs(targetZ - z) < 0.01f * rush) { walking = false; rush = 1f; }
            }
            else face = Mathf.MoveTowardsAngle(face, sitYaw, dt * 200f);
            // встаёт и садится не мгновенно
            sitK = Mathf.MoveTowards(sitK, walking ? 0f : (Mathf.Abs(Mathf.DeltaAngle(face, sitYaw)) < 20f ? 1f : 0f), dt * 2.2f);
            root.position = Vector3.Lerp(a, b, z) + Vector3.forward * shelter;
            root.rotation = Quaternion.Euler(0f, face, 0f);
            // голова: следит за одним из бойцов (по очереди), не выворачиваясь
            float yaw = 0f, pitch = 0f;
            if (!walking && fighters != null && fighters.Length > 0 && fighters[0] != null)
            {
                FighterController f = fighters[(int)(t / 7f) % fighters.Length];
                if (f != null)
                {
                    Vector3 d = root.InverseTransformDirection(f.transform.position + Vector3.up - head.position);
                    yaw = Mathf.Clamp(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -75f, 75f);
                    pitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -20f, 25f);
                }
            }
            lookYaw = Mathf.LerpAngle(lookYaw, yaw, 1f - Mathf.Exp(-dt * 4f));
            lookPitch = Mathf.Lerp(lookPitch, pitch, 1f - Mathf.Exp(-dt * 4f));
            tailPh += dt * (walking ? 3f : 1.3f);
            Pose(Mathf.SmoothStep(0f, 1f, sitK), groomT, walkPh, lookYaw, lookPitch, tailPh);
        }
        float lookYaw, lookPitch, shelter;

        /// <summary>
        /// Мяуканье: голос ~600 Гц взлетает к 820 и опадает к 480 («ми-я-у»), форманты скользят от «и» к «а» и к «у»,
        /// лёгкий придых. pitchK — выше для короткого испуганного «мя!».
        /// </summary>
        static AudioClip Meow(int seed, float T, float pitchK)
        {
            const int rate = 22050;
            int n = (int)(rate * T);
            float[] d = new float[n];
            System.Random rnd = new System.Random(seed);
            float ph = 0f, prev = 0f, peak = 1e-4f;
            float[] y1 = new float[3], y2 = new float[3], bw = { 160f, 200f, 260f }, gk = { 1f, 0.7f, 0.3f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;   // 0…1
                float f0 = (t < 0.35f ? Mathf.Lerp(600f, 820f, t / 0.35f) : Mathf.Lerp(820f, 480f, (t - 0.35f) / 0.65f)) * pitchK;
                ph += f0 / rate; if (ph >= 1f) ph -= 1f;
                float g = ph < 0.55f ? 0.5f * (1f - Mathf.Cos(Mathf.PI * ph / 0.55f)) : Mathf.Max(0f, Mathf.Cos(Mathf.PI * (ph - 0.55f) / 0.5f));
                float exc = (g - prev) * 20f + ((float)rnd.NextDouble() * 2f - 1f) * 0.08f; prev = g;
                // «и» (F1 400, F2 2400) → «а» (1000, 1600) → «у» (500, 900)
                float a = Mathf.Clamp01(t / 0.4f), u = Mathf.Clamp01((t - 0.5f) / 0.5f);
                float F1 = Mathf.Lerp(Mathf.Lerp(400f, 1000f, a), 500f, u), F2 = Mathf.Lerp(Mathf.Lerp(2400f, 1600f, a), 900f, u), F3 = 3200f;
                float v = 0f;
                for (int k = 0; k < 3; k++)
                {
                    float fk = k == 0 ? F1 : k == 1 ? F2 : F3;
                    float r = Mathf.Exp(-Mathf.PI * bw[k] / rate), w = 2f * Mathf.PI * fk / rate;
                    float y = (1f - r) * exc + 2f * r * Mathf.Cos(w) * y1[k] - r * r * y2[k];
                    y2[k] = y1[k]; y1[k] = y; v += y * gk[k];
                }
                v *= Mathf.SmoothStep(0f, 1f, t / 0.08f) * Mathf.SmoothStep(0f, 1f, (1f - t) / 0.3f);
                d[i] = v; peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            for (int i = 0; i < n; i++) d[i] *= 0.8f / peak;
            AudioClip c = AudioClip.Create("meow_" + seed, n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }

    }
}
