using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Зона тела бойца. Висит на объекте с коллайдером. Больше ничего не решает:
    /// только хранит принадлежность и умеет подсвечиваться при попадании.
    /// </summary>
    [DisallowMultipleComponent]
    public class BodyZone : MonoBehaviour
    {
        public BodyZoneId zoneId;
        public FighterController owner;
        public Renderer flashRenderer;

        Color baseColor;
        bool hasBaseColor;
        float flashUntil;

        void Awake() { CacheBaseColor(); }

        void CacheBaseColor()
        {
            if (flashRenderer == null || hasBaseColor) return;
            baseColor = flashRenderer.material.color;
            hasBaseColor = true;
        }

        public void Flash(Color color, float duration)
        {
            if (flashRenderer == null) return;
            CacheBaseColor();
            flashRenderer.material.color = color;
            flashUntil = Time.time + duration;
        }

        public void ClearFlash()
        {
            if (flashRenderer != null && hasBaseColor) flashRenderer.material.color = baseColor;
            flashUntil = 0f;
        }

        void Update()
        {
            if (flashUntil > 0f && Time.time >= flashUntil) ClearFlash();
        }
    }
}
