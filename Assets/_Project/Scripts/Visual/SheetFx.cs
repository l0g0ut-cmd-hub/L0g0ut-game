using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Кадры листа на плоскости с наплывом: каждый миг видны два соседних кадра (текущий гаснет, следующий проступает,
    /// со сглаживанием) — анимация не дёргается даже на 24 кадрах в секунду. Плоскость стоит вертикально и повёрнута к камере
    /// (вокруг вертикали), может зеркалиться по горизонтали. Светится поверх (сложение цвета), чёрное в листе невидимо.
    /// Кадры: first…first+count−1; по кругу или один раз (после последнего — гаснет и удаляется).
    /// Сделано для кровавой волны (2026-11-09), годится для любых листов владельца.
    /// </summary>
    public class SheetFx : MonoBehaviour
    {
        public int cols = 1, rows = 1, first, count = 1;
        public float fps = 24f, width = 1f, aspect = 1f;   // aspect — высота кадра к ширине
        public bool loop, flipX, bottomAnchor;
        public Color tint = Color.white;
        public float fadeOut = 0.12f;   // одноразовый: последние доли секунды гаснет
        public Transform follow; public Vector3 followOffset;
        // вступление и качание: сначала кадры first…first+intro−1, потом туда-обратно loopFrom…loopTo (лист без бесшовного круга)
        public int intro = -1, loopFrom, loopTo;
        // второй слой: тот же лист зеркально, со сдвигом по кадрам и чуть ниже — рисунок не повторяется, переходы мягче
        // (на листах ChatGPT соседние кадры сильно разные — один слой выглядит рваным)
        public bool mirrorLayer; public int mirrorShift = 4; public float mirrorAlpha = 0.5f, mirrorScale = 0.88f;
        // сдвиг по кадрам качания (несколько языков из одного листа не повторяют друг друга)
        public int frameShift;
        // колыхание: плоскость нарезана на полосы по высоте, полосы гнутся волной, бегущей вверх, — пламя течёт между
        // кадрами непрерывно, а не только сменой картинок (владелец: «как будто в пару кадров»)
        public float sway, swaySpeed = 3.2f, swayFreq = 5f, swayPhase;
        // низ плоскости мягко гаснет (доля высоты): там, где пламя стоит на полу, нет резкой черты — сверху плоскость не видна
        public float bottomFade;
        // поворот к камере: 0 — плоскость стоит вертикально (поворот только вокруг вертикали), 1 — целиком лицом к камере.
        // Сверху вертикальная плоскость видна ребром — линией (владелец, скриншоты сверху); наклон к камере это убирает
        public float faceCam;
        // направление полёта в мире: плоскость лицом к камере, правый край кадра (+x листа) смотрит туда, куда летит эффект
        // на экране. Иначе сверху волна, летящая «вверх» по экрану, рисовалась боком (владелец)
        public Vector3 alignDir;
        const int Bands = 8, PerLayer = (Bands + 1) * 2;
        float born;
        Mesh mesh; MeshRenderer mr;
        readonly Vector3[] v = new Vector3[PerLayer * 4]; readonly Vector2[] uv = new Vector2[PerLayer * 4]; readonly Color[] col = new Color[PerLayer * 4];
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        public float Age { get { return Time.time - born; } }

        /// <summary>Доиграть хвост листа (кадры from…from+n−1) один раз и исчезнуть.</summary>
        public void PlayOut(int from, int n, float outFps)
        {
            intro = -1; loop = false; first = from; count = Mathf.Max(1, n); fps = outFps; born = Time.time;
        }

        int Seq(int i)
        {
            if (intro >= 0)
            {
                if (i < intro) return first + i;
                int n = loopTo - loopFrom + 1;
                if (n <= 1) return loopFrom;
                int per = 2 * (n - 1), p = (i - intro + frameShift) % per;
                return p < n - 1 ? loopFrom + p : loopTo - (p - (n - 1));
            }
            return first + (loop ? i % count : Mathf.Min(i, count - 1));
        }
        public float Length { get { return count / Mathf.Max(1f, fps); } }

        public static SheetFx Spawn(string tex, Vector3 pos, int cols, int rows, int first, int count, float fps, bool loop, float width, float aspect = 1f)
        {
            Material m = Mat(tex);
            if (m == null) return null;
            GameObject g = new GameObject("Sheet_" + tex);
            g.transform.position = pos;
            SheetFx s = g.AddComponent<SheetFx>();
            s.cols = cols; s.rows = rows; s.first = first; s.count = Mathf.Max(1, count); s.fps = fps; s.loop = loop; s.width = width; s.aspect = aspect;
            s.born = Time.time;
            s.mesh = new Mesh { name = "SheetFx" };
            s.mesh.MarkDynamic();
            s.mesh.vertices = s.v; s.mesh.uv = s.uv; s.mesh.colors = s.col;
            int[] tri = new int[4 * Bands * 6]; int t = 0;
            for (int l = 0; l < 4; l++)
                for (int b = 0; b < Bands; b++)
                {
                    int o = l * PerLayer + b * 2;   // o — левый низ полосы, o+1 — правый низ, o+2/o+3 — верх
                    tri[t++] = o; tri[t++] = o + 2; tri[t++] = o + 1;
                    tri[t++] = o + 1; tri[t++] = o + 2; tri[t++] = o + 3;
                }
            s.mesh.triangles = tri;
            g.AddComponent<MeshFilter>().sharedMesh = s.mesh;
            s.mr = g.AddComponent<MeshRenderer>();
            s.mr.sharedMaterial = m;
            s.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; s.mr.receiveShadows = false;
            s.LateUpdate();
            return s;
        }

        static Material Mat(string tex)
        {
            Material m;
            if (mats.TryGetValue(tex, out m) && m != null) return m;
            Texture2D t = Resources.Load<Texture2D>("FX/" + tex);
            Shader sh = Shader.Find("Legacy Shaders/Particles/Additive");
            if (t == null || sh == null) return null;
            m = new Material(sh) { mainTexture = t };
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            mats[tex] = m;
            return m;
        }

        void LateUpdate()
        {
            if (follow != null) transform.position = follow.position + followOffset;
            float ft = Age * fps;
            float a = tint.a;
            int f0, f1; float k;
            int i0 = Mathf.FloorToInt(ft);
            if (!loop && intro < 0)
            {
                if (ft >= count) { Destroy(gameObject); return; }
                float left = (count - ft) / fps;
                if (fadeOut > 0f) a *= Mathf.Clamp01(left / fadeOut);
            }
            f0 = Seq(i0); f1 = Seq(i0 + 1); k = f1 == f0 ? 0f : ft - i0;
            k = k * k * (3f - 2f * k);
            Camera cam = Camera.main;
            Vector3 right = Vector3.right;
            if (cam != null) { right = cam.transform.right; right.y = 0f; right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right; }
            transform.rotation = Quaternion.identity;
            float hw = width * 0.5f, h = width * aspect;
            Vector3 upDir = Vector3.up;
            if (cam != null && faceCam > 0f)
            {
                Vector3 cu = cam.transform.up;
                upDir = Vector3.Lerp(Vector3.up, cu, faceCam);
                upDir = upDir.sqrMagnitude > 1e-4f ? upDir.normalized : Vector3.up;
                right = Vector3.Cross(upDir, cam.transform.forward).normalized * -1f;
                if (Vector3.Dot(right, cam.transform.right) < 0f) right = -right;
            }
            if (cam != null && alignDir.sqrMagnitude > 1e-6f)
            {
                Vector3 fw = cam.transform.forward;
                Vector3 ds = alignDir - fw * Vector3.Dot(alignDir, fw);
                if (ds.sqrMagnitude < 1e-4f) ds = cam.transform.right * Mathf.Sign(Vector3.Dot(alignDir, cam.transform.right) + 1e-4f);
                right = ds.normalized;
                upDir = Vector3.Cross(fw, right).normalized;   // поворот в плоскости экрана, без зеркала
            }
            Vector3 up = upDir * h;
            Vector3 b0 = bottomAnchor ? Vector3.zero : -up * 0.5f;
            Vector3 L = -right * hw + b0, R = right * hw + b0;
            float tt = Time.time * swaySpeed + swayPhase;
            Quad(0, f0, L, R, up, right, a * (1f - k), flipX, tt);
            Quad(PerLayer, f1, L, R, up, right, a * k, flipX, tt);
            if (mirrorLayer)
            {
                int j = i0 + mirrorShift;
                Vector3 up2 = up * mirrorScale, b2 = bottomAnchor ? Vector3.zero : -up2 * 0.5f;
                Vector3 L2 = -right * hw * mirrorScale + b2, R2 = right * hw * mirrorScale + b2;
                Quad(PerLayer * 2, Seq(j), L2, R2, up2, right, a * mirrorAlpha * (1f - k), !flipX, tt + 2.1f);
                Quad(PerLayer * 3, Seq(j + 1), L2, R2, up2, right, a * mirrorAlpha * k, !flipX, tt + 2.1f);
            }
            else for (int q = PerLayer * 2; q < PerLayer * 4; q++) { v[q] = Vector3.zero; col[q] = Color.clear; }
            mesh.vertices = v; mesh.uv = uv; mesh.colors = col;
            mesh.RecalculateBounds();
        }

        void Quad(int o, int frame, Vector3 L, Vector3 R, Vector3 up, Vector3 right, float alpha, bool flipX, float tt)
        {
            float du = 1f / cols, dv = 1f / rows;
            float u0 = (frame % cols) * du, v0 = 1f - (frame / cols + 1) * dv;
            float ul = flipX ? u0 + du : u0, ur = flipX ? u0 : u0 + du;
            Color c = new Color(tint.r, tint.g, tint.b, alpha);
            float wdt = (R - L).magnitude;
            for (int b = 0; b <= Bands; b++)
            {
                float y = b / (float)Bands;
                // волна идёт снизу вверх, у основания пламя стоит, кверху гнётся сильнее
                float off = sway <= 0f ? 0f : sway * wdt * Mathf.Pow(y, 1.4f) * (Mathf.Sin(tt - y * swayFreq) + 0.45f * Mathf.Sin(tt * 1.73f - y * swayFreq * 1.6f + 1.3f));
                Vector3 sh = up * y + right * off;
                int i = o + b * 2;
                v[i] = L + sh; v[i + 1] = R + sh;
                float vv = v0 + dv * y;
                uv[i] = new Vector2(ul, vv); uv[i + 1] = new Vector2(ur, vv);
                if (bottomFade > 0f) { float fb = Mathf.Clamp01(y / bottomFade); fb = fb * fb * (3f - 2f * fb); col[i] = col[i + 1] = new Color(c.r, c.g, c.b, c.a * fb); }
                else col[i] = col[i + 1] = c;
            }
        }

        void OnDestroy() { if (mesh != null) Destroy(mesh); }
    }
}
