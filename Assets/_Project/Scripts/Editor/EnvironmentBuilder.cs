#if UNITY_EDITOR
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
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.05f;
            RenderSettings.fogColor = FogColor;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.17f, 0.18f, 0.26f);

            // лунный свет
            GameObject moonLight = new GameObject("Moonlight");
            Light l = moonLight.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(0.62f, 0.70f, 1f);
            l.intensity = 0.95f;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.8f;
            moonLight.transform.rotation = Quaternion.Euler(38f, -35f, 0f);

            // площадка
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "ArenaFloor";
            floor.transform.position = new Vector3(0f, -0.05f, 0f);
            floor.transform.localScale = new Vector3(7.2f, 0.1f, 5.4f);
            floor.GetComponent<Renderer>().sharedMaterial = look.stone;

            // ворота тории на заднем плане
            Transform torii = new GameObject("Torii").transform;
            CharacterVisuals.Part(torii, "PillarL", PrimitiveType.Cylinder, new Vector3(0.7f, 1.3f, 3.6f), Vector3.zero, new Vector3(0.22f, 1.3f, 0.22f), look.lacquer);
            CharacterVisuals.Part(torii, "PillarR", PrimitiveType.Cylinder, new Vector3(2.7f, 1.3f, 3.6f), Vector3.zero, new Vector3(0.22f, 1.3f, 0.22f), look.lacquer);
            CharacterVisuals.Part(torii, "Kasagi", PrimitiveType.Cube, new Vector3(1.7f, 2.72f, 3.6f), Vector3.zero, new Vector3(3.4f, 0.16f, 0.28f), look.stoneDark);
            CharacterVisuals.Part(torii, "Shimaki", PrimitiveType.Cube, new Vector3(1.7f, 2.60f, 3.6f), Vector3.zero, new Vector3(3.1f, 0.07f, 0.24f), look.lacquer);
            CharacterVisuals.Part(torii, "Nuki", PrimitiveType.Cube, new Vector3(1.7f, 2.25f, 3.6f), Vector3.zero, new Vector3(2.2f, 0.12f, 0.16f), look.lacquer);

            // зал храма за ареной
            Transform hall = new GameObject("TempleHall").transform;
            CharacterVisuals.Part(hall, "Base", PrimitiveType.Cube, new Vector3(-0.8f, 0.8f, 5.4f), Vector3.zero, new Vector3(7f, 1.6f, 0.8f), look.stoneDark);
            CharacterVisuals.Part(hall, "Roof", PrimitiveType.Cube, new Vector3(-0.8f, 1.85f, 5.3f), Vector3.zero, new Vector3(8f, 0.22f, 1.6f), look.stoneDark);
            CharacterVisuals.Part(hall, "RoofTop", PrimitiveType.Cube, new Vector3(-0.8f, 2.15f, 5.4f), Vector3.zero, new Vector3(6f, 0.3f, 0.8f), look.stoneDark);

            // пагода вдали
            Transform pagoda = new GameObject("Pagoda").transform;
            for (int i = 0; i < 4; i++)
            {
                float w = 1.2f - 0.2f * i;
                float y = 0.9f + i * 1.0f;
                CharacterVisuals.Part(pagoda, "Tier" + i, PrimitiveType.Cube, new Vector3(-3.4f, y, 7.0f), Vector3.zero, new Vector3(w, 0.7f, w), look.stoneDark);
                CharacterVisuals.Part(pagoda, "Eave" + i, PrimitiveType.Cube, new Vector3(-3.4f, y + 0.45f, 7.0f), Vector3.zero, new Vector3(w + 0.8f, 0.1f, w + 0.8f), look.stoneDark);
            }
            CharacterVisuals.Part(pagoda, "Finial", PrimitiveType.Cylinder, new Vector3(-3.4f, 5.2f, 7.0f), Vector3.zero, new Vector3(0.05f, 0.5f, 0.05f), look.bronze);

            // луна
            GameObject moon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            moon.name = "Moon";
            Object.DestroyImmediate(moon.GetComponent<Collider>());
            moon.transform.position = new Vector3(-4.2f, 4.8f, 11f);
            moon.transform.localScale = Vector3.one * 1.8f;
            moon.GetComponent<Renderer>().sharedMaterial = look.moon;

            // жаровни с мерцающим огнём
            AddBrazier(look, new Vector3(-3.2f, 0f, 2.2f));
            AddBrazier(look, new Vector3(3.2f, 0f, 2.2f));

            AddPetals(look);
        }

        static void AddBrazier(Look look, Vector3 pos)
        {
            Transform b = new GameObject("Brazier").transform;
            b.position = pos;
            b.gameObject.AddComponent<ArenaObstacle>().radius = 0.32f;
            CharacterVisuals.Part(b, "Stand", PrimitiveType.Cylinder, new Vector3(0f, 0.45f, 0f), Vector3.zero, new Vector3(0.30f, 0.45f, 0.30f), look.stoneDark);
            GameObject flame = CharacterVisuals.Part(b, "Flame", PrimitiveType.Sphere, new Vector3(0f, 1.08f, 0f), Vector3.zero, new Vector3(0.20f, 0.26f, 0.20f), look.flame);

            GameObject lightObj = new GameObject("FireLight");
            lightObj.transform.SetParent(b, false);
            lightObj.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            Light fl = lightObj.AddComponent<Light>();
            fl.type = LightType.Point;
            fl.color = new Color(1f, 0.6f, 0.25f);
            fl.range = 5f;
            fl.intensity = 1.7f;

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
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.startColor = new Color(1f, 0.72f, 0.82f, 0.9f);
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
        }
    }
}
#endif
