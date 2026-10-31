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
        [Header("Объектив")]
        [Range(0f, 1f)] public float vignette = 0.38f;
        [Range(0f, 0.1f)] public float grain = 0.025f;
        [Header("Глубина резкости (высокая графика)")]
        public float dofStart = 3.2f;      // м за точкой фокуса — начало размытия
        public float dofRange = 12f;       // м — до полной силы
        [Range(0f, 1f)] public float dofStrength = 0.75f;
        Camera cam;

        static float aberr, desatTarget, desat, edgeBlur;
        /// <summary>Импульс хроматической аберрации (тяжёлый удар, отражение, добивание): сила 0.004…0.015, гаснет за ~0,3 с.</summary>
        public static void Kick(float amount) { aberr = Mathf.Max(aberr, amount); }
        /// <summary>Обесцвечивание кадра 0…1 (смерть игрока, предложение воскрешения).</summary>
        public static void Desaturate(float k) { desatTarget = Mathf.Clamp01(k); }

        void Update()
        {
            float dt = Application.isPlaying ? Time.unscaledDeltaTime : 0.016f;
            aberr = Mathf.MoveTowards(aberr, 0f, dt * 0.05f);
            desat = Mathf.MoveTowards(desat, desatTarget, dt * 1.5f);
            // фокус на паре: добивание — края кадра размыты; гибель игрока — сильнее
            DuelManager dm = DuelManager.Instance;
            float eb = 0f;
            if (dm != null && Application.isPlaying)
            {
                if (dm.DeathblowActive) eb = 1f;
                else if (dm.State == DuelState.PlayerLost || dm.ResurrectOffered) eb = 1.3f;
            }
            edgeBlur = Mathf.MoveTowards(edgeBlur, eb, dt * (eb > edgeBlur ? 2.5f : 1.2f));
        }

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
            material.SetFloat("_Vignette", vignette);
            material.SetFloat("_Grain", DuelManager.Gfx == 1 ? grain : 0f);
            material.SetFloat("_Aberr", aberr);
            material.SetFloat("_Desat", desat);
            material.SetFloat("_EdgeBlur", edgeBlur);
            if (DuelManager.Gfx != 1) { material.SetVector("_Dof", Vector4.zero); Graphics.Blit(src, dst, material, 4); return; }

            // глубина резкости: фокус — на бойцах (точка обзора камеры), дальше — размытая копия кадра в ¼ и ⅛
            if (cam == null) cam = GetComponent<Camera>();
            RenderTexture d1 = null, d2 = null;
            if (cam != null && dofStrength > 0.001f)
            {
                cam.depthTextureMode |= DepthTextureMode.Depth;
                float focus = CameraOrbit.Instance != null ? Vector3.Distance(cam.transform.position, CameraOrbit.Instance.Focus) : 6f;
                material.SetVector("_Dof", new Vector4(focus + dofStart, dofRange, dofStrength, 0f));
                d1 = RenderTexture.GetTemporary(Mathf.Max(8, src.width / 4), Mathf.Max(8, src.height / 4), 0, src.format);
                d2 = RenderTexture.GetTemporary(Mathf.Max(8, src.width / 8), Mathf.Max(8, src.height / 8), 0, src.format);
                d1.filterMode = FilterMode.Bilinear; d2.filterMode = FilterMode.Bilinear;
                RenderTexture h = RenderTexture.GetTemporary(Mathf.Max(8, src.width / 2), Mathf.Max(8, src.height / 2), 0, src.format);
                Graphics.Blit(src, h, material, 1);    // ступенями по ×2 — без мерцающих «пропущенных» пикселей
                Graphics.Blit(h, d1, material, 1);
                Graphics.Blit(d1, d2, material, 1);
                RenderTexture.ReleaseTemporary(h);
                material.SetTexture("_DofTex", d1); material.SetTexture("_DofTex2", d2);
            }
            else material.SetVector("_Dof", Vector4.zero);

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
            if (d1 != null) RenderTexture.ReleaseTemporary(d1);
            if (d2 != null) RenderTexture.ReleaseTemporary(d2);
        }
    }
}
