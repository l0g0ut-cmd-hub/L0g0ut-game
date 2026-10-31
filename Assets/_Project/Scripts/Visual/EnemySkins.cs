using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Облики противника на «Пути»: несколько собранных моделей под одним бойцом (у каждой свой скелет, аниматор и драйвер),
    /// активна одна — по ступени лестницы (Ladder.Stage.model). Зоны поражения, клинок и ИИ общие.
    /// </summary>
    public class EnemySkins : MonoBehaviour
    {
        public GameObject[] models = new GameObject[0];
        public int Active { get; private set; }

        public void Apply(int index)
        {
            if (models == null || models.Length == 0) return;
            index = Mathf.Clamp(index, 0, models.Length - 1);
            for (int i = 0; i < models.Length; i++) if (models[i] != null) models[i].SetActive(i == index);
            Active = index;
        }
    }
}
