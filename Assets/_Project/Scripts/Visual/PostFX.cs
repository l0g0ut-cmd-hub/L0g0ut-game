using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Пост-обработка камеры боя (встроенный конвейер, без пакетов): свечение огня, фонарей, искр и клинка (bloom)
    /// и цветокоррекция. Высокая графика — bloom + цвет, экономная — только цвет (один проход).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]   // и в редакторе: кадры сцены из CI (SceneShots) — с той же пост-обработкой, что в игре
    public class PostFX : MonoBehaviour
    {
        public Material material;
        [Header("Свечение")]
        [Range(0f, 2f)] public float threshold = 0.72f;
        [Range(0f, 1f)] public float softKnee = 0.5f;
        [Range(0f, 3f)] public float intensity = 0.85f;
        [Range(1, 6)] public int iterations = 5;
        [Header("Цвет")]
        public float exposure = 1.04f, contrast = 1.10f, saturation = 1.06f;
        public Color shadows = new Color(0.93f, 0.98f, 1.10f), highlights = new Color(1.07f, 1.0f, 0.92f);

        readonly RenderTexture[] chain = new RenderTexture[8];

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (material == null) { Graphics.Blit(src, dst); return; }
            float knee = threshold * softKnee + 1e-5f;
            material.SetVector("_Filter", new Vector4(threshold, threshold - knee, 2f * knee, 0.25f / knee));
            material.SetFloat("_Intensity", intensity);
            material.SetFloat("_Exposure", exposure);
            material.SetFloat("_Contrast", contrast);
            material.SetFloat("_Saturation", saturation);
            material.SetVector("_Shadows", shadows);
            material.SetVector("_Highlights", highlights);
            if (DuelManager.Gfx != 1) { Graphics.Blit(src, dst, material, 4); return; }

            int w = src.width / 2, h = src.height / 2;
            RenderTextureFormat fmt = src.format;
            RenderTexture cur = RenderTexture.GetTemporary(w, h, 0, fmt);
            Graphics.Blit(src, cur, material, 0);
            chain[0] = cur;
            int n = 1;
            for (; n < iterations && n < chain.Length; n++)
            {
                w /= 2; h /= 2;
                if (w < 8 || h < 8) break;
                RenderTexture next = RenderTexture.GetTemporary(w, h, 0, fmt);
                Graphics.Blit(cur, next, material, 1);
                chain[n] = next; cur = next;
            }
            for (int i = n - 2; i >= 0; i--)
            {
                Graphics.Blit(cur, chain[i], material, 2);
                cur = chain[i];
            }
            material.SetTexture("_BloomTex", cur);
            Graphics.Blit(src, dst, material, 3);
            for (int i = 0; i < n; i++) { if (chain[i] != null) RenderTexture.ReleaseTemporary(chain[i]); chain[i] = null; }
        }
    }
}
