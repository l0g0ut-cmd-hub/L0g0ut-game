using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Боец: перемещение вдоль оси дуэли (мировая X), границы арены, дистанция до противника.
    /// Логика попаданий здесь не живёт.
    /// </summary>
    public class FighterController : MonoBehaviour
    {
        public string displayName = "Fighter";
        public FighterController opponent;
        public WeaponController weapon;
        public BodyZone[] zones;

        [Header("Movement")]
        public float moveSpeed = 1.6f;
        public float minSeparation = 0.7f;      // ближе этого бойцы не сходятся (корпуса)
        public float arenaHalfWidth = 2.4f;
        [Tooltip("+1: смотрит в +X, -1: смотрит в -X")]
        public int facing = 1;

        float forwardInput;      // -1 назад .. +1 вперёд (относительно facing)
        Vector3 startPosition;

        void Awake() { startPosition = transform.position; }

        /// <summary>Задать ось движения на этот кадр (+1 вперёд, -1 назад).</summary>
        public void SetMoveInput(float axis) { forwardInput = Mathf.Clamp(axis, -1f, 1f); }

        /// <summary>Начать удар, если боец не занят предыдущим.</summary>
        public bool Attack() { return weapon != null && weapon.TryStartSwing(); }

        void Update()
        {
            // во время самого удара боец «закреплён» (ТЗ 5.2: обязательство движения)
            bool committed = weapon != null && weapon.ContactWindowOpen;
            if (!committed && Mathf.Abs(forwardInput) > 0.001f) Move(forwardInput * facing * moveSpeed * Time.deltaTime);
            forwardInput = 0f;
        }

        void Move(float dx)
        {
            Vector3 pos = transform.position;
            float newX = Mathf.Clamp(pos.x + dx, -arenaHalfWidth, arenaHalfWidth);

            if (opponent != null)
            {
                float ox = opponent.transform.position.x;
                float oldDist = Mathf.Abs(pos.x - ox);
                float newDist = Mathf.Abs(newX - ox);
                // нельзя сближаться за минимальную дистанцию и проходить сквозь противника
                bool closing = newDist < oldDist;
                bool crossing = Mathf.Sign(newX - ox) != Mathf.Sign(pos.x - ox);
                if (closing && (newDist < minSeparation || crossing)) return;
            }
            pos.x = newX;
            transform.position = pos;
        }

        public void ResetFighter()
        {
            transform.position = startPosition;
            forwardInput = 0f;
            if (weapon != null) weapon.ResetState();
            if (zones != null) for (int i = 0; i < zones.Length; i++) if (zones[i] != null) zones[i].ClearFlash();
        }
    }
}
