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
                case AttackType.Thrust:      // колющий выпад вперёд (позы 3/6/15): руки назад к поясу, затем резко вперёд
                    p.label = "УКОЛ";
                    p.Hands(new Vector3(0.10f, -0.34f, 0.04f), new Vector3(0.00f, -0.08f, 0.44f));
                    p.windupEuler = new Vector3(-6f, 0f, 0f);
                    p.strikeEndEuler = new Vector3(3f, 0f, 0f);
                    p.followEuler = new Vector3(4f, 0f, 0f);
                    p.pivotOffset = new Vector3(0f, -0.10f, -0.28f);
                    p.pivotOffsetEnd = new Vector3(0f, -0.02f, 0.40f);
                    p.hasOffsetEnd = true;
                    p.windupTime = 0.30f; p.strikeTime = 0.11f; p.followTime = 0.10f; p.recoveryTime = 0.42f;
                    p.bodyStep = 0.30f; p.strideDeg = 16f; p.crouchBoost = 0.25f;
                    break;
                case AttackType.Slash:       // горизонтальный рез: скрутка корпуса вправо с клинком у бедра, затем разворот и рез по дуге на уровне пояса
                    p.label = "РЕЗ";
                    p.Hands(new Vector3(0.24f, -0.10f, 0.10f), new Vector3(-0.26f, -0.12f, 0.34f));
                    p.windupEuler = new Vector3(4f, 118f, 0f);
                    p.strikeEndEuler = new Vector3(4f, -85f, 0f);
                    p.followEuler = new Vector3(4f, -108f, 0f);
                    p.pivotOffset = new Vector3(0.34f, -0.08f, -0.18f);
                    p.pivotOffsetEnd = new Vector3(-0.32f, -0.10f, 0.30f);
                    p.arcBulge = 0.12f;
                    p.hasOffsetEnd = true;
                    p.windupTime = 0.40f; p.strikeTime = 0.15f; p.followTime = 0.14f; p.recoveryTime = 0.46f;
                    p.bodyStep = 0.38f; p.strideDeg = 24f; p.crouchBoost = 0.25f; p.bodyYawMax = 85f;
                    break;
                case AttackType.SlashBack:   // то же в обратную сторону
                    p.label = "РЕЗ <";
                    p.Hands(new Vector3(-0.24f, -0.10f, 0.10f), new Vector3(0.26f, -0.12f, 0.34f));
                    p.windupEuler = new Vector3(4f, -118f, 0f);
                    p.strikeEndEuler = new Vector3(4f, 85f, 0f);
                    p.followEuler = new Vector3(4f, 108f, 0f);
                    p.pivotOffset = new Vector3(-0.34f, -0.08f, -0.18f);
                    p.pivotOffsetEnd = new Vector3(0.32f, -0.10f, 0.30f);
                    p.arcBulge = 0.12f;
                    p.hasOffsetEnd = true;
                    p.windupTime = 0.40f; p.strikeTime = 0.15f; p.followTime = 0.14f; p.recoveryTime = 0.46f;
                    p.bodyStep = 0.38f; p.strideDeg = 24f; p.crouchBoost = 0.25f; p.bodyYawMax = 85f;
                    break;
                case AttackType.Rising:      // гяку-кэса: присел, клинок низко у бедра остриём вниз-назад, затем диагональный рывок вверх с подъёмом рук и выпрямлением тела
                    p.label = "СНИЗУ";
                    p.Hands(new Vector3(0.20f, -0.38f, 0.10f), new Vector3(-0.12f, 0.20f, 0.40f));
                    p.windupEuler = new Vector3(68f, -30f, 0f);
                    p.strikeEndEuler = new Vector3(-118f, 22f, 0f);
                    p.followEuler = new Vector3(-134f, 26f, 0f);
                    p.pivotOffset = new Vector3(0.20f, -0.32f, -0.10f);
                    p.pivotOffsetEnd = new Vector3(-0.14f, 0.12f, 0.20f);
                    p.hasOffsetEnd = true;
                    p.windupTime = 0.34f; p.strikeTime = 0.15f; p.followTime = 0.10f; p.recoveryTime = 0.46f;
                    p.bodyStep = 0.34f; p.strideDeg = 16f; p.crouchBoost = 0f; p.bodyYawMax = 60f;
                    break;
                case AttackType.LegSweep:    // низкий рез по голеням: присед с клинком сбоку у земли, широкая дуга при развороте корпуса
                    p.label = "ПО НОГАМ";
                    p.Hands(new Vector3(0.26f, -0.40f, 0.08f), new Vector3(-0.26f, -0.42f, 0.40f));
                    p.windupEuler = new Vector3(24f, 112f, 0f);
                    p.strikeEndEuler = new Vector3(24f, -85f, 0f);
                    p.followEuler = new Vector3(24f, -105f, 0f);
                    p.arcBulge = 0.10f;
                    p.pivotOffset = new Vector3(0.26f, -0.44f, -0.10f);
                    p.pivotOffsetEnd = new Vector3(-0.26f, -0.46f, 0.24f);
                    p.hasOffsetEnd = true;
                    p.windupTime = 0.36f; p.strikeTime = 0.17f; p.followTime = 0.10f; p.recoveryTime = 0.50f;
                    p.bodyStep = 0.36f; p.strideDeg = 22f; p.crouchBoost = 0.35f; p.bodyYawMax = 85f;
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
                default:                     // Overhead — по референс-видео: руки вытянуты высоко над головой, клинок завален назад, ноги сходятся; затем бросок вперёд-вниз в глубокий выпад
                    p.label = "СВЕРХУ";
                    p.Hands(new Vector3(0.00f, 0.40f, 0.02f), new Vector3(0.00f, -0.14f, 0.42f));
                    p.windupEuler = new Vector3(-165f, 10f, 0f);
                    p.strikeEndEuler = new Vector3(32f, -10f, 0f);
                    p.followEuler = new Vector3(45f, -12f, 0f);
                    p.pivotOffset = new Vector3(0f, 0.85f, -0.10f);
                    p.pivotOffsetEnd = new Vector3(0f, -0.22f, 0.30f);
                    p.hasOffsetEnd = true;
                    p.windupTime = 0.42f; p.strikeTime = 0.15f; p.followTime = 0.12f; p.recoveryTime = 0.50f;
                    p.bodyStep = 0.34f; p.strideDeg = 22f; p.crouchBoost = 0.50f; p.windupRise = 0.8f;
                    break;
            }
            return p;
        }
    }
}
