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
        [Tooltip("Положение рук относительно середины плеч в системе корпуса (x вправо, y вверх, z вперёд), м. hand0 — конец замаха, hand1 — конец удара. Руки идут за корпусом, поэтому всегда достают рукоять.")]
        public Vector3 hand0, hand1;
        public bool hasHand;
        [Tooltip("Визуальный выпад корпуса вперёд на ударе, м (зоны поражения не двигаются).")]
        public float bodyStep = 0.16f;
        [Tooltip("Добавочный разнос ног на ударе (шаг), град.")]
        public float strideDeg = 10f;
        [Tooltip("Добавочный присед на ударе, 0…1.")]
        public float crouchBoost;
        [Tooltip("0…1: на замахе боец «вытягивается» — ноги сходятся, присед уходит (дзё-дан: руки вверх, тело на носки).")]
        public float windupRise;
        [Tooltip("Сколько градусов поворота клинка (yaw) корпус отрабатывает в максимуме: у горизонтальных ударов корпус скручивается сильнее.")]
        public float bodyYawMax = 45f;
        [Tooltip("Множитель скрутки корпуса за клинком: у размашистых ударов корпус почти разворачивается спиной к врагу.")]
        public float twistMul = 1f;
        [Tooltip("Выгиб траектории рук вперёд в середине удара, м: руки идут по дуге перед телом, а не по хорде.")]
        public float arcBulge;
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

        void Hands(Vector3 h0, Vector3 h1) { hand0 = h0; hand1 = h1; hasHand = true; }

        static SwingProfile Create(AttackType t)
        {
            SwingProfile p = new SwingProfile();
            p.type = t;
            switch (t)
            {
                // Позы сняты с кадров референс-видео: широкая стойка, корпус на замахе разворачивается почти спиной к врагу,
                // меч заносится за плечо/над головой, удар идёт большой дугой всем телом с глубоким выпадом.
                case AttackType.Thrust:      // цуки: рукоять прижата к правому бедру, затем длинный выпад с полностью вытянутыми руками
                    p.label = "УКОЛ";
                    p.Hands(new Vector3(0.16f, -0.24f, -0.10f), new Vector3(0.02f, -0.04f, 0.52f));
                    p.windupEuler = new Vector3(-6f, 8f, 0f);
                    p.strikeEndEuler = new Vector3(3f, 0f, 0f);
                    p.followEuler = new Vector3(5f, 0f, 0f);
                    p.windupTime = 0.26f; p.strikeTime = 0.10f; p.followTime = 0.12f; p.recoveryTime = 0.40f;
                    p.bodyStep = 0.48f; p.strideDeg = 10f; p.crouchBoost = 0f; p.bodyYawMax = 30f;
                    break;
                case AttackType.Slash:       // большой горизонтальный рез: меч за правым плечом, корпус скручен вправо → дуга на уровне груди через всё тело
                    p.label = "РЕЗ";
                    p.Hands(new Vector3(0.20f, 0.14f, 0.00f), new Vector3(-0.22f, 0.00f, 0.42f));
                    p.windupEuler = new Vector3(-22f, 145f, 0f);
                    p.strikeEndEuler = new Vector3(4f, -100f, 0f);
                    p.followEuler = new Vector3(8f, -125f, 0f);
                    p.arcBulge = 0.26f;
                    p.windupTime = 0.36f; p.strikeTime = 0.14f; p.followTime = 0.14f; p.recoveryTime = 0.44f;
                    p.bodyStep = 0.45f; p.strideDeg = 6f; p.crouchBoost = 0f; p.bodyYawMax = 100f; p.twistMul = 1.3f;
                    break;
                case AttackType.SlashBack:   // то же в обратную сторону: меч за левым плечом
                    p.label = "РЕЗ <";
                    p.Hands(new Vector3(-0.20f, 0.14f, 0.00f), new Vector3(0.22f, 0.00f, 0.42f));
                    p.windupEuler = new Vector3(-22f, -145f, 0f);
                    p.strikeEndEuler = new Vector3(4f, 100f, 0f);
                    p.followEuler = new Vector3(8f, 125f, 0f);
                    p.arcBulge = 0.26f;
                    p.windupTime = 0.36f; p.strikeTime = 0.14f; p.followTime = 0.14f; p.recoveryTime = 0.44f;
                    p.bodyStep = 0.45f; p.strideDeg = 6f; p.crouchBoost = 0f; p.bodyYawMax = 100f; p.twistMul = 1.3f;
                    break;
                case AttackType.Rising:      // гяку-кэса: клинок низко за правым бедром, диагональ снизу-справа вверх-влево через корпус
                    p.label = "СНИЗУ";
                    p.Hands(new Vector3(0.22f, -0.30f, 0.06f), new Vector3(-0.14f, 0.18f, 0.42f));
                    p.windupEuler = new Vector3(40f, 120f, 0f);
                    p.strikeEndEuler = new Vector3(-45f, -60f, 0f);
                    p.followEuler = new Vector3(-58f, -75f, 0f);
                    p.arcBulge = 0.22f;
                    p.windupTime = 0.32f; p.strikeTime = 0.14f; p.followTime = 0.12f; p.recoveryTime = 0.44f;
                    p.bodyStep = 0.42f; p.strideDeg = 6f; p.crouchBoost = 0f; p.bodyYawMax = 100f; p.twistMul = 1.2f;
                    break;
                case AttackType.LegSweep:    // нижний рез: глубокий присед, клинок у земли за правой ногой, широкая дуга по голеням
                    p.label = "ПО НОГАМ";
                    p.Hands(new Vector3(0.24f, -0.30f, 0.04f), new Vector3(-0.22f, -0.36f, 0.44f));
                    p.windupEuler = new Vector3(28f, 130f, 0f);
                    p.strikeEndEuler = new Vector3(26f, -100f, 0f);
                    p.followEuler = new Vector3(26f, -120f, 0f);
                    p.arcBulge = 0.22f;
                    p.windupTime = 0.34f; p.strikeTime = 0.15f; p.followTime = 0.12f; p.recoveryTime = 0.48f;
                    p.bodyStep = 0.42f; p.strideDeg = 6f; p.crouchBoost = 0.15f; p.bodyYawMax = 100f; p.twistMul = 1.2f;
                    break;
                case AttackType.JumpStrike:  // рубящий в воздухе, с броском вперёд
                    p.label = "В ПРЫЖКЕ";
                    p.Hands(new Vector3(0.00f, 0.42f, 0.00f), new Vector3(0.00f, -0.14f, 0.42f));
                    p.windupEuler = new Vector3(-150f, 10f, 0f);
                    p.strikeEndEuler = new Vector3(55f, -10f, 0f);
                    p.followEuler = new Vector3(70f, -12f, 0f);
                    p.windupTime = 0.18f; p.strikeTime = 0.13f; p.followTime = 0.08f; p.recoveryTime = 0.30f;
                    p.lungeSpeed = 2.5f;
                    p.bodyStep = 0f; p.strideDeg = 0f;
                    break;
                case AttackType.Energy:      // шар духа: клинок вскинут вверх остриём, энергия собирается и срывается вперёд
                    p.label = "ШАР";
                    p.Hands(new Vector3(0.04f, 0.12f, 0.26f), new Vector3(0.02f, 0.02f, 0.40f));
                    p.ranged = true;
                    p.windupEuler = new Vector3(-80f, 0f, 0f);
                    p.strikeEndEuler = new Vector3(-10f, 0f, 0f);
                    p.followEuler = new Vector3(-5f, 0f, 0f);
                    p.windupTime = 0.35f; p.strikeTime = 0.08f; p.followTime = 0.12f; p.recoveryTime = 0.42f;
                    p.bodyStep = 0.2f; p.strideDeg = 10f;
                    break;
                default:                     // кири-оросi (дзё-дан): руки высоко над головой, клинок заведён за спину → рубящий вниз с глубоким выпадом
                    p.label = "СВЕРХУ";
                    p.Hands(new Vector3(0.02f, 0.46f, -0.02f), new Vector3(0.00f, -0.20f, 0.40f));
                    p.windupEuler = new Vector3(-162f, 8f, 0f);
                    p.strikeEndEuler = new Vector3(40f, -6f, 0f);
                    p.followEuler = new Vector3(55f, -8f, 0f);
                    p.windupTime = 0.40f; p.strikeTime = 0.14f; p.followTime = 0.12f; p.recoveryTime = 0.48f;
                    p.bodyStep = 0.40f; p.strideDeg = 8f; p.crouchBoost = 0.08f; p.windupRise = 0.6f;
                    break;
            }
            return p;
        }
    }
}
