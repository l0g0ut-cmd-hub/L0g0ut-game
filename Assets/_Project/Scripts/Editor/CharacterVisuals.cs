#if UNITY_EDITOR
using UnityEngine;

namespace BladePath.EditorTools
{
    /// <summary>
    /// «Одевает» бойца: броня, шлем, пояс, наплечники и т.д. из примитивов с текстурами.
    /// Всё это ТОЛЬКО ВИЗУАЛ без коллайдеров: зоны поражения (BodyZone) не меняются.
    /// Координаты — локальные у бойца: +Z вперёд, +X вправо от бойца, Y вверх, рост ≈ 1,75 м.
    /// </summary>
    public static class CharacterVisuals
    {
        public static GameObject Part(Transform parent, string name, PrimitiveType shape, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            GameObject g = GameObject.CreatePrimitive(shape);
            g.name = name;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localEulerAngles = euler;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            return g;
        }

        static Transform VisualsRoot(Transform fighter)
        {
            GameObject v = new GameObject("Visuals");
            v.transform.SetParent(fighter, false);
            return v.transform;
        }

        /// <summary>Общая броня: наплечники, нагрудник, пояс, юбка, колени, голени, ступни.</summary>
        static void DressArmor(Transform v, Look look, Material armor)
        {
            // наплечники: три слоя пластин, чуть наклонены наружу
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 3; i++)
                {
                    Part(v, "Sode" + side + "_" + i, PrimitiveType.Cube,
                         new Vector3(side * (0.27f + 0.012f * i), 1.45f - 0.045f * i, 0f),
                         new Vector3(0f, 0f, -side * (10f + 4f * i)),
                         new Vector3(0.20f - 0.012f * i, 0.04f, 0.20f), armor);
                }
            }
            // нагрудная пластина и красный шнур поперёк груди
            Part(v, "Chest", PrimitiveType.Cube, new Vector3(0f, 1.27f, 0.172f), Vector3.zero, new Vector3(0.30f, 0.34f, 0.05f), armor);
            Part(v, "ChestCord", PrimitiveType.Cube, new Vector3(0f, 1.38f, 0.200f), Vector3.zero, new Vector3(0.30f, 0.018f, 0.012f), look.lacquer);

            // пояс (оби)
            Part(v, "Obi", PrimitiveType.Cylinder, new Vector3(0f, 0.93f, 0f), Vector3.zero, new Vector3(0.36f, 0.035f, 0.34f), look.lacquer);

            // юбка из пластин (кусадзури)
            Part(v, "SkirtFront", PrimitiveType.Cube, new Vector3(0f, 0.78f, 0.150f), new Vector3(-8f, 0f, 0f), new Vector3(0.26f, 0.26f, 0.03f), armor);
            Part(v, "SkirtBack", PrimitiveType.Cube, new Vector3(0f, 0.78f, -0.150f), new Vector3(8f, 0f, 0f), new Vector3(0.26f, 0.26f, 0.03f), armor);
            Part(v, "SkirtLeft", PrimitiveType.Cube, new Vector3(-0.150f, 0.78f, 0f), new Vector3(0f, 0f, 8f), new Vector3(0.03f, 0.26f, 0.24f), armor);
            Part(v, "SkirtRight", PrimitiveType.Cube, new Vector3(0.150f, 0.78f, 0f), new Vector3(0f, 0f, -8f), new Vector3(0.03f, 0.26f, 0.24f), armor);

            // колени, голени, ступни
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 0.11f;
                Part(v, "Knee" + side, PrimitiveType.Sphere, new Vector3(x, 0.50f, 0.075f), Vector3.zero, new Vector3(0.11f, 0.11f, 0.11f), armor);
                Part(v, "Shin" + side, PrimitiveType.Cube, new Vector3(x, 0.29f, 0.085f), Vector3.zero, new Vector3(0.11f, 0.30f, 0.035f), armor);
                Part(v, "Foot" + side, PrimitiveType.Cube, new Vector3(x, 0.035f, 0.07f), Vector3.zero, new Vector3(0.11f, 0.07f, 0.25f), look.handle);
            }
        }

        /// <summary>Главный герой: волосы, борода, ножны на поясе.</summary>
        public static void DressHero(Transform fighter, Look look)
        {
            Transform v = VisualsRoot(fighter);
            DressArmor(v, look, look.heroArmor);

            Part(v, "Neck", PrimitiveType.Cylinder, new Vector3(0f, 1.50f, 0f), Vector3.zero, new Vector3(0.09f, 0.04f, 0.09f), look.skin);
            Part(v, "Hair", PrimitiveType.Sphere, new Vector3(0f, 1.665f, -0.005f), Vector3.zero, new Vector3(0.125f, 0.105f, 0.13f), look.hair);
            Part(v, "Beard", PrimitiveType.Sphere, new Vector3(0f, 1.565f, 0.045f), Vector3.zero, new Vector3(0.10f, 0.075f, 0.085f), look.hair);
            Part(v, "Headband", PrimitiveType.Cylinder, new Vector3(0f, 1.675f, 0f), Vector3.zero, new Vector3(0.235f, 0.012f, 0.245f), look.lacquer);

            // ножны на левом бедре (клинок, которым он бьёт, — в руке, это запасные ножны)
            Part(v, "Saya", PrimitiveType.Cube, new Vector3(-0.19f, 0.92f, -0.20f), new Vector3(-8f, 16f, 0f), new Vector3(0.035f, 0.035f, 0.78f), look.handle);
            Part(v, "SayaCord", PrimitiveType.Cube, new Vector3(-0.17f, 0.95f, 0.10f), new Vector3(-8f, 16f, 0f), new Vector3(0.04f, 0.04f, 0.05f), look.lacquer);
        }

        /// <summary>Мёртвый самурай: шлем с рогами, лохмотья, щупальца с пастями.</summary>
        public static void DressUndead(Transform fighter, Look look)
        {
            Transform v = VisualsRoot(fighter);
            DressArmor(v, look, look.enemyArmor);

            // шлем: купол, поля, затылочные пластины, рога
            Part(v, "Helmet", PrimitiveType.Sphere, new Vector3(0f, 1.665f, 0f), Vector3.zero, new Vector3(0.30f, 0.26f, 0.32f), look.enemyArmor);
            Part(v, "Brim", PrimitiveType.Cylinder, new Vector3(0f, 1.60f, 0f), Vector3.zero, new Vector3(0.34f, 0.012f, 0.34f), look.enemyArmor);
            for (int i = 0; i < 3; i++)
            {
                Part(v, "Shikoro" + i, PrimitiveType.Cube, new Vector3(0f, 1.55f - 0.045f * i, -0.10f - 0.012f * i),
                     new Vector3(-12f, 0f, 0f), new Vector3(0.27f + 0.03f * i, 0.045f, 0.035f), look.enemyArmor);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Part(v, "Horn" + side, PrimitiveType.Cube, new Vector3(side * 0.055f, 1.79f, 0.10f), new Vector3(0f, 0f, -side * 25f),
                     new Vector3(0.012f, 0.20f, 0.008f), look.bronze);
            }
            Part(v, "Medallion", PrimitiveType.Sphere, new Vector3(0f, 1.705f, 0.14f), Vector3.zero, new Vector3(0.04f, 0.04f, 0.02f), look.bronze);

            // лохмотья на подоле
            for (int i = -2; i <= 2; i++)
            {
                Part(v, "Rag" + i, PrimitiveType.Cube, new Vector3(i * 0.06f, 0.60f, 0.168f), new Vector3(0f, 0f, i * 3f),
                     new Vector3(0.035f, 0.18f, 0.008f), look.cloth);
            }

            // щупальца: из-под доспехов, у каждого пасть с зубами
            AddTentacle(v, look, "Tentacle0", new Vector3(0.30f, 1.40f, -0.02f), new Vector3(0.9f, 0.3f, 0.3f), 0.95f, 0.0f);
            AddTentacle(v, look, "Tentacle1", new Vector3(-0.30f, 1.40f, -0.02f), new Vector3(-0.9f, 0.3f, 0.3f), 0.95f, 1.3f);
            AddTentacle(v, look, "Tentacle2", new Vector3(0.26f, 1.15f, -0.08f), new Vector3(0.8f, -0.2f, 0.5f), 0.85f, 2.1f);
            AddTentacle(v, look, "Tentacle3", new Vector3(-0.26f, 1.15f, -0.08f), new Vector3(-0.8f, -0.2f, 0.5f), 0.85f, 3.4f);
            AddTentacle(v, look, "Tentacle4", new Vector3(0.12f, 1.25f, -0.18f), new Vector3(0.6f, 0.2f, -0.8f), 0.90f, 4.2f);
            AddTentacle(v, look, "Tentacle5", new Vector3(-0.12f, 1.25f, -0.18f), new Vector3(-0.6f, 0.2f, -0.8f), 0.90f, 5.0f);
        }

        static void AddTentacle(Transform parent, Look look, string name, Vector3 anchor, Vector3 dir, float length, float phase)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = anchor;

            const int n = 10;
            Transform[] segs = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float diameter = Mathf.Lerp(0.09f, 0.045f, t);
                GameObject s = Part(root.transform, "Seg" + i, PrimitiveType.Sphere, Vector3.zero, Vector3.zero, Vector3.one * diameter, look.flesh);
                segs[i] = s.transform;
            }

            // пасть на кончике: светлое зубчатое кольцо и тёмная глубина (масштабы заданы относительно конечного сегмента)
            Transform tip = segs[n - 1];
            Part(tip, "MawTeeth", PrimitiveType.Sphere, Vector3.zero, Vector3.zero, Vector3.one * 2.5f, look.tooth);
            Part(tip, "MawInside", PrimitiveType.Sphere, dir.normalized * 0.45f, Vector3.zero, Vector3.one * 1.6f, look.maw);

            TentacleWiggle tw = root.AddComponent<TentacleWiggle>();
            tw.segments = segs;
            tw.direction = dir;
            tw.length = length;
            tw.amplitude = 0.10f;
            tw.speed = 1.8f + 0.15f * phase;
            tw.phase = phase;
            tw.curl = 0.25f;
        }
    }
}
#endif
