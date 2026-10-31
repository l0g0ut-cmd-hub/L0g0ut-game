using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Живность над двором (всё строится при запуске, сцене нужен только компонент):
    ///  • ворона прилетает и садится на тории или конёк храма, переминается, крутит головой, каркает — и срывается,
    ///    когда бойцы подходят близко или просто надоело;
    ///  • днём воробьи прыгают и клюют у ступеней храма, вспархивают от шагов бойцов и через время возвращаются;
    ///  • днём и на закате высоко проходит клин гусей, на рассвете над прудом медленно пролетает цапля;
    ///  • ночью у фонарей и тории мечутся летучие мыши.
    /// Птицы — процедурные: тело-веретено и два крыла на шарнирах (взмахи, планирование, сложенные крылья).
    /// </summary>
    public class Birds : MonoBehaviour
    {
        enum Kind { Crow, Sparrow, Goose, Heron, Bat }
        enum State { Away, Fly, Land, Perch, Ground, Takeoff }

        class Bird
        {
            public Kind kind; public State state;
            public Transform root, body, wingL, wingR;
            public Vector3 pos, vel, from, to, perch; public float t, dur, flap, flapRate, timer, hop, size, heading;
            public float glide;   // 0 — машет, 1 — планирует
        }

        readonly List<Bird> birds = new List<Bird>();
        Material dark, brown, batMat;
        static Mesh bodyMesh, wingMesh;
        AudioSource src;
        static AudioClip caw, chirp, flutter;
        FighterController[] fighters;
        Bird crow, heron;
        readonly List<Bird> sparrows = new List<Bird>(), geese = new List<Bird>(), bats = new List<Bird>();
        float crowNext, geeseNext, heronNext, sparrowBack, chirpNext;
        static int startle; int seenStartle; bool startled;
        /// <summary>Громкое во дворе (добивание, сломанная стойка, раскат грома): сидящие птицы разом срываются.</summary>
        public static void Startle() { startle++; }
        public static int StartleCount { get { return startle; } }

        static readonly Vector3[] Perches =
        {
            new Vector3(1.0f, 2.81f, 3.6f), new Vector3(1.7f, 2.81f, 3.6f), new Vector3(2.45f, 2.81f, 3.6f),   // касаги тории
            new Vector3(-2.6f, 3.97f, 6.0f), new Vector3(-0.8f, 3.97f, 6.0f), new Vector3(1.2f, 3.97f, 6.0f),  // конёк храма
            new Vector3(-6.4f, 1.23f, 1.5f), new Vector3(6.4f, 1.23f, -1.0f)                                  // колпаки стен
        };

        void Start()
        {
            Shader sh = Shader.Find("Standard");
            dark = new Material(sh) { name = "BirdDark", color = new Color(0.035f, 0.035f, 0.04f) }; dark.SetFloat("_Glossiness", 0.25f);
            brown = new Material(sh) { name = "BirdBrown", color = new Color(0.32f, 0.22f, 0.14f) }; brown.SetFloat("_Glossiness", 0.1f);
            batMat = new Material(sh) { name = "Bat", color = new Color(0.05f, 0.04f, 0.045f) }; batMat.SetFloat("_Glossiness", 0.05f);
            if (bodyMesh == null) BuildMeshes();
            src = gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0.5f; src.minDistance = 4f; src.maxDistance = 40f;
            if (caw == null) caw = Caw();
            if (chirp == null) chirp = Chirp();
            if (flutter == null) flutter = Flutter();
            fighters = FindObjectsByType<FighterController>(FindObjectsSortMode.None);
            crow = Make(Kind.Crow, 0.42f, dark);
            heron = Make(Kind.Heron, 0.95f, dark);
            for (int i = 0; i < 4; i++) sparrows.Add(Make(Kind.Sparrow, 0.13f, brown));
            for (int i = 0; i < 7; i++) geese.Add(Make(Kind.Goose, 0.75f, dark));
            for (int i = 0; i < 3; i++) bats.Add(Make(Kind.Bat, 0.26f, batMat));
            crowNext = Time.time + Random.Range(4f, 12f);
            geeseNext = Time.time + Random.Range(15f, 40f);
            heronNext = Time.time + Random.Range(10f, 25f);
            sparrowBack = Time.time + Random.Range(1f, 4f);
        }

        // ---------- построение ----------

        static void BuildMeshes()
        {
            // тело: веретено (клюв вперёд, хвост назад, сверху чуть горб)
            bodyMesh = new Mesh { name = "BirdBody" };
            Vector3[] v = { new Vector3(0, 0, 0.55f), new Vector3(0, 0, -0.5f), new Vector3(0.14f, 0, 0.05f), new Vector3(-0.14f, 0, 0.05f), new Vector3(0, 0.12f, 0.1f), new Vector3(0, -0.1f, 0.05f),
                            new Vector3(0.1f, 0, -0.55f), new Vector3(-0.1f, 0, -0.55f) };
            int[] t = { 0, 2, 4, 0, 4, 3, 0, 5, 2, 0, 3, 5, 1, 4, 2, 1, 3, 4, 1, 2, 5, 1, 5, 3, 1, 6, 7, 1, 7, 6 };
            bodyMesh.vertices = v; bodyMesh.triangles = t; bodyMesh.RecalculateNormals(); bodyMesh.RecalculateBounds();
            // крыло: от плеча (0,0,0) вбок по +X, стреловидное, с маховыми перьями по заднему краю
            wingMesh = new Mesh { name = "BirdWing" };
            Vector3[] w = { new Vector3(0, 0, 0.18f), new Vector3(0.55f, 0.02f, 0.08f), new Vector3(1f, 0, -0.18f), new Vector3(0.8f, 0, -0.3f), new Vector3(0.45f, 0, -0.3f), new Vector3(0, 0, -0.16f) };
            int[] wt = { 0, 1, 5, 1, 4, 5, 1, 2, 3, 1, 3, 4, 5, 1, 0, 5, 4, 1, 4, 3, 1, 3, 2, 1 };   // двусторонне
            wingMesh.vertices = w; wingMesh.triangles = wt; wingMesh.RecalculateNormals(); wingMesh.RecalculateBounds();
        }

        Bird Make(Kind k, float size, Material m)
        {
            Bird b = new Bird { kind = k, size = size, state = State.Away };
            b.root = new GameObject("Bird_" + k).transform; b.root.SetParent(transform, false);
            b.body = Part(b.root, "Body", bodyMesh, m, Vector3.zero);
            float span = k == Kind.Bat ? 0.75f : k == Kind.Goose || k == Kind.Heron ? 1.15f : 0.9f;
            b.wingR = Part(b.root, "WingR", wingMesh, m, new Vector3(0.08f, 0.03f, 0.08f));
            b.wingL = Part(b.root, "WingL", wingMesh, m, new Vector3(-0.08f, 0.03f, 0.08f));
            b.wingR.localScale = new Vector3(span, 1f, 1f); b.wingL.localScale = new Vector3(-span, 1f, 1f);
            if (k == Kind.Heron) { b.body.localScale = new Vector3(0.8f, 0.8f, 1.6f); }
            b.root.localScale = Vector3.one * size;
            b.flapRate = k == Kind.Sparrow ? 14f : k == Kind.Bat ? 11f : k == Kind.Crow ? 4.5f : k == Kind.Goose ? 3.2f : 2.2f;
            b.flap = Random.value * 6f;
            b.root.gameObject.SetActive(false);
            birds.Add(b);
            return b;
        }

        static Transform Part(Transform parent, string name, Mesh mesh, Material m, Vector3 pos)
        {
            GameObject g = new GameObject(name);
            g.transform.SetParent(parent, false); g.transform.localPosition = pos;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g.transform;
        }

        // ---------- поведение ----------

        bool Night { get { return DayNight.Instance == null || !DayNight.Instance.Day; } }
        bool Dawn { get { return DayNight.Instance != null && DayNight.Instance.Dawn; } }
        bool Dusk { get { return DayNight.Instance != null && DayNight.Instance.Dusk; } }
        bool Wet { get { Weather w = Weather.Instance; return w != null && (w.Raining || w.Snowing); } }

        float NearestFighter(Vector3 p)
        {
            float best = 99f;
            if (fighters != null) foreach (FighterController f in fighters) if (f != null) best = Mathf.Min(best, Vector3.Distance(Flat(f.transform.position), Flat(p)));
            return best;
        }
        static Vector3 Flat(Vector3 p) { p.y = 0f; return p; }

        void Update()
        {
            float dt = Time.deltaTime, now = Time.time;
            startled = startle != seenStartle; seenStartle = startle;
            UpdateCrow(dt, now);
            UpdateSparrows(dt, now);
            UpdateGeese(dt, now);
            UpdateHeron(dt, now);
            UpdateBats(dt, now);
        }

        void Show(Bird b, bool on) { if (b.root.gameObject.activeSelf != on) b.root.gameObject.SetActive(on); }

        /// <summary>Полёт по дуге from → to за dur (провис/подъём sag), машет с паузами-планированием.</summary>
        void FlyArc(Bird b, float dt, float sag)
        {
            b.t += dt / b.dur;
            float u = Mathf.Clamp01(b.t);
            Vector3 p = Vector3.Lerp(b.from, b.to, u) + Vector3.up * sag * 4f * u * (1f - u);
            Vector3 v = (p - b.pos) / Mathf.Max(dt, 1e-4f);
            b.pos = p; if (v.sqrMagnitude > 1e-4f) b.vel = v;
            Pose(b, dt, Mathf.PerlinNoise(b.flap * 0.1f, 3f) > 0.62f ? 1f : 0f);
        }

        void Pose(Bird b, float dt, float glideTarget, bool folded = false)
        {
            b.glide = Mathf.MoveTowards(b.glide, glideTarget, dt * 3f);
            b.flap += dt * b.flapRate * Mathf.PI * 2f * (1f - 0.8f * b.glide);
            float a;
            if (folded) { b.wingR.localRotation = Quaternion.Euler(10f, -78f, -12f); b.wingL.localRotation = Quaternion.Euler(10f, 78f, 12f); }
            else
            {
                a = Mathf.Lerp(Mathf.Sin(b.flap) * (b.kind == Kind.Bat ? 60f : 48f), 8f, b.glide);
                b.wingR.localRotation = Quaternion.Euler(0f, 0f, a); b.wingL.localRotation = Quaternion.Euler(0f, 0f, -a);
            }
            b.root.position = b.pos;
            Vector3 dir = b.vel; dir.y *= 0.5f;
            if (!folded && dir.sqrMagnitude > 1e-4f) b.root.rotation = Quaternion.Slerp(b.root.rotation, Quaternion.LookRotation(dir.normalized, Vector3.up), 1f - Mathf.Exp(-dt * 8f));
            // при взмахе тело чуть подпрыгивает
            if (!folded) b.body.localPosition = Vector3.up * Mathf.Cos(b.flap) * 0.03f * (1f - b.glide);
        }

        void Play(AudioClip c, Vector3 at, float vol, float pitch = 1f)
        {
            if (src == null || c == null) return;
            src.transform.position = at; src.pitch = pitch * Random.Range(0.94f, 1.06f);
            src.PlayOneShot(c, vol);
        }

        void UpdateCrow(float dt, float now)
        {
            Bird b = crow;
            switch (b.state)
            {
                case State.Away:
                    Show(b, false);
                    if (now >= crowNext && !Wet && !Dawn)
                    {
                        b.perch = Perches[Random.Range(0, Perches.Length)];
                        float side = Random.value < 0.5f ? -1f : 1f;
                        b.from = new Vector3(side * 16f, b.perch.y + Random.Range(4f, 7f), b.perch.z + Random.Range(4f, 10f));
                        b.to = b.perch + new Vector3(-side * 0.6f, 0.35f, 0f);
                        b.pos = b.from; b.t = 0f; b.dur = Vector3.Distance(b.from, b.to) / 5.5f;
                        b.state = State.Fly; Show(b, true);
                    }
                    break;
                case State.Fly:
                    FlyArc(b, dt, -1.5f);
                    if (b.t >= 1f) { b.state = State.Land; b.t = 0f; b.from = b.pos; }
                    break;
                case State.Land:
                    // хлопает часто, гасит скорость, садится
                    b.t += dt / 0.6f;
                    b.pos = Vector3.Lerp(b.from, b.perch, Mathf.SmoothStep(0f, 1f, b.t));
                    b.vel = Vector3.Lerp(b.vel, (b.perch - b.from).normalized * 0.3f, dt * 6f);
                    b.flapRate = 8f; Pose(b, dt, 0f);
                    if (b.t >= 1f)
                    {
                        b.state = State.Perch; b.timer = Random.Range(14f, 40f); b.flapRate = 4.5f; b.heading = Random.Range(0f, 360f);
                        Play(caw, b.pos, 0.35f);
                    }
                    break;
                case State.Perch:
                    b.timer -= dt; b.pos = b.perch;
                    // переминается, вертит головой, изредка каркает
                    b.heading += Mathf.Sin(now * 0.7f + 3f) * dt * 40f;
                    if (Random.value < dt * 0.25f) b.heading += Random.Range(-70f, 70f);
                    b.root.position = b.pos;
                    b.root.rotation = Quaternion.Slerp(b.root.rotation, Quaternion.Euler(-14f + Mathf.Sin(now * 5f) * (Random.value < 0.02f ? 20f : 0f), b.heading, 0f), 1f - Mathf.Exp(-dt * 6f));
                    Pose(b, dt, 0f, true);
                    b.root.position = b.pos; b.body.localPosition = Vector3.zero;
                    if (Random.value < dt * 0.08f) Play(caw, b.pos, 0.3f, Random.Range(0.9f, 1.05f));
                    if (b.timer <= 0f || NearestFighter(b.perch) < 2.2f || Wet || startled)
                    {
                        if (startled) Play(caw, b.pos, 0.4f, 1.12f);   // испуганное «кар!»
                        b.state = State.Takeoff; b.t = 0f; b.from = b.pos;
                        float side = Random.value < 0.5f ? -1f : 1f;
                        b.to = new Vector3(side * 18f, b.pos.y + Random.Range(6f, 10f), b.pos.z + Random.Range(6f, 14f));
                        b.dur = Vector3.Distance(b.from, b.to) / 6.5f;
                        Play(flutter, b.pos, 0.35f); if (Random.value < 0.6f) Play(caw, b.pos, 0.32f, 1.05f);
                    }
                    break;
                case State.Takeoff:
                    b.flapRate = Mathf.MoveTowards(b.flapRate, 4.5f, dt * 4f) + (b.t < 0.15f ? 4f : 0f);
                    FlyArc(b, dt, 1.2f);
                    if (b.t >= 1f) { b.state = State.Away; crowNext = now + Random.Range(25f, 60f); Show(b, false); }
                    break;
            }
        }

        void UpdateSparrows(float dt, float now)
        {
            bool day = !Night && !Wet;
            foreach (Bird b in sparrows)
            {
                switch (b.state)
                {
                    case State.Away:
                        Show(b, false);
                        if (day && now >= sparrowBack)
                        {
                            // прилетают стайкой к ступеням храма или к краю двора
                            Vector3 spot = new Vector3(Random.Range(-3.2f, 3.2f), 0f, Random.Range(3.15f, 3.9f));
                            if (Random.value < 0.35f) spot = new Vector3(Random.Range(-5.6f, -4.2f) * (Random.value < 0.5f ? 1f : -1f), -0.07f, Random.Range(-2f, 2f));
                            b.perch = spot; b.from = spot + new Vector3(Random.Range(-6f, 6f), Random.Range(3f, 5f), Random.Range(4f, 8f));
                            b.to = spot; b.pos = b.from; b.t = Random.Range(-0.4f, 0f); b.dur = 1.6f; b.state = State.Fly; Show(b, true);
                        }
                        break;
                    case State.Fly:
                        if (b.t < 0f) { b.t += dt; Show(b, false); break; }
                        Show(b, true);
                        FlyArc(b, dt, -0.6f);
                        if (b.t >= 1f) { b.state = State.Ground; b.timer = Random.Range(0.3f, 1.2f); b.hop = 0f; b.heading = Random.Range(0f, 360f); }
                        break;
                    case State.Ground:
                        // прыжок-прыжок-клевок
                        b.timer -= dt;
                        if (b.timer <= 0f)
                        {
                            b.timer = Random.Range(0.35f, 1.4f);
                            if (Random.value < 0.6f) { b.heading += Random.Range(-90f, 90f); b.hop = 1f; b.vel = Quaternion.Euler(0f, b.heading, 0f) * Vector3.forward * Random.Range(0.25f, 0.5f); }
                            else b.hop = -1f;   // клюёт
                            if (Random.value < 0.25f && now > chirpNext) { chirpNext = now + 0.6f; Play(chirp, b.pos, 0.18f, Random.Range(0.95f, 1.15f)); }
                        }
                        float y = 0f;
                        if (b.hop > 0f) { b.hop = Mathf.Max(0f, b.hop - dt / 0.22f); b.perch += b.vel * dt; y = Mathf.Sin(b.hop * Mathf.PI) * 0.05f; }
                        float peck = b.hop < 0f ? Mathf.Sin(Mathf.Clamp01(-b.hop) * Mathf.PI) : 0f;
                        if (b.hop < 0f) b.hop = Mathf.Min(0f, b.hop + dt / 0.25f);
                        b.perch.x = Mathf.Clamp(b.perch.x, -6f, 6f); b.perch.z = Mathf.Clamp(b.perch.z, -7f, 4.1f);
                        b.pos = b.perch + Vector3.up * (y + 0.035f);
                        b.root.position = b.pos;
                        b.root.rotation = Quaternion.Euler(-10f + 40f * peck, b.heading, 0f);
                        Pose(b, dt, 0f, true); b.root.position = b.pos; b.body.localPosition = Vector3.zero;
                        if (NearestFighter(b.pos) < 2.0f || !day || startled)
                        {
                            // вспорхнули от шагов
                            b.state = State.Takeoff; b.t = 0f; b.from = b.pos;
                            b.to = b.pos + new Vector3(Random.Range(-8f, 8f), Random.Range(4f, 7f), Random.Range(5f, 10f)); b.dur = Random.Range(1.2f, 1.8f);
                            if (Random.value < 0.5f) Play(flutter, b.pos, 0.22f, 1.6f);
                        }
                        break;
                    case State.Takeoff:
                        FlyArc(b, dt, 0.8f);
                        if (b.t >= 1f) { b.state = State.Away; Show(b, false); sparrowBack = now + Random.Range(18f, 40f); }
                        break;
                }
            }
        }

        void UpdateGeese(float dt, float now)
        {
            bool any = false;
            for (int i = 0; i < geese.Count; i++)
            {
                Bird b = geese[i];
                if (b.state == State.Fly)
                {
                    any = true;
                    FlyArc(b, dt, 1.5f);
                    b.glide = 0f;
                    if (b.t >= 1f) { b.state = State.Away; Show(b, false); }
                }
            }
            if (!any && now >= geeseNext)
            {
                geeseNext = now + Random.Range(60f, 120f);
                if (Night || Dawn || Wet) return;
                // клин: вожак впереди, по бокам уступами
                float side = Random.value < 0.5f ? -1f : 1f, alt = Random.Range(13f, 18f), z = Random.Range(18f, 30f);
                Vector3 a = new Vector3(side * 40f, alt, z), c = new Vector3(-side * 40f, alt + Random.Range(-2f, 2f), z + Random.Range(-6f, 6f));
                Vector3 dir = (c - a).normalized, right = Vector3.Cross(Vector3.up, dir);
                for (int i = 0; i < geese.Count; i++)
                {
                    Bird b = geese[i];
                    int rank = (i + 1) / 2; float s = i == 0 ? 0f : (i % 2 == 0 ? 1f : -1f);
                    Vector3 off = -dir * rank * 1.6f + right * s * rank * 1.4f;
                    b.from = a + off; b.to = c + off; b.pos = b.from; b.t = 0f; b.dur = 26f + Random.Range(-0.3f, 0.3f);
                    b.flap = i * 0.7f; b.state = State.Fly; Show(b, true);
                }
            }
        }

        void UpdateHeron(float dt, float now)
        {
            Bird b = heron;
            if (b.state == State.Fly)
            {
                FlyArc(b, dt, 0.6f);
                if (b.t >= 1f) { b.state = State.Away; Show(b, false); }
                return;
            }
            Show(b, false);
            if (now < heronNext) return;
            heronNext = now + Random.Range(50f, 90f);
            if (!(Dawn || Dusk) || Wet) return;
            // медленно, низко — над прудом у ворот и дальше, к роще
            float side = Random.value < 0.5f ? -1f : 1f;
            b.from = new Vector3(side * 22f, Random.Range(5f, 7f), Random.Range(-2f, 8f));
            b.to = new Vector3(-side * 22f, Random.Range(7f, 10f), Random.Range(10f, 18f));
            b.pos = b.from; b.t = 0f; b.dur = 18f; b.state = State.Fly; Show(b, true);
        }

        void UpdateBats(float dt, float now)
        {
            bool on = Night && !Wet;
            foreach (Bird b in bats)
            {
                if (!on) { Show(b, false); b.state = State.Away; continue; }
                if (b.state == State.Away)
                {
                    b.state = State.Fly; b.pos = new Vector3(Random.Range(-4f, 4f), Random.Range(3f, 4.5f), Random.Range(2f, 5f)); b.to = b.pos; b.timer = 0f; Show(b, true);
                }
                // рваный полёт: новая точка каждые 0,4–1 с у тории и фонарей, резкие развороты
                b.timer -= dt;
                if (b.timer <= 0f)
                {
                    b.timer = Random.Range(0.4f, 1.0f);
                    b.to = new Vector3(Random.Range(-4.5f, 4.5f), Random.Range(2.6f, 4.8f), Random.Range(1.8f, 6f));
                }
                Vector3 want = (b.to - b.pos).normalized * 4.2f;
                b.vel = Vector3.Lerp(b.vel, want, 1f - Mathf.Exp(-dt * 4f));
                b.pos += b.vel * dt + new Vector3(0f, Mathf.Sin(now * 9f + b.flap) * 0.6f * dt, 0f);
                Pose(b, dt, 0f);
            }
        }

        // ---------- звуки (синтез) ----------

        const int Rate = 22050;

        /// <summary>Карканье: хриплое «кар» ~700 Гц с шумом и спадом высоты, 0,35 с.</summary>
        static AudioClip Caw()
        {
            int n = (int)(Rate * 0.4f); float[] d = new float[n];
            System.Random r = new System.Random(3); float ph = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, f = Mathf.Lerp(760f, 560f, t / 0.4f);
                ph += 2f * Mathf.PI * f / Rate;
                float x = (float)(r.NextDouble() * 2.0 - 1.0); lp += 0.35f * (x - lp);
                float saw = (ph / Mathf.PI % 2f) - 1f;
                float env = Mathf.Min(1f, t * 60f) * Mathf.Pow(1f - t / 0.4f, 1.5f);
                d[i] = (saw * 0.6f + lp * 0.8f) * env * (0.7f + 0.3f * Mathf.Sin(t * 2f * Mathf.PI * 38f));
            }
            return Clip("crow_caw", d);
        }

        /// <summary>Воробьиное «чик-чирик»: два-три коротких свиста 4–6 кГц со скольжением.</summary>
        static AudioClip Chirp()
        {
            int n = (int)(Rate * 0.32f); float[] d = new float[n]; float ph = 0f;
            float[] st = { 0f, 0.09f, 0.17f }, len = { 0.05f, 0.05f, 0.11f }, f0 = { 5200f, 5600f, 4300f }, f1 = { 4400f, 4800f, 5900f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, a = 0f, f = 5000f;
                for (int k = 0; k < 3; k++) { float u = (t - st[k]) / len[k]; if (u < 0f || u > 1f) continue; a = Mathf.Sin(Mathf.PI * u); f = Mathf.Lerp(f0[k], f1[k], u); }
                ph += 2f * Mathf.PI * f / Rate; d[i] = Mathf.Sin(ph) * a * 0.8f;
            }
            return Clip("sparrow_chirp", d);
        }

        /// <summary>Хлопанье крыльев при взлёте: пачка глухих шумовых ударов, 0,5 с.</summary>
        static AudioClip Flutter()
        {
            int n = (int)(Rate * 0.5f); float[] d = new float[n];
            System.Random r = new System.Random(8); float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = (float)(r.NextDouble() * 2.0 - 1.0); lp += 0.18f * (x - lp);
                float beat = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 2f * Mathf.PI * 9f)), 6f);
                d[i] = lp * beat * (1f - t / 0.5f) * 2.2f;
            }
            return Clip("wing_flutter", d);
        }

        static AudioClip Clip(string name, float[] d)
        {
            float peak = 1e-4f; foreach (float v in d) peak = Mathf.Max(peak, Mathf.Abs(v));
            for (int i = 0; i < d.Length; i++) d[i] *= 0.85f / peak;
            AudioClip c = AudioClip.Create(name, d.Length, 1, Rate, false); c.SetData(d, 0); return c;
        }
    }
}
