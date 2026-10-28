#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace BladePath.EditorTools
{
    /// <summary>Набор материалов внешнего вида сцены.</summary>
    public class Look
    {
        public Material heroArmor, enemyArmor, cloth, skin, hair, lacquer, bronze, steel, handle;
        public Material flesh, tooth, maw, voidBlack, deadSkin, ember, tentacle, ronin;
        public Material stone, stoneDark, flame, moon, petal;
        public Material wood, woodDark, roof, plaster, stoneBlock, gravel, bark, blossom, shoji, glow, sky, paper, mist;
    }

    /// <summary>
    /// Создаёт материалы из текстур Assets/_Project/Art/Textures (их генерирует tools/gen_textures.py).
    /// Шейдер — Standard (Built-in): проект не использует URP. Если какой-то текстуры нет, материал остаётся однотонным.
    /// </summary>
    public static class VisualMaterials
    {
        const string TexFolder = "Assets/_Project/Art/Textures/";
        const string MatFolder = "Assets/_Project/Materials/";

        public static Look Create()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Materials")) AssetDatabase.CreateFolder("Assets/_Project", "Materials");

            Look l = new Look();
            l.heroArmor = Textured("Armor_Hero", "armor_albedo.png", "armor_normal.png", new Vector2(1f, 1.5f), Color.white, 0.35f, 0.55f);
            l.enemyArmor = Textured("Armor_Enemy", "armor_albedo.png", "armor_normal.png", new Vector2(1f, 1.5f), new Color(0.85f, 0.68f, 0.64f), 0.30f, 0.45f);
            l.cloth = Textured("Cloth", "cloth_albedo.png", "cloth_normal.png", new Vector2(3f, 3f), Color.white, 0f, 0.12f);
            l.skin = Solid("Skin", new Color(0.74f, 0.54f, 0.43f), 0f, 0.25f);
            l.hair = Solid("Hair", new Color(0.05f, 0.04f, 0.04f), 0f, 0.30f);
            l.lacquer = Textured("Lacquer_Red", "lacquer_albedo.png", null, Vector2.one, Color.white, 0.10f, 0.65f);
            l.bronze = Solid("Bronze", new Color(0.62f, 0.44f, 0.20f), 0.85f, 0.55f);
            l.steel = Textured("Steel", "steel_albedo.png", null, Vector2.one, Color.white, 0.90f, 0.80f);
            l.handle = Solid("Handle", new Color(0.07f, 0.05f, 0.05f), 0f, 0.30f);
            l.flesh = Textured("Flesh", "flesh_albedo.png", "flesh_normal.png", new Vector2(1f, 3f), Color.white, 0f, 0.70f);
            l.tooth = Solid("Tooth", new Color(0.92f, 0.86f, 0.76f), 0f, 0.45f);
            l.maw = Solid("Maw", new Color(0.12f, 0.01f, 0.03f), 0f, 0.60f);
            l.voidBlack = Solid("Void", new Color(0.01f, 0.01f, 0.012f), 0f, 0.10f);
            l.deadSkin = Solid("Skin_Dead", new Color(0.36f, 0.42f, 0.38f), 0f, 0.20f);
            l.tentacle = Solid("Tentacle", new Color(0.16f, 0.06f, 0.20f), 0f, 0.55f);
            l.ember = Solid("Ember", new Color(1f, 0.40f, 0.08f), 0f, 0.30f);
            l.ember.EnableKeyword("_EMISSION");
            l.ember.SetColor("_EmissionColor", new Color(2.4f, 0.8f, 0.1f));
            l.ember.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            l.ronin = Ronin();
            l.stone = Textured("Stone_Floor", "stone_albedo.png", "stone_normal.png", new Vector2(4f, 2f), Color.white, 0f, 0.35f);
            l.stoneDark = Solid("Stone_Dark", new Color(0.12f, 0.12f, 0.15f), 0f, 0.20f);
            // окружение v2: UV боксов в мировом масштабе (EnvMesh), поэтому tiling материалов = 1
            l.wood = Textured("Env_Wood", "wood_albedo.png", "wood_normal.png", Vector2.one, new Color(0.95f, 0.92f, 0.9f), 0f, 0.25f);
            l.woodDark = Textured("Env_WoodDark", "wood_albedo.png", "wood_normal.png", Vector2.one, new Color(0.42f, 0.38f, 0.36f), 0f, 0.3f);
            l.roof = Textured("Env_Roof", "roof_albedo.png", "roof_normal.png", Vector2.one, Color.white, 0.05f, 0.45f);
            l.plaster = Textured("Env_Plaster", "plaster_albedo.png", "plaster_normal.png", Vector2.one, new Color(0.9f, 0.9f, 0.92f), 0f, 0.1f);
            l.stoneBlock = Textured("Env_StoneBlock", "stoneblock_albedo.png", "stoneblock_normal.png", Vector2.one, Color.white, 0f, 0.25f);
            l.gravel = Textured("Env_Gravel", "gravel_albedo.png", "gravel_normal.png", Vector2.one, Color.white, 0f, 0.15f);
            l.bark = Textured("Env_Bark", "bark_albedo.png", "bark_normal.png", new Vector2(2f, 2f), Color.white, 0f, 0.1f);
            l.blossom = Textured("Env_Blossom", "blossom_albedo.png", null, new Vector2(3f, 3f), new Color(1f, 0.92f, 0.95f), 0f, 0.1f);
            l.shoji = Textured("Env_Shoji", "shoji_albedo.png", null, Vector2.one, new Color(0.9f, 0.85f, 0.75f), 0f, 0.2f);
            Texture2D se = Tex("shoji_emission.png", false);
            if (se != null)
            {
                l.shoji.SetTexture("_EmissionMap", se);
                l.shoji.EnableKeyword("_EMISSION");
                l.shoji.SetColor("_EmissionColor", new Color(1.1f, 0.8f, 0.45f));
                l.shoji.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            l.glow = Unlit("Env_Glow", new Color(1f, 0.72f, 0.32f));
            l.paper = Unlit("Env_Paper", new Color(1f, 0.78f, 0.45f));
            l.mist = SpriteLike("Env_Mist", new Color(0.55f, 0.62f, 0.8f));
            l.sky = Sky();
            l.flame = Unlit("Flame", new Color(1f, 0.55f, 0.12f));
            l.moon = Unlit("Moon", new Color(0.86f, 0.90f, 1f));
            l.petal = SpriteLike("Petal", new Color(1f, 0.72f, 0.82f));
            // Материалы зон тела подсвечиваются при попадании через emission (см. BodyZone):
            // включаем _EMISSION заранее, иначе нужный вариант шейдера вырежется из сборки.
            EnableRuntimeEmission(l.heroArmor);
            EnableRuntimeEmission(l.enemyArmor);
            EnableRuntimeEmission(l.cloth);
            EnableRuntimeEmission(l.skin);
            EnableRuntimeEmission(l.voidBlack);
            EnableRuntimeEmission(l.deadSkin);
            return l;
        }

        /// <summary>Материал игрока из Meshy: цвет + нормали + металл/гладкость (R = metallic, A = smoothness).</summary>
        static Material Ronin()
        {
            Material m = Textured("Ronin", "ronin_albedo.jpg", "ronin_normal.png", Vector2.one, Color.white, 1f, 0.5f);
            string mp = TexFolder + "ronin_metal_smooth.png";
            TextureImporter ti = AssetImporter.GetAtPath(mp) as TextureImporter;
            if (ti != null && ti.sRGBTexture)
            {
                ti.sRGBTexture = false;       // маска данных, не цвет
                ti.SaveAndReimport();
            }
            Texture2D mt = AssetDatabase.LoadAssetAtPath<Texture2D>(mp);
            if (mt != null)
            {
                m.SetTexture("_MetallicGlossMap", mt);
                m.EnableKeyword("_METALLICGLOSSMAP");
                m.SetFloat("_GlossMapScale", 1f);
            }
            EnableRuntimeEmission(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Небо-панорама (tools/gen_sky.py) на шейдере Skybox/Panoramic.</summary>
        static Material Sky()
        {
            string tp = TexFolder + "sky_night.png";
            TextureImporter ti = AssetImporter.GetAtPath(tp) as TextureImporter;
            if (ti != null && (ti.mipmapEnabled || ti.maxTextureSize < 2048))
            {
                ti.mipmapEnabled = false;
                ti.maxTextureSize = 2048;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.SaveAndReimport();
            }
            Shader sh = Shader.Find("Skybox/Panoramic");
            if (sh == null) return null;
            string path = MatFolder + "Sky_Night.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(sh);
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(tp));
            m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 1f));
            m.SetFloat("_Exposure", 1.3f);
            m.SetFloat("_Rotation", 0f);
            if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        static void EnableRuntimeEmission(Material m)
        {
            if (m == null) return;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
        }

        static Texture2D Tex(string file, bool isNormal)
        {
            string path = TexFolder + file;
            if (isNormal)
            {
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti != null && ti.textureType != TextureImporterType.NormalMap)
                {
                    ti.textureType = TextureImporterType.NormalMap;
                    ti.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Textured(string name, string albedo, string normal, Vector2 tiling, Color tint, float metallic, float smoothness)
        {
            Shader sh = Shader.Find("Standard");
            if (sh == null) return Solid(name, tint, metallic, smoothness);

            string path = MatFolder + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(sh);

            m.color = tint;
            Texture2D a = Tex(albedo, false);
            if (a != null) { m.mainTexture = a; m.mainTextureScale = tiling; }
            if (!string.IsNullOrEmpty(normal))
            {
                Texture2D n = Tex(normal, true);
                if (n != null)
                {
                    m.SetTexture("_BumpMap", n);
                    m.EnableKeyword("_NORMALMAP");
                    m.SetFloat("_BumpScale", 1f);
                }
            }
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Glossiness", smoothness);

            if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        static Material Solid(string name, Color color, float metallic, float smoothness)
        {
            string path = MatFolder + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew)
            {
                Shader sh = Shader.Find("Standard");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                m = new Material(sh);
            }
            m.color = color;
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Неосвещаемый материал: пламя, луна.</summary>
        static Material Unlit(string name, Color color)
        {
            string path = MatFolder + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew)
            {
                Shader sh = Shader.Find("Unlit/Color");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                m = new Material(sh);
            }
            m.color = color;
            if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Материал частиц: Sprites/Default учитывает цвет частицы.</summary>
        static Material SpriteLike(string name, Color color)
        {
            string path = MatFolder + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew)
            {
                Shader sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Unlit/Color");
                m = new Material(sh);
            }
            m.color = color;
            if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }
    }
}
#endif
