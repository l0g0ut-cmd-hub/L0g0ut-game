using UnityEngine;

namespace BladePath
{
    /// <summary>Затухающее покачивание предмета после толчка (жаровня, в которую врезался боец): наклон в сторону удара и назад.</summary>
    public class Wobble : MonoBehaviour
    {
        Quaternion rest; bool hasRest;
        Vector3 axis = Vector3.right; float amp, t = 99f;

        public void Kick(Vector3 dir, float degrees)
        {
            if (!hasRest) { rest = transform.localRotation; hasRest = true; }
            dir.y = 0f;
            axis = dir.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, dir.normalized) : Vector3.right;
            amp = degrees; t = 0f;
        }

        void Update()
        {
            if (!hasRest || t > 3f) return;
            t += Time.deltaTime;
            float a = amp * Mathf.Exp(-t * 2.6f) * Mathf.Sin(t * 14f);
            transform.localRotation = Quaternion.AngleAxis(a, axis) * rest;
            if (t > 3f) transform.localRotation = rest;
        }
    }
}
