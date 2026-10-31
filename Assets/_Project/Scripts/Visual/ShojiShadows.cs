using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Жизнь за сёдзи храма ночью: на светящейся бумаге — тень монаха в сэйдза (чуть дышит), а раз в 25–55 с вдоль
    /// всех створок неспешно проходит другой монах (8 кадров шага, тень проявляется и тает у краёв). Днём бумага не светится
    /// изнутри — теней не видно. Тени — наклейки умножения (BladePath/AO) прямо на бумаге; кадр и сила — через MaterialPropertyBlock.
    /// </summary>
    public class ShojiShadows : MonoBehaviour
    {
        public Renderer monk, walker;
        public float xMin = -4.0f, xMax = 2.4f, speed = 0.42f, stride = 0.0975f;   // stride — путь за кадр (8 кадров ≈ 2 шага, 0,78 м)
        public int frames = 8;
        public float dir = 1f;
        MaterialPropertyBlock mpb;
        float nextAt, t = -1f, monkSy = 1f, walkStr = 0.66f;
        static readonly int StId = Shader.PropertyToID("_MainTex_ST"), StrId = Shader.PropertyToID("_Strength");

        void Start()
        {
            mpb = new MaterialPropertyBlock();
            nextAt = Time.time + Random.Range(6f, 14f);
            if (monk != null) monkSy = monk.transform.localScale.z;
            if (walker != null) { if (walker.sharedMaterial != null) walkStr = walker.sharedMaterial.GetFloat(StrId); walker.enabled = false; }
        }

        void Update()
        {
            bool night = DayNight.Instance == null || !DayNight.Instance.Day;
            if (monk != null)
            {
                monk.enabled = night;
                Vector3 s = monk.transform.localScale; s.z = monkSy * (1f + 0.008f * Mathf.Sin(Time.time * 1.25f)); monk.transform.localScale = s;   // дыхание
            }
            if (walker == null) return;
            if (t < 0f)
            {
                walker.enabled = false;
                if (!night || Time.time < nextAt) return;
                t = 0f; dir = Random.value < 0.5f ? 1f : -1f;
            }
            t += Time.deltaTime;
            float dur = (xMax - xMin) / speed;
            if (t > dur || !night) { t = -1f; walker.enabled = false; nextAt = Time.time + Random.Range(25f, 55f); return; }
            Pose(t / dur);
        }

        /// <summary>Поставить идущего в долю пути u (0…1): позиция, разворот по направлению, кадр шага, проявление у краёв.</summary>
        public void Pose(float u)
        {
            if (walker == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            float len = xMax - xMin;
            Transform w = walker.transform;
            Vector3 p = w.localPosition; p.x = dir > 0f ? Mathf.Lerp(xMin, xMax, u) : Mathf.Lerp(xMax, xMin, u); w.localPosition = p;
            Vector3 sc = w.localScale; sc.x = Mathf.Abs(sc.x) * (dir > 0f ? 1f : -1f); w.localScale = sc;
            int frame = (int)(u * len / stride) % frames;
            float fade = Mathf.Clamp01(Mathf.Min(u, 1f - u) * len / 0.6f);
            walker.enabled = true;
            walker.GetPropertyBlock(mpb);
            float fw = 1f / frames;
            mpb.SetVector(StId, new Vector4(fw, 1f, frame * fw, 0f));
            mpb.SetFloat(StrId, walkStr * fade);
            walker.SetPropertyBlock(mpb);
        }
    }
}
