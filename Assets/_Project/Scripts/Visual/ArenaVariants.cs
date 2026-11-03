using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Облики арены: 0 — двор храма (собран кодом, EnvironmentBuilder), 1 — «Осенний сад святилища» (цельная модель Meshy, тест).
    /// Переключение — в меню разработчика; выбор живёт до перезапуска игры. Свет, погода и камера общие.
    /// </summary>
    public class ArenaVariants : MonoBehaviour
    {
        public GameObject[] temple = new GameObject[0];
        public GameObject forest;
        public static int Current;
        public static ArenaVariants Instance { get; private set; }

        public static readonly string[] Names = { "ДВОР ХРАМА", "САД СВЯТИЛИЩА (ТЕСТ)" };
        public static int Count { get { return Instance != null && Instance.forest != null ? 2 : 1; } }

        void Awake() { Instance = this; }
        void Start() { Apply(Current); }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public static void Toggle() { if (Instance != null) Instance.Apply((Current + 1) % Count); }

        public void Apply(int i)
        {
            Current = forest != null ? Mathf.Clamp(i, 0, 1) : 0;
            bool f = Current == 1;
            foreach (GameObject g in temple) if (g != null) g.SetActive(!f);
            if (forest != null) forest.SetActive(f);
        }
    }
}
