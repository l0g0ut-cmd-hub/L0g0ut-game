using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Волна по костям щупалец врага (Tent{i}_{j} из Blender-модели). Работает в LateUpdate поверх аниматора:
    /// к позе покоя каждой кости добавляется небольшое вращение, фаза сдвигается по номеру щупальца и звена,
    /// поэтому кончик гуляет сильнее основания. ТОЛЬКО ВИЗУАЛ: зон поражения у щупалец нет.
    /// </summary>
    public class TentacleBoneWiggle : MonoBehaviour
    {
        [System.Serializable]
        public class Link { public Transform bone; public Quaternion rest; public int chain; public int index; }

        public List<Link> links = new List<Link>();
        public float amplitude = 14f;   // градусов на звено
        public float speed = 2.0f;

        /// <summary>Находит кости по именам Tent{i}_{j} (регистр и разделители не важны) и запоминает позу покоя.</summary>
        public void Collect()
        {
            links.Clear();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.Replace(".", "").Replace(":", "").ToLowerInvariant();
                if (!n.StartsWith("tent")) continue;
                string[] parts = n.Substring(4).Split('_');
                int i, j;
                if (parts.Length != 2 || !int.TryParse(parts[0], out i) || !int.TryParse(parts[1], out j)) continue;
                links.Add(new Link { bone = t, rest = t.localRotation, chain = i, index = j });
            }
        }

        void LateUpdate()
        {
            float time = Time.time * speed;
            for (int k = 0; k < links.Count; k++)
            {
                Link l = links[k];
                if (l.bone == null) continue;
                float ph = time + l.chain * 1.3f + l.index * 0.9f;
                float a = amplitude * (0.5f + 0.25f * l.index);
                l.bone.localRotation = l.rest * Quaternion.Euler(Mathf.Sin(ph) * a * 0.6f, Mathf.Cos(ph * 0.8f) * a * 0.4f, Mathf.Sin(ph) * a);
            }
        }
    }
}
