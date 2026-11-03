using UnityEngine;
using UnityEngine.SceneManagement;

namespace BladePath
{
    /// <summary>
    /// Кости на столе трактира — настоящие: два кубика с физикой в отдельной маленькой физической сцене (основной бой не трогается,
    /// шаг — по unscaledDeltaTime, т.е. и при остановленном времени карты). Своя камера рисует их в RenderTexture с прозрачным фоном,
    /// экран игры (TavernDiceGame) кладёт её поверх нарисованного стола. Выпавшее — по грани, смотрящей вверх.
    /// Масштаб «настоящий»: кубик 10 см, стол ~1,2×0,7 м, обычная гравитация.
    /// </summary>
    public class TavernDiceTable : MonoBehaviour
    {
        public static TavernDiceTable Instance { get; private set; }

        public RenderTexture RT { get; private set; }
        public bool Rolling { get; private set; }
        public bool Settled { get; private set; }
        public int D1 { get; private set; }
        public int D2 { get; private set; }
        /// <summary>Сколько секунд кости уже лежат неподвижно (для паузы перед объявлением).</summary>
        public float StillFor { get; private set; }

        static readonly Vector3 Origin = new Vector3(0f, -1000f, 0f);   // далеко от арены: камера стола её не видит
        const float Size = 0.1f, Step = 1f / 120f;
        const float HalfW = 0.40f, HalfD = 0.30f;   // площадка стола, м (по бокам на арте бутылка и стаканчик — туда не катятся)

        Scene scene; PhysicsScene phys; bool ready;
        Camera cam; Light lamp;
        Rigidbody[] dice = new Rigidbody[2];
        Transform[] blobs = new Transform[2];
        float acc, rollTime;

        public static TavernDiceTable Ensure()
        {
            if (Instance != null) return Instance;
            GameObject go = new GameObject("TavernDiceTable");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<TavernDiceTable>();
            Instance.Build();
            return Instance;
        }

        public static void Release()
        {
            if (Instance == null) return;
            Instance.Teardown();
            Destroy(Instance.gameObject);
            Instance = null;
        }

        void Build()
        {
            scene = SceneManager.CreateScene("TavernDice_" + GetInstanceID(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            phys = scene.GetPhysicsScene();

            PhysicsMaterial felt = new PhysicsMaterial("dice_table") { bounciness = 0.35f, dynamicFriction = 0.45f, staticFriction = 0.6f, bounceCombine = PhysicsMaterialCombine.Average, frictionCombine = PhysicsMaterialCombine.Average };
            // стол и невидимые бортики
            Box("Table", new Vector3(0f, -0.05f, 0f), new Vector3(3f, 0.1f, 3f), felt);
            Box("WallL", new Vector3(-HalfW - 0.05f, 0.2f, 0f), new Vector3(0.1f, 0.6f, 2f), felt);
            Box("WallR", new Vector3(HalfW + 0.05f, 0.2f, 0f), new Vector3(0.1f, 0.6f, 2f), felt);
            Box("WallF", new Vector3(0f, 0.2f, HalfD + 0.05f), new Vector3(2f, 0.6f, 0.1f), felt);
            Box("WallN", new Vector3(0f, 0.2f, -HalfD - 0.05f), new Vector3(2f, 0.6f, 0.1f), felt);

            Mesh mesh = DieMesh();
            Material mat = new Material(Shader.Find("Standard"));
            mat.mainTexture = DieAtlas();
            mat.SetFloat("_Glossiness", 0.4f);
            // тени граней — тёплые: иначе ночной свет арены красил их в голубой
            mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", new Color(0.22f, 0.15f, 0.09f));
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            PhysicsMaterial bone = new PhysicsMaterial("dice_bone") { bounciness = 0.4f, dynamicFriction = 0.35f, staticFriction = 0.5f };
            Material blobMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = BlobTex(), color = new Color(0f, 0f, 0f, 0.55f) };
            for (int i = 0; i < 2; i++)
            {
                GameObject d = new GameObject("Die" + i);
                d.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer mr = d.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                BoxCollider bc = d.AddComponent<BoxCollider>(); bc.size = Vector3.one; bc.sharedMaterial = bone;
                d.transform.localScale = Vector3.one * Size;
                Rigidbody rb = d.AddComponent<Rigidbody>();
                rb.mass = 0.02f; rb.linearDamping = 0.1f; rb.angularDamping = 0.35f; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.maxAngularVelocity = 60f;
                d.transform.position = Origin + new Vector3(i == 0 ? -0.08f : 0.08f, Size * 0.5f, -0.05f);
                SceneManager.MoveGameObjectToScene(d, scene);
                dice[i] = rb;
                // мягкая тень под кубиком (тени в стороннем RT не рисуются)
                GameObject b = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(b.GetComponent<Collider>());
                b.name = "Blob" + i; b.GetComponent<MeshRenderer>().sharedMaterial = blobMat;
                b.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                SceneManager.MoveGameObjectToScene(b, scene);
                blobs[i] = b.transform;
            }

            // камера: сверху-спереди, как глаза сидящего за столом; фон прозрачный
            GameObject cg = new GameObject("DiceCam");
            cam = cg.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            // кадр подогнан под арт стола: дальний бортик площадки — по центру кадра (у края стола на арте), ближний — у низа экрана
            // (CI: раньше кости ложились у дальнего края, то есть под жуликом, и были мелкие)
            cam.fieldOfView = 40f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 4f;
            cam.allowHDR = false; cam.depth = -50;
            cam.transform.position = Origin + new Vector3(0f, 0.5f, -0.9f);
            cam.transform.rotation = Quaternion.LookRotation((Origin + new Vector3(0f, 0f, 0.3f)) - cam.transform.position, Vector3.up);
            SceneManager.MoveGameObjectToScene(cg, scene);
            int w = Mathf.Clamp(Screen.width, 640, 1600), h = Mathf.RoundToInt(w * 9f / 16f);
            RT = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "TavernDiceRT" };
            cam.targetTexture = RT;

            // тёплый свет фонаря над столом
            GameObject lg = new GameObject("DiceLamp");
            lamp = lg.AddComponent<Light>();
            lamp.type = LightType.Point; lamp.color = new Color(1f, 0.74f, 0.45f); lamp.intensity = 3.2f; lamp.range = 3f; lamp.shadows = LightShadows.None;
            lg.transform.position = Origin + new Vector3(0.15f, 0.9f, -0.1f);
            SceneManager.MoveGameObjectToScene(lg, scene);

            ready = true;
            Settled = true; D1 = 1; D2 = 1;
            Place();
        }

        void Box(string n, Vector3 local, Vector3 size, PhysicsMaterial m)
        {
            GameObject g = new GameObject(n);
            g.transform.position = Origin + local;
            BoxCollider c = g.AddComponent<BoxCollider>(); c.size = size; c.sharedMaterial = m;
            SceneManager.MoveGameObjectToScene(g, scene);
        }

        void Teardown()
        {
            ready = false;
            if (cam != null) cam.targetTexture = null;
            if (RT != null) { RT.Release(); Destroy(RT); RT = null; }
            if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        }

        void OnDestroy() { if (Instance == this) { Teardown(); Instance = null; } }

        /// <summary>Отрисовать кадр сейчас (автотест в CI без окна).</summary>
        public void RenderNow() { if (cam != null) cam.Render(); }

        /// <summary>Бросок от игрока: кубики вылетают из-под камеры вперёд-вверх с закруткой.</summary>
        public void Throw()
        {
            if (!ready) return;
            for (int i = 0; i < 2; i++)
            {
                Rigidbody rb = dice[i];
                rb.isKinematic = false;
                // из-под камеры, по дуге в середину стола; закрутка сильная — кувыркаются и разбегаются
                rb.position = Origin + new Vector3((i == 0 ? -0.08f : 0.08f) + Random.Range(-0.03f, 0.03f), 0.18f + Random.Range(0f, 0.04f), -HalfD + 0.05f);
                rb.rotation = Random.rotationUniform;
                rb.linearVelocity = new Vector3(Random.Range(-0.45f, 0.45f), Random.Range(0.7f, 1.1f), Random.Range(1.1f, 1.6f));
                rb.angularVelocity = Random.onUnitSphere * Random.Range(22f, 34f);
                rb.WakeUp();
            }
            Rolling = true; Settled = false; StillFor = 0f; rollTime = 0f;
        }

        /// <summary>Жулик «задел локтем» стол: один кубик подпрыгивает и перекатывается.</summary>
        public void Bump(int which)
        {
            if (!ready) return;
            Rigidbody rb = dice[Mathf.Clamp(which, 0, 1)];
            rb.WakeUp();
            rb.linearVelocity = new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.9f, 1.2f), Random.Range(-0.3f, 0.1f));
            rb.angularVelocity = Random.onUnitSphere * Random.Range(14f, 22f);
            Rolling = true; Settled = false; StillFor = 0f; rollTime = 0f;
        }

        void Update()
        {
            if (!ready) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (Rolling)
            {
                acc += dt;
                while (acc >= Step) { phys.Simulate(Step); acc -= Step; }
                rollTime += dt;
                bool still = true;
                foreach (Rigidbody rb in dice) if (rb.linearVelocity.sqrMagnitude > 0.0009f || rb.angularVelocity.sqrMagnitude > 0.04f) still = false;
                StillFor = still ? StillFor + dt : 0f;
                if ((StillFor > 0.25f && rollTime > 0.4f) || rollTime > 5f)
                {
                    // кубик встал на ребро (упёрся в другой) — подтолкнуть
                    for (int i = 0; i < 2; i++)
                        if (TopDot(dice[i].transform) < 0.92f && rollTime < 5f) { dice[i].AddTorque(Random.onUnitSphere * 0.002f, ForceMode.Impulse); dice[i].AddForce(Vector3.up * 0.015f, ForceMode.Impulse); StillFor = 0f; }
                    if (StillFor > 0.25f || rollTime > 5f)
                    {
                        Rolling = false; Settled = true;
                        D1 = TopValue(dice[0].transform); D2 = TopValue(dice[1].transform);
                    }
                }
            }
            else if (Settled) StillFor += dt;
            Place();
        }

        void Place()
        {
            for (int i = 0; i < 2; i++)
            {
                if (dice[i] == null || blobs[i] == null) continue;
                Vector3 p = dice[i].position; float hgt = Mathf.Max(0f, p.y - Origin.y - Size * 0.5f);
                blobs[i].position = new Vector3(p.x, Origin.y + 0.001f, p.z);
                blobs[i].localScale = Vector3.one * Size * (1.5f + hgt * 3f);
            }
        }

        // ---------------- грани: +Y 1, −Y 6, +Z 2, −Z 5, +X 3, −X 4 (противоположные — 7 в сумме)
        static readonly Vector3[] FaceDir = { Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
        static readonly int[] FaceVal = { 1, 6, 2, 5, 3, 4 };

        static int TopValue(Transform t)
        {
            int best = 0; float bd = -2f;
            for (int i = 0; i < 6; i++) { float d = Vector3.Dot(t.rotation * FaceDir[i], Vector3.up); if (d > bd) { bd = d; best = i; } }
            return FaceVal[best];
        }
        static float TopDot(Transform t)
        {
            float bd = -2f;
            for (int i = 0; i < 6; i++) bd = Mathf.Max(bd, Vector3.Dot(t.rotation * FaceDir[i], Vector3.up));
            return bd;
        }

        static Mesh DieMesh()
        {
            Mesh m = new Mesh { name = "Die" };
            Vector3[] v = new Vector3[24]; Vector3[] nrm = new Vector3[24]; Vector2[] uv = new Vector2[24]; int[] tri = new int[36];
            for (int f = 0; f < 6; f++)
            {
                Vector3 n = FaceDir[f];
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.forward : Vector3.up;
                Vector3 r = Vector3.Cross(u, n);
                int cell = FaceVal[f] - 1; float cu = (cell % 3) / 3f, cv = (cell / 3) / 2f;
                Vector3 c = n * 0.5f;
                Vector3[] q = { c - r * 0.5f - u * 0.5f, c - r * 0.5f + u * 0.5f, c + r * 0.5f + u * 0.5f, c + r * 0.5f - u * 0.5f };
                Vector2[] qu = { new Vector2(cu, cv), new Vector2(cu, cv + 0.5f), new Vector2(cu + 1f / 3f, cv + 0.5f), new Vector2(cu + 1f / 3f, cv) };
                for (int k = 0; k < 4; k++) { v[f * 4 + k] = q[k]; nrm[f * 4 + k] = n; uv[f * 4 + k] = qu[k]; }
                int b = f * 4;
                // лицевая сторона в Unity — по часовой, нормаль Cross(v1−v0, v2−v0) к наблюдателю
                bool ok = Vector3.Dot(Vector3.Cross(q[1] - q[0], q[2] - q[0]), n) > 0f;
                int[] t = ok ? new[] { b, b + 1, b + 2, b, b + 2, b + 3 } : new[] { b, b + 2, b + 1, b, b + 3, b + 2 };
                System.Array.Copy(t, 0, tri, f * 6, 6);
            }
            m.vertices = v; m.normals = nrm; m.uv = uv; m.triangles = tri; m.RecalculateBounds();
            return m;
        }

        /// <summary>Атлас граней 3×2: слоновая кость, тёмная кромка (фаска), чёрные точки, «1» — крупная красная (японские кости).</summary>
        static Texture2D DieAtlas()
        {
            const int C = 128;
            Texture2D t = new Texture2D(C * 3, C * 2, TextureFormat.RGBA32, true) { name = "DieAtlas", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            Color ivory = new Color(0.93f, 0.88f, 0.78f), edge = new Color(0.62f, 0.55f, 0.45f), ink = new Color(0.08f, 0.06f, 0.05f), red = new Color(0.72f, 0.08f, 0.06f);
            float[][] pips = {
                new[] { 0.5f, 0.5f },
                new[] { 0.27f, 0.27f, 0.73f, 0.73f },
                new[] { 0.25f, 0.25f, 0.5f, 0.5f, 0.75f, 0.75f },
                new[] { 0.27f, 0.27f, 0.73f, 0.27f, 0.27f, 0.73f, 0.73f, 0.73f },
                new[] { 0.25f, 0.25f, 0.75f, 0.25f, 0.5f, 0.5f, 0.25f, 0.75f, 0.75f, 0.75f },
                new[] { 0.27f, 0.22f, 0.27f, 0.5f, 0.27f, 0.78f, 0.73f, 0.22f, 0.73f, 0.5f, 0.73f, 0.78f },
            };
            Color[] px = new Color[C * 3 * C * 2];
            for (int cell = 0; cell < 6; cell++)
            {
                int ox = (cell % 3) * C, oy = (cell / 3) * C;
                float pr = cell == 0 ? 0.17f : 0.085f;
                for (int y = 0; y < C; y++)
                    for (int x = 0; x < C; x++)
                    {
                        float u = (x + 0.5f) / C, v = (y + 0.5f) / C;
                        float e = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                        Color col = Color.Lerp(edge, ivory, Mathf.SmoothStep(0f, 1f, e / 0.12f));
                        float[] p = pips[cell];
                        for (int k = 0; k < p.Length; k += 2)
                        {
                            float d = Vector2.Distance(new Vector2(u, v), new Vector2(p[k], p[k + 1]));
                            if (d < pr + 0.012f)
                            {
                                float a = Mathf.Clamp01((pr + 0.012f - d) / 0.024f);
                                Color pc = cell == 0 ? red : ink;
                                if (d < pr) pc = Color.Lerp(pc, pc * 0.6f, d / pr);   // точки чуть утоплены
                                col = Color.Lerp(col, pc, a);
                            }
                        }
                        col.a = 1f;
                        px[(oy + y) * C * 3 + ox + x] = col;
                    }
            }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        static Texture2D BlobTex()
        {
            const int N = 64;
            Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N * 0.5f, N * 0.5f)) / (N * 0.5f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d)));
                }
            t.Apply();
            return t;
        }
    }
}
