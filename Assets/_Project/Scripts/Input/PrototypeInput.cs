using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// ВРЕМЕННЫЙ ввод для проверки этапа 1 (клавиатура через старый Input Manager + экранные кнопки IMGUI).
    /// Не является сенсорным управлением из раздела 6 ТЗ — оно делается на этапе 3 (GestureRecognizer + Input System).
    /// Намеренно без пакета Input System: проект этапа 1 не должен зависеть от версии пакета.
    ///
    /// Клавиши: A/D или стрелки — ход, J или Space — удар игрока, L — тестовый удар противника, R — сброс.
    /// </summary>
    public class PrototypeInput : MonoBehaviour
    {
        public FighterController player;
        public FighterController enemy;

        float touchMove;
        bool touchAttack, touchEnemyAttack, touchReset;

        void Update()
        {
            float move = touchMove;
            bool attack = touchAttack, enemyAttack = touchEnemyAttack, reset = touchReset;
            touchMove = 0f; touchAttack = touchEnemyAttack = touchReset = false;

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move -= 1f;
            attack |= Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.Space);
            enemyAttack |= Input.GetKeyDown(KeyCode.L);
            reset |= Input.GetKeyDown(KeyCode.R);
#endif
            if (player != null)
            {
                player.SetMoveInput(move);
                if (attack) player.Attack();
            }
            if (enemy != null && enemyAttack) enemy.Attack();
            if (reset)
            {
                if (player != null) player.ResetFighter();
                if (enemy != null) enemy.ResetFighter();
            }
        }

        // Экранные кнопки: работают мышью в редакторе и касанием на телефоне.
        void OnGUI()
        {
            float scale = Mathf.Max(1f, Screen.height / 540f);
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float W = Screen.width / scale, H = Screen.height / scale;

            if (GUI.RepeatButton(new Rect(12f, H - 76f, 96f, 64f), "< назад")) touchMove -= 1f;
            if (GUI.RepeatButton(new Rect(116f, H - 76f, 96f, 64f), "вперёд >")) touchMove += 1f;
            if (GUI.Button(new Rect(W - 148f, H - 100f, 136f, 88f), "УДАР")) touchAttack = true;
            if (GUI.Button(new Rect(W - 148f, H - 148f, 136f, 40f), "удар врага (тест)")) touchEnemyAttack = true;
            if (GUI.Button(new Rect(W - 84f, 8f, 76f, 32f), "Сброс")) touchReset = true;

            GUI.matrix = old;
        }
    }
}
