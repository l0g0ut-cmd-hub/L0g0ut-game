using System.Collections.Generic;
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
        public Material wood, woodDark, roof, plaster, stoneBlock, gravel, bark, blossom, shoji, glow, sky, skyDay, paper, mist, haloWarm, haloFire, haloMoon, bamboo, bambooLeaf, moss, fireSprite, banner, puddle, tsuka, iron, mapleCrown, bambooFoliage, fallenLeaves, grass, cedarFoliage;
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
            l.steel = KatanaBlade();
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
            // мостовая из неправильных плит (исидатами, процедурная 1024²): квадратная плитка 2,4 м на площадке 7,2×5,4;
            // гладкость — по карте: мокрые плиты и лужицы блестят, швы с землёй и мхом матовые
            l.stone = Textured("Stone_Floor", "flagstone_albedo.png", "flagstone_normal.png", new Vector2(4.2f, 3.15f), new Color(0.62f, 0.62f, 0.66f), 0f, 0.62f);
            Gloss(l.stone, "flagstone_ms.png", 0.85f);   // в дождь Weather поднимает до 1
            Aniso("flagstone_albedo.png", 4); Aniso("flagstone_normal.png", 4);
            l.stoneDark = Solid("Stone_Dark", new Color(0.12f, 0.12f, 0.15f), 0f, 0.20f);
            // окружение v2: UV боксов в мировом масштабе (EnvMesh), поэтому tiling материалов = 1
            l.wood = Textured("Env_Wood", "wood_albedo.png", "wood_normal.png", Vector2.one, new Color(0.95f, 0.92f, 0.9f), 0f, 0.25f);
            l.woodDark = Textured("Env_WoodDark", "wood_albedo.png", "wood_normal.png", Vector2.one, new Color(0.42f, 0.38f, 0.36f), 0f, 0.3f);
            l.roof = Textured("Env_Roof", "roof_albedo.png", "roof_normal.png", Vector2.one, Color.white, 0.05f, 0.45f);
            l.plaster = Textured("Env_Plaster", "plaster_albedo.png", "plaster_normal.png", Vector2.one, new Color(0.9f, 0.9f, 0.92f), 0f, 0.1f);
            l.stoneBlock = Textured("Env_StoneBlock", "stoneblock_albedo.png", "stoneblock_normal.png", Vector2.one, Color.white, 0f, 0.25f);
            Gloss(l.stoneBlock, "stoneblock_ms.png", 1f);   // тёсаный гранит: следы зубила, фаски, лишайник (tools: texgen/stoneblock.py)
            l.gravel = Textured("Env_Gravel", "gravel_albedo.png", "gravel_normal.png", Vector2.one, Color.white, 0f, 0.15f);
            l.bark = Textured("Env_Bark", "bark_albedo.png", "bark_normal.png", new Vector2(2f, 2f), Color.white, 0f, 0.1f);
            // крона клёна: карточки гроздей листьев с альфа-срезом (tools/texgen/maple_cluster.py)
            l.bambooFoliage = Cutout("Env_BambooFoliage", "bamboo_leaves.png", Color.white, 0.42f, new Color(0.006f, 0.012f, 0.004f));
            l.mapleCrown = Cutout("Env_MapleCrown", "maple_cluster.png", new Color(0.92f, 0.92f, 0.92f), 0.42f, new Color(0.05f, 0.006f, 0.004f));
            l.cedarFoliage = Cutout("Env_CedarFoliage", "bamboo_leaves.png", new Color(0.26f, 0.36f, 0.3f), 0.35f, new Color(0.002f, 0.005f, 0.003f));   // хвоя криптомерии: те же листья-карточки, темнее и холоднее
            l.fallenLeaves = Cutout("Env_FallenLeaves", "fallen_leaves.png", new Color(1f, 1f, 1f), 0.5f, new Color(0.03f, 0.004f, 0.003f));
            l.grass = Cutout("Env_Grass", "grass_tuft.png", new Color(0.9f, 0.95f, 0.9f), 0.45f, new Color(0.004f, 0.007f, 0.003f));
            l.blossom = Textured("Env_Blossom", "blossom_albedo.png", null, new Vector2(3f, 3f), new Color(0.95f, 0.85f, 0.85f), 0f, 0.1f);   // кроны — текстура красного клёна (лист 2)
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
            l.mist = SpriteLike("Env_Mist", Color.white);
            l.sky = Sky();
            l.skyDay = SkyDay();
            Texture2D halo = HaloTexture();
            Texture2D fireTex = Tex("fire_sprite.png", false);
            l.fireSprite = Additive("Fire_Sprite", fireTex != null ? fireTex : halo, new Color(0.6f, 0.55f, 0.5f, 0.7f));
            l.banner = Cutout("Env_Banner", "banner.png");
            l.puddle = Puddle();
            l.tsuka = Textured("Katana_Tsuka", "tsuka_albedo.png", null, Vector2.one, Color.white, 0f, 0.3f);
            l.iron = Solid("Katana_Iron", new Color(0.09f, 0.08f, 0.08f), 0.7f, 0.45f);
            l.bamboo = Solid("Env_Bamboo", new Color(0.36f, 0.48f, 0.22f), 0f, 0.55f);
            l.bambooLeaf = Solid("Env_BambooLeaf", new Color(0.16f, 0.30f, 0.12f), 0f, 0.2f);
            l.moss = Textured("Env_MossRock", "stoneblock_albedo.png", "stoneblock_normal.png", new Vector2(0.5f, 0.5f), new Color(0.62f, 0.72f, 0.58f), 0f, 0.15f);
            l.haloWarm = Additive("Halo_Warm", halo, new Color(1f, 0.62f, 0.28f, 0.55f));
            l.haloFire = Additive("Halo_Fire", halo, new Color(1f, 0.45f, 0.12f, 0.75f));
            l.haloMoon = Additive("Halo_Moon", halo, new Color(0.55f, 0.65f, 1f, 0.35f));
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
            if (ti != null && (ti.mipmapEnabled || ti.maxTextureSize < 4096))
            {
                ti.mipmapEnabled = false;
                ti.maxTextureSize = 4096;   // панорама 4096: при FOV 32° на экран приходится ~360 px неба — меньше уже «мыло»
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

        /// <summary>Дневное небо-панорама (tools/gen_sky_day.py): ясный день, солнце высоко за левым плечом камеры боя.</summary>
        static Material SkyDay()
        {
            string tp = TexFolder + "sky_day.png";
            TextureImporter ti = AssetImporter.GetAtPath(tp) as TextureImporter;
            if (ti == null) return null;
            if (ti.mipmapEnabled || ti.maxTextureSize < 4096)
            {
                ti.mipmapEnabled = false; ti.maxTextureSize = 4096; ti.wrapMode = TextureWrapMode.Repeat;
                ti.SaveAndReimport();
            }
            Shader sh = Shader.Find("Skybox/Panoramic");
            if (sh == null) return null;
            string path = MatFolder + "Sky_Day.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(sh);
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(tp));
            m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 1f));
            m.SetFloat("_Exposure", 1.0f);
            m.SetFloat("_Rotation", 0f);
            if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Мягкий радиальный ореол (сохраняется ассетом, чтобы попал в сборку).</summary>
        static Texture2D HaloTexture()
        {
            string path = MatFolder + "HaloTex.asset";
            Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t != null) return t;
            const int n = 128;
            t = new Texture2D(n, n, TextureFormat.RGBA32, true);
            t.wrapMode = TextureWrapMode.Clamp;
            Color[] px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - (n - 1) * 0.5f) / (n * 0.5f), dy = (y - (n - 1) * 0.5f) / (n * 0.5f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (0.35f + 0.65f * a * a);   // яркое ядро + длинный мягкий хвост
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            t.SetPixels(px); t.Apply();
            AssetDatabase.CreateAsset(t, path);
            return t;
        }

        /// <summary>Standard в режиме Cutout: рваная ткань знамён с прозрачными краями.</summary>
        static Material Cutout(string name, string albedo)
        {
            Material m = Textured(name, albedo, null, Vector2.one, Color.white, 0f, 0.15f);
            m.SetFloat("_Mode", 1f);
            m.SetFloat("_Cutoff", 0.45f);
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            m.SetInt("_ZWrite", 1);
            m.EnableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            m.doubleSidedGI = true;
            Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(TexFolder + albedo);
            if (t != null) { t.wrapMode = TextureWrapMode.Clamp; }
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Лужа: почти чёрная, зеркально гладкая, полупрозрачная по форме кляксы (Standard, режим Fade).</summary>
        static Material Puddle()
        {
            Material m = Textured("Env_Puddle", "blood_splat.png", null, Vector2.one, new Color(0.05f, 0.055f, 0.07f, 0.75f), 0.2f, 0.97f);
            m.SetFloat("_Mode", 2f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 10;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Additive(string name, Texture2D tex, Color tint)
        {
            Shader sh = Shader.Find("Legacy Shaders/Particles/Additive");
            if (sh == null) sh = Shader.Find("Particles/Additive");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            string path = MatFolder + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(sh);
            m.mainTexture = tex;
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", tint); else m.color = tint;
            if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        const string SamuraiTex = "Assets/_Project/Art/Samurai/";
        /// <summary>Цвета деталей самурая, пока нет текстур художника (и тон-подкраска поверх них).</summary>
        static readonly Dictionary<string, Color> SamuraiPalette = new Dictionary<string, Color> {
            { "Char", new Color(0.62f, 0.47f, 0.38f) }, { "Helmet", new Color(0.10f, 0.09f, 0.10f) }, { "Horn", new Color(0.78f, 0.60f, 0.22f) },
            { "Helmetdeko", new Color(0.78f, 0.60f, 0.22f) }, { "Helmetband", new Color(0.72f, 0.69f, 0.63f) }, { "Chestplate", new Color(0.36f, 0.07f, 0.06f) },
            { "ChestplateDeko", new Color(0.78f, 0.60f, 0.22f) }, { "Shoulderprotection", new Color(0.15f, 0.14f, 0.15f) }, { "ForearmProtection", new Color(0.12f, 0.13f, 0.18f) },
            { "Shirt", new Color(0.14f, 0.16f, 0.24f) }, { "Trouser", new Color(0.20f, 0.20f, 0.22f) }, { "Belt", new Color(0.42f, 0.09f, 0.07f) },
            { "Neckprotection", new Color(0.13f, 0.12f, 0.13f) }, { "leg", new Color(0.34f, 0.25f, 0.17f) }, { "Shin", new Color(0.17f, 0.16f, 0.17f) },
            { "LegProtection", new Color(0.31f, 0.07f, 0.06f) }, { "Eye", new Color(0.85f, 0.83f, 0.80f) }, { "Wakizashi", new Color(0.08f, 0.07f, 0.07f) },
            { "WakizashiSaya", new Color(0.07f, 0.06f, 0.06f) }, { "KatanaSaya", new Color(0.07f, 0.06f, 0.06f) } };
        static readonly Dictionary<string, Material> samuraiCache = new Dictionary<string, Material>();

        /// <summary>
        /// Материал детали самурая (имя в FBX: SAM_&lt;папка&gt;). Текстуры художника берутся из Assets/_Project/Art/Samurai/&lt;папка&gt;/
        /// (*Base_Color*, *Normal_OpenGL*, *Metallic*, *Emissive*); если их нет — сплошной цвет из палитры.
        /// </summary>
        /// <summary>Тёмная раскраска («тень клана»): чёрный лак, воронёное железо, тусклая бронза, пепельная кожа.</summary>
        static readonly Dictionary<string, Color> ShadowPalette = new Dictionary<string, Color> {
            { "Char", new Color(0.30f, 0.30f, 0.33f) }, { "Helmet", new Color(0.04f, 0.04f, 0.05f) }, { "Horn", new Color(0.42f, 0.30f, 0.14f) },
            { "Helmetdeko", new Color(0.42f, 0.30f, 0.14f) }, { "Helmetband", new Color(0.18f, 0.12f, 0.22f) }, { "Chestplate", new Color(0.06f, 0.05f, 0.07f) },
            { "ChestplateDeko", new Color(0.42f, 0.30f, 0.14f) }, { "Shoulderprotection", new Color(0.07f, 0.06f, 0.08f) }, { "ForearmProtection", new Color(0.06f, 0.05f, 0.08f) },
            { "Shirt", new Color(0.12f, 0.06f, 0.16f) }, { "Trouser", new Color(0.08f, 0.07f, 0.09f) }, { "Belt", new Color(0.25f, 0.06f, 0.20f) },
            { "Neckprotection", new Color(0.06f, 0.05f, 0.07f) }, { "leg", new Color(0.10f, 0.08f, 0.08f) }, { "Shin", new Color(0.07f, 0.06f, 0.08f) },
            { "LegProtection", new Color(0.10f, 0.05f, 0.12f) }, { "Eye", new Color(0.05f, 0.02f, 0.06f) }, { "Wakizashi", new Color(0.05f, 0.04f, 0.05f) },
            { "WakizashiSaya", new Color(0.05f, 0.04f, 0.05f) }, { "KatanaSaya", new Color(0.05f, 0.04f, 0.05f) } };

        /// <summary>Игрок (Meshy «Crimson Shadow Samurai»): base color 2K, нормали 2K, металл (R) + гладкость (A).</summary>
        public static Material Hero3()
        {
            const string dir = "Assets/_Project/Art/Hero3/";
            Shader sh = Shader.Find("Standard");
            string path = MatFolder + "Hero3_Body.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(sh);
            m.color = Color.white;
            Texture2D a = LoadTex(dir + "hero3_albedo.jpg", false, true, 2048);   // главный герой: лицо и шнуры видны крупно
            Texture2D n = LoadTex(dir + "hero3_normal.jpg", true, false, 2048);
            Texture2D ms = LoadTex(dir + "hero3_metal_smooth.png", false, false);
            if (a != null) m.mainTexture = a;
            if (n != null) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1f); }
            if (ms != null) { m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICGLOSSMAP"); m.SetFloat("_GlossMapScale", 0.85f); }
            else { m.SetFloat("_Metallic", 0.1f); m.SetFloat("_Glossiness", 0.4f); }
            if (isNew) AssetDatabase.CreateAsset(m, path);
            EnableRuntimeEmission(m);
            return m;
        }

        /// <summary>Игрок: один материал на весь доспех — base color, нормали, металл (R) + гладкость (A) из FBX модели.</summary>
        public static Material Samurai2()
        {
            const string dir = "Assets/_Project/Art/Samurai2/";
            Shader sh = Shader.Find("Standard");
            string path = MatFolder + "Samurai2_Body.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(sh);
            m.color = Color.white;
            Texture2D a = LoadTex(dir + "samurai2_albedo.jpg", false, true, 2048);   // враг крупно в добивании и у камеры
            Texture2D n = LoadTex(dir + "samurai2_normal.jpg", true, false, 2048);
            Texture2D ms = LoadTex(dir + "samurai2_metal_smooth.png", false, false);
            if (a != null) m.mainTexture = a;
            if (n != null) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1f); }
            if (ms != null) { m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICGLOSSMAP"); m.SetFloat("_GlossMapScale", 0.8f); }
            else { m.SetFloat("_Metallic", 0.2f); m.SetFloat("_Glossiness", 0.4f); }
            if (isNew) AssetDatabase.CreateAsset(m, path);
            EnableRuntimeEmission(m);
            return m;
        }

        /// <summary>
        /// Персонаж на шейдере BladePath/Character (Standard PBR + контровой свет по силуэту).
        /// Свойства те же, что у Standard — текстуры, ключевые слова и вспышки попаданий переносятся как есть.
        /// Игрок — холодный лунный контур, враг-тень — лиловый (под цвет глаз и ауры).
        /// </summary>
        public static Material CharacterRim(Material m, bool enemy)
        {
            if (m == null) return null;
            Shader sh = Shader.Find("BladePath/Character");
            if (sh == null) { Debug.LogWarning("VisualMaterials: нет шейдера BladePath/Character — персонаж остаётся на Standard"); return m; }
            if (m.shader != sh) m.shader = sh;
            m.SetColor("_RimColor", enemy ? new Color(0.15f, 0.09f, 0.22f) : new Color(0.17f, 0.21f, 0.30f));
            m.SetFloat("_RimPower", 3.6f);
            // маска брызг и потёков крови (раны проступают с потерей здоровья)
            Texture2D wm = LoadTex(TexFolder + "wound_mask.png", false, false, 256);
            if (wm != null) { m.SetTexture("_WoundMask", wm); m.SetFloat("_Wound", 0f); }
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// Клинок: хамон (матовая белая закалка у кромки с волнистой границей), зеркальная дзигане с узором хада, воронёные синоги-дзи
        /// (tools/texgen/katana_blade.py под развёртку KatanaBuilder). Без текстур — прежняя ровная сталь.
        /// </summary>
        static Material KatanaBlade()
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(TexFolder + "katana_blade_albedo.png") == null)
                return Textured("Steel", "steel_albedo.png", null, Vector2.one, Color.white, 0.90f, 0.80f);
            Material m = Textured("Steel", "katana_blade_albedo.png", null, Vector2.one, Color.white, 0.9f, 0.8f);
            foreach (string f in new[] { "katana_blade_albedo.png", "katana_blade_ms.png" })
            {
                TextureImporter ti = AssetImporter.GetAtPath(TexFolder + f) as TextureImporter;
                if (ti != null && ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport(); }
            }
            LoadTex(TexFolder + "katana_blade_albedo.png", false, true, 1024);
            Texture2D ms = LoadTex(TexFolder + "katana_blade_ms.png", false, false, 1024);
            if (ms != null) { m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICGLOSSMAP"); m.SetFloat("_GlossMapScale", 1f); }
            Texture2D a = AssetDatabase.LoadAssetAtPath<Texture2D>(TexFolder + "katana_blade_albedo.png");
            if (a != null) { m.mainTexture = a; m.mainTextureScale = Vector2.one; }
            // кровь на клинке по маске (BladePath/Blade): растёт от острия потёками, подсыхает, стряхивается тибури
            Shader bs = Shader.Find("BladePath/Blade");
            Texture2D bm = LoadTex(TexFolder + "katana_blood_mask.png", false, false, 1024);
            if (bs != null && bm != null)
            {
                TextureImporter bi = AssetImporter.GetAtPath(TexFolder + "katana_blood_mask.png") as TextureImporter;
                if (bi != null && bi.wrapMode != TextureWrapMode.Clamp) { bi.wrapMode = TextureWrapMode.Clamp; bi.SaveAndReimport(); }
                bm = AssetDatabase.LoadAssetAtPath<Texture2D>(TexFolder + "katana_blood_mask.png");
                m.shader = bs;
                m.SetTexture("_BloodMask", bm);
                m.SetFloat("_Blood", 0f); m.SetFloat("_BloodDry", 0f);
                if (ms != null) m.EnableKeyword("_METALLICGLOSSMAP");
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D LoadTex(string tp, bool normal, bool srgb, int maxSize = 1024)
        {
            TextureImporter ti = AssetImporter.GetAtPath(tp) as TextureImporter;
            if (ti != null && (ti.maxTextureSize != maxSize || ti.sRGBTexture != srgb || (normal && ti.textureType != TextureImporterType.NormalMap)))
            {
                ti.maxTextureSize = maxSize;
                if (normal) ti.textureType = TextureImporterType.NormalMap;
                ti.sRGBTexture = srgb;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
        }

        public static Material Samurai(string folder, bool shadow = false)
        {
            Material cached;
            string key = (shadow ? "D_" : "") + folder;
            if (samuraiCache.TryGetValue(key, out cached) && cached != null) return cached;
            Color c; if (!(shadow ? ShadowPalette : SamuraiPalette).TryGetValue(folder, out c)) c = new Color(0.3f, 0.3f, 0.3f);
            string dir = SamuraiTex + folder;
            Texture2D albedo = null, normal = null, metal = null, emis = null;
            if (AssetDatabase.IsValidFolder(dir))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
                {
                    string tp = AssetDatabase.GUIDToAssetPath(guid);
                    string fn = System.IO.Path.GetFileName(tp);
                    TextureImporter ti = AssetImporter.GetAtPath(tp) as TextureImporter;
                    bool isN = fn.Contains("Normal"), isData = isN || fn.Contains("Metallic");
                    if (ti != null && (ti.maxTextureSize != 1024 || (isN && ti.textureType != TextureImporterType.NormalMap) || (isData && ti.sRGBTexture)))
                    {
                        ti.maxTextureSize = 1024;                         // мобильный бюджет
                        if (isN) ti.textureType = TextureImporterType.NormalMap;
                        if (isData) ti.sRGBTexture = false;
                        ti.SaveAndReimport();
                    }
                    Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
                    if (fn.Contains("Base_Color")) albedo = t;
                    else if (isN) normal = t;
                    else if (fn.Contains("Metallic")) metal = t;
                    else if (fn.Contains("Emissive")) emis = t;
                }
            }
            if (shadow) albedo = null;   // тень клана — своя тёмная раскраска поверх формы (нормали художника остаются)
            string name = (shadow ? "SamuraiShadow_" : "Samurai_") + folder;
            Material m = albedo != null ? Textured(name, null, null, Vector2.one, Color.white, 0f, 0.35f) : Solid(name, c, folder.Contains("Horn") || folder.Contains("deko") || folder.Contains("Deko") ? 0.8f : 0.1f, 0.35f);
            if (albedo != null) m.mainTexture = albedo;
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            if (metal != null) { m.SetTexture("_MetallicGlossMap", metal); m.EnableKeyword("_METALLICGLOSSMAP"); m.SetFloat("_GlossMapScale", 0.45f); }
            EnableRuntimeEmission(m);
            if (emis != null) { m.SetTexture("_EmissionMap", emis); m.SetColor("_EmissionColor", Color.white); }
            EditorUtility.SetDirty(m);
            samuraiCache[key] = m;
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

        /// <summary>
        /// Листва/гроздья: Standard в режиме Cutout (альфа-срез, тень по альфе). Мип-уровни сохраняют покрытие —
        /// издалека крона не «лысеет». Лёгкое свечение — листва просвечивает в свете фонарей и не тонет в ночи.
        /// </summary>
        static Material Cutout(string name, string file, Color tint, float cutoff, Color emission)
        {
            Shader sh = Shader.Find("Standard");
            string tp = TexFolder + file;
            TextureImporter ti = AssetImporter.GetAtPath(tp) as TextureImporter;
            if (ti != null && (!ti.mipMapsPreserveCoverage || !ti.alphaIsTransparency))
            {
                ti.alphaIsTransparency = true;
                ti.mipMapsPreserveCoverage = true;
                ti.alphaTestReferenceValue = cutoff;
                ti.SaveAndReimport();
            }
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
            if (sh == null || tex == null) return Solid(name, tint, 0f, 0.1f);
            string path = MatFolder + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(sh);
            m.mainTexture = tex; m.color = tint;
            m.SetFloat("_Mode", 1f);
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            m.SetInt("_ZWrite", 1);
            m.EnableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetFloat("_Cutoff", cutoff);
            m.SetFloat("_Metallic", 0f); m.SetFloat("_Glossiness", 0.12f);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            if (emission.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission); m.SetTexture("_EmissionMap", tex); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Карта металличности/гладкости (A — гладкость), линейная.</summary>
        static void Gloss(Material m, string file, float scale)
        {
            if (m == null) return;
            TextureImporter ti = AssetImporter.GetAtPath(TexFolder + file) as TextureImporter;
            if (ti != null && ti.sRGBTexture) { ti.sRGBTexture = false; ti.SaveAndReimport(); }
            Texture2D t = Tex(file, false);
            if (t == null) return;
            m.SetTexture("_MetallicGlossMap", t);
            m.EnableKeyword("_METALLICGLOSSMAP");
            m.SetFloat("_GlossMapScale", scale);
            EditorUtility.SetDirty(m);
        }

        /// <summary>Анизотропная фильтрация: пол под скользящим углом камеры не «мылится».</summary>
        static void Aniso(string file, int level)
        {
            TextureImporter ti = AssetImporter.GetAtPath(TexFolder + file) as TextureImporter;
            if (ti != null && ti.anisoLevel != level) { ti.anisoLevel = level; ti.SaveAndReimport(); }
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
