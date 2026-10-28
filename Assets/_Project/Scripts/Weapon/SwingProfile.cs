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

        /// <summary>Ключи позы во времени от начала удара. Если заданы — руки, клинок, таз, корпус и стопы ведутся по ним, а не по старой паре поз.</summary>
        [System.NonSerialized] public PoseTrack track;
        [Tooltip("Кинетическая цепь: на сколько секунд стопы / таз / грудь опережают руки, а клинок отстаёт от кистей (хлёст).")]
        public float leadFeet = 0.05f, leadPelvis = 0.04f, leadChest = 0.02f, bladeLag = 0.012f;

        /// <summary>Стойка готовности (чудан): умеренный шаг, колени мягко согнуты, правый бок чуть вперёд, клинок смотрит в горло противнику.</summary>
        public static readonly float[] ReadyPose = {
            0.06f, -0.40f, 0.30f,    // руки у пупка, вперёд
            -15f, 0f, 0f,            // клинок вперёд-вверх
            0f, 0f,                  // таз по центру
            -12f, 3f,                // таз: правый бок вперёд, чуть наклонён
            -4f, 2f,                 // грудь
            0f, 0f, 0f };            // стопы в стойке

        void K(float time, params float[] v) { if (track == null) track = new PoseTrack(); track.Add(time, v); }
        void KReady(float time) { K(time, ReadyPose); }

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
                default:                     // сёмэн-кири из дзё-дана: вес назад и клинок над головой → таз и шаг вперёд ведут, руки и клинок догоняют хлёстом
                    p.label = "СВЕРХУ";
                    p.Hands(new Vector3(0.03f, 0.30f, 0.10f), new Vector3(0.02f, -0.10f, 0.52f));
                    p.windupEuler = new Vector3(-146f, 5f, 0f);
                    p.strikeEndEuler = new Vector3(10f, -2f, 0f);
                    p.followEuler = new Vector3(44f, -4f, 0f);
                    p.windupTime = 0.36f; p.strikeTime = 0.13f; p.followTime = 0.15f; p.recoveryTime = 0.46f;
                    p.bodyStep = 0.22f; p.strideDeg = 8f; p.crouchBoost = 0.08f; p.windupRise = 0.6f;
                    //        t      руки x,y,z              клинок p,y,r      таз F,D        таз yaw,pitch  грудь yaw,pitch  стопы F,R,пятка
                    p.KReady(0f);
                    p.K(0.12f,  0.06f, -0.18f, 0.32f,   -48f, 1f, 0f,     -0.04f, 0.00f,  -10f, 0f,     -2f, -2f,      0f, 0f, 0f);      // вес уходит на заднюю ногу, руки поднимают клинок
                    p.K(0.26f,  0.04f,  0.30f, 0.26f,  -120f, 2f, 0f,     -0.07f, -0.01f, -8f, -2f,     0f, -6f,       0f, 0f, 0f);      // дзё-дан: руки над лбом и впереди, клинок над головой
                    p.K(0.36f,  0.04f,  0.33f, 0.25f,  -140f, 2f, 0f,     -0.08f, -0.01f, -10f, -3f,    0f, -9f,       0f, 0f, 0f);      // взвод: остриё за головой, пауза перед ударом
                    p.K(0.45f,  0.03f,  0.12f, 0.46f,   -40f, 0f, 0f,      0.14f, 0.03f,  -12f, 2f,     -2f, 3f,       0.24f, 0f, 0.4f); // таз и шаг уже впереди, руки разгоняют клинок
                    p.K(0.49f,  0.03f, -0.10f, 0.54f,    10f, -1f, 0f,     0.21f, 0.05f,  -13f, 4f,     -2f, 6f,       0.30f, 0f, 0.6f); // контакт: руки вытянуты, вес на передней ноге, спина прямая
                    p.K(0.64f,  0.03f, -0.28f, 0.46f,    42f, -2f, 0f,     0.24f, 0.06f,  -14f, 5f,     -2f, 9f,       0.30f, 0f, 0.7f); // проводка: клинок продолжает вниз и гасится
                    p.K(0.88f,  0.05f, -0.36f, 0.34f,    -6f, -1f, 0f,     0.08f, 0.02f,  -15f, 3f,     -4f, 3f,       0.06f, 0f, 0.15f); // шаг назад, клинок поднимается в чудан
                    p.KReady(1.10f);
                    break;
            }
            return p;
        }
    }
}
