using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>Печать духа на земле под ударом духа: разворачивается, крутится и гаснет за 1,2 с.</summary>
    public class SealSpin : MonoBehaviour
    {
        float born; Material m;
        void Start() { born = Time.time; Renderer r = GetComponent<Renderer>(); if (r != null) m = r.material; }
        void Update()
        {
            float t = (Time.time - born) / 1.2f;
            if (t >= 1f) { if (m != null) Destroy(m); Destroy(gameObject); return; }
            float s = Mathf.Lerp(0.6f, 2.4f, 1f - (1f - t) * (1f - t));
            transform.localScale = new Vector3(s, s, 1f);
            transform.Rotate(0f, 0f, 90f * Time.deltaTime, Space.Self);
            if (m != null) m.SetColor("_TintColor", new Color(0.5f, 0.42f, 0.3f, 0.5f * Mathf.Clamp01((1f - t) / 0.4f)));
        }
    }
}
