using UnityEngine;

namespace BladePath
{
    // ВАЖНО: MonoBehaviour, который сохраняется в сцене, обязан жить в файле с тем же именем.
    // Раньше класс лежал в SoftSprites.cs — в сборке Unity не находил для него скрипт, читал данные компонента
    // «вслепую» и при добавлении полей сцена боя считалась повреждённой (level1 corrupted → вылет).
    /// <summary>Вешается на систему частиц: подменяет материал копией с мягкой текстурой (лепестки сакуры вместо квадратов).</summary>
    public class PetalTexture : MonoBehaviour
    {
        public bool disc;   // круглый мягкий спрайт (светлячки) вместо лепестка
        public bool fog;    // гауссово пятно без края (дымка)
        public bool leaf;   // красный лист клёна из референса (Resources/FX/maple_leaf)
        public bool dragonfly;   // стрекоза (день)
        void Awake()
        {
            Breadcrumbs.Mark("частицы: " + name);
            ParticleSystemRenderer r = GetComponent<ParticleSystemRenderer>();
            if (r == null || r.sharedMaterial == null) return;
            Material m = new Material(r.sharedMaterial);
            Texture2D lt = leaf ? Resources.Load<Texture2D>("FX/maple_leaf") : dragonfly ? Resources.Load<Texture2D>("FX/dragonfly") : null;   // стрекоза — фото из атласа
            // восемь разных лепестков / листьев из атласа 38: у каждой частицы своя картинка (случайный кадр листа 4×2)
            Texture2D sheet8 = leaf ? Resources.Load<Texture2D>("FX/leaves8") : (!disc && !fog && !dragonfly ? Resources.Load<Texture2D>("FX/petals8") : null);
            if (sheet8 != null) lt = sheet8;
            m.mainTexture = lt != null ? lt : dragonfly ? SoftSprites.Dragonfly : (fog ? SoftSprites.Fog : (disc ? SoftSprites.Disc : SoftSprites.Petal));
            if (lt != null) m.color = Color.white;
            r.material = m;
            ParticleSystem ps = GetComponent<ParticleSystem>();
            if (sheet8 != null && ps != null)
            {
                var ts = ps.textureSheetAnimation;
                ts.enabled = true; ts.mode = ParticleSystemAnimationMode.Grid;
                ts.numTilesX = 4; ts.numTilesY = 2; ts.animation = ParticleSystemAnimationType.WholeSheet;
                ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.999f);   // случайный кадр на всю жизнь частицы
                ts.cycleCount = 1;
                if (!leaf)
                {
                    // лепесток уже розовый — частицы больше не подкрашиваем, только лёгкий разброс яркости
                    var main = ps.main;
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 1f), new Color(0.88f, 0.86f, 0.88f, 1f));
                }
            }
        }
    }
}
