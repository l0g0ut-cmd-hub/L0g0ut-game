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
            m.mainTexture = lt != null ? lt : dragonfly ? SoftSprites.Dragonfly : (fog ? SoftSprites.Fog : (disc ? SoftSprites.Disc : SoftSprites.Petal));
            if (lt != null) m.color = Color.white;
            r.material = m;
        }
    }
}
