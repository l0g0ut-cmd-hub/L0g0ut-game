using UnityEngine;

namespace BladePath
{
    /// <summary>Линия, которая гаснет и удаляется (по реальному времени — живёт и во время замедления).</summary>
    public class FadeLine : MonoBehaviour
    {
        LineRenderer lr; Color c; float life, born;
        public void Init(LineRenderer l, Color col, float t) { lr = l; c = col; life = t; born = Time.unscaledTime; Apply(1f); }
        void Apply(float a)
        {
            Gradient g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(c, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(a * 0.9f, 0.6f), new GradientAlphaKey(a, 1f) });
            lr.colorGradient = g;
        }
        void Update()
        {
            float k = (Time.unscaledTime - born) / life;
            if (k >= 1f) { Destroy(gameObject); return; }
            Apply(1f - k);
        }
    }
}
