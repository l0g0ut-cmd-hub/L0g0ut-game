using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>Круглое препятствие на площадке (жаровня и т.п.): бойцы его обходят.</summary>
    public class ArenaObstacle : MonoBehaviour
    {
        public static readonly List<ArenaObstacle> All = new List<ArenaObstacle>();
        public float radius = 0.35f;
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }
    }
}
