using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Зона тела бойца. Висит на объекте с коллайдером. Больше ничего не решает:
    /// только хранит принадлежность и умеет подсвечиваться при попадании.
    ///
    /// Подсветка идёт и цветом, и СВЕЧЕНИЕМ (emission): на тёмной текстурированной броне одного умножения цвета
    /// не видно. Чтобы вариант шейдера с emission попал в сборку, сборщик сцены включает _EMISSION на этих материалах заранее.
    /// </summary>
    [DisallowMultipleComponent]
    public class BodyZone : MonoBehaviour
    {
        public BodyZoneId zoneId;
        public FighterController owner;
        public Renderer flashRenderer;
        [Tooltip("Зона невидима в покое и показывается только на время подсветки попадания (для скинутых моделей).")]
        public bool overlayFlash;

        Material mat;
        Color baseColor;
        bool hasBase;
        float flashUntil;

        void Awake()
        {
            CacheBase();
            if (overlayFlash && flashRenderer != null) flashRenderer.enabled = false;
        }

        void CacheBase()
        {
            if (flashRenderer == null || hasBase) return;
            mat = flashRenderer.material;   // персональный экземпляр материала этой зоны
            baseColor = mat.color;
            hasBase = true;
        }

        public void Flash(Color color, float duration)
        {
            // У скинутых моделей зона — грубая капсула/сфера, а не часть тела: её не показываем никогда
            // (иначе при ударе «вылетает» примитивная голова, а после смерти стоит кусок туловища).
            // Подсветку попадания делает DuelManager по материалам самой модели.
            if (overlayFlash) { if (flashRenderer != null) flashRenderer.enabled = false; return; }
            CacheBase();
            if (mat == null) return;
            mat.color = color;
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 1.6f);
            flashUntil = Time.time + duration;
        }

        public void ClearFlash()
        {
            if (mat != null && hasBase)
            {
                mat.color = baseColor;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
            }
            if (overlayFlash && flashRenderer != null) flashRenderer.enabled = false;
            flashUntil = 0f;
        }

        void Update()
        {
            if (flashUntil > 0f && Time.time >= flashUntil) ClearFlash();
        }
    }
}
