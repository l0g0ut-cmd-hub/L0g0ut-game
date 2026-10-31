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
            BuildFrontWall(env, look);
            BuildCedarForest(env, look);

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
            if (look.grass != null) AddGrass(env, look.grass);
            AddShishiOdoshi(env, look, new Vector3(-3.25f, -0.07f, 3.55f));
            AddIncense(env, look, new Vector3(1.7f, -0.07f, 3.32f));
            AddWindChime(env, look, new Vector3(1.3f, 2.24f, 4.98f));
            new GameObject("CrowFlock").AddComponent<CrowFlock>();
            new GameObject("Birds").AddComponent<BladePath.Birds>();
            new GameObject("ShootingStars").AddComponent<BladePath.ShootingStars>();   // метеоры ясной ночью
            new GameObject("TempleCat").AddComponent<BladePath.TempleCat>();           // кот на веранде храма
            new GameObject("MoonClouds").AddComponent<BladePath.MoonClouds>();         // облака бегут по луне
            // эхо каменного двора: звон клинков и шаги отражаются от стен и храма (короткий хвост, без гула)
            {
                AudioReverbZone rz = new GameObject("CourtyardReverb").AddComponent<AudioReverbZone>();
                rz.transform.position = new Vector3(0f, 1.5f, 1.5f);
                rz.minDistance = 9f; rz.maxDistance = 16f;
                rz.reverbPreset = AudioReverbPreset.User;
                rz.room = -1400; rz.roomHF = -600; rz.decayTime = 1.25f; rz.decayHFRatio = 0.6f;
                rz.reflections = -900; rz.reflectionsDelay = 0.012f; rz.reverb = -700; rz.reverbDelay = 0.03f;
                rz.diffusion = 80f; rz.density = 90f;
            }   // ворона на тории/коньке, воробьи, клин гусей, цапля, летучие мыши
            AddLightShafts(moonLight.transform.forward);

            // луна
            GameObject moon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            moon.name = "Moon";
            Object.DestroyImmediate(moon.GetComponent<Collider>());
            moon.transform.position = new Vector3(4.8f, 6.4f, 16f);
            moon.transform.localScale = Vector3.one * 2.8f;
            EnvMesh.Glow(null, "MoonHalo", moon.transform.position + new Vector3(0f, 0f, 0.5f), 11f, look.haloMoon, 0f);
            moon.GetComponent<Renderer>().sharedMaterial = look.moon;

            // день и ночь: дневное небо и тусклые днём огни — переключает Weather при броске погоды на матч
            BladePath.DayNight dn = new GameObject("DayNight").AddComponent<BladePath.DayNight>();
            dn.daySky = look.skyDay;
            dn.duskSky = look.skyDusk;
            dn.glowMats = new[] { look.haloWarm, look.haloFire, look.haloMoon, look.glow, look.paper, look.shoji };
            // отражения неба в мокром камне, доспехе, крови: свои кубкарты на ночь, день и рассвет (иначе отражалось то небо,
            // что было при последнем обновлении окружения, — на телефоне и после дневных кадров CI серые «дыры» на глянце)
            dn.nightRefl = SkyCube("sky_night.png", new Color(0.5f, 0.5f, 0.5f), 1.3f, "Refl_Night", new Color(0.025f, 0.028f, 0.04f));
            dn.dayRefl = SkyCube("sky_day.png", new Color(0.5f, 0.5f, 0.5f), 1.0f, "Refl_Day", new Color(0.26f, 0.25f, 0.23f));
            dn.dawnRefl = SkyCube("sky_day.png", new Color(0.62f, 0.5f, 0.48f), 0.82f, "Refl_Dawn", new Color(0.24f, 0.2f, 0.19f));
            dn.duskRefl = SkyCube("sky_dusk.png", new Color(0.5f, 0.5f, 0.5f), 1.0f, "Refl_Dusk", new Color(0.2f, 0.13f, 0.14f));
            if (dn.nightRefl != null) { BladePath.DayNight.SetReflection(dn.nightRefl); RenderSettings.reflectionIntensity = 1f; }

            // жаровни с мерцающим огнём
            AddBrazier(look, new Vector3(-3.2f, 0f, 2.2f));
            AddBrazier(look, new Vector3(3.2f, 0f, 2.2f));

            BuildDetails(env, look);
            AddGroundAO(env, look);
            AddPond(env, look);
            AddBackCourt(env, look);
            AddKaresansui(env, look);
            AddShojiShadows(look);
            MarkStatic(env);
            AddMist(look);
            AddFireflies(look);
            AddDragonflies(look);
            AddPetals(look);
        }

        /// <summary>
        /// Затенение у основания (AO): мягкие тёмные полосы на земле вдоль стен (с обеих сторон), у платформы храма и бордюров
        /// арены, пятна под фонарями, жаровнями, столбами тории и ворот, стволами клёнов и пагодой. Без него предметы
        /// «стояли на воздухе» — SSAO на телефоне не по карману, а запечённая тень стоит одну наклейку.
        /// </summary>
        static void AddGroundAO(Transform env, Look look)
        {
            if (look.aoStrip == null || look.aoBlob == null) return;
            Transform ao = new GameObject("GroundAO").transform;
            ao.SetParent(env, false);
            const float yGravel = -0.068f, yForest = -0.058f, yArena = 0.002f;
            int k = 0;
            // полоса от a до b вдоль основания; toWall — куда стена (тень темнее у неё), width — на сколько отходит
            System.Action<Vector3, Vector3, Vector3, float, float> Strip = (Vector3 a, Vector3 b, Vector3 toWall, float width, float y) =>
            {
                Vector3 c = (a + b) * 0.5f - toWall.normalized * width * 0.5f; c.y = y;
                float yaw = Mathf.Atan2(-toWall.x, -toWall.z) * Mathf.Rad2Deg;
                EnvMesh.Decal(ao, "Strip" + k++, c, yaw, (b - a).magnitude + width * 0.6f, width, look.aoStrip);
            };
            System.Action<float, float, float, float> Blob = (float x, float z, float size, float y) =>
                EnvMesh.Decal(ao, "Blob" + k++, new Vector3(x, y, z), 0f, size, size, look.aoBlob);

            // боковые стены: внутри двора (гравий) и снаружи (лес)
            for (int side = -1; side <= 1; side += 2)
            {
                Strip(new Vector3(side * 6.21f, 0f, -7.41f), new Vector3(side * 6.21f, 0f, 11.8f), new Vector3(side, 0f, 0f), 0.85f, yGravel);
                Strip(new Vector3(side * 6.59f, 0f, -7.79f), new Vector3(side * 6.59f, 0f, 12.2f), new Vector3(-side, 0f, 0f), 0.85f, yForest);
                // передняя стена по сторонам ворот
                Strip(new Vector3(side * 1.56f, 0f, -7.41f), new Vector3(side * 6.21f, 0f, -7.41f), new Vector3(0f, 0f, -1f), 0.75f, yGravel);
                Strip(new Vector3(side * 1.56f, 0f, -7.79f), new Vector3(side * 6.59f, 0f, -7.79f), new Vector3(0f, 0f, 1f), 0.75f, yForest);
            }
            // платформа храма (x −5,4…3,8, фасад z = 4,5) и её торцы
            Strip(new Vector3(-5.4f, 0f, 4.5f), new Vector3(3.8f, 0f, 4.5f), new Vector3(0f, 0f, 1f), 0.7f, yGravel);
            Strip(new Vector3(-5.4f, 0f, 4.5f), new Vector3(-5.4f, 0f, 7.1f), new Vector3(1f, 0f, 0f), 0.6f, yGravel);
            Strip(new Vector3(3.8f, 0f, 4.5f), new Vector3(3.8f, 0f, 7.1f), new Vector3(-1f, 0f, 0f), 0.6f, yGravel);
            // бордюр арены: снаружи на гравии и внутри на мостовой (узко)
            Strip(new Vector3(-3.9f, 0f, 3.0f), new Vector3(3.9f, 0f, 3.0f), new Vector3(0f, 0f, -1f), 0.4f, yGravel);
            Strip(new Vector3(-3.9f, 0f, -3.0f), new Vector3(3.9f, 0f, -3.0f), new Vector3(0f, 0f, 1f), 0.4f, yGravel);
            Strip(new Vector3(-3.9f, 0f, -3.0f), new Vector3(-3.9f, 0f, 3.0f), new Vector3(1f, 0f, 0f), 0.4f, yGravel);
            Strip(new Vector3(3.9f, 0f, -3.0f), new Vector3(3.9f, 0f, 3.0f), new Vector3(-1f, 0f, 0f), 0.4f, yGravel);
            Strip(new Vector3(-3.55f, 0f, 2.7f), new Vector3(3.55f, 0f, 2.7f), new Vector3(0f, 0f, 1f), 0.3f, yArena);
            Strip(new Vector3(-3.55f, 0f, -2.7f), new Vector3(3.55f, 0f, -2.7f), new Vector3(0f, 0f, -1f), 0.3f, yArena);
            Strip(new Vector3(-3.6f, 0f, -2.65f), new Vector3(-3.6f, 0f, 2.65f), new Vector3(-1f, 0f, 0f), 0.3f, yArena);
            Strip(new Vector3(3.6f, 0f, -2.65f), new Vector3(3.6f, 0f, 2.65f), new Vector3(1f, 0f, 0f), 0.3f, yArena);
            // фонари, жаровни, тории, ворота, клёны, пагода
            foreach (Vector2 p in new[] { new Vector2(-4.5f, -1.6f), new Vector2(4.5f, -1.6f), new Vector2(-4.5f, 2.2f), new Vector2(4.5f, 2.2f) }) Blob(p.x, p.y, 1.15f, yGravel);
            Blob(-3.2f, 2.2f, 0.95f, yArena); Blob(3.2f, 2.2f, 0.95f, yArena);
            Blob(0.7f, 3.6f, 0.85f, yGravel); Blob(2.7f, 3.6f, 0.85f, yGravel);
            Blob(-1.35f, -7.6f, 0.9f, yGravel); Blob(1.35f, -7.6f, 0.9f, yGravel);
            Blob(5.4f, 3.4f, 1.6f, yGravel); Blob(-5.5f, 4.4f, 1.7f, yGravel); Blob(5.6f, -0.6f, 1.4f, yGravel); Blob(-5.4f, -1.0f, 1.45f, yGravel); Blob(4.6f, 8.2f, 1.9f, yGravel);
            Blob(-3.4f, 9.6f, 4.2f, yGravel);
        }

        /// <summary>
        /// Задний двор за храмом (был пустой гравий до леса): боковые стены доведены до задней, задняя стена с калиткой,
        /// колокольня-сёро справа (столбы, загнутая крыша, бронзовый колокол и бревно-било — BellTower бьёт и звонит),
        /// белый амбар-кура слева от неё, каменные фонари, тени у оснований. Колокольня ниже линии взгляда на луну.
        /// </summary>
        static void AddBackCourt(Transform env, Look look)
        {
            Transform g = new GameObject("BackCourt").transform; g.SetParent(env, false);
            const float zb = 12f;
            System.Action<string, Vector3, Vector3> Wall = (nm, c, size) =>
            {
                bool alongX = size.x > size.z;
                EnvMesh.Box(g, nm, new Vector3(c.x, 0.55f, c.z), Vector3.zero, new Vector3(alongX ? size.x : 0.3f, 1.1f, alongX ? 0.3f : size.z), look.plaster, 2f);
                EnvMesh.Box(g, nm + "Cap", new Vector3(c.x, 1.16f, c.z), Vector3.zero, new Vector3(alongX ? size.x + 0.4f : 0.6f, 0.12f, alongX ? 0.6f : size.z + 0.4f), look.roof, 1.2f, true);
                EnvMesh.Box(g, nm + "Base", new Vector3(c.x, 0.17f, c.z), Vector3.zero, new Vector3(alongX ? size.x + 0.05f : 0.38f, 0.34f, alongX ? 0.38f : size.z + 0.05f), look.stoneBlock, 1.1f);
                EnvMesh.Box(g, nm + "Beam", new Vector3(c.x, 0.98f, c.z), Vector3.zero, new Vector3(alongX ? size.x + 0.02f : 0.33f, 0.07f, alongX ? 0.33f : size.z + 0.02f), look.woodDark, 1f, true);
                Vector3 thin = alongX ? new Vector3(size.x, 0f, 0f) : new Vector3(0f, 0f, size.z);
                int posts = Mathf.Max(1, Mathf.RoundToInt(thin.magnitude / 2.9f));
                for (int i = 0; i <= posts; i++)
                {
                    Vector3 pp = c - thin * 0.5f + thin * (i / (float)posts);
                    EnvMesh.Box(g, nm + "Post" + i, new Vector3(pp.x, 0.55f, pp.z), Vector3.zero, alongX ? new Vector3(0.18f, 1.1f, 0.36f) : new Vector3(0.36f, 1.1f, 0.18f), look.woodDark, 1f);
                }
            };
            for (int side = -1; side <= 1; side += 2)
                Wall("SideBack" + side, new Vector3(6.4f * side, 0f, (8.5f + zb) * 0.5f), new Vector3(0.3f, 1.1f, zb - 8.5f + 0.3f));
            Wall("BackL", new Vector3(-3.7f, 0f, zb), new Vector3(5.4f, 1.1f, 0.3f));
            Wall("BackR", new Vector3(3.7f, 0f, zb), new Vector3(5.4f, 1.1f, 0.3f));
            // калитка: два столба, перекладина, маленькая крыша, закрытые створки
            EnvMesh.Box(g, "GatePostL", new Vector3(-0.95f, 0.9f, zb), Vector3.zero, new Vector3(0.2f, 1.8f, 0.2f), look.woodDark, 1f);
            EnvMesh.Box(g, "GatePostR", new Vector3(0.95f, 0.9f, zb), Vector3.zero, new Vector3(0.2f, 1.8f, 0.2f), look.woodDark, 1f);
            EnvMesh.Box(g, "GateDoor", new Vector3(0f, 0.8f, zb + 0.02f), Vector3.zero, new Vector3(1.7f, 1.5f, 0.06f), look.wood, 1f);
            EnvMesh.Box(g, "GateLintel", new Vector3(0f, 1.82f, zb), Vector3.zero, new Vector3(2.3f, 0.12f, 0.24f), look.woodDark, 1f, true);
            EnvMesh.CurvedHip(g, "GateRoof", new Vector3(0f, 1.9f, zb), 1.35f, 0.45f, 0.4f, 0.1f, look.roof, look.woodDark, 1.2f);

            // ---- колокольня
            Transform bt = new GameObject("BellTower").transform; bt.SetParent(g, false); bt.localPosition = new Vector3(2.6f, 0f, 10.4f);
            EnvMesh.Box(bt, "Base", new Vector3(0f, 0.22f, 0f), Vector3.zero, new Vector3(2.4f, 0.44f, 2.4f), look.stoneBlock, 1.2f);
            for (int ix = -1; ix <= 1; ix += 2)
                for (int iz = -1; iz <= 1; iz += 2)
                    EnvMesh.Box(bt, "Post" + ix + iz, new Vector3(ix * 0.85f, 0.44f + 1.35f, iz * 0.85f), Vector3.zero, new Vector3(0.18f, 2.7f, 0.18f), look.lacquer, 1f);
            for (int iz = -1; iz <= 1; iz += 2)
            {
                EnvMesh.Box(bt, "TieX" + iz, new Vector3(0f, 2.95f, iz * 0.85f), Vector3.zero, new Vector3(1.95f, 0.14f, 0.16f), look.woodDark, 1f, true);
                EnvMesh.Box(bt, "TieZ" + iz, new Vector3(iz * 0.85f, 2.95f, 0f), Vector3.zero, new Vector3(0.16f, 0.14f, 1.95f), look.woodDark, 1f);
                EnvMesh.Box(bt, "Rail" + iz, new Vector3(0f, 0.75f, iz * 0.85f), Vector3.zero, new Vector3(1.75f, 0.06f, 0.06f), look.woodDark, 1f, true);
            }
            EnvMesh.Box(bt, "BellBeam", new Vector3(0f, 2.98f, 0f), Vector3.zero, new Vector3(1.9f, 0.16f, 0.2f), look.woodDark, 1f, true);
            EnvMesh.CurvedHip(bt, "Roof", new Vector3(0f, 3.1f, 0f), 1.5f, 1.5f, 1.0f, 0.3f, look.roof, look.woodDark, 1.2f);
            Material bronze = look.bellBronze != null ? look.bellBronze : look.bronze;
            Transform bell = new GameObject("Bell").transform; bell.SetParent(bt, false); bell.localPosition = new Vector3(0f, 2.9f, 0f);
            CharacterVisuals.Part(bell, "Loop", PrimitiveType.Cube, new Vector3(0f, -0.04f, 0f), Vector3.zero, new Vector3(0.05f, 0.12f, 0.16f), bronze);
            CharacterVisuals.Part(bell, "Dome", PrimitiveType.Sphere, new Vector3(0f, -0.2f, 0f), Vector3.zero, new Vector3(0.58f, 0.3f, 0.58f), bronze);
            CharacterVisuals.Part(bell, "Body", PrimitiveType.Cylinder, new Vector3(0f, -0.62f, 0f), Vector3.zero, new Vector3(0.6f, 0.42f, 0.6f), bronze);
            CharacterVisuals.Part(bell, "Band1", PrimitiveType.Cylinder, new Vector3(0f, -0.48f, 0f), Vector3.zero, new Vector3(0.615f, 0.012f, 0.615f), bronze);
            CharacterVisuals.Part(bell, "Band2", PrimitiveType.Cylinder, new Vector3(0f, -0.82f, 0f), Vector3.zero, new Vector3(0.615f, 0.012f, 0.615f), bronze);
            CharacterVisuals.Part(bell, "Lip", PrimitiveType.Cylinder, new Vector3(0f, -1.03f, 0f), Vector3.zero, new Vector3(0.66f, 0.025f, 0.66f), bronze);
            CharacterVisuals.Part(bell, "Boss", PrimitiveType.Sphere, new Vector3(-0.3f, -0.85f, 0f), Vector3.zero, new Vector3(0.07f, 0.07f, 0.07f), bronze);   // место удара (цуки-за)
            // бревно-било на двух верёвках: точка подвеса над центром бревна, конец бревна у колокола
            Transform log = new GameObject("Shumoku").transform; log.SetParent(bt, false); log.localPosition = new Vector3(-0.92f, 2.88f, 0f);
            CharacterVisuals.Part(log, "Log", PrimitiveType.Cylinder, new Vector3(0f, -0.9f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.13f, 0.5f, 0.13f), look.wood);
            EnvMesh.Box(log, "RopeA", new Vector3(-0.3f, -0.45f, 0f), Vector3.zero, new Vector3(0.015f, 0.9f, 0.015f), look.woodDark, 0.2f);
            EnvMesh.Box(log, "RopeB", new Vector3(0.3f, -0.45f, 0f), Vector3.zero, new Vector3(0.015f, 0.9f, 0.015f), look.woodDark, 0.2f);
            BladePath.BellTower tower = bt.gameObject.AddComponent<BladePath.BellTower>();
            tower.bell = bell; tower.log = log;

            // ---- амбар-кура: белые стены, тёмный цоколь-намако, двускатная крыша, окошко и дверь
            Transform kura = new GameObject("Kura").transform; kura.SetParent(g, false); kura.localPosition = new Vector3(-0.6f, 0f, 10.4f);
            EnvMesh.Box(kura, "Plinth", new Vector3(0f, 0.15f, 0f), Vector3.zero, new Vector3(2.3f, 0.3f, 1.7f), look.stoneBlock, 1.1f);
            EnvMesh.Box(kura, "Skirt", new Vector3(0f, 0.6f, 0f), Vector3.zero, new Vector3(2.16f, 0.6f, 1.56f), look.woodDark, 1f);
            EnvMesh.Box(kura, "Walls", new Vector3(0f, 1.55f, 0f), Vector3.zero, new Vector3(2.1f, 1.3f, 1.5f), look.plaster, 2f);
            EnvMesh.Box(kura, "Door", new Vector3(0.35f, 1.0f, -0.79f), Vector3.zero, new Vector3(0.6f, 1.2f, 0.05f), look.woodDark, 1f);
            EnvMesh.Box(kura, "Window", new Vector3(-0.5f, 1.75f, -0.77f), Vector3.zero, new Vector3(0.36f, 0.3f, 0.04f), look.woodDark, 1f);
            EnvMesh.Gable(kura, "Roof", new Vector3(0f, 2.2f, 0f), 2.6f, 2.0f, 0.75f, look.roof, look.plaster, 1.2f);

            // ---- быт: поленница вдоль задней стены, бочки для воды у амбара, ведро
            Transform wood = new GameObject("Woodpile").transform; wood.SetParent(g, false); wood.localPosition = new Vector3(-3.6f, 0f, 11.45f);
            EnvMesh.Box(wood, "Rack", new Vector3(0f, 0.04f, 0f), Vector3.zero, new Vector3(1.8f, 0.08f, 0.5f), look.woodDark, 1f, true);
            for (int row = 0; row < 4; row++)
                for (int k = 0; k < 11 - row; k++)
                    CharacterVisuals.Part(wood, "Log" + row + "_" + k, PrimitiveType.Cylinder, new Vector3(-0.78f + k * 0.155f + row * 0.077f, 0.15f + row * 0.13f, (k % 2) * 0.03f), new Vector3(90f, 0f, 0f), new Vector3(0.075f + 0.02f * ((k * 7 + row) % 3) * 0.3f, 0.22f, 0.075f), look.wood);
            EnvMesh.Box(wood, "Roof", new Vector3(0f, 0.82f, -0.05f), new Vector3(-12f, 0f, 0f), new Vector3(2.0f, 0.04f, 0.75f), look.roof, 1.2f, true);
            for (int i = 0; i < 2; i++)
            {
                Vector3 bp = new Vector3(0.95f + i * 0.02f, 0f, 9.15f + i * 0.62f);
                CharacterVisuals.Part(g, "Barrel" + i, PrimitiveType.Cylinder, bp + new Vector3(0f, 0.36f, 0f), Vector3.zero, new Vector3(0.5f, 0.36f, 0.5f), look.wood);
                for (int h = 0; h < 2; h++)
                    CharacterVisuals.Part(g, "Hoop" + i + h, PrimitiveType.Cylinder, bp + new Vector3(0f, 0.16f + h * 0.4f, 0f), Vector3.zero, new Vector3(0.515f, 0.02f, 0.515f), look.woodDark);
                CharacterVisuals.Part(g, "BarrelWater" + i, PrimitiveType.Cylinder, bp + new Vector3(0f, 0.68f, 0f), Vector3.zero, new Vector3(0.44f, 0.005f, 0.44f), look.pondWater != null ? look.pondWater : look.puddle);
            }
            CharacterVisuals.Part(g, "Bucket", PrimitiveType.Cylinder, new Vector3(1.55f, 0.13f, 8.95f), Vector3.zero, new Vector3(0.26f, 0.13f, 0.26f), look.wood);
            CharacterVisuals.Part(g, "Ladle", PrimitiveType.Cylinder, new Vector3(1.55f, 0.36f, 8.95f), new Vector3(0f, 0f, 35f), new Vector3(0.015f, 0.2f, 0.015f), look.bamboo);

            // ---- фонари и тени у оснований
            AddLantern(env, look, new Vector3(5.2f, 0f, 9.2f));
            AddLantern(env, look, new Vector3(-5.5f, 0f, 11.0f));
            if (look.aoStrip != null && look.aoBlob != null)
            {
                EnvMesh.Decal(g, "AOBackWallL", new Vector3(-3.7f, -0.068f, zb - 0.6f), 180f, 5.6f, 0.8f, look.aoStrip);
                EnvMesh.Decal(g, "AOBackWallR", new Vector3(3.7f, -0.068f, zb - 0.6f), 180f, 5.6f, 0.8f, look.aoStrip);
                EnvMesh.Decal(g, "AOTower", bt.localPosition + new Vector3(0f, -0.067f, 0f), 0f, 3.6f, 3.6f, look.aoBlob);
                EnvMesh.Decal(g, "AOKura", kura.localPosition + new Vector3(0f, -0.067f, 0f), 0f, 3.4f, 2.8f, look.aoBlob);
                EnvMesh.Decal(g, "AOLantern1", new Vector3(5.2f, -0.068f, 9.2f), 0f, 1.15f, 1.15f, look.aoBlob);
                EnvMesh.Decal(g, "AOLantern2", new Vector3(-5.5f, -0.068f, 11.0f), 0f, 1.15f, 1.15f, look.aoBlob);
            }
        }

        /// <summary>
        /// Кусари-тои — цепь-водосток с карниза храма в каменную чашу: звенья-чашечки из старой бронзы; в дождь по ним
        /// бежит вода и плещет в чашу (RainChain). Отдельный корень — цепь качается на ветру.
        /// </summary>
        static void AddRainChain(Look look, Vector3 top)
        {
            Material bronze = look.bellBronze != null ? look.bellBronze : look.bronze;
            Transform root = new GameObject("RainChain").transform; root.position = top;
            Transform chain = new GameObject("Chain").transform; chain.SetParent(root, false);
            float len = top.y - 0.25f;
            int links = Mathf.RoundToInt(len / 0.16f);
            for (int i = 0; i < links; i++)
                CharacterVisuals.Part(chain, "Cup" + i, PrimitiveType.Cylinder, new Vector3(0f, -0.08f - i * 0.16f, 0f), new Vector3(0f, i * 45f, 0f), new Vector3(0.07f, 0.045f, 0.07f), bronze);
            CharacterVisuals.Part(chain, "Hook", PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0f), Vector3.zero, new Vector3(0.02f, 0.05f, 0.02f), bronze);
            // каменная чаша и галька
            Material rockM = look.rock != null ? look.rock : look.stoneBlock;
            CharacterVisuals.Part(root, "Basin", PrimitiveType.Cylinder, new Vector3(0f, -top.y + 0.07f, 0f), Vector3.zero, new Vector3(0.38f, 0.08f, 0.38f), rockM);
            CharacterVisuals.Part(root, "BasinWater", PrimitiveType.Cylinder, new Vector3(0f, -top.y + 0.152f, 0f), Vector3.zero, new Vector3(0.3f, 0.002f, 0.3f), look.pondWater != null ? look.pondWater : look.puddle);
            // вода по цепи и брызги в чаше (выключены, включает RainChain)
            ParticleSystem water = FireLayer(root, "ChainWater", new Vector3(0f, -0.05f, 0f), look.petal, 0.55f, 0.65f, 3.2f, 3.6f, 0.02f, 0.035f, 0f, 120, new Color(0.7f, 0.76f, 0.88f, 0.55f), 0.02f, false);
            water.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // вниз
            { var r = water.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 1f; }
            { var main = water.main; main.gravityModifier = 0.2f; var sh = water.shape; sh.angle = 1f; }
            water.gameObject.AddComponent<PetalTexture>().disc = true;
            ParticleSystem splash = FireLayer(root, "ChainSplash", new Vector3(0f, -top.y + 0.17f, 0f), look.petal, 0.2f, 0.35f, 0.5f, 1.0f, 0.01f, 0.02f, 0f, 60, new Color(0.7f, 0.76f, 0.88f, 0.5f), 0.05f, false);
            { var main = splash.main; main.gravityModifier = 1f; var sh = splash.shape; sh.angle = 50f; }
            splash.gameObject.AddComponent<PetalTexture>().disc = true;
            BladePath.RainChain rc = root.gameObject.AddComponent<BladePath.RainChain>();
            rc.water = water; rc.splash = splash; rc.chain = chain;
        }

        /// <summary>
        /// Пруд в саду слева от дорожки к воротам: неровный контур, тёмная гладкая вода с плывущей рябью (отражает небо,
        /// фонари и луну), бордюр из камней — часть замшелые, листья лотоса, тень у кромки.
        /// </summary>
        static void AddPond(Transform env, Look look)
        {
            if (look.pondWater == null) return;
            Transform g = new GameObject("Pond").transform; g.SetParent(env, false);
            Vector3 c = new Vector3(-3.6f, 0f, -5.4f);
            System.Random r = new System.Random(77);
            const int n = 28;
            Vector2[] outline = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                float k = 1f + 0.12f * Mathf.Sin(a * 3f + 0.7f) + 0.07f * Mathf.Sin(a * 5f + 2.1f);   // бобовидный, не эллипс
                outline[i] = new Vector2(Mathf.Cos(a) * 1.45f * k, Mathf.Sin(a) * 0.85f * k);
            }
            GameObject water = EnvMesh.Flat(g, "Water", c + new Vector3(0f, -0.062f, 0f), outline, look.pondWater, 1.2f);
            water.AddComponent<BladePath.WaterRipple>();
            MeshRenderer wr = water.GetComponent<MeshRenderer>(); wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // тёмная кромка воды (AO) и камни бордюра
            if (look.aoBlob != null) EnvMesh.Decal(g, "PondAO", c + new Vector3(0f, -0.067f, 0f), 0f, 3.9f, 2.4f, look.aoBlob);
            // бордюр — плотный ряд разномастных тёмных камней (вплотную, разного размера и наклона), часть замшелые
            Material rockM = look.rock != null ? look.rock : look.stoneBlock;
            for (int i = 0; i < n; i++)
            {
                if (i % 7 == 3) continue;   // кое-где берег без камня — трава и гравий
                Vector2 o = outline[i] * (1.03f + 0.04f * (float)r.NextDouble());
                float s = 0.11f + 0.13f * (float)r.NextDouble();
                Material m = r.NextDouble() < 0.35 && look.moss != null ? look.moss : rockM;
                CharacterVisuals.Part(g, "RimStone" + i, PrimitiveType.Sphere, c + new Vector3(o.x, -0.06f + s * 0.2f, o.y),
                    new Vector3((float)r.NextDouble() * 18f - 9f, (float)r.NextDouble() * 180f, (float)r.NextDouble() * 18f - 9f), new Vector3(s * 1.9f, s * 0.8f, s * 1.3f), m);
            }
            // карпы кои под водой (тонкий слой между дном-гравием и гладью)
            if (look.koi != null)
            {
                Transform kp = new GameObject("Koi").transform; kp.SetParent(g, false); kp.localPosition = c + new Vector3(0f, -0.066f, 0f);
                Transform[] fish = new Transform[4];
                for (int i = 0; i < 4; i++) { float s = 0.32f + 0.08f * i; fish[i] = EnvMesh.Decal(kp, "Fish" + i, Vector3.zero, 0f, s * 0.5f, s, look.koi).transform; }
                BladePath.KoiPond kpond = kp.gameObject.AddComponent<BladePath.KoiPond>(); kpond.fish = fish;
            }
            // мостик-плита (исибаси) через узкое место пруда: длинный гранитный брус чуть изогнут (две плиты под углом),
            // концы лежат на опорных камнях
            {
                Material slab = look.stoneBlock != null ? look.stoneBlock : rockM;
                Vector3 bc = c + new Vector3(-0.55f, 0f, 0f);
                for (int s = -1; s <= 1; s += 2)
                    EnvMesh.Box(g, "BridgeSlab" + s, bc + new Vector3(0f, -0.005f, s * 0.52f), new Vector3(s * 3.5f, 6f, 0f), new Vector3(0.36f, 0.075f, 1.06f), slab, 0.6f);
                for (int s = -1; s <= 1; s += 2)
                    CharacterVisuals.Part(g, "BridgeFoot" + s, PrimitiveType.Sphere, bc + new Vector3(0.02f * s, -0.06f, s * 1.02f), new Vector3(0f, 30f * s, 0f), new Vector3(0.5f, 0.14f, 0.36f), rockM);
                if (look.aoBlob != null) EnvMesh.Decal(g, "BridgeAO", bc + new Vector3(0f, -0.065f, 0f), 6f, 0.8f, 2.4f, look.aoBlob);
            }
            // каменный фонарь у берега — ночью сад у ворот не тонет во тьме, огонь дрожит в воде
            AddLantern(env, look, c + new Vector3(1.75f, 0f, 0.75f));
            if (look.aoBlob != null) EnvMesh.Decal(g, "LanternAO", c + new Vector3(1.75f, -0.067f, 0.75f), 0f, 1.15f, 1.15f, look.aoBlob);
            // листья лотоса — плоские диски у дальнего берега, разного размера
            if (look.lotus != null)
                for (int i = 0; i < 7; i++)
                {
                    float a = 1.2f + (float)r.NextDouble() * 2.6f, rr = 0.35f + (float)r.NextDouble() * 0.45f;
                    float s = 0.14f + 0.12f * (float)r.NextDouble();
                    CharacterVisuals.Part(g, "Lotus" + i, PrimitiveType.Cylinder, c + new Vector3(Mathf.Cos(a) * rr * 1.45f, -0.058f, Mathf.Sin(a) * rr * 0.85f),
                        Vector3.zero, new Vector3(s, 0.003f, s), look.lotus);
                }
        }

        /// <summary>
        /// Сухой сад карэсансуй справа от дорожки: бороздки граблей по гравию (кольца вокруг камней, прямые вдоль сада) и три
        /// замшелых камня — позиции совпадают с tools/texgen/karesansui.py.
        /// </summary>
        static void AddKaresansui(Transform env, Look look)
        {
            if (look.karesansui == null) return;
            Transform g = new GameObject("Karesansui").transform; g.SetParent(env, false);
            Vector3 c = new Vector3(3.6f, 0f, -5.3f);
            EnvMesh.Decal(g, "Rake", c + new Vector3(0f, -0.068f, 0f), 0f, 4.0f, 2.0f, look.karesansui);
            float[,] rocks = { { -1.05f, 0.15f, 0.32f }, { 0.55f, -0.35f, 0.24f }, { 1.25f, 0.42f, 0.18f } };
            AddLantern(env, look, c + new Vector3(-1.75f, 0f, -1.25f));   // фонарь у края сада, ближе к воротам
            if (look.aoBlob != null) EnvMesh.Decal(g, "LanternAO", c + new Vector3(-1.75f, -0.067f, -1.25f), 0f, 1.15f, 1.15f, look.aoBlob);
            for (int i = 0; i < 3; i++)
            {
                float rx = rocks[i, 0], rz = rocks[i, 1], rr = rocks[i, 2];
                Material rockMat = look.rock != null ? look.rock : look.stoneBlock;
                Material m = i == 2 && look.moss != null ? look.moss : rockMat;   // зелёный мох — только на низком камне (стоячий-кубоид в мху читался упавшим ящиком)
                // главный камень — стоячий (татэ-иси), остальные — пологие; из двух сфер, чтобы не было «яиц»
                float hgt = i == 0 ? rr * 3.0f : rr * 1.4f;
                // стоячий — гранёный (кубоид с наклоном), пологие — округлые
                CharacterVisuals.Part(g, "Rock" + i, i == 0 ? PrimitiveType.Cube : PrimitiveType.Sphere, c + new Vector3(rx, -0.07f + hgt * (i == 0 ? 0.4f : 0.42f), rz),
                    i == 0 ? new Vector3(3f, 28f, -4f) : new Vector3(8f * i - 4f, 37f * i, -6f * i + 5f), i == 0 ? new Vector3(rr * 1.5f, hgt, rr * 1.1f) : new Vector3(rr * 2.0f, hgt, rr * 1.7f), m);
                // стоячий камень гранёный: второй кубоид, повёрнутый и чуть ниже, ломает силуэт «ящика»
                if (i == 0)
                    CharacterVisuals.Part(g, "Rock0c", PrimitiveType.Cube, c + new Vector3(rx - rr * 0.18f, -0.07f + hgt * 0.32f, rz + rr * 0.12f),
                        new Vector3(-6f, 71f, 11f), new Vector3(rr * 1.25f, hgt * 0.8f, rr * 1.0f), rockMat);
                CharacterVisuals.Part(g, "Rock" + i + "b", PrimitiveType.Sphere, c + new Vector3(rx + rr * 0.45f, -0.07f + hgt * 0.22f, rz - rr * 0.3f),
                    new Vector3(-12f, 60f * i + 20f, 9f), new Vector3(rr * 1.3f, hgt * 0.55f, rr * 1.2f), m);
                if (look.aoBlob != null) EnvMesh.Decal(g, "RockAO" + i, c + new Vector3(rx, -0.067f, rz), 0f, rr * 3.4f, rr * 3.4f, look.aoBlob);
            }
        }

        /// <summary>
        /// Тени людей на светящихся сёдзи (вне статики — двигаются): сидящий монах во второй створке и идущий вдоль всех.
        /// Наклейки — прямо перед бумагой (сёдзи: z = 5,24, толщина 0,05), снизу — уровень пола веранды.
        /// </summary>
        static void AddShojiShadows(Look look)
        {
            if (look.shojiMonk == null || look.shojiWalker == null) return;
            Transform root = new GameObject("ShojiShadows").transform;
            const float z = 5.211f;
            // монах — в средней створке (входной): на остальных висят знамёна и закрывали его
            GameObject monk = EnvMesh.Decal(root, "Monk", new Vector3(-1.1f, 0.6f + 0.7f, z), 0f, 0.7f, 1.4f, look.shojiMonk);
            monk.transform.localEulerAngles = new Vector3(-90f, 0f, 0f);
            // идущий: рост тени ~1,38 м — макушка под верхней рамой створки, ступни на полу веранды
            GameObject walker = EnvMesh.Decal(root, "Walker", new Vector3(-0.8f, 0.54f + 0.775f, z), 0f, 0.775f, 1.55f, look.shojiWalker);
            walker.transform.localEulerAngles = new Vector3(-90f, 0f, 0f);
            BladePath.ShojiShadows ss = root.gameObject.AddComponent<BladePath.ShojiShadows>();
            // сидящий монах читает сутру под стук мокугё — звук из глубины зала
            GameObject chant = new GameObject("MonkChant"); chant.transform.SetParent(root, false); chant.transform.localPosition = new Vector3(-1.1f, 1.0f, 6.2f);
            chant.AddComponent<BladePath.MonkChant>();
            ss.monk = monk.GetComponent<Renderer>(); ss.walker = walker.GetComponent<Renderer>();
            walker.GetComponent<Renderer>().enabled = false;   // появляется сам, когда пойдёт
        }

        /// <summary>
        /// Кубкарта отражений из панорамы неба (та же раскладка, что у Skybox/Panoramic: u = 0,5 − atan2(z, x)/2π,
        /// v = 1 − acos(y)/π; цвет = текстура × оттенок × 2 × экспозиция; ниже горизонта — цвет земли). Панорама сперва ужимается до 256×128 (звёзды не
        /// мерцают шумом), грани 64² с мип-уровнями — по ним Standard размывает отражение на шершавом.
        /// </summary>
        static Cubemap SkyCube(string file, Color tint, float exposure, string name, Color ground)
        {
            string src = "Assets/_Project/Art/Textures/" + file;
            if (!System.IO.File.Exists(src)) return null;
            Texture2D big = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!big.LoadImage(System.IO.File.ReadAllBytes(src))) return null;
            const int SW = 256, SH = 128, N = 64;
            Color32[] bp = big.GetPixels32();
            int bw = big.width, bh = big.height, fx = bw / SW, fy = bh / SH;
            Color[] sp = new Color[SW * SH];
            for (int y = 0; y < SH; y++)
                for (int x = 0; x < SW; x++)
                {
                    float r = 0f, g = 0f, b = 0f;
                    for (int j = 0; j < fy; j += 2)
                        for (int i = 0; i < fx; i += 2)
                        {
                            Color32 c = bp[(y * fy + j) * bw + x * fx + i];
                            r += c.r; g += c.g; b += c.b;
                        }
                    float k = 1f / (255f * ((fx + 1) / 2) * ((fy + 1) / 2));
                    sp[y * SW + x] = new Color(r * k, g * k, b * k, 1f);
                }
            Object.DestroyImmediate(big);
            Texture2D small = new Texture2D(SW, SH, TextureFormat.RGBAFloat, false, true);
            small.wrapModeU = TextureWrapMode.Repeat; small.wrapModeV = TextureWrapMode.Clamp; small.filterMode = FilterMode.Bilinear;
            small.SetPixels(sp); small.Apply(false);
            Color mul = new Color(tint.r * 2f * exposure, tint.g * 2f * exposure, tint.b * 2f * exposure, 1f);
            Cubemap cm = new Cubemap(N, TextureFormat.RGBA32, true);
            CubemapFace[] faces = { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ };
            Color[] px = new Color[N * N];
            for (int f = 0; f < 6; f++)
            {
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        // строка 0 — верх грани (раскладка кубкарт GL/D3D)
                        float sc = 2f * (x + 0.5f) / N - 1f, tc = 2f * (y + 0.5f) / N - 1f;
                        Vector3 d;
                        switch (f)
                        {
                            case 0: d = new Vector3(1f, -tc, -sc); break;
                            case 1: d = new Vector3(-1f, -tc, sc); break;
                            case 2: d = new Vector3(sc, 1f, tc); break;
                            case 3: d = new Vector3(sc, -1f, -tc); break;
                            case 4: d = new Vector3(sc, -tc, 1f); break;
                            default: d = new Vector3(-sc, -tc, -1f); break;
                        }
                        d.Normalize();
                        float u = 0.5f - Mathf.Atan2(d.z, d.x) / (2f * Mathf.PI);
                        float v = 1f - Mathf.Acos(Mathf.Clamp(d.y, -1f, 1f)) / Mathf.PI;
                        Color c = small.GetPixelBilinear(u, v) * mul;
                        c = Color.Lerp(c, ground, Mathf.SmoothStep(0f, 1f, -d.y / 0.18f));   // ниже горизонта — двор и земля (глянец снизу темнее)
                        c.r = Mathf.Clamp01(c.r); c.g = Mathf.Clamp01(c.g); c.b = Mathf.Clamp01(c.b); c.a = 1f;
                        px[y * N + x] = c;
                    }
                cm.SetPixels(px, faces[f]);
            }
            Object.DestroyImmediate(small);
            cm.SmoothEdges(1);
            cm.Apply(true);
            string path = "Assets/_Project/Materials/" + name + ".asset";
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<Cubemap>(path) != null) UnityEditor.AssetDatabase.DeleteAsset(path);
            UnityEditor.AssetDatabase.CreateAsset(cm, path);
            return cm;
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
            // сидэ — бумажные молнии-зигзаги: лёгкие, трепещут на ветру часто и мелко
            for (int i = 0; i < 4; i++)
            {
                Transform sp = SwayPivot(d, "ShidePivot" + i, new Vector3(1.2f + i * 0.34f, 2.0f, 3.52f), 14f);
                sp.GetComponent<BladePath.BannerSway>().speed = 3.4f + i * 0.37f;
                for (int k = 0; k < 4; k++)
                    EnvMesh.Box(sp, "Shide" + i + "_" + k, new Vector3(k % 2 == 0 ? 0f : 0.032f, -0.045f - k * 0.058f, 0f), new Vector3(0f, 0f, k % 2 == 0 ? 6f : -6f), new Vector3(0.062f, 0.062f, 0.008f), look.plaster, 0.3f);
            }
            // знамёна-нобори по краям площадки
            for (int side = -1; side <= 1; side += 2)
            {
                float x = 4.25f * side;
                CharacterVisuals.Part(d, "BannerPole" + side, PrimitiveType.Cylinder, new Vector3(x, 1.5f, 0.5f), Vector3.zero, new Vector3(0.05f, 1.5f, 0.05f), look.woodDark);
                EnvMesh.Box(d, "BannerCross" + side, new Vector3(x - 0.2f * side, 2.88f, 0.5f), Vector3.zero, new Vector3(0.5f, 0.04f, 0.04f), look.woodDark, 0.5f, true);
                Transform bp = SwayPivot(d, "BannerPivot" + side, new Vector3(x - 0.22f * side, 2.86f, 0.5f), 6f);
                Material nb = side < 0 ? (look.nobori1 != null ? look.nobori1 : look.banner) : (look.nobori2 != null ? look.nobori2 : look.banner);
                EnvMesh.Box(bp, "Banner" + side, new Vector3(0f, -0.76f, 0f), Vector3.zero, new Vector3(0.42f, 1.5f, 0.01f), nb, 0f);
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
        /// <summary>
        /// Стрекозы над двором в ясный день (тонбо — «насекомое победы», их рисовали на доспехах): 2–4 штуки, зависают и
        /// резко перелетают (сильный медленный шум), повёрнуты по ходу полёта. Ночью, в дождь и снег их нет (Weather).
        /// </summary>
        static void AddDragonflies(Look look)
        {
            GameObject go = new GameObject("Dragonflies");
            go.transform.position = new Vector3(0f, 1.5f, 0.8f);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.085f);
            main.startColor = new Color(0.8f, 1f, 1f, 1f);   // материал лепестков розоватый — крылья выходят дымчато-прозрачными, а не розовыми
            main.maxParticles = 4;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule em = ps.emission; em.rateOverTime = 0.3f; em.enabled = false;   // включает Weather днём
            ParticleSystem.ShapeModule sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(8f, 1.2f, 6f);
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true; noise.strength = 1.4f; noise.frequency = 0.35f; noise.scrollSpeed = 0.6f; noise.damping = false;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.9f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = look.petal;
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0f; r.lengthScale = 1.6f;   // повёрнуты по ходу полёта
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            go.AddComponent<PetalTexture>().dragonfly = true;
        }

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
                // движущиеся части (трубка сиси-одоси, полоска фурина) — не в статический батч, иначе застынут
                if (t.GetComponentInParent<BladePath.ShishiOdoshi>() != null || t.GetComponentInParent<BladePath.WindChime>() != null) continue;
                if (t.GetComponent<BladePath.WaterRipple>() != null) continue;
                if (t.GetComponentInParent<BladePath.KoiPond>() != null) continue;   // карпы плавают
                if (t.GetComponentInParent<BladePath.BellTower>() != null) continue;   // колокол и било качаются   // у пруда свой экземпляр материала (рябь плывёт)
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

        /// <summary>
        /// Капель с карниза храма в дождь: вода срывается со свеса неровными струйками и разбивается о камень брызгами.
        /// Система стоит выключенной (эмиссия 0) — включает Weather на время дождя. Отдельный корень (не статика).
        /// </summary>
        static void AddEaveDrips(Vector3 edge, float halfWidth, Look look)
        {
            GameObject go = new GameObject("EaveDrips");
            go.transform.position = edge;
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.loop = true; main.playOnAwake = true;
            main.startLifetime = 1.4f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.022f);
            main.startColor = new Color(0.72f, 0.78f, 0.9f, 0.55f);
            main.gravityModifier = 1f;
            main.maxParticles = 300;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule em = ps.emission; em.rateOverTime = 0f;
            ParticleSystem.ShapeModule sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(halfWidth * 2f, 0.01f, 0.03f);
            // струйки, а не сплошная завеса: шум по ширине сгущает капли в «жёлобки»
            ParticleSystem.NoiseModule noise = ps.noise; noise.enabled = true; noise.strength = 0.04f; noise.frequency = 0.3f;
            ParticleSystem.CollisionModule col = ps.collision;
            col.enabled = true; col.type = ParticleSystemCollisionType.Planes; col.dampen = 1f; col.bounce = 0f; col.lifetimeLoss = 1f;
            GameObject floor = new GameObject("EaveFloor"); floor.transform.SetParent(go.transform, false); floor.transform.position = new Vector3(edge.x, 0.005f, edge.z);
            col.SetPlane(0, floor.transform);
            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = look.petal;
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.035f; r.lengthScale = 1.2f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            go.AddComponent<PetalTexture>().disc = true;

            // брызги о камень: на каждую разбившуюся каплю — несколько мелких капель вверх-вбок
            GameObject sg = new GameObject("EaveSplash");
            sg.transform.SetParent(go.transform, false);
            ParticleSystem sp = sg.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule sm = sp.main;
            sm.loop = false; sm.playOnAwake = false;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.016f);
            sm.startColor = new Color(0.72f, 0.78f, 0.9f, 0.5f);
            sm.gravityModifier = 0.9f; sm.maxParticles = 400;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule se = sp.emission; se.rateOverTime = 0f;
            se.SetBursts(new[] { new ParticleSystem.Burst(0f, 2, 4) });
            ParticleSystem.ShapeModule ss = sp.shape; ss.shapeType = ParticleSystemShapeType.Hemisphere; ss.radius = 0.02f;
            sg.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            ParticleSystemRenderer sr = sg.GetComponent<ParticleSystemRenderer>();
            sr.sharedMaterial = look.petal;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; sr.receiveShadows = false;
            sg.AddComponent<PetalTexture>().disc = true;
            ParticleSystem.SubEmittersModule sub = ps.subEmitters;
            sub.enabled = true;
            sub.AddSubEmitter(sp, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing);
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
            AddEaveDrips(new Vector3(cx, roofY - 0.16f, 6.0f - 2.08f), 4.7f, look);
            AddRainChain(look, new Vector3(-4.3f, roofY - 0.18f, 3.96f));
            AddRainChain(look, new Vector3(3.35f, roofY - 0.18f, 3.96f));
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

        /// <summary>
        /// Роща криптомерий (суги) за стенами двора и позади храма — как в лесу у настоящих святилищ: высокие стволы,
        /// хвоя ярусами сужается к вершине. Ночью — тёмные силуэты на фоне неба (глубина), днём закрывают пустую равнину за стеной.
        /// Дёшево: ствол-цилиндр и 5–7 ярусов карточек хвои на дерево, всё статично (батчинг).
        /// </summary>
        static void BuildCedarForest(Transform env, Look look)
        {
            Transform f = new GameObject("CedarForest").transform;
            f.SetParent(env, false);
            // за стенами — лесная подстилка (мох, земля, рыжая хвоя), а не гравий двора: сверху и при облёте
            // светлый гравий до горизонта читался как бетонная площадка. Чуть выше гравия (1 см) — без z-мерцания.
            if (look.forestFloor != null)
            {
                for (int side = -1; side <= 1; side += 2)
                    EnvMesh.Box(f, "Floor" + side, new Vector3(side * 15.3f, -0.11f, 6f), Vector3.zero, new Vector3(17.4f, 0.1f, 40f), look.forestFloor, 3.2f);
                EnvMesh.Box(f, "FloorFront", new Vector3(0f, -0.11f, -10.9f), Vector3.zero, new Vector3(13.2f, 0.1f, 6.2f), look.forestFloor, 3.2f);
                EnvMesh.Box(f, "FloorBack", new Vector3(0f, -0.11f, 19.2f), Vector3.zero, new Vector3(13.2f, 0.1f, 13.6f), look.forestFloor, 3.2f);
            }
            if (look.cedarFoliage == null) return;
            System.Random r = new System.Random(101);
            System.Func<float> R = () => (float)r.NextDouble();
            int n = 0;
            System.Action<Vector3, float> Tree = (Vector3 p, float h) =>
            {
                Transform t = new GameObject("Cedar" + n).transform;
                t.SetParent(f, false); t.localPosition = p;
                float th = 0.12f + 0.05f * R();
                CharacterVisuals.Part(t, "Trunk", PrimitiveType.Cylinder, new Vector3(0f, h * 0.5f, 0f), Vector3.zero, new Vector3(th, h * 0.5f, th), look.bark);
                // крона от самой земли — плотная тёмная стена хвои (по кадрам CI голые стволы читались как частокол)
                int tiers = 7 + r.Next(3);
                for (int k = 0; k < tiers; k++)
                {
                    float u = (k + 0.5f) / tiers;                       // 0 внизу кроны … 1 у вершины
                    float y = Mathf.Lerp(h * 0.12f, h * 0.97f, u);
                    float rad = Mathf.Lerp(1.9f, 0.35f, Mathf.Pow(u, 0.85f)) * (0.85f + 0.3f * R());
                    TreeBuilder.Foliage(t, "Needles" + k, new Vector3(0f, y, 0f), rad, 9, Mathf.Lerp(1.9f, 0.8f, u), look.cedarFoliage, n * 53 + k * 7);
                }
                // подлесок у корней
                TreeBuilder.Foliage(t, "Brush", new Vector3((R() - 0.5f) * 0.8f, 0.45f, (R() - 0.5f) * 0.8f), 1.0f, 9, 1.2f, look.cedarFoliage, n * 97 + 3);
                n++;
            };
            // по бокам за стенами (стены на x = ±6,4)
            for (int side = -1; side <= 1; side += 2)
                for (float z = -4.5f; z < 13f; z += 2.1f + R() * 0.8f)
                {
                    Tree(new Vector3(side * (8.2f + R() * 1.6f), -0.07f, z + (R() - 0.5f) * 0.8f), 7.5f + R() * 3f);
                    if (R() < 0.6f) Tree(new Vector3(side * (10.6f + R() * 2f), -0.07f, z + 1f + (R() - 0.5f)), 8.5f + R() * 3f);   // второй ряд, выше
                }
            // перед передней стеной (за воротами) — лес, в который уходит дорожка
            for (float x = -12f; x <= 12f; x += 2.0f + R() * 0.8f)
            {
                if (Mathf.Abs(x) < 1.2f) continue;   // просвет дорожки в воротах
                Tree(new Vector3(x + (R() - 0.5f) * 0.6f, -0.07f, -9.6f - R() * 1.6f), 7.5f + R() * 3f);
                if (R() < 0.7f) Tree(new Vector3(x + 1f + (R() - 0.5f), -0.07f, -12.2f - R() * 2f), 9f + R() * 3f);
            }
            // позади храма и пагоды
            for (float x = -10f; x <= 10f; x += 2.3f + R() * 0.8f)
            {
                if (x > 1.5f && x < 7.5f) continue;   // луна (4,8; 6,4; 16) с камеры боя — не заслоняем
                Tree(new Vector3(x + (R() - 0.5f) * 0.8f, -0.07f, 12.5f + R() * 2.5f), 9f + R() * 3.5f);
            }
        }

        /// <summary>
        /// Двор закрыт и спереди: боковые стены доведены до передней, в передней — ворота якуимон (два столба, балка,
        /// крыша с загнутыми углами, распахнутые створки) и каменная дорожка наружу, к роще. Камера боя при облёте
        /// видит не пустую равнину до горизонта, а ограду храма и лес за ней.
        /// </summary>
        static void BuildFrontWall(Transform env, Look look)
        {
            Transform w = new GameObject("FrontWall").transform;
            w.SetParent(env, false);
            const float zf = -7.6f, gate = 1.35f;
            System.Action<Transform, string, Vector3, Vector3> WallSeg = (Transform par, string nm, Vector3 c, Vector3 size) =>
            {
                bool alongX = size.x > size.z;
                Vector3 thin = alongX ? new Vector3(size.x, 0f, 0f) : new Vector3(0f, 0f, size.z);
                EnvMesh.Box(par, nm, new Vector3(c.x, 0.55f, c.z), Vector3.zero, new Vector3(alongX ? size.x : 0.3f, 1.1f, alongX ? 0.3f : size.z), look.plaster, 2f);
                EnvMesh.Box(par, nm + "Cap", new Vector3(c.x, 1.16f, c.z), Vector3.zero, new Vector3(alongX ? size.x + 0.4f : 0.6f, 0.12f, alongX ? 0.6f : size.z + 0.4f), look.roof, 1.2f, true);
                EnvMesh.Box(par, nm + "Base", new Vector3(c.x, 0.17f, c.z), Vector3.zero, new Vector3(alongX ? size.x + 0.05f : 0.38f, 0.34f, alongX ? 0.38f : size.z + 0.05f), look.stoneBlock, 1.1f);
                EnvMesh.Box(par, nm + "Beam", new Vector3(c.x, 0.98f, c.z), Vector3.zero, new Vector3(alongX ? size.x + 0.02f : 0.33f, 0.07f, alongX ? 0.33f : size.z + 0.02f), look.woodDark, 1f, true);
                int posts = Mathf.Max(1, Mathf.RoundToInt(thin.magnitude / 2.9f));
                for (int i = 0; i <= posts; i++)
                {
                    Vector3 pp = c - thin * 0.5f + thin * (i / (float)posts);
                    EnvMesh.Box(par, nm + "Post" + i, new Vector3(pp.x, 0.55f, pp.z), Vector3.zero, alongX ? new Vector3(0.18f, 1.1f, 0.36f) : new Vector3(0.36f, 1.1f, 0.18f), look.woodDark, 1f);
                }
            };
            // боковые стены до передней (были до z = −3,5)
            for (int side = -1; side <= 1; side += 2)
                WallSeg(w, "SideFront" + side, new Vector3(6.4f * side, 0f, (zf - 3.5f) * 0.5f), new Vector3(0.3f, 1.1f, -3.5f - zf + 0.3f));
            // передняя стена по обе стороны ворот
            float segL = 6.4f - gate;
            WallSeg(w, "FrontL", new Vector3(-(gate + segL * 0.5f), 0f, zf), new Vector3(segL, 1.1f, 0.3f));
            WallSeg(w, "FrontR", new Vector3(gate + segL * 0.5f, 0f, zf), new Vector3(segL, 1.1f, 0.3f));
            // ворота якуимон: столбы, балки, крыша, распахнутые наружу створки, каменный порог
            Transform g = new GameObject("Gate").transform; g.SetParent(w, false);
            for (int side = -1; side <= 1; side += 2)
            {
                EnvMesh.Box(g, "Pillar" + side, new Vector3(gate * side, 1.3f, zf), Vector3.zero, new Vector3(0.28f, 2.6f, 0.28f), look.lacquer, 1f);
                EnvMesh.Box(g, "PillarBase" + side, new Vector3(gate * side, 0.1f, zf), Vector3.zero, new Vector3(0.42f, 0.2f, 0.42f), look.stoneBlock, 1f);
                EnvMesh.Box(g, "BackPost" + side, new Vector3(gate * side, 1.1f, zf - 0.75f), Vector3.zero, new Vector3(0.18f, 2.2f, 0.18f), look.woodDark, 1f);
                EnvMesh.Box(g, "Tie" + side, new Vector3(gate * side, 2.05f, zf - 0.38f), Vector3.zero, new Vector3(0.14f, 0.14f, 0.85f), look.woodDark, 1f, true);
                // створка распахнута наружу (к лесу)
                Transform leaf = new GameObject("Leaf" + side).transform; leaf.SetParent(g, false);
                leaf.localPosition = new Vector3((gate - 0.16f) * side, 0f, zf - 0.08f);
                leaf.localRotation = Quaternion.Euler(0f, side * -72f, 0f);
                EnvMesh.Box(leaf, "Door", new Vector3(-side * 0.6f, 1.05f, 0f), Vector3.zero, new Vector3(1.15f, 2.0f, 0.07f), look.woodDark, 1f);
                for (int k = 0; k < 4; k++)
                    EnvMesh.Box(leaf, "Rib" + k, new Vector3(-side * 0.6f, 0.35f + k * 0.48f, -0.045f), Vector3.zero, new Vector3(1.15f, 0.06f, 0.03f), look.iron != null ? look.iron : look.woodDark, 1f);
            }
            EnvMesh.Box(g, "Lintel", new Vector3(0f, 2.45f, zf), Vector3.zero, new Vector3(gate * 2f + 0.6f, 0.2f, 0.3f), look.lacquer, 1f, true);
            EnvMesh.Box(g, "Lintel2", new Vector3(0f, 2.2f, zf), Vector3.zero, new Vector3(gate * 2f + 0.3f, 0.1f, 0.22f), look.woodDark, 1f, true);
            EnvMesh.Box(g, "Plaque", new Vector3(0f, 2.33f, zf + 0.17f), Vector3.zero, new Vector3(0.6f, 0.18f, 0.04f), look.woodDark, 1f);
            EnvMesh.CurvedHip(g, "GateRoof", new Vector3(0f, 2.6f, zf - 0.38f), gate + 0.75f, 0.85f, 0.75f, 0.18f, look.roof, look.woodDark, 1.2f);
            EnvMesh.Box(g, "Threshold", new Vector3(0f, 0.05f, zf), Vector3.zero, new Vector3(gate * 2f, 0.1f, 0.34f), look.stoneBlock, 1f);
            // пара нобори у ворот изнутри двора (колышутся на ветру)
            for (int side = -1; side <= 1; side += 2)
            {
                float nx = 2.35f * side;
                CharacterVisuals.Part(g, "NoboriPole" + side, PrimitiveType.Cylinder, new Vector3(nx, 1.6f, zf + 0.55f), Vector3.zero, new Vector3(0.05f, 1.6f, 0.05f), look.woodDark);
                EnvMesh.Box(g, "NoboriCross" + side, new Vector3(nx - 0.22f * side, 3.08f, zf + 0.55f), Vector3.zero, new Vector3(0.52f, 0.04f, 0.04f), look.woodDark, 0.5f, true);
                Transform np = SwayPivot(g, "NoboriPivot" + side, new Vector3(nx - 0.24f * side, 3.06f, zf + 0.55f), 6f);
                Material nm2 = side < 0 ? (look.nobori2 != null ? look.nobori2 : look.banner) : (look.nobori1 != null ? look.nobori1 : look.banner);
                EnvMesh.Box(np, "Nobori" + side, new Vector3(0f, -0.82f, 0f), Vector3.zero, new Vector3(0.44f, 1.62f, 0.01f), nm2, 0f);
            }
            // бумажные фонари-тётин под балкой ворот (изнутри двора): ворота читаются ночью тёплым светом, без лишних источников
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 lp = new Vector3(side * 0.85f, 1.78f, zf + 0.22f);
                EnvMesh.Box(g, "LanternCord" + side, lp + new Vector3(0f, 0.27f, 0f), Vector3.zero, new Vector3(0.012f, 0.2f, 0.012f), look.woodDark, 0.2f);
                EnvMesh.Box(g, "LanternCap" + side, lp + new Vector3(0f, 0.19f, 0f), Vector3.zero, new Vector3(0.17f, 0.035f, 0.17f), look.woodDark, 1f);
                EnvMesh.Box(g, "LanternBase" + side, lp - new Vector3(0f, 0.19f, 0f), Vector3.zero, new Vector3(0.17f, 0.035f, 0.17f), look.woodDark, 1f);
                CharacterVisuals.Part(g, "Lantern" + side, PrimitiveType.Sphere, lp, Vector3.zero, new Vector3(0.24f, 0.36f, 0.24f), look.paper);
                EnvMesh.Glow(null, "LanternHalo" + side, lp, 1.1f, look.haloWarm, 0.1f);
            }
            // за воротами дорожку в рощу провожают каменные фонари (ночью — огоньки, уходящие в лес)
            foreach (float lz in new[] { -9.3f, -11.7f })
                for (int side = -1; side <= 1; side += 2)
                    AddLantern(env, look, new Vector3(side * 1.05f, -0.06f, lz));
            // дорожка из плоских камней от площадки к воротам и за них
            System.Random r = new System.Random(55);
            for (float z = -3.2f; z > -12.5f; z -= 0.62f)
            {
                if (z < zf + 0.3f && z > zf - 0.3f) continue;
                float sx = 0.5f + (float)r.NextDouble() * 0.2f;
                EnvMesh.Box(g, "Step" + z.ToString("F1"), new Vector3(((float)r.NextDouble() - 0.5f) * 0.25f, -0.045f, z), new Vector3(0f, (float)r.NextDouble() * 20f - 10f, 0f), new Vector3(sx, 0.06f, 0.42f), look.stoneBlock, 0.6f);
            }
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

        /// <summary>
        /// Мотыльки у огня фонаря: 2–4 светлых пятнышка кружат вокруг света по неровным орбитам, крылья «мелькают»
        /// (размер дёргается шумом), то подлетают к самому огню, то отлетают. В дождь и снег их нет (Weather).
        /// </summary>
        static void AddMoths(Transform lamp, Look look)
        {
            GameObject go = new GameObject("Moths");
            go.transform.SetParent(lamp, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.022f, 0.034f);
            main.startColor = new Color(0.95f, 0.88f, 0.72f, 0.9f);
            main.maxParticles = 4;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            ParticleSystem.EmissionModule em = ps.emission; em.rateOverTime = 0.45f;
            ParticleSystem.ShapeModule sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.32f; sh.radiusThickness = 0.3f;
            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.orbitalY = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
            vel.orbitalX = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);   // все оси орбиты в одном режиме (две константы)
            vel.radial = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true; noise.strength = 0.55f; noise.frequency = 2.2f; noise.scrollSpeed = 1.2f;
            noise.sizeAmount = new ParticleSystem.MinMaxCurve(0.7f);   // крылья мелькают
            noise.quality = ParticleSystemNoiseQuality.Medium;
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = look.petal;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            PetalTexture pt = go.AddComponent<PetalTexture>();
            pt.disc = true;
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
            AddMoths(lp.transform, look);
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
            for (int i = 0; i < 520 && v.Count < 4 * 640; i++)
            {
                Vector3 p = new Vector3((R() - 0.5f) * 7.0f, 0f, (R() - 0.5f) * 5.2f);
                float edge = Mathf.Max(Mathf.Abs(p.x) / 3.5f, Mathf.Abs(p.z) / 2.6f);   // 0 в центре … 1 у бордюра
                if (R() > Mathf.Pow(edge, 2.4f) * 0.9f + 0.05f) continue;
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

        /// <summary>
        /// Трава (один меш): пучки-«крестики» из двух карточек вдоль внешней стороны бордюров, у подножий фонарей, тории и стволов,
        /// у ступеней храма. Перед камерой — реже и ниже (не закрывают ноги бойцов).
        /// </summary>
        static void AddGrass(Transform env, Material mat)
        {
            System.Random r = new System.Random(91);
            List<Vector3> v = new List<Vector3>(); List<Vector3> n = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> t = new List<int>();
            System.Func<float> R = () => (float)r.NextDouble();
            System.Action<Vector3, float> Tuft = (Vector3 p, float h) =>
            {
                float a0 = R() * 180f;
                for (int c = 0; c < 2; c++)
                {
                    float a = (a0 + c * 90f) * Mathf.Deg2Rad;
                    Vector3 ex = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * h * 0.55f;
                    Vector3 lean = new Vector3(R() - 0.5f, 0f, R() - 0.5f) * h * 0.25f;
                    int i0 = v.Count;
                    v.Add(p - ex); v.Add(p + ex); v.Add(p + ex + Vector3.up * h + lean); v.Add(p - ex + Vector3.up * h + lean);
                    for (int k = 0; k < 4; k++) n.Add(Vector3.up);   // свет как у земли — без чёрных изнанок
                    bool flip = R() < 0.5f;
                    uv.Add(new Vector2(flip ? 1f : 0f, 0f)); uv.Add(new Vector2(flip ? 0f : 1f, 0f)); uv.Add(new Vector2(flip ? 0f : 1f, 1f)); uv.Add(new Vector2(flip ? 1f : 0f, 1f));
                    t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1); t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2);
                    t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);   // двусторонняя
                }
            };
            float gy = -0.07f;   // верх гравия
            // вдоль бордюров снаружи площадки: по бокам гуще, спереди (к камере) реже и ниже
            for (int i = 0; i < 70; i++)
            {
                float z = (R() - 0.5f) * 6.0f, side = R() < 0.5f ? -1f : 1f;
                Tuft(new Vector3(side * (3.95f + R() * 0.45f), gy, z), 0.16f + 0.2f * R());
            }
            for (int i = 0; i < 40; i++)
                Tuft(new Vector3((R() - 0.5f) * 8.2f, gy, 3.05f + R() * 0.9f), 0.16f + 0.2f * R());   // между площадкой и храмом
            for (int i = 0; i < 18; i++)
                Tuft(new Vector3((R() - 0.5f) * 8.2f, gy, -3.05f - R() * 0.5f), 0.1f + 0.1f * R());   // перед камерой — низкая
            // у подножий: фонари, тории, стволы клёнов
            Vector3[] bases = { new Vector3(-4.5f, 0f, -1.6f), new Vector3(4.5f, 0f, -1.6f), new Vector3(-4.5f, 0f, 2.2f), new Vector3(4.5f, 0f, 2.2f),
                                new Vector3(0.7f, 0f, 3.6f), new Vector3(2.7f, 0f, 3.6f),
                                new Vector3(5.4f, 0f, 3.4f), new Vector3(-5.5f, 0f, 4.4f), new Vector3(5.6f, 0f, -0.6f), new Vector3(-5.4f, 0f, -1.0f) };
            foreach (Vector3 b in bases)
                for (int i = 0; i < 9; i++)
                {
                    float a = R() * Mathf.PI * 2f, d = 0.22f + R() * 0.35f;
                    Vector3 p = b + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d); p.y = gy;
                    if (Mathf.Abs(p.x) < 3.95f && Mathf.Abs(p.z) < 3.0f) continue;   // не на камне площадки
                    Tuft(p, 0.2f + 0.22f * R());
                }
            // луг под криптомериями за стенами и за воротами: выше и гуще (днём там была голая серая равнина)
            for (int i = 0; i < 420; i++)
            {
                Vector3 p;
                float q = R();
                if (q < 0.7f) p = new Vector3((R() < 0.5f ? -1f : 1f) * (6.75f + R() * 7f), gy, -12f + R() * 26f);
                else p = new Vector3((R() - 0.5f) * 26f, gy, -7.95f - R() * 6f);
                if (Mathf.Abs(p.x) < 0.7f && p.z < -7.8f) continue;   // дорожка
                Tuft(p, 0.25f + 0.3f * R());
            }
            Mesh m = new Mesh { name = "Grass" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds(); m.RecalculateTangents();
            GameObject g = new GameObject("Grass");
            g.transform.SetParent(env, false);
            g.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
        }

        /// <summary>
        /// Цукубаи с сиси-одоси: каменная чаша с водой, бамбуковый жёлоб льёт в трубку на оси, та перевешивает, выливает воду
        /// в чашу и стучит закрытым концом о камень (ShishiOdoshi). Трубка вдоль X — камера боя видит её в профиль.
        /// </summary>
        static void AddShishiOdoshi(Transform env, Look look, Vector3 pos)
        {
            Transform root = new GameObject("ShishiOdoshi").transform;
            root.SetParent(env, false); root.position = pos;
            // чаша и вода
            Object.DestroyImmediate(CharacterVisuals.Part(root, "Basin", PrimitiveType.Cylinder, new Vector3(-0.42f, 0.13f, 0f), Vector3.zero, new Vector3(0.56f, 0.13f, 0.56f), look.stoneBlock).GetComponent<Collider>());
            Object.DestroyImmediate(CharacterVisuals.Part(root, "Water", PrimitiveType.Cylinder, new Vector3(-0.42f, 0.262f, 0f), Vector3.zero, new Vector3(0.44f, 0.004f, 0.44f), look.puddle).GetComponent<Collider>());
            // камень, о который бьёт трубка, и пара камней сада
            Object.DestroyImmediate(CharacterVisuals.Part(root, "KnockStone", PrimitiveType.Sphere, new Vector3(0.36f, 0.06f, 0f), new Vector3(0f, 20f, 0f), new Vector3(0.22f, 0.13f, 0.18f), look.stoneBlock).GetComponent<Collider>());
            Object.DestroyImmediate(CharacterVisuals.Part(root, "Rock1", PrimitiveType.Sphere, new Vector3(-0.85f, 0.05f, 0.22f), new Vector3(0f, 40f, 0f), new Vector3(0.3f, 0.12f, 0.24f), look.stoneBlock).GetComponent<Collider>());
            Object.DestroyImmediate(CharacterVisuals.Part(root, "Rock2", PrimitiveType.Sphere, new Vector3(0.05f, 0.04f, 0.3f), new Vector3(0f, -25f, 0f), new Vector3(0.22f, 0.09f, 0.2f), look.stoneBlock).GetComponent<Collider>());
            // стойки оси
            for (int z = -1; z <= 1; z += 2)
                Object.DestroyImmediate(CharacterVisuals.Part(root, "Post" + z, PrimitiveType.Cylinder, new Vector3(0f, 0.24f, 0.07f * z), Vector3.zero, new Vector3(0.035f, 0.24f, 0.035f), look.bamboo).GetComponent<Collider>());
            Object.DestroyImmediate(CharacterVisuals.Part(root, "Axle", PrimitiveType.Cylinder, new Vector3(0f, 0.42f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.016f, 0.09f, 0.016f), look.woodDark).GetComponent<Collider>());
            // трубка на оси: открытый конец (−X) под жёлобом над чашей, закрытый (+X) над камнем
            Transform pivot = new GameObject("TubePivot").transform;
            pivot.SetParent(root, false); pivot.localPosition = new Vector3(0f, 0.42f, 0f);
            Object.DestroyImmediate(CharacterVisuals.Part(pivot, "Tube", PrimitiveType.Cylinder, new Vector3(0.0f, 0f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.075f, 0.36f, 0.075f), look.bamboo).GetComponent<Collider>());
            Object.DestroyImmediate(CharacterVisuals.Part(pivot, "Node", PrimitiveType.Cylinder, new Vector3(0.2f, 0f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.083f, 0.008f, 0.083f), look.bamboo).GetComponent<Collider>());
            // жёлоб: столбик и короткий бамбук, из которого течёт вода
            Object.DestroyImmediate(CharacterVisuals.Part(root, "SpoutPost", PrimitiveType.Cylinder, new Vector3(-0.62f, 0.38f, -0.02f), Vector3.zero, new Vector3(0.05f, 0.38f, 0.05f), look.bamboo).GetComponent<Collider>());
            Object.DestroyImmediate(CharacterVisuals.Part(root, "Spout", PrimitiveType.Cylinder, new Vector3(-0.5f, 0.7f, -0.02f), new Vector3(0f, 0f, 75f), new Vector3(0.03f, 0.13f, 0.03f), look.bamboo).GetComponent<Collider>());
            Material wm = look.mist;   // заглушка: материал капель (мягкий диск) ставит ShishiOdoshi при запуске
            // тонкая струйка из жёлоба
            {
                GameObject g = new GameObject("Trickle"); g.transform.SetParent(root, false); g.transform.localPosition = new Vector3(-0.4f, 0.66f, -0.02f);
                g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                ParticleSystem ps = g.AddComponent<ParticleSystem>();
                var main = ps.main; main.loop = true; main.prewarm = true; main.startLifetime = 0.22f; main.startSpeed = 0.6f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.014f); main.startColor = new Color(0.75f, 0.85f, 1f, 0.55f);
                main.gravityModifier = 1f; main.maxParticles = 40; main.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = ps.emission; em.rateOverTime = 50f;
                var shp = ps.shape; shp.shapeType = ParticleSystemShapeType.Cone; shp.angle = 2f; shp.radius = 0.004f;
                var r = g.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = wm; r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.04f; r.lengthScale = 1.5f;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // всплеск: вода выливается из трубки в чашу
            ParticleSystem spill;
            {
                GameObject g = new GameObject("Spill"); g.transform.SetParent(pivot, false); g.transform.localPosition = new Vector3(-0.36f, 0f, 0f);
                g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                spill = g.AddComponent<ParticleSystem>();
                spill.Stop();
                var main = spill.main; main.loop = false; main.duration = 0.35f; main.playOnAwake = false; main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f); main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.026f);
                main.startColor = new Color(0.75f, 0.85f, 1f, 0.6f); main.gravityModifier = 1.2f; main.maxParticles = 80;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = spill.emission; em.rateOverTime = 160f;
                var shp = spill.shape; shp.shapeType = ParticleSystemShapeType.Cone; shp.angle = 18f; shp.radius = 0.02f;
                var r = g.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = wm; r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 1.2f;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            ShishiOdoshi so = pivot.gameObject.AddComponent<ShishiOdoshi>();
            so.spill = spill;
            so.trickle = root.Find("Trickle").GetComponent<ParticleSystem>();
        }

        /// <summary>Бронзовая курильница под тории: три ножки, чаша, тлеющие палочки и тонкий вьющийся дым.</summary>
        static void AddIncense(Transform env, Look look, Vector3 pos)
        {
            Transform root = new GameObject("Incense").transform;
            root.SetParent(env, false); root.position = pos;
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f + 0.4f;
                Object.DestroyImmediate(CharacterVisuals.Part(root, "Leg" + i, PrimitiveType.Cylinder, new Vector3(Mathf.Cos(a) * 0.15f, 0.08f, Mathf.Sin(a) * 0.15f), new Vector3(Mathf.Sin(a) * 12f, 0f, -Mathf.Cos(a) * 12f), new Vector3(0.04f, 0.08f, 0.04f), look.bronze).GetComponent<Collider>());
            }
            Object.DestroyImmediate(CharacterVisuals.Part(root, "Bowl", PrimitiveType.Sphere, new Vector3(0f, 0.26f, 0f), Vector3.zero, new Vector3(0.44f, 0.24f, 0.44f), look.bronze).GetComponent<Collider>());
            Object.DestroyImmediate(CharacterVisuals.Part(root, "Rim", PrimitiveType.Cylinder, new Vector3(0f, 0.34f, 0f), Vector3.zero, new Vector3(0.4f, 0.015f, 0.4f), look.bronze).GetComponent<Collider>());
            Object.DestroyImmediate(CharacterVisuals.Part(root, "Ash", PrimitiveType.Cylinder, new Vector3(0f, 0.352f, 0f), Vector3.zero, new Vector3(0.34f, 0.004f, 0.34f), look.plaster).GetComponent<Collider>());
            for (int i = 0; i < 3; i++)
            {
                Vector3 sp = new Vector3(-0.04f + i * 0.04f, 0.42f, (i - 1) * 0.02f);
                Object.DestroyImmediate(CharacterVisuals.Part(root, "Stick" + i, PrimitiveType.Cylinder, sp, new Vector3((i - 1) * 6f, 0f, (i - 1) * 5f), new Vector3(0.006f, 0.07f, 0.006f), look.woodDark).GetComponent<Collider>());
                Object.DestroyImmediate(CharacterVisuals.Part(root, "Ember" + i, PrimitiveType.Sphere, sp + Vector3.up * 0.072f, Vector3.zero, new Vector3(0.012f, 0.012f, 0.012f), look.glow).GetComponent<Collider>());
            }
            // дым: медленный, вьётся (шум), расплывается и тает
            GameObject g = new GameObject("IncenseSmoke");
            g.transform.SetParent(root, false); g.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            g.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.18f, 0.28f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.1f); main.startColor = new Color(0.62f, 0.62f, 0.66f, 0.09f);
            main.maxParticles = 60; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 9f;
            var shp = ps.shape; shp.shapeType = ParticleSystemShapeType.Cone; shp.angle = 4f; shp.radius = 0.02f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 6f)));
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.22f; noise.frequency = 0.6f; noise.scrollSpeed = 0.2f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.x = new ParticleSystem.MinMaxCurve(0.05f); vel.y = new ParticleSystem.MinMaxCurve(0f); vel.z = new ParticleSystem.MinMaxCurve(0f);
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            ParticleSystemRenderer r = g.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = look.mist;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g.AddComponent<PetalTexture>().fog = true;   // мягкая текстура дымки (как у тумана)
        }

        /// <summary>Фурин: чугунный колокольчик на шнурке под карнизом и бумажная полоска, качается на ветру (WindChime).</summary>
        static void AddWindChime(Transform env, Look look, Vector3 pos)
        {
            Transform root = new GameObject("WindChime").transform;
            root.SetParent(env, false); root.position = pos;
            Object.DestroyImmediate(CharacterVisuals.Part(root, "Cord", PrimitiveType.Cylinder, new Vector3(0f, -0.06f, 0f), Vector3.zero, new Vector3(0.004f, 0.06f, 0.004f), look.woodDark).GetComponent<Collider>());
            Object.DestroyImmediate(CharacterVisuals.Part(root, "Bell", PrimitiveType.Sphere, new Vector3(0f, -0.15f, 0f), Vector3.zero, new Vector3(0.075f, 0.065f, 0.075f), look.iron).GetComponent<Collider>());
            Transform sp = new GameObject("StripPivot").transform;
            sp.SetParent(root, false); sp.localPosition = new Vector3(0f, -0.19f, 0f);
            Object.DestroyImmediate(CharacterVisuals.Part(sp, "StripCord", PrimitiveType.Cylinder, new Vector3(0f, -0.05f, 0f), Vector3.zero, new Vector3(0.003f, 0.05f, 0.003f), look.woodDark).GetComponent<Collider>());
            EnvMesh.Box(sp, "Strip", new Vector3(0f, -0.2f, 0f), Vector3.zero, new Vector3(0.05f, 0.18f, 0.003f), look.paper, 0f);
            root.gameObject.AddComponent<WindChime>().strip = sp;
        }

        /// <summary>Лунные лучи сквозь кроны клёнов в дымке (LightShaft): от кроны по направлению лунного света к земле.</summary>
        static void AddLightShafts(Vector3 lightDir)
        {
            Transform root = new GameObject("LightShafts").transform;
            Vector3 d = lightDir.normalized;
            // (точка в кроне, ширина, длина, сила)
            Vector4[] shafts = {
                new Vector4(5.2f, 4.6f, -0.9f, 0.9f), new Vector4(5.9f, 4.9f, -0.2f, 0.55f), new Vector4(4.9f, 5.0f, 3.1f, 1.0f),
                new Vector4(5.8f, 5.3f, 3.7f, 0.6f), new Vector4(-5.0f, 4.8f, 4.0f, 0.8f), new Vector4(-5.2f, 4.4f, -1.3f, 0.7f) };
            for (int i = 0; i < shafts.Length; i++)
            {
                GameObject g = new GameObject("Shaft" + i);
                g.transform.SetParent(root, false);
                g.transform.position = new Vector3(shafts[i].x, shafts[i].y, shafts[i].z);
                LightShaft ls = g.AddComponent<LightShaft>();
                ls.dir = d; ls.width = shafts[i].w; ls.length = shafts[i].y / Mathf.Max(0.2f, -d.y) * 0.95f;
                ls.strength = 0.11f + 0.04f * (i % 3);
            }
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
        /// <summary>
        /// Огонь жаровни: языки пламени — флипбук 4×4 (каждая частица проживает язык от рождения до отрыва), под ними —
        /// яркое низкое ядро; искры-росчерки вылетают вверх и гаснут; над огнём вьётся дым. Мировое пространство — ветер
        /// (FlickerLight) клонит пламя и сносит дым.
        /// </summary>
        static ParticleSystem FireLayer(Transform parent, string name, Vector3 pos, Material mat, float life0, float life1, float spd0, float spd1, float size0, float size1, float rate, int max, Color c0, float radius, bool sheet)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life0, life1);
            main.startSpeed = new ParticleSystem.MinMaxCurve(spd0, spd1);
            main.startSize = new ParticleSystem.MinMaxCurve(size0, size1);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.22f, 0.22f);
            main.startColor = c0;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6f; sh.radius = radius;
            if (sheet)
            {
                var ts = ps.textureSheetAnimation; ts.enabled = true; ts.numTilesX = 4; ts.numTilesY = 4;
                ts.animation = ParticleSystemAnimationType.WholeSheet;
                ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            }
            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return ps;
        }

        static void AddFire(Transform parent, Look look)
        {
            Material fm = look.flameSheet != null ? look.flameSheet : look.fireSprite;
            bool sheet = look.flameSheet != null;
            // языки пламени: каждая частица — целый рваный язык из флипбука (основание у нижнего края кадра), почти не летит —
            // подъём уже нарисован в кадрах; немного и редко, иначе аддитивные языки сливаются в жёлтый прямоугольник
            ParticleSystem tongues = sheet
                ? FireLayer(parent, "Fire", new Vector3(0f, 1.2f, 0f), fm, 0.7f, 1.0f, 0.06f, 0.16f, 0.4f, 0.56f, 11f, 22, new Color(1f, 0.85f, 0.72f, 1f), 0.11f, true)
                : FireLayer(parent, "Fire", new Vector3(0f, 0.98f, 0f), fm, 0.55f, 0.85f, 0.34f, 0.58f, 0.28f, 0.46f, 26f, 44, new Color(1f, 0.85f, 0.72f, 1f), 0.14f, false);
            {
                var col = tongues.colorOverLifetime; col.enabled = true;
                Gradient g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.85f, 0.7f), 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.9f, 0.65f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var sz = tongues.sizeOverLifetime; sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.85f)));
                var noise = tongues.noise; noise.enabled = true; noise.strength = 0.14f; noise.frequency = 1.3f; noise.scrollSpeed = 0.6f;
            }
            // низкое яркое ядро над углями
            ParticleSystem core = FireLayer(parent, "FireCore", new Vector3(0f, 1.06f, 0f), fm, 0.35f, 0.5f, 0.05f, 0.12f, 0.15f, 0.21f, 8f, 12, new Color(1f, 0.95f, 0.85f, 0.5f), 0.08f, sheet);
            {
                var col = core.colorOverLifetime; col.enabled = true;
                Gradient g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
            }
            // искры: короткие росчерки вверх, гаснут оранжево-красными
            ParticleSystem es = FireLayer(parent, "Embers", new Vector3(0f, 1.05f, 0f), look.haloFire, 1.0f, 2.2f, 0.8f, 1.9f, 0.012f, 0.026f, 9f, 40, new Color(1f, 0.75f, 0.35f, 1f), 0.1f, false);
            {
                var esh = es.shape; esh.angle = 22f;
                var en = es.noise; en.enabled = true; en.strength = 0.7f; en.frequency = 0.9f;
                var ec = es.colorOverLifetime; ec.enabled = true;
                Gradient g2 = new Gradient();
                g2.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.08f), 1f) },
                           new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
                ec.color = g2;
                var er = es.GetComponent<ParticleSystemRenderer>(); er.renderMode = ParticleSystemRenderMode.Stretch; er.velocityScale = 0.04f; er.lengthScale = 1.5f;
                var em = es.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0.4f, 2, 5, -1, 1.7f) });   // иногда треск — пучок искр
            }
            // дым над огнём: тёмный, медленно растёт и тает
            if (look.smoke != null)
            {
                ParticleSystem sm = FireLayer(parent, "Smoke", new Vector3(0f, 1.45f, 0f), look.smoke, 2.6f, 4.0f, 0.25f, 0.45f, 0.25f, 0.45f, 3.5f, 20, new Color(0.16f, 0.14f, 0.13f, 0.32f), 0.08f, false);
                var col = sm.colorOverLifetime; col.enabled = true;
                Gradient g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var sz = sm.sizeOverLifetime; sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 2.6f)));
                var noise = sm.noise; noise.enabled = true; noise.strength = 0.25f; noise.frequency = 0.5f;
                var rot = sm.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            }
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
            // поленья в чаше (тёмные, крест-накрест) — огонь растёт из дров, а не из шара
            for (int k = 0; k < 3; k++)
                CharacterVisuals.Part(b, "Log" + k, PrimitiveType.Cylinder, new Vector3(0f, 1.03f, 0f), new Vector3(78f, k * 60f, 0f), new Vector3(0.05f, 0.17f, 0.05f), look.woodDark);
            AddFire(b, look);
            EnvMesh.Glow(b, "Halo", new Vector3(0f, 1.18f, 0f), 0.95f, look.haloFire, 0.18f);   // ореол меньше: был жёлтым шаром во всю жаровню

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
