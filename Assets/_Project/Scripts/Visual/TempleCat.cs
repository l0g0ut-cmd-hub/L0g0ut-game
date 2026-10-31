using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Храмовый кот на веранде храма (за спинами бойцов, на свету сёдзи): сидит, обвив лапы хвостом, кончик хвоста подёргивается, голова поворачивается за
    /// бойцами; иногда умывается, иногда встаёт и неспешно перебегает по стене на другое место. В дождь и снег прячется.
    /// Фигура из примитивов (тёмный силуэт): туловище, голова с ушами, четыре лапы, хвост из звеньев.
    /// </summary>
    public class TempleCat : MonoBehaviour
    {
        public Vector3 a = new Vector3(-4.1f, 0.6f, 4.85f), b = new Vector3(2.5f, 0.6f, 4.85f);   // край веранды
        public float sitYaw = 180f;   // сидя — мордой во двор
        Transform root, body, head, tailRoot;
        Transform[] legs = new Transform[4], tail = new Transform[5];
        float z, targetZ, timer, groom, walkPh, face = 90f, tailPh;
        bool walking, hidden;
        FighterController[] fighters;
        // голос: тихое «мя-у» изредка, испуганное — когда во дворе громыхнуло (Birds.Startle); тогда же кот убегает вдоль веранды
        AudioSource voice; static AudioClip meow, meowShort; float meowAt; int seenStartle; float rush = 1f;

        void Start()
        {
            Shader sh = Shader.Find("Standard");
            Material m = new Material(sh) { name = "Cat", color = new Color(0.05f, 0.045f, 0.045f) }; m.SetFloat("_Glossiness", 0.2f);
            Material eye = new Material(sh) { name = "CatEye", color = new Color(0.5f, 0.45f, 0.1f) };
            eye.EnableKeyword("_EMISSION"); eye.SetColor("_EmissionColor", new Color(0.55f, 0.48f, 0.08f));
            root = new GameObject("TempleCat").transform; root.SetParent(transform, false);
            body = Prim(root, PrimitiveType.Sphere, new Vector3(0f, 0.14f, 0f), new Vector3(0.16f, 0.17f, 0.36f), m);
            head = new GameObject("Head").transform; head.SetParent(root, false); head.localPosition = new Vector3(0f, 0.27f, 0.17f);
            Prim(head, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.13f, 0.11f, 0.12f), m);
            Prim(head, PrimitiveType.Cube, new Vector3(-0.04f, 0.065f, 0f), new Vector3(0.035f, 0.05f, 0.015f), m).localRotation = Quaternion.Euler(0f, 0f, 20f);
            Prim(head, PrimitiveType.Cube, new Vector3(0.04f, 0.065f, 0f), new Vector3(0.035f, 0.05f, 0.015f), m).localRotation = Quaternion.Euler(0f, 0f, -20f);
            Prim(head, PrimitiveType.Sphere, new Vector3(-0.028f, 0.01f, 0.055f), Vector3.one * 0.018f, eye);
            Prim(head, PrimitiveType.Sphere, new Vector3(0.028f, 0.01f, 0.055f), Vector3.one * 0.018f, eye);
            for (int i = 0; i < 4; i++)
            {
                Transform p = new GameObject("Leg" + i).transform; p.SetParent(root, false);
                p.localPosition = new Vector3(i % 2 == 0 ? -0.045f : 0.045f, 0.12f, i < 2 ? 0.11f : -0.11f);
                Prim(p, PrimitiveType.Cylinder, new Vector3(0f, -0.06f, 0f), new Vector3(0.035f, 0.06f, 0.035f), m);
                legs[i] = p;
            }
            tailRoot = new GameObject("Tail").transform; tailRoot.SetParent(root, false); tailRoot.localPosition = new Vector3(0f, 0.1f, -0.17f);
            Transform parent = tailRoot;
            for (int i = 0; i < tail.Length; i++)
            {
                Transform s = new GameObject("T" + i).transform; s.SetParent(parent, false); s.localPosition = i == 0 ? Vector3.zero : new Vector3(0f, 0f, -0.06f);
                Prim(s, PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.03f), new Vector3(0.025f, 0.032f, 0.025f), m).localRotation = Quaternion.Euler(90f, 0f, 0f);
                tail[i] = s; parent = s;
            }
            z = targetZ = Random.Range(0f, 1f);
            timer = Random.Range(8f, 20f);
            fighters = FindObjectsByType<FighterController>(FindObjectsSortMode.None);
            voice = gameObject.AddComponent<AudioSource>(); voice.playOnAwake = false; voice.spatialBlend = 0.75f; voice.minDistance = 2f; voice.maxDistance = 25f;
            if (meow == null) { meow = Meow(51, 0.62f, 1f); meowShort = Meow(52, 0.3f, 1.25f); }
            meowAt = Time.time + Random.Range(30f, 70f);
            seenStartle = Birds.StartleCount;
        }

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

        static Transform Prim(Transform parent, PrimitiveType t, Vector3 pos, Vector3 scale, Material m)
        {
            GameObject g = GameObject.CreatePrimitive(t);
            Collider c = g.GetComponent<Collider>(); if (c != null) Destroy(c);
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            Renderer r = g.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g.transform;
        }

        void Update()
        {
            if (root == null) return;
            float dt = Time.deltaTime, t = Time.time;
            Weather w = Weather.Instance;
            bool hide = w != null && (w.Raining || w.Snowing);
            if (hide != hidden) { hidden = hide; root.gameObject.SetActive(!hide); }
            if (hide) return;
            timer -= dt;
            // во дворе громыхнуло — испуганное «мя!» и бегом на другой конец веранды
            if (Birds.StartleCount != seenStartle)
            {
                seenStartle = Birds.StartleCount;
                walking = true; rush = 3f; targetZ = z < 0.5f ? Random.Range(0.85f, 1f) : Random.Range(0f, 0.15f);
                if (voice != null && meowShort != null) { voice.pitch = Random.Range(0.95f, 1.1f); voice.PlayOneShot(meowShort, 0.35f); Ambience.Extend(0.4f); }
            }
            if (!walking && t >= meowAt)
            {
                meowAt = t + Random.Range(50f, 120f);
                if (voice != null && meow != null && Ambience.Claim(0.7f, 3f, 7f)) { voice.pitch = Random.Range(0.92f, 1.08f); voice.PlayOneShot(meow, 0.22f); }
            }
            if (!walking && timer <= 0f)
            {
                if (Random.value < 0.45f) { walking = true; targetZ = Mathf.Clamp01(z + Random.Range(-0.6f, 0.6f)); }
                else groom = 2.5f;
                timer = Random.Range(10f, 25f);
            }
            float sitK;
            if (walking)
            {
                float dir = Mathf.Sign(targetZ - z);
                float len = Mathf.Max(0.1f, Vector3.Distance(a, b));
                z += dir * 0.5f * rush / len * dt;
                walkPh += dt * 9f * Mathf.Sqrt(rush);
                Vector3 ab = (b - a) * dir;
                face = Mathf.MoveTowardsAngle(face, Mathf.Atan2(ab.x, ab.z) * Mathf.Rad2Deg, dt * 360f);
                if (Mathf.Abs(targetZ - z) < 0.01f * rush) { walking = false; rush = 1f; }
                sitK = 0f;
            }
            else { face = Mathf.MoveTowardsAngle(face, sitYaw, dt * 200f); sitK = 1f; }
            root.position = Vector3.Lerp(a, b, z);
            root.rotation = Quaternion.Euler(0f, face, 0f);
            // сидя — круп опущен, передние лапы прямые; на ходу — лапы переступают
            body.localRotation = Quaternion.Euler(-28f * sitK, 0f, 0f);
            body.localPosition = new Vector3(0f, 0.14f + 0.03f * sitK, -0.02f * sitK);
            for (int i = 0; i < 4; i++)
            {
                float sw = walking ? Mathf.Sin(walkPh + (i == 0 || i == 3 ? 0f : Mathf.PI)) * 30f : 0f;
                legs[i].localRotation = Quaternion.Euler(sw + (i >= 2 ? -60f * sitK : 0f), 0f, 0f);
            }
            // голова: следит за ближайшим бойцом; умывание — кивки к лапе
            Vector3 look = Vector3.forward;
            if (fighters != null && fighters.Length > 0 && fighters[0] != null && !walking)
            {
                Vector3 d = fighters[(int)(t / 7f) % fighters.Length].transform.position - head.position;
                look = root.InverseTransformDirection(d);
            }
            float yaw = Mathf.Clamp(Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, -80f, 80f);
            float pitch = 0f;
            if (groom > 0f) { groom -= dt; yaw = 25f; pitch = 35f + Mathf.Sin(t * 14f) * 12f; }
            head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(pitch, yaw, 0f), 1f - Mathf.Exp(-dt * 5f));
            head.localPosition = new Vector3(0f, 0.27f + 0.04f * sitK, 0.17f - 0.03f * sitK);
            // хвост: сидя обвит и подёргивается кончиком, на ходу — трубой с изгибом
            tailPh += dt * (walking ? 3f : 1.4f);
            for (int i = 0; i < tail.Length; i++)
            {
                float k = i / (float)(tail.Length - 1);
                float side = walking ? Mathf.Sin(tailPh + i * 0.5f) * 8f : 22f + Mathf.Sin(tailPh * (1f + k) + i) * 10f * k * k;
                float up = walking ? (i == 0 ? -55f : 10f) : (i == 0 ? 60f : -8f);
                tail[i].localRotation = Quaternion.Euler(up, side, 0f);
            }
        }
    }
}
