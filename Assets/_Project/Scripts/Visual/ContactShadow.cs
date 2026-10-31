using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Мягкая контактная тень под бойцом (как AO под ступнями): тёмное размытое пятно между стопами, вытянуто по стойке.
    /// Реальная тень от луны на телефоне низкого разрешения и косая — без пятна боец «висит» над камнем.
    /// В прыжке пятно светлеет и сжимается, у павшего — гаснет (лежит на земле — хватает обычной тени).
    /// </summary>
    [DefaultExecutionOrder(400)]
    public class ContactShadow : MonoBehaviour
    {
        public HeroRigDriver rig;
        public float strength = 0.42f;
        static Material mat;
        static Mesh quad;
        Transform q;
        MeshRenderer rend;
        MaterialPropertyBlock mpb;

        void Start()
        {
            GameObject g = new GameObject("ContactShadow");
            q = g.transform;
            g.AddComponent<MeshFilter>().sharedMesh = Quad();
            rend = g.AddComponent<MeshRenderer>();
            rend.sharedMaterial = Mat();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rend.receiveShadows = false;
            mpb = new MaterialPropertyBlock();
        }

        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "contact_quad" };
            quad.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateNormals(); quad.RecalculateBounds();
            return quad;
        }

        static Material Mat()
        {
            if (mat != null) return mat;
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) return null;
            mat = new Material(sh) { name = "ContactShadow" };
            const int n = 64;
            Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "contact_shadow" };
            Color[] px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float r2 = u * u + v * v;
                    px[y * n + x] = new Color(0f, 0f, 0f, Mathf.Clamp01(Mathf.Exp(-r2 * 3.2f) - 0.04f));
                }
            t.SetPixels(px); t.Apply(true);
            mat.mainTexture = t;
            mat.renderQueue = 2460;   // после пола и луж, до прозрачного
            return mat;
        }

        void LateUpdate()
        {
            if (rig == null || rig.footL == null || rig.footR == null || q == null) return;
            FighterController f = rig.fighter;
            Vector3 a = rig.footL.position, b = rig.footR.position;
            float gy = f != null ? f.transform.position.y : 0f;
            float lift = Mathf.Max(0f, Mathf.Min(a.y, b.y) - gy - 0.08f);
            Vector3 c = (a + b) * 0.5f;
            if (rig.hips != null) c = Vector3.Lerp(c, rig.hips.position, 0.35f);   // тело над стопами — пятно под центром масс
            Vector3 span = b - a; span.y = 0f;
            float len = Mathf.Max(0.55f, span.magnitude + 0.45f);
            q.position = new Vector3(c.x, 0.014f, c.z);
            q.rotation = span.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(Vector3.Cross(Vector3.up, span.normalized), Vector3.up) : (f != null ? f.transform.rotation : Quaternion.identity);
            float shrink = Mathf.Clamp01(1f - lift * 1.6f);
            q.localScale = new Vector3(len, 1f, 0.55f) * Mathf.Lerp(0.6f, 1f, shrink);
            float alpha = strength * shrink * (f != null && f.Dead ? 0f : 1f);
            rend.enabled = alpha > 0.01f;
            mpb.SetColor("_Color", new Color(0f, 0f, 0f, alpha));
            rend.SetPropertyBlock(mpb);
        }

        void OnDestroy() { if (q == null) return; if (Application.isPlaying) Destroy(q.gameObject); else DestroyImmediate(q.gameObject); }
    }
}
