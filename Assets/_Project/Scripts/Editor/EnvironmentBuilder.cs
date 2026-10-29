#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Окружение арены: ночь, туман, лунный свет, каменная площадка, ворота тории, пагода, жаровни с огнём и лепестки.
    /// Всё из примитивов, без запекания света: реалтайм-свет Built-in конвейера.
    /// </summary>
    public static class EnvironmentBuilder
    {
        public static readonly Color FogColor = new Color(0.07f, 0.08f, 0.12f);

        public static void Build(Look look)
        {
            reflRoot = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.05f;
            RenderSettings.fogColor = FogColor;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.17f, 0.18f, 0.26f);

            if (look.sky != null) RenderSettings.skybox = look.sky;

            // Realtime ReflectionProbe убран: на части Android-драйверов рендер пробы при загрузке сцены роняет приложение.

            // лунный свет
            GameObject moonLight = new GameObject("Moonlight");
            Light l = moonLight.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(0.62f, 0.70f, 1f);
            l.intensity = 0.95f;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.8f;
            moonLight.transform.rotation = Quaternion.Euler(38f, -35f, 0f);

            // площадка (с коллайдером) + каменный бордюр
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "ArenaFloor";
            floor.transform.position = new Vector3(0f, -0.05f, 0f);
            floor.transform.localScale = new Vector3(7.2f, 0.1f, 5.4f);
            floor.GetComponent<Renderer>().sharedMaterial = look.stone;

            Transform env = new GameObject("Environment").transform;
            // лужи на камне арены: в них отражаются огни и небо
            System.Random pr = new System.Random(5);
            for (int i = 0; i < 7; i++)
            {
                Vector3 pp = new Vector3((float)(pr.NextDouble() - 0.5) * 6.4f, 0.004f + i * 0.0005f, (float)(pr.NextDouble() - 0.5) * 4.6f);
                float sz = 0.6f + (float)pr.NextDouble() * 1.1f;
                EnvMesh.Box(env, "Puddle" + i, pp, new Vector3(0f, (float)pr.NextDouble() * 360f, 0f), new Vector3(sz * 1.3f, 0.002f, sz), look.puddle, 0f);
            }
            EnvMesh.Box(env, "Gravel", new Vector3(0f, -0.12f, 6f), Vector3.zero, new Vector3(48f, 0.1f, 40f), look.gravel, 2.2f);
            EnvMesh.Box(env, "CurbBack", new Vector3(0f, 0.04f, 2.85f), Vector3.zero, new Vector3(7.8f, 0.1f, 0.3f), look.stoneBlock, 1.5f);
            EnvMesh.Box(env, "CurbFront", new Vector3(0f, 0.04f, -2.85f), Vector3.zero, new Vector3(7.8f, 0.1f, 0.3f), look.stoneBlock, 1.5f);
            EnvMesh.Box(env, "CurbL", new Vector3(-3.75f, 0.04f, 0f), Vector3.zero, new Vector3(0.3f, 0.1f, 5.4f), look.stoneBlock, 1.5f);
            EnvMesh.Box(env, "CurbR", new Vector3(3.75f, 0.04f, 0f), Vector3.zero, new Vector3(0.3f, 0.1f, 5.4f), look.stoneBlock, 1.5f);

            // ворота тории на заднем плане
            Transform torii = new GameObject("Torii").transform;
            torii.SetParent(env, false);
            CharacterVisuals.Part(torii, "PillarL", PrimitiveType.Cylinder, new Vector3(0.7f, 1.3f, 3.6f), Vector3.zero, new Vector3(0.22f, 1.3f, 0.22f), look.lacquer);
            CharacterVisuals.Part(torii, "PillarR", PrimitiveType.Cylinder, new Vector3(2.7f, 1.3f, 3.6f), Vector3.zero, new Vector3(0.22f, 1.3f, 0.22f), look.lacquer);
            EnvMesh.Box(torii, "BaseL", new Vector3(0.7f, 0.08f, 3.6f), Vector3.zero, new Vector3(0.42f, 0.16f, 0.42f), look.stoneBlock, 1.2f);
            EnvMesh.Box(torii, "BaseR", new Vector3(2.7f, 0.08f, 3.6f), Vector3.zero, new Vector3(0.42f, 0.16f, 0.42f), look.stoneBlock, 1.2f);
            // касаги с загнутыми вверх концами
            EnvMesh.Box(torii, "Kasagi", new Vector3(1.7f, 2.72f, 3.6f), Vector3.zero, new Vector3(2.5f, 0.16f, 0.28f), look.woodDark, 1f, true);
            EnvMesh.Box(torii, "KasagiL", new Vector3(1.7f - 1.48f, 2.76f, 3.6f), new Vector3(0f, 0f, -9f), new Vector3(0.5f, 0.16f, 0.28f), look.woodDark, 1f, true);
            EnvMesh.Box(torii, "KasagiR", new Vector3(1.7f + 1.48f, 2.76f, 3.6f), new Vector3(0f, 0f, 9f), new Vector3(0.5f, 0.16f, 0.28f), look.woodDark, 1f, true);
            EnvMesh.Box(torii, "Gakuzuka", new Vector3(1.7f, 2.43f, 3.6f), Vector3.zero, new Vector3(0.14f, 0.3f, 0.12f), look.lacquer, 1f);
            EnvMesh.Box(torii, "Shimaki", new Vector3(1.7f, 2.60f, 3.6f), Vector3.zero, new Vector3(3.1f, 0.07f, 0.24f), look.lacquer, 1f, true);
            EnvMesh.Box(torii, "Nuki", new Vector3(1.7f, 2.25f, 3.6f), Vector3.zero, new Vector3(2.2f, 0.12f, 0.16f), look.lacquer, 1f, true);

            BuildHall(env, look);
            BuildGarden(env, look);
            BuildPagoda(env, look);
            BuildWalls(env, look);

            // каменные фонари у бордюра
            AddLantern(env, look, new Vector3(-4.5f, 0f, -1.6f));
            AddLantern(env, look, new Vector3(4.5f, 0f, -1.6f));
            AddLantern(env, look, new Vector3(-4.5f, 0f, 2.2f));
            AddLantern(env, look, new Vector3(4.5f, 0f, 2.2f));

            // сакуры
            AddTree(env, look, new Vector3(5.4f, 0f, 3.4f), 1.25f, 1);
            AddTree(env, look, new Vector3(-5.5f, 0f, 4.4f), 1.4f, 2);
            AddTree(env, look, new Vector3(5.6f, 0f, -0.6f), 1.0f, 3);
            AddTree(env, look, new Vector3(-5.4f, 0f, -1.0f), 1.1f, 4);
            AddTree(env, look, new Vector3(4.6f, 0f, 8.2f), 1.6f, 5);
            if (look.fallenLeaves != null) AddFallenLeaves(env, look.fallenLeaves);

            // луна
            GameObject moon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            moon.name = "Moon";
            Object.DestroyImmediate(moon.GetComponent<Collider>());
            moon.transform.position = new Vector3(4.8f, 6.4f, 16f);
            moon.transform.localScale = Vector3.one * 2.8f;
            EnvMesh.Glow(null, "MoonHalo", moon.transform.position + new Vector3(0f, 0f, 0.5f), 11f, look.haloMoon, 0f);
            moon.GetComponent<Renderer>().sharedMaterial = look.moon;

            // жаровни с мерцающим огнём
            AddBrazier(look, new Vector3(-3.2f, 0f, 2.2f));
            AddBrazier(look, new Vector3(3.2f, 0f, 2.2f));

            BuildDetails(env, look);
            MarkStatic(env);
            AddMist(look);
            AddFireflies(look);
            AddPetals(look);
        }

        /// <summary>Мелочи, оживляющие кадр: бумажные фонари на веранде, верёвка симэнава на тории, знамёна.</summary>
        static void BuildDetails(Transform env, Look look)
        {
            Transform d = new GameObject("Details").transform;
            d.SetParent(env, false);
            // бумажные фонари под карнизом храма
            for (int i = 0; i < 5; i++)
            {
                float x = -0.8f - 2.8f + i * 1.4f + 0.7f;
                EnvMesh.Box(d, "String" + i, new Vector3(x, 2.28f, 4.75f), Vector3.zero, new Vector3(0.012f, 0.16f, 0.012f), look.woodDark, 0.2f);
                GameObject lamp = CharacterVisuals.Part(d, "PaperLamp" + i, PrimitiveType.Sphere, new Vector3(x, 2.05f, 4.75f), Vector3.zero, new Vector3(0.28f, 0.36f, 0.28f), look.paper);
                AddReflection(lamp.transform, null, 1f, new Color(1f, 0.8f, 0.5f), 0.24f, 1.5f, 0.36f);
                EnvMesh.Glow(d, "LampHalo" + i, new Vector3(x, 2.05f, 4.6f), 1.0f, look.haloWarm, 0.06f);
                CharacterVisuals.Part(d, "LampCapT" + i, PrimitiveType.Cylinder, new Vector3(x, 2.25f, 4.75f), Vector3.zero, new Vector3(0.12f, 0.012f, 0.12f), look.woodDark);
                CharacterVisuals.Part(d, "LampCapB" + i, PrimitiveType.Cylinder, new Vector3(x, 1.86f, 4.75f), Vector3.zero, new Vector3(0.10f, 0.012f, 0.10f), look.woodDark);
            }
            // красные знамёна на фасаде храма (как в референсе)
            for (int i = 0; i < 5; i++)
            {
                if (i == 2) continue;   // по центру — вход
                float x = -0.8f - 2.8f + i * 1.4f;
                Transform hp = SwayPivot(d, "HallBannerPivot" + i, new Vector3(x, 2.2f, 5.02f), 3f);
                EnvMesh.Box(hp, "HallBanner" + i, new Vector3(0f, -0.65f, 0f), Vector3.zero, new Vector3(0.36f, 1.3f, 0.01f), look.banner, 0f);
            }
            // симэнава между столбами тории и бумажные зигзаги
            CharacterVisuals.Part(d, "Shimenawa", PrimitiveType.Cylinder, new Vector3(1.7f, 2.05f, 3.52f), new Vector3(0f, 0f, 90f), new Vector3(0.07f, 1.0f, 0.07f), look.bark);
            for (int i = 0; i < 4; i++)
                EnvMesh.Box(d, "Shide" + i, new Vector3(1.2f + i * 0.34f, 1.88f, 3.52f), new Vector3(0f, 0f, i % 2 == 0 ? 8f : -8f), new Vector3(0.1f, 0.22f, 0.012f), look.plaster, 0.3f);
            // знамёна-нобори по краям площадки
            for (int side = -1; side <= 1; side += 2)
            {
                float x = 4.25f * side;
                CharacterVisuals.Part(d, "BannerPole" + side, PrimitiveType.Cylinder, new Vector3(x, 1.5f, 0.5f), Vector3.zero, new Vector3(0.05f, 1.5f, 0.05f), look.woodDark);
                EnvMesh.Box(d, "BannerCross" + side, new Vector3(x - 0.2f * side, 2.88f, 0.5f), Vector3.zero, new Vector3(0.5f, 0.04f, 0.04f), look.woodDark, 0.5f, true);
                Transform bp = SwayPivot(d, "BannerPivot" + side, new Vector3(x - 0.22f * side, 2.86f, 0.5f), 6f);
                EnvMesh.Box(bp, "Banner" + side, new Vector3(0f, -0.76f, 0f), Vector3.zero, new Vector3(0.42f, 1.5f, 0.01f), look.banner, 0f);
            }
        }

        /// <summary>Низкая дымка над гравием.</summary>
        static void AddMist(Look look)
        {
            GameObject go = new GameObject("GroundMist");
            go.transform.position = new Vector3(0f, 0.25f, 3f);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.loop = true; main.prewarm = true;
            main.startLifetime = 14f;
            main.startSpeed = 0.05f;
            main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startColor = new Color(0.42f, 0.42f, 0.44f, 0.05f);   // нейтральная серая дымка, без синевы
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule em = ps.emission;
            em.rateOverTime = 3f;
            ParticleSystem.ShapeModule sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(13f, 0.2f, 11f);
            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(0.12f);
            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = look.mist;
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            PetalTexture pt = go.AddComponent<PetalTexture>();
            pt.fog = true;
        }

        /// <summary>Светлячки / искры, плывущие над двором.</summary>
        static void AddFireflies(Look look)
        {
            GameObject go = new GameObject("Fireflies");
            go.transform.position = new Vector3(0f, 1.3f, 2f);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.loop = true; main.prewarm = true;
            main.startLifetime = 7f;
            main.startSpeed = 0.1f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.startColor = new Color(1f, 0.85f, 0.45f, 0.9f);
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule em = ps.emission;
            em.rateOverTime = 6f;
            ParticleSystem.ShapeModule sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(10f, 1.8f, 8f);
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true; noise.strength = 0.5f; noise.frequency = 0.5f;
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = look.petal;
            PetalTexture pt = go.AddComponent<PetalTexture>();
            pt.disc = true;
        }

        /// <summary>Сад: бамбуковые рощи по углам двора, замшелые камни, дорожка из плоских камней к храму.</summary>
        /// <summary>Неподвижные декорации — в статический батчинг: сотни объектов сада и храма сливаются в несколько draw call.</summary>
        static Transform SwayPivot(Transform parent, string name, Vector3 pos, float amp)
        {
            Transform p = new GameObject(name).transform;
            p.SetParent(parent, false);
            p.localPosition = pos;
            p.gameObject.AddComponent<BladePath.BannerSway>().amp = amp;
            return p;
        }

        static Transform reflRoot;
        /// <summary>Отражение источника в мокром камне (FloorReflection) — в отдельном нестатичном корне: полоса движется за камерой.</summary>
        static void AddReflection(Transform src, Light l, float baseI, Color c, float width, float length, float strength)
        {
            if (reflRoot == null) reflRoot = new GameObject("FloorReflections").transform;
            GameObject g = new GameObject("Refl_" + src.parent.name);
            g.transform.SetParent(reflRoot, false);
            g.AddComponent<MeshFilter>(); g.AddComponent<MeshRenderer>();
            BladePath.FloorReflection fr = g.AddComponent<BladePath.FloorReflection>();
            fr.source = src; fr.lightRef = l; fr.baseIntensity = baseI; fr.color = c; fr.width = width; fr.length = length; fr.strength = strength;
        }

        static void MarkStatic(Transform root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<BladePath.GlowSprite>() != null || t.GetComponent<ParticleSystem>() != null || t.GetComponentInParent<BladePath.BannerSway>() != null) continue;
                UnityEditor.GameObjectUtility.SetStaticEditorFlags(t.gameObject, UnityEditor.StaticEditorFlags.BatchingStatic);
            }
        }

        static void BuildGarden(Transform env, Look look)
        {
            Transform g = new GameObject("Garden").transform;
            g.SetParent(env, false);
            System.Random r = new System.Random(77);
            Vector3[] groves = { new Vector3(-5.6f, 0f, 7.6f), new Vector3(5.7f, 0f, 6.4f), new Vector3(-5.7f, 0f, -3.4f), new Vector3(5.6f, 0f, -3.2f) };
            int n = 0;
            foreach (Vector3 c in groves)
            {
                for (int i = 0; i < 9; i++)
                {
                    Vector3 p = c + new Vector3((float)(r.NextDouble() - 0.5) * 1.4f, 0f, (float)(r.NextDouble() - 0.5) * 1.4f);
                    float h = 2.6f + (float)r.NextDouble() * 2.2f;
                    float th = 0.05f + (float)r.NextDouble() * 0.035f;
                    Vector3 tilt = new Vector3((float)(r.NextDouble() - 0.5) * 8f, 0f, (float)(r.NextDouble() - 0.5) * 8f);
                    Transform stalk = new GameObject("Bamboo" + n++).transform;
                    stalk.SetParent(g, false);
                    stalk.localPosition = p;
                    stalk.localEulerAngles = tilt;
                    CharacterVisuals.Part(stalk, "Stem", PrimitiveType.Cylinder, new Vector3(0f, h * 0.5f, 0f), Vector3.zero, new Vector3(th, h * 0.5f, th), look.bamboo);
                    for (float y = 0.45f; y < h - 0.2f; y += 0.42f)
                        CharacterVisuals.Part(stalk, "Node", PrimitiveType.Cylinder, new Vector3(0f, y, 0f), Vector3.zero, new Vector3(th * 1.25f, 0.012f, th * 1.25f), look.bambooLeaf);
                    if (look.bambooFoliage != null)
                        TreeBuilder.Foliage(stalk, "Leaves", new Vector3(0f, h - 0.55f, 0f), 0.55f, 6, 0.9f, look.bambooFoliage, n * 31 + 7);   // веера листьев-карточек у верхушки
                    else
                        for (int k = 0; k < 3; k++)
                        {
                            float a = (float)r.NextDouble() * 360f;
                            CharacterVisuals.Part(stalk, "Leaves", PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * 0.25f, h - 0.2f - k * 0.35f, Mathf.Sin(a) * 0.25f), new Vector3(0f, a, 20f), new Vector3(0.7f, 0.14f, 0.32f), look.bambooLeaf);
                        }
                }
                for (int k = 0; k < 3; k++)
                {
                    float s = 0.25f + (float)r.NextDouble() * 0.35f;
                    CharacterVisuals.Part(g, "Rock", PrimitiveType.Sphere, c + new Vector3((float)(r.NextDouble() - 0.5) * 2.2f, s * 0.25f, (float)(r.NextDouble() - 0.5) * 2.2f),
                        new Vector3(0f, (float)r.NextDouble() * 360f, 0f), new Vector3(s * 1.4f, s * 0.7f, s), look.moss);
                }
            }
            // дорожка к ступеням храма
            for (int i = 0; i < 3; i++)
                CharacterVisuals.Part(g, "SteppingStone" + i, PrimitiveType.Cylinder, new Vector3(-0.8f + (i % 2 == 0 ? -0.12f : 0.12f), -0.03f, 3.15f + i * 0.33f), new Vector3(0f, i * 40f, 0f), new Vector3(0.42f, 0.04f, 0.32f), look.stoneBlock);
        }

        static void BuildHall(Transform env, Look look)
        {
            Transform hall = new GameObject("TempleHall").transform;
            hall.SetParent(env, false);
            const float cx = -0.8f;
            // каменный подиум и ступени
            EnvMesh.Box(hall, "Platform", new Vector3(cx, 0.25f, 5.8f), Vector3.zero, new Vector3(9.2f, 0.5f, 2.6f), look.stoneBlock, 1.5f);
            EnvMesh.Box(hall, "Step1", new Vector3(cx, 0.17f, 4.35f), Vector3.zero, new Vector3(2.6f, 0.34f, 0.32f), look.stoneBlock, 1.5f);
            EnvMesh.Box(hall, "Step0", new Vector3(cx, 0.085f, 4.05f), Vector3.zero, new Vector3(2.6f, 0.17f, 0.3f), look.stoneBlock, 1.5f);
            // стены и потолок
            EnvMesh.Box(hall, "Wall", new Vector3(cx, 1.5f, 6.1f), Vector3.zero, new Vector3(7.6f, 2.0f, 1.4f), look.plaster, 2f);
            EnvMesh.Box(hall, "Ceiling", new Vector3(cx, 2.55f, 6.0f), Vector3.zero, new Vector3(8.2f, 0.08f, 3.0f), look.woodDark, 1f, true);
            // колонны, ставни-сёдзи, балка
            for (int i = 0; i < 6; i++)
            {
                float x = cx - 3.5f + i * 1.4f;
                CharacterVisuals.Part(hall, "Column" + i, PrimitiveType.Cylinder, new Vector3(x, 1.5f, 5.15f), Vector3.zero, new Vector3(0.2f, 1.0f, 0.2f), look.lacquer);
                EnvMesh.Box(hall, "ColBase" + i, new Vector3(x, 0.55f, 5.15f), Vector3.zero, new Vector3(0.3f, 0.1f, 0.3f), look.stoneBlock, 1f);
                if (i < 5)
                {
                    GameObject sj = EnvMesh.Box(hall, "Shoji" + i, new Vector3(x + 0.7f, 1.3f, 5.24f), Vector3.zero, new Vector3(1.2f, 1.5f, 0.05f), look.shoji, 0f);
                    // светящиеся сёдзи отражаются в мокрой площадке широкими тёплыми полосами
                    GameObject sp = new GameObject("ShojiPoint"); sp.transform.SetParent(sj.transform.parent, false); sp.transform.localPosition = new Vector3(x + 0.7f, 1.15f, 5.2f);
                    AddReflection(sp.transform, null, 1f, new Color(1f, 0.74f, 0.42f), 1.0f, 2.4f, 0.36f);
                }
            }
            EnvMesh.Box(hall, "Lintel", new Vector3(cx, 2.35f, 5.15f), Vector3.zero, new Vector3(7.8f, 0.2f, 0.24f), look.lacquer, 1f, true);
            EnvMesh.Box(hall, "Veranda", new Vector3(cx, 0.55f, 4.95f), Vector3.zero, new Vector3(7.6f, 0.1f, 0.6f), look.wood, 1f, true);
            // крыша: скаты, конёк, свесы
            const float roofY = 2.6f;
            // вальмовая крыша с вогнутыми скатами и загнутыми углами (как в референсе)
            EnvMesh.CurvedHip(hall, "Roof", new Vector3(cx, roofY - 0.1f, 6.0f), 5.0f, 2.1f, 1.45f, 0.3f, look.roof, look.woodDark, 1.6f);
            EnvMesh.Box(hall, "Ridge", new Vector3(cx, roofY - 0.1f + 1.47f, 6.0f), Vector3.zero, new Vector3(2f * (5.0f - 2.1f) + 0.4f, 0.18f, 0.24f), look.woodDark, 1f, true);
            CharacterVisuals.Part(hall, "RidgeEndL", PrimitiveType.Sphere, new Vector3(cx - 5.0f + 2.1f - 0.2f, roofY + 1.5f, 6.0f), Vector3.zero, new Vector3(0.22f, 0.32f, 0.22f), look.woodDark);
            CharacterVisuals.Part(hall, "RidgeEndR", PrimitiveType.Sphere, new Vector3(cx + 5.0f - 2.1f + 0.2f, roofY + 1.5f, 6.0f), Vector3.zero, new Vector3(0.22f, 0.32f, 0.22f), look.woodDark);

            GameObject lg = new GameObject("HallGlow");
            lg.transform.SetParent(hall, false);
            lg.transform.localPosition = new Vector3(cx, 1.5f, 4.4f);
            Light gl = lg.AddComponent<Light>();
            gl.type = LightType.Point;
            gl.color = new Color(1f, 0.7f, 0.4f);
            gl.range = 4.5f;
            gl.intensity = 0.7f;
        }

        static void BuildPagoda(Transform env, Look look)
        {
            Transform pagoda = new GameObject("Pagoda").transform;
            pagoda.SetParent(env, false);
            Vector3 c = new Vector3(-3.4f, 0f, 9.6f);
            EnvMesh.Box(pagoda, "Base", c + new Vector3(0f, 0.2f, 0f), Vector3.zero, new Vector3(2.8f, 0.4f, 2.8f), look.stoneBlock, 1.5f);
            for (int i = 0; i < 5; i++)
            {
                float w = 1.6f - 0.2f * i;
                float y0 = 0.4f + 1.3f * i;
                EnvMesh.Box(pagoda, "Tier" + i, c + new Vector3(0f, y0 + 0.42f, 0f), Vector3.zero, new Vector3(w, 0.84f, w), look.plaster, 2f);
                EnvMesh.Box(pagoda, "Trim" + i, c + new Vector3(0f, y0 + 0.42f, 0f), Vector3.zero, new Vector3(w + 0.05f, 0.12f, w + 0.05f), look.lacquer, 1f);
                float hw = (w + 1.1f) * 0.5f;
                EnvMesh.CurvedHip(pagoda, "Roof" + i, c + new Vector3(0f, y0 + 0.8f, 0f), hw, hw, 0.55f, 0.2f, look.roof, look.woodDark, 1.2f);
            }
            CharacterVisuals.Part(pagoda, "Finial", PrimitiveType.Cylinder, c + new Vector3(0f, 0.4f + 1.3f * 5 + 0.35f, 0f), Vector3.zero, new Vector3(0.05f, 0.45f, 0.05f), look.bronze);
        }

        static void BuildWalls(Transform env, Look look)
        {
            Transform walls = new GameObject("Walls").transform;
            walls.SetParent(env, false);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = 6.4f * side;
                EnvMesh.Box(walls, "Wall" + side, new Vector3(x, 0.55f, 2.5f), Vector3.zero, new Vector3(0.3f, 1.1f, 12f), look.plaster, 2f);
                EnvMesh.Box(walls, "Cap" + side, new Vector3(x, 1.16f, 2.5f), Vector3.zero, new Vector3(0.6f, 0.12f, 12.4f), look.roof, 1.2f, true);
                EnvMesh.Box(walls, "Base" + side, new Vector3(x, 0.17f, 2.5f), Vector3.zero, new Vector3(0.38f, 0.34f, 12.05f), look.stoneBlock, 1.1f);   // каменный цоколь стены
                EnvMesh.Box(walls, "Beam" + side, new Vector3(x, 0.98f, 2.5f), Vector3.zero, new Vector3(0.33f, 0.07f, 12.02f), look.woodDark, 1f, true); // брус под кровлей
                for (int i = 0; i < 5; i++)
                    EnvMesh.Box(walls, "Post" + side + i, new Vector3(x, 0.55f, -3.2f + i * 2.9f), Vector3.zero, new Vector3(0.36f, 1.1f, 0.18f), look.woodDark, 1f);
            }
        }

        static void AddLantern(Transform env, Look look, Vector3 pos)
        {
            Transform t = new GameObject("StoneLantern").transform;
            t.SetParent(env, false);
            t.position = pos;
            EnvMesh.Box(t, "Foot", new Vector3(0f, 0.075f, 0f), Vector3.zero, new Vector3(0.46f, 0.15f, 0.46f), look.stoneBlock, 0.8f);
            CharacterVisuals.Part(t, "Shaft", PrimitiveType.Cylinder, new Vector3(0f, 0.4f, 0f), Vector3.zero, new Vector3(0.18f, 0.25f, 0.18f), look.stoneBlock);
            EnvMesh.Box(t, "Plate", new Vector3(0f, 0.69f, 0f), Vector3.zero, new Vector3(0.52f, 0.08f, 0.52f), look.stoneBlock, 0.8f);
            EnvMesh.Box(t, "Lamp", new Vector3(0f, 0.9f, 0f), Vector3.zero, new Vector3(0.34f, 0.34f, 0.34f), look.stoneBlock, 0.8f);
            EnvMesh.Box(t, "GlowA", new Vector3(0f, 0.9f, 0f), Vector3.zero, new Vector3(0.24f, 0.2f, 0.36f), look.glow, 1f);
            EnvMesh.Box(t, "GlowB", new Vector3(0f, 0.9f, 0f), Vector3.zero, new Vector3(0.36f, 0.2f, 0.24f), look.glow, 1f);
            EnvMesh.Glow(t, "Halo", new Vector3(0f, 0.9f, 0f), 0.9f, look.haloWarm, 0.08f);
            GameObject lp = new GameObject("LampPoint"); lp.transform.SetParent(t, false); lp.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            AddReflection(lp.transform, null, 1f, new Color(1f, 0.72f, 0.38f), 0.34f, 1.7f, 0.5f);
            EnvMesh.Pyramid(t, "Cap", new Vector3(0f, 1.07f, 0f), 0.3f, 0.22f, look.stoneBlock, 0.8f);
            CharacterVisuals.Part(t, "Jewel", PrimitiveType.Sphere, new Vector3(0f, 1.32f, 0f), Vector3.zero, new Vector3(0.09f, 0.09f, 0.09f), look.stoneBlock);
        }

        /// <summary>
        /// Опавшие листья клёна (один меш, 1 вызов отрисовки): гуще под кронами на гравии и у бордюров, редко — на камне арены
        /// (середину бойцы «вымели»). Каждый лист — карточка 7…13 см из атласа 2×2 (алый, оранжевый, намокший винный, сухой бурый),
        /// лежит чуть наклонно, свет — как у земли (нормаль вверх).
        /// </summary>
        static void AddFallenLeaves(Transform env, Material mat)
        {
            System.Random r = new System.Random(77);
            List<Vector3> v = new List<Vector3>(); List<Vector3> n = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> t = new List<int>();
            Vector3[] trees = { new Vector3(5.4f, 0f, 3.4f), new Vector3(-5.5f, 0f, 4.4f), new Vector3(5.6f, 0f, -0.6f), new Vector3(-5.4f, 0f, -1.0f) };
            System.Func<float> R = () => (float)r.NextDouble();
            System.Action<Vector3> Leaf = (Vector3 p) =>
            {
                float size = 0.07f + 0.06f * R();
                float a = R() * Mathf.PI * 2f;
                Vector3 ex = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * size * 0.5f, ez = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * size * 0.5f;
                ex.y = (R() - 0.5f) * size * 0.25f; ez.y = (R() - 0.5f) * size * 0.25f;   // лист покороблен — чуть наклонён
                int cell = r.Next(4); if (R() < 0.35f) cell = 2;                          // на мокром камне больше тёмных
                Vector2 o = new Vector2((cell % 2) * 0.5f, (cell / 2) * 0.5f);
                bool flip = R() < 0.5f;
                int i0 = v.Count;
                Vector3[] q = { p - ex - ez, p + ex - ez, p + ex + ez, p - ex + ez };
                Vector2[] u = { new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f) };
                for (int k = 0; k < 4; k++) { v.Add(q[k]); n.Add(Vector3.up); Vector2 uu = u[k]; if (flip) uu.x = 0.5f - uu.x; uv.Add(o + uu); }
                t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1); t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2);
            };
            // под кронами (на гравии; верх гравия y = −0,07): плотнее к стволу
            foreach (Vector3 tp in trees)
                for (int i = 0; i < 70; i++)
                {
                    float d = 0.3f + 2.4f * Mathf.Pow(R(), 0.8f), a = R() * Mathf.PI * 2f;
                    Vector3 p = tp + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                    bool onArena = Mathf.Abs(p.x) < 3.9f && Mathf.Abs(p.z) < 3.0f;
                    if (onArena) continue;
                    p.y = -0.062f + 0.004f * R();
                    Leaf(p);
                }
            // на камне арены: у бордюров, середина почти чистая
            for (int i = 0; i < 200 && v.Count < 4 * 380; i++)
            {
                Vector3 p = new Vector3((R() - 0.5f) * 7.0f, 0f, (R() - 0.5f) * 5.2f);
                float edge = Mathf.Max(Mathf.Abs(p.x) / 3.5f, Mathf.Abs(p.z) / 2.6f);   // 0 в центре … 1 у бордюра
                if (R() > Mathf.Pow(edge, 3f) * 0.9f + 0.03f) continue;
                p.y = 0.009f + 0.003f * R();
                Leaf(p);
            }
            Mesh m = new Mesh { name = "FallenLeaves" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds(); m.RecalculateTangents();
            GameObject g = new GameObject("FallenLeaves");
            g.transform.SetParent(env, false);
            g.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
        }

        static void AddTree(Transform env, Look look, Vector3 pos, float s, int seed)
        {
            // настоящее дерево: ветвящиеся ветви и карточки гроздей листьев (TreeBuilder); старые шары — только без материала кроны
            if (look.mapleCrown != null)
            {
                GameObject tr = TreeBuilder.Maple(env, "Maple" + seed, pos, s, seed, look.bark, look.mapleCrown);
                BladePath.BannerSway tsw = tr.AddComponent<BladePath.BannerSway>();   // крона покачивается
                tsw.amp = 0.8f; tsw.speed = 0.5f;
                return;
            }
            System.Random r = new System.Random(seed * 7919);
            Transform t = new GameObject("Sakura" + seed).transform;
            t.SetParent(env, false);
            t.position = pos;
            BladePath.BannerSway sw = t.gameObject.AddComponent<BladePath.BannerSway>();   // крона покачивается
            sw.amp = 1.1f; sw.speed = 0.55f;
            CharacterVisuals.Part(t, "Trunk", PrimitiveType.Cylinder, new Vector3(0f, 1.0f * s, 0f), new Vector3(0f, 0f, (float)(r.NextDouble() * 8 - 4)), new Vector3(0.26f * s, 1.0f * s, 0.26f * s), look.bark);
            for (int i = 0; i < 3; i++)
            {
                float yaw = (float)(r.NextDouble() * 360);
                float tilt = 35f + (float)r.NextDouble() * 25f;
                Quaternion q = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(tilt, 0f, 0f);
                Vector3 dir = q * Vector3.up;
                float len = 1.0f * s;
                Vector3 basePt = new Vector3(0f, 1.7f * s, 0f);
                CharacterVisuals.Part(t, "Branch" + i, PrimitiveType.Cylinder, basePt + dir * len * 0.5f, q.eulerAngles, new Vector3(0.12f * s, len * 0.5f, 0.12f * s), look.bark);
            }
            for (int i = 0; i < 6; i++)
            {
                float a = (float)(r.NextDouble() * Mathf.PI * 2f);
                float rad = (0.4f + (float)r.NextDouble() * 1.0f) * s;
                float h = (2.5f + (float)r.NextDouble() * 0.9f) * s;
                float sc = (1.3f + (float)r.NextDouble() * 0.8f) * s;
                CharacterVisuals.Part(t, "Bloom" + i, PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * rad, h, Mathf.Sin(a) * rad), Vector3.zero, new Vector3(sc, sc * 0.75f, sc), look.blossom);
            }
        }

        /// <summary>Живой огонь: языки пламени и искры, аддитивные частицы.</summary>
        static void AddFire(Transform parent, Look look)
        {
            GameObject go = new GameObject("Fire");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 1.0f, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
            main.startColor = new Color(1f, 0.85f, 0.7f, 1f);
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 26f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.12f;
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0.12f), 0.4f), new GradientColorKey(new Color(0.6f, 0.08f, 0.02f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.8f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.2f)));
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 1.5f;
            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = look.fireSprite;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // искры
            GameObject sp = new GameObject("Embers");
            sp.transform.SetParent(go.transform, false);
            ParticleSystem es = sp.AddComponent<ParticleSystem>();
            var em2 = es.main;
            em2.loop = true; em2.prewarm = true;
            em2.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            em2.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            em2.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
            em2.startColor = new Color(1f, 0.6f, 0.2f, 1f);
            em2.maxParticles = 40;
            em2.simulationSpace = ParticleSystemSimulationSpace.World;
            var ee = es.emission; ee.rateOverTime = 7f;
            var esh = es.shape; esh.shapeType = ParticleSystemShapeType.Cone; esh.angle = 20f; esh.radius = 0.1f;
            var en = es.noise; en.enabled = true; en.strength = 0.6f; en.frequency = 0.8f;
            var ec = es.colorOverLifetime; ec.enabled = true;
            Gradient g2 = new Gradient();
            g2.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            ec.color = g2;
            ParticleSystemRenderer r2 = sp.GetComponent<ParticleSystemRenderer>();
            r2.sharedMaterial = look.haloFire;
            r2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void AddBrazier(Look look, Vector3 pos)
        {
            Transform b = new GameObject("Brazier").transform;
            b.position = pos;
            b.gameObject.AddComponent<ArenaObstacle>().radius = 0.32f;
            CharacterVisuals.Part(b, "Stand", PrimitiveType.Cylinder, new Vector3(0f, 0.45f, 0f), Vector3.zero, new Vector3(0.22f, 0.45f, 0.22f), look.stoneBlock);
            EnvMesh.Box(b, "Foot", new Vector3(0f, 0.06f, 0f), Vector3.zero, new Vector3(0.46f, 0.12f, 0.46f), look.stoneBlock, 0.8f);
            CharacterVisuals.Part(b, "Bowl", PrimitiveType.Cylinder, new Vector3(0f, 0.93f, 0f), Vector3.zero, new Vector3(0.52f, 0.05f, 0.52f), look.bronze);
            CharacterVisuals.Part(b, "BowlRim", PrimitiveType.Cylinder, new Vector3(0f, 0.99f, 0f), Vector3.zero, new Vector3(0.56f, 0.02f, 0.56f), look.bronze);
            CharacterVisuals.Part(b, "Coals", PrimitiveType.Sphere, new Vector3(0f, 0.99f, 0f), Vector3.zero, new Vector3(0.42f, 0.08f, 0.42f), look.ember);
            GameObject flame = CharacterVisuals.Part(b, "Flame", PrimitiveType.Sphere, new Vector3(0f, 1.1f, 0f), Vector3.zero, new Vector3(0.12f, 0.16f, 0.12f), look.flame);
            AddFire(b, look);
            EnvMesh.Glow(b, "Halo", new Vector3(0f, 1.2f, 0f), 1.6f, look.haloFire, 0.18f);

            GameObject lightObj = new GameObject("FireLight");
            lightObj.transform.SetParent(b, false);
            lightObj.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            Light fl = lightObj.AddComponent<Light>();
            fl.type = LightType.Point;
            fl.color = new Color(1f, 0.6f, 0.25f);
            fl.range = 5f;
            fl.intensity = 1.7f;
            AddReflection(lightObj.transform, fl, 1.7f, new Color(1f, 0.52f, 0.18f), 0.6f, 3.0f, 1.25f);

            BladePath.FlickerLight flicker = lightObj.AddComponent<BladePath.FlickerLight>();
            flicker.lightSource = fl;
            flicker.flame = flame.transform;
            flicker.baseIntensity = 1.7f;
        }

        /// <summary>Падающие лепестки сакуры: лёгкая система частиц над ареной.</summary>
        static void AddPetals(Look look)
        {
            GameObject go = new GameObject("Petals");
            go.transform.position = new Vector3(0f, 3.6f, 0.5f);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = 6f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.13f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.55f, 0.5f, 0.95f), new Color(1f, 0.85f, 0.8f, 0.95f));   // листья клёна (текстура красная)
            main.gravityModifier = 0.05f;
            main.maxParticles = 150;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule em = ps.emission;
            em.rateOverTime = 14f;

            ParticleSystem.ShapeModule sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(7f, 0.1f, 5.2f);

            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.4f;

            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = look.petal;
            go.AddComponent<PetalTexture>().leaf = true;
        }
    }
}
#endif
