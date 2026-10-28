#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Tools → Blade Path → Build Stage 1 Prototype Scene.
    /// Собирает тестовую сцену программно (сцена не хранится «вручную»): арена, боковая камера, два бойца
    /// из примитивов с коллайдерами зон тела, по катане, системы боя и отладки.
    /// Перезапуск пересоздаёт сцену заново (Assets/_Project/Scenes/Prototype_Stage1.unity).
    /// </summary>
    public static class PrototypeSceneBuilder
    {
        const string SceneFolder = "Assets/_Project/Scenes";
        const string MatFolder = "Assets/_Project/Materials";
        public const string ScenePath = SceneFolder + "/Prototype_Stage1.unity";

        [MenuItem("Tools/Blade Path/Build Stage 1 Prototype Scene")]
        public static void Build()
        {
            EnsureFolder("Assets/_Project", "Scenes");
            EnsureFolder("Assets/_Project", "Materials");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Look look = VisualMaterials.Create();
            BuildCamera();
            EnvironmentBuilder.Build(look);

            // игрок слева, смотрит в +X; противник справа, смотрит в -X. Камера видит бойцов сбоку (вдоль мировой Z).
            // Оружие у игрока в правой руке (ближе к камере), у противника зеркально — тоже со стороны камеры.
            FighterController player = BuildFighter("Player", -1.0f, +1, +1, look, false);
            FighterController enemy = BuildFighter("Enemy", +1.0f, -1, -1, look, true);
            player.opponent = enemy;
            enemy.opponent = player;

            BladeTrajectorySampler sp = player.weapon.GetComponent<BladeTrajectorySampler>();
            BladeTrajectorySampler se = enemy.weapon.GetComponent<BladeTrajectorySampler>();
            BladeTrajectorySampler[] samplers = new BladeTrajectorySampler[] { sp, se };

            GameObject systems = new GameObject("CombatSystems");
            CombatResolver resolver = systems.AddComponent<CombatResolver>();
            resolver.samplers = samplers;

            CombatDebugVisualizer viz = systems.AddComponent<CombatDebugVisualizer>();
            viz.samplers = samplers;
            viz.fighters = new FighterController[] { player, enemy };
            viz.resolver = resolver;
            viz.lineMaterial = MakeLineMaterial();
            viz.visible = false;   // зоны и журнал только по запросу (пауза → ОТЛАДКА / F1)

            DuelManager duel = systems.AddComponent<DuelManager>();
            duel.player = player;
            duel.enemy = enemy;
            DuelHud hud = systems.AddComponent<DuelHud>();
            hud.duel = duel;
            EnemyAI ai = systems.AddComponent<EnemyAI>();
            ai.self = enemy;
            ai.target = player;

            PrototypeInput input = systems.AddComponent<PrototypeInput>();
            input.player = player;
            input.enemy = enemy;

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("Blade Path: сцена собрана → " + ScenePath + ". Нажми Play.");
        }

        // ---------------------------------------------------------------- сцена

        static void BuildCamera()
        {
            GameObject cam = new GameObject("Main Camera");
            cam.tag = "MainCamera";
            Camera c = cam.AddComponent<Camera>();
            cam.AddComponent<AudioListener>();
            cam.AddComponent<CameraOrbit>();   // облёт камеры свайпом, зум щипком (позицию задаёт сам компонент)
            c.clearFlags = CameraClearFlags.Skybox;   // ночное небо из EnvironmentBuilder
            c.backgroundColor = EnvironmentBuilder.FogColor;
            c.farClipPlane = 80f;
            c.fieldOfView = 32f;
            cam.transform.position = new Vector3(0f, 1.15f, -5.2f);
            cam.transform.rotation = Quaternion.identity; // смотрит вдоль +Z
        }

        static void BuildArena()
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "ArenaFloor";
            floor.transform.position = new Vector3(0f, -0.05f, 0f);
            floor.transform.localScale = new Vector3(6.4f, 0.1f, 3f);
            floor.GetComponent<Renderer>().sharedMaterial = MakeMaterial("Arena_Floor", new Color(0.22f, 0.21f, 0.20f));
        }

        // ---------------------------------------------------------------- боец

        static FighterController BuildFighter(string name, float x, int facing, int handSide, Look look, bool undead)
        {
            Material armor = undead ? look.enemyArmor : look.heroArmor;
            Material headMat = undead ? look.voidBlack : look.skin;
            GameObject root = new GameObject(name);
            root.transform.position = new Vector3(x, 0f, 0f);
            // локальная +Z бойца = направление взгляда
            root.transform.rotation = Quaternion.Euler(0f, facing > 0 ? 90f : -90f, 0f);

            FighterController f = root.AddComponent<FighterController>();
            f.displayName = name;
            f.facing = facing;

            List<BodyZone> zones = new List<BodyZone>();
            zones.Add(MakeZone(f, BodyZoneId.Head,     PrimitiveType.Sphere,  new Vector3(0f, 1.62f, 0f),       0.11f, 0f,    headMat));
            zones.Add(MakeZone(f, BodyZoneId.Torso,    PrimitiveType.Capsule, new Vector3(0f, 1.18f, 0f),       0.18f, 0.62f, armor));
            zones.Add(MakeZone(f, BodyZoneId.ArmLeft,  PrimitiveType.Capsule, new Vector3(-0.24f, 1.22f, 0f),   0.06f, 0.55f, armor));
            zones.Add(MakeZone(f, BodyZoneId.ArmRight, PrimitiveType.Capsule, new Vector3(0.24f, 1.22f, 0f),    0.06f, 0.55f, armor));
            zones.Add(MakeZone(f, BodyZoneId.LegLeft,  PrimitiveType.Capsule, new Vector3(-0.11f, 0.43f, 0f),   0.085f, 0.86f, look.cloth));
            zones.Add(MakeZone(f, BodyZoneId.LegRight, PrimitiveType.Capsule, new Vector3(0.11f, 0.43f, 0f),    0.085f, 0.86f, look.cloth));
            f.zones = zones.ToArray();

            // Оружие: pivot (рука) → bladeRoot (основание клинка, +Z вдоль клинка)
            GameObject pivot = new GameObject("WeaponPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0.06f * handSide, 0.80f, 0.46f);   // руки перед солнечным сплетением, руки почти вытянуты (ближе/ниже — «тираннозавр»)

            GameObject bladeRoot = new GameObject("BladeRoot");
            bladeRoot.transform.SetParent(pivot.transform, false);

            MakeBox("Blade",  bladeRoot.transform, new Vector3(0f, 0f, 0.40f),   new Vector3(0.008f, 0.032f, 0.80f), look.steel);
            CharacterVisuals.Part(bladeRoot.transform, "Handle", PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.125f), new Vector3(90f, 0f, 0f), new Vector3(0.030f, 0.125f, 0.030f), look.handle);
            CharacterVisuals.Part(bladeRoot.transform, "HandleWrap", PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.125f), new Vector3(90f, 0f, 0f), new Vector3(0.034f, 0.06f, 0.034f), look.lacquer);
            CharacterVisuals.Part(bladeRoot.transform, "Tsuba", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.008f), new Vector3(90f, 0f, 0f), new Vector3(0.095f, 0.004f, 0.095f), look.bronze);

            WeaponController w = pivot.AddComponent<WeaponController>();
            w.owner = f;
            w.pivot = pivot.transform;
            w.bladeRoot = bladeRoot.transform;
            w.bladeLength = 0.80f;
            w.restEuler = new Vector3(22f, 0f, 0f);   // чудан: клинок вперёд, остриё чуть выше рук, смотрит противнику в горло; хват в досягаемости рук
            w.yawSign = handSide;   // зеркалит диагональ удара для руки с другой стороны
            pivot.AddComponent<BladeTrajectorySampler>();
            f.weapon = w;
            f.projectileMaterial = MakeEnergyMaterial();

            if (!HeroModelBuilder.TryAttach(f, bladeRoot.transform, look, undead))
            {
                if (undead) CharacterVisuals.DressUndead(root.transform, look);
                else CharacterVisuals.DressHero(root.transform, look);
            }

            return f;
        }

        static BodyZone MakeZone(FighterController owner, BodyZoneId id, PrimitiveType shape,
                                 Vector3 localPos, float radius, float height, Material mat)
        {
            GameObject zone = new GameObject("Zone_" + id);
            zone.transform.SetParent(owner.transform, false);
            zone.transform.localPosition = localPos;

            // коллайдер — на неотмасштабированном объекте, чтобы размеры были ровно такие, какие заданы
            if (shape == PrimitiveType.Sphere)
            {
                SphereCollider sc = zone.AddComponent<SphereCollider>();
                sc.radius = radius;
            }
            else
            {
                CapsuleCollider cc = zone.AddComponent<CapsuleCollider>();
                cc.direction = 1; // вдоль Y
                cc.radius = radius;
                cc.height = height;
            }

            // визуал — отдельный дочерний примитив без коллайдера
            GameObject vis = GameObject.CreatePrimitive(shape);
            vis.name = "Visual";
            Object.DestroyImmediate(vis.GetComponent<Collider>());
            vis.transform.SetParent(zone.transform, false);
            vis.transform.localPosition = Vector3.zero;
            if (shape == PrimitiveType.Sphere) vis.transform.localScale = Vector3.one * (radius * 2f);
            else vis.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f); // примитив-капсула: радиус 0.5, высота 2
            Renderer rend = vis.GetComponent<Renderer>();
            rend.sharedMaterial = mat;

            BodyZone bz = zone.AddComponent<BodyZone>();
            bz.zoneId = id;
            bz.owner = owner;
            bz.flashRenderer = rend;
            return bz;
        }

        static void MakeBox(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = name;
            Object.DestroyImmediate(b.GetComponent<Collider>()); // у клинка нет Unity-коллайдеров: попадания считает сэмплер
            b.transform.SetParent(parent, false);
            b.transform.localPosition = localPos;
            b.transform.localScale = scale;
            b.GetComponent<Renderer>().sharedMaterial = mat;
        }

        // ---------------------------------------------------------------- вспомогательное

        static Material MakeMaterial(string name, Color color)
        {
            string path = MatFolder + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) { existing.color = color; return existing; }

            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            Material m = new Material(sh);
            m.color = color;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>Материал отладочных линий: должен учитывать цвет вершин, поэтому Sprites/Default.</summary>
        static Material MakeLineMaterial()
        {
            string path = MatFolder + "/DebugLines.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            Material m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>Материал энергошара: Sprites/Default, оранжевый, светится за счёт яркого цвета.</summary>
        static Material MakeEnergyMaterial()
        {
            string path = MatFolder + "/EnergyBall.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            Material m = new Material(sh);
            m.color = new Color(1f, 0.55f, 0.1f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child)) AssetDatabase.CreateFolder(parent, child);
        }

        static void AddSceneToBuildSettings(string path)
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++) if (scenes[i].path == path) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif
