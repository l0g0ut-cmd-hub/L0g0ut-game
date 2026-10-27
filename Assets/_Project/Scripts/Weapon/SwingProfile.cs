using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Описание одной атаки: позы руки (Euler pivot, локально бойцу), тайминги, смещение руки (приседание/подъём),
    /// бросок вперёд. Позы идут через Euler-интерполяцию (см. WeaponController), X = pitch (+ вниз), Y = yaw.
    /// Значения подобраны расчётом по геометрии тел (дальность/зоны) — не измерены в Unity.
    /// </summary>
    [System.Serializable]
    public class SwingProfile
    {
        public string label = "Удар";
        public AttackType type;
        [Tooltip("Дальняя атака: клинок не проверяется, в фазе Strike выпускается снаряд.")]
        public bool ranged;
        public Vector3 windupEuler, strikeEndEuler, followEuler;
        [Tooltip("Смещение pivot относительно базовой руки на время удара (x зеркалится вместе с yaw).")]
        public Vector3 pivotOffset;
        [Tooltip("Смещение pivot к КОНЦУ удара (укол: руки уходят назад на замахе и выносятся вперёд на ударе). Если не задано — остаётся pivotOffset.")]
        public Vector3 pivotOffsetEnd;
        public bool hasOffsetEnd;
        [Tooltip("Визуальный выпад корпуса вперёд на ударе, м (зоны поражения не двигаются).")]
        public float bodyStep = 0.16f;
        [Tooltip("Добавочный разнос ног на ударе (шаг), град.")]
        public float strideDeg = 10f;
        [Tooltip("Добавочный присед на ударе, 0…1.")]
        public float crouchBoost;
        public float windupTime, strikeTime, followTime, recoveryTime;
        [Tooltip("Дополнительная скорость вперёд в фазе удара, м/с.")]
        public float lungeSpeed;

        static readonly SwingProfile[] cache = new SwingProfile[System.Enum.GetValues(typeof(AttackType)).Length];

        public static SwingProfile Get(AttackType t)
        {
            int i = (int)t;
            if (cache[i] == null) cache[i] = Create(t);
            return cache[i];
        }

        static SwingProfile Create(AttackType t)
        {
            SwingProfile p = new SwingProfile();
            p.type = t;
            switch (t)
            {
                case AttackType.Thrust:      // колющий выпад вперёд (позы 3/6/15): руки назад к поясу, затем резко вперёд
                    p.label = "УКОЛ";
                    p.windupEuler = new Vector3(-6f, 0f, 0f);
                    p.strikeEndEuler = new Vector3(3f, 0f, 0f);
                    p.followEuler = new Vector3(4f, 0f, 0f);
                    p.pivotOffset = new Vector3(0f, -0.10f, -0.28f);
                    p.pivotOffsetEnd = new Vector3(0f, -0.02f, 0.40f);
                    p.hasOffsetEnd = true;
                    p.windupTime = 0.30f; p.strikeTime = 0.11f; p.followTime = 0.10f; p.recoveryTime = 0.42f;
                    p.bodyStep = 0.30f; p.strideDeg = 16f; p.crouchBoost = 0.25f;
                    break;
                case AttackType.Slash:       // горизонтальный рез слева направо (поза 5)
                    p.label = "РЕЗ";
                    p.windupEuler = new Vector3(6f, 75f, 0f);
                    p.strikeEndEuler = new Vector3(6f, -75f, 0f);
                    p.followEuler = new Vector3(6f, -90f, 0f);
                    p.pivotOffset = new Vector3(0f, -0.04f, 0.02f);
                    p.windupTime = 0.30f; p.strikeTime = 0.17f; p.followTime = 0.08f; p.recoveryTime = 0.42f;
                    p.bodyStep = 0.12f; p.strideDeg = 8f; p.crouchBoost = 0.15f;
                    break;
                case AttackType.SlashBack:   // горизонтальный рез справа налево (позы 12/14)
                    p.label = "РЕЗ <";
                    p.windupEuler = new Vector3(6f, -75f, 0f);
                    p.strikeEndEuler = new Vector3(6f, 75f, 0f);
                    p.followEuler = new Vector3(6f, 90f, 0f);
                    p.pivotOffset = new Vector3(0f, -0.04f, 0.02f);
                    p.windupTime = 0.30f; p.strikeTime = 0.17f; p.followTime = 0.08f; p.recoveryTime = 0.42f;
                    p.bodyStep = 0.12f; p.strideDeg = 8f; p.crouchBoost = 0.15f;
                    break;
                case AttackType.Rising:      // снизу вверх по диагонали
                    p.label = "СНИЗУ";
                    p.windupEuler = new Vector3(75f, -25f, 0f);
                    p.strikeEndEuler = new Vector3(-100f, 25f, 0f);
                    p.followEuler = new Vector3(-120f, 30f, 0f);
                    p.pivotOffset = new Vector3(0f, -0.20f, 0f);
                    p.windupTime = 0.30f; p.strikeTime = 0.15f; p.followTime = 0.08f; p.recoveryTime = 0.42f;
                    p.bodyStep = 0.14f; p.strideDeg = 10f; p.crouchBoost = 0.20f;
                    break;
                case AttackType.LegSweep:    // низкий горизонтальный рез, боец приседает
                    p.label = "ПО НОГАМ";
                    p.windupEuler = new Vector3(35f, 70f, 0f);
                    p.strikeEndEuler = new Vector3(35f, -70f, 0f);
                    p.followEuler = new Vector3(35f, -85f, 0f);
                    p.pivotOffset = new Vector3(0f, -0.60f, 0.10f);
                    p.windupTime = 0.34f; p.strikeTime = 0.18f; p.followTime = 0.08f; p.recoveryTime = 0.50f;
                    break;
                case AttackType.JumpStrike:  // рубящий в воздухе, с броском вперёд
                    p.label = "В ПРЫЖКЕ";
                    p.windupEuler = new Vector3(-100f, 20f, 0f);
                    p.strikeEndEuler = new Vector3(70f, -20f, 0f);
                    p.followEuler = new Vector3(95f, -25f, 0f);
                    p.pivotOffset = new Vector3(0f, 0.10f, 0.05f);
                    p.windupTime = 0.18f; p.strikeTime = 0.14f; p.followTime = 0.06f; p.recoveryTime = 0.30f;
                    p.lungeSpeed = 2.5f;
                    p.bodyStep = 0f; p.strideDeg = 0f;
                    break;
                case AttackType.Energy:      // дальний энергетический шар
                    p.label = "ШАР";
                    p.ranged = true;
                    p.windupEuler = new Vector3(-70f, 0f, 0f);
                    p.strikeEndEuler = new Vector3(-70f, 0f, 0f);
                    p.followEuler = new Vector3(-60f, 0f, 0f);
                    p.windupTime = 0.35f; p.strikeTime = 0.05f; p.followTime = 0.10f; p.recoveryTime = 0.45f;
                    break;
                default:                     // Overhead — рубящий сверху по диагонали
                    p.label = "СВЕРХУ";
                    p.windupEuler = new Vector3(-120f, 25f, 0f);
                    p.strikeEndEuler = new Vector3(55f, -25f, 0f);
                    p.followEuler = new Vector3(85f, -30f, 0f);
                    p.windupTime = 0.28f; p.strikeTime = 0.16f; p.followTime = 0.08f; p.recoveryTime = 0.40f;
                    p.bodyStep = 0.20f; p.strideDeg = 14f; p.crouchBoost = 0.15f;
                    break;
            }
            return p;
        }
    }
}
