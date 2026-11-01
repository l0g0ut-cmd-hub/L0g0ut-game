using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Предметы вокруг арены (камни сада, фонари, жаровни у ворот, стойки знамён), которые оказываются между камерой и
    /// бойцами, на это время прячутся — иначе при облёте камеры кадр закрывал, например, стоячий камень сухого сада.
    /// Список рендереров собирает EnvironmentBuilder (всё невысокое и некрупное вне площадки, перед храмом).
    /// </summary>
    public class CameraOccluders : MonoBehaviour
    {
        public Renderer[] items = new Renderer[0];
        bool[] hidden;
        Camera cam;
        float nextCameraLookup;

        void OnDisable()
        {
            // Re-enable scene objects hidden while the camera crossed their bounds.
            if (items == null || hidden == null) return;
            for (int i = 0; i < items.Length && i < hidden.Length; i++)
            {
                if (hidden[i] && items[i] != null) items[i].enabled = true;
                hidden[i] = false;
            }
        }

        void LateUpdate()
        {
            if (items == null || items.Length == 0) return;
            if (hidden == null || hidden.Length != items.Length) hidden = new bool[items.Length];
            if (cam == null && Time.unscaledTime >= nextCameraLookup)
            {
                cam = Camera.main;
                nextCameraLookup = Time.unscaledTime + 0.5f;
            }
            if (cam == null) return;
            Vector3 a = cam.transform.position;
            Vector3 b = CameraOrbit.Instance != null ? CameraOrbit.Instance.Focus : new Vector3(0f, 1.1f, 0f);
            Vector3 ab = b - a; float len2 = Mathf.Max(1e-4f, ab.sqrMagnitude);
            for (int i = 0; i < items.Length; i++)
            {
                Renderer r = items[i];
                if (r == null) continue;
                Bounds bb = r.bounds;
                // ближайшая к центру предмета точка на луче «камера → бойцы»: предмет закрывает, если луч проходит сквозь его
                // габарит (с запасом) и предмет стоит ближе к камере, чем бойцы
                float t = Vector3.Dot(bb.center - a, ab) / len2;
                bool block = false;
                if (t > 0.02f && t < 0.92f)
                {
                    Vector3 p = a + ab * t;
                    Vector3 e = bb.extents + new Vector3(0.12f, 0.12f, 0.12f);
                    Vector3 d = p - bb.center;
                    block = Mathf.Abs(d.x) < e.x && Mathf.Abs(d.y) < e.y + 0.6f && Mathf.Abs(d.z) < e.z;   // по высоте — щедрее: закрывает и голову
                }
                // прячем, пока луч проходит сквозь предмет; ушёл — возвращаем
                if (block != hidden[i]) { hidden[i] = block; r.enabled = !block; }
            }
        }
    }
}
