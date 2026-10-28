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

        /// <summary>Стойка готовности (как в референсе): широкая низкая открытая стойка, корпус в три четверти, клинок низко вниз-вперёд.</summary>
        public static readonly float[] ReadyPose = {
            0.13f, -0.46f, 0.40f,    // руки перед правым бедром, вынесены вперёд от живота
            -6f, -10f, 0f,           // клинок вперёд и чуть вверх (выше горизонта), остриё на грудь противника
            0.04f, 0.10f,            // таз чуть вперёд и низко: вес на передней ноге, колени согнуты
            40f, 14f,                // таз раскрыт к камере (корпус в три четверти), наклон вперёд
            14f, 14f,                // грудь
            0f, 0f, 0f };            // стопы в стойке

        void K(float time, params float[] v) { if (track == null) track = new PoseTrack(); track.Add(time, v); }
        void KReady(float time) { K(time, ReadyPose); }
        /// <summary>Ключ зеркально (удар в другую сторону): x рук, yaw и крен клинка, повороты таза и груди меняют знак.</summary>
        void KMirror(float time, params float[] v)
        {
            float[] m = (float[])v.Clone();
            m[PoseCh.HX] = -m[PoseCh.HX]; m[PoseCh.EY] = -m[PoseCh.EY]; m[PoseCh.ER] = -m[PoseCh.ER];
            m[PoseCh.HYAW] = -m[PoseCh.HYAW]; m[PoseCh.CYAW] = -m[PoseCh.CYAW];
            K(time, m);
        }

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
                case AttackType.Thrust:      // цуки: рукоять к бедру и взвод, затем выпад тазом и шагом, руки вытягиваются последними
                    p.label = "УКОЛ";
                    p.windupTime = 0.26f; p.strikeTime = 0.1f; p.followTime = 0.12f; p.recoveryTime = 0.4f;
                    //        t      руки x,y,z              клинок p,y,r      таз F,D        таз yaw,pitch  грудь yaw,pitch  стопы F,R,пятка
                    p.KReady(0f);
                    p.K(0.14f, 0.2f, -0.32f, 0.14f, -6f, 4f, 0f, -0.05f, 0.08f, 10f, 6f, 4f, 6f, 0f, 0f, 0f);   // рукоять к правому бедру, вес назад
                    p.K(0.26f, 0.22f, -0.3f, 0.1f, -4f, 2f, 0f, -0.07f, 0.1f, 14f, 6f, 6f, 6f, 0f, 0f, 0f);   // взвод: колени согнуты, остриё на врага
                    p.K(0.32f, 0.05f, -0.2f, 0.4f, -2f, 1f, 0f, 0.22f, 0.13f, -6f, 9f, -2f, 8f, 0.18f, 0.072f, 0.5f);   // выпад: таз и шаг ведут
                    p.K(0.36f, 0.02f, -0.13f, 0.58f, 0f, 0f, 0f, 0.34f, 0.15f, -16f, 11f, -6f, 10f, 0.24f, 0.096f, 0.7f);   // укол: руки вытянуты, вес на передней ноге
                    p.K(0.48f, 0.02f, -0.13f, 0.6f, 1f, 0f, 0f, 0.36f, 0.16f, -18f, 12f, -6f, 11f, 0.24f, 0.096f, 0.75f);   // дожим
                    p.K(0.7f, 0.05f, -0.3f, 0.38f, -8f, 0f, 0f, 0.12f, 0.1f, -14f, 9f, -4f, 9f, 0.072f, 0.029f, 0.2f);   // шаг назад в стойку
                    p.KReady(0.88f);
                    break;
                case AttackType.Slash:      // ёко-гири: клинок за правым плечом, таз разворачивается первым и тянет руки — горизонтальная дуга на уровне груди
                    p.label = "РЕЗ";
                    p.windupTime = 0.36f; p.strikeTime = 0.14f; p.followTime = 0.14f; p.recoveryTime = 0.44f;
                    //        t      руки x,y,z              клинок p,y,r      таз F,D        таз yaw,pitch  грудь yaw,pitch  стопы F,R,пятка
                    p.KReady(0f);
                    p.K(0.14f, 0.2f, -0.1f, 0.2f, -35f, 70f, -60f, -0.03f, 0.07f, 8f, 6f, 6f, 4f, 0f, 0f, 0f);   // руки уходят к правому плечу, вес назад
                    p.K(0.28f, 0.27f, 0.06f, 0.02f, -14f, 150f, -90f, -0.06f, 0.08f, 24f, 4f, 16f, 2f, 0f, 0f, 0f);   // клинок за правым плечом, кромка вперёд, таз и грудь скручены вправо
                    p.K(0.36f, 0.28f, 0.08f, -0.03f, -12f, 162f, -90f, -0.07f, 0.08f, 28f, 3f, 18f, 1f, 0f, 0f, 0f);   // взвод: пауза перед ударом
                    p.K(0.43f, 0.18f, 0.04f, 0.36f, -6f, 70f, -90f, 0.1f, 0.1f, 0f, 8f, 4f, 8f, 0.12f, 0.048f, 0.3f);   // таз уже развернулся к врагу и тянет руки, шаг вперёд
                    p.K(0.5f, -0.1f, 0f, 0.5f, 0f, -40f, -90f, 0.2f, 0.11f, -22f, 10f, -10f, 10f, 0.168f, 0.067f, 0.5f);   // клинок прошёл через центр на уровне груди
                    p.K(0.64f, -0.24f, -0.06f, 0.3f, 4f, -105f, -90f, 0.22f, 0.12f, -30f, 11f, -20f, 11f, 0.168f, 0.067f, 0.6f);   // проводка влево, корпус докручивается
                    p.K(0.86f, -0.04f, -0.3f, 0.32f, -10f, -25f, -30f, 0.06f, 0.08f, -16f, 8f, -6f, 8f, 0.03f, 0.012f, 0.1f);   // шаг назад, клинок возвращается в чудан
                    p.KReady(1.08f);
                    break;
                case AttackType.SlashBack:      // то же зеркально: от левого плеча вправо
                    p.label = "РЕЗ <";
                    p.windupTime = 0.36f; p.strikeTime = 0.14f; p.followTime = 0.14f; p.recoveryTime = 0.44f;
                    //        t      руки x,y,z              клинок p,y,r      таз F,D        таз yaw,pitch  грудь yaw,pitch  стопы F,R,пятка
                    p.KReady(0f);
                    p.KMirror(0.14f, 0.2f, -0.1f, 0.2f, -35f, 70f, -60f, -0.03f, 0.07f, 8f, 6f, 6f, 4f, 0f, 0f, 0f);   // руки уходят к левому плечу, вес назад
                    p.KMirror(0.28f, 0.27f, 0.06f, 0.02f, -14f, 150f, -90f, -0.06f, 0.08f, 24f, 4f, 16f, 2f, 0f, 0f, 0f);   // клинок за левым плечом, кромка вперёд, таз и грудь скручены влево
                    p.KMirror(0.36f, 0.28f, 0.08f, -0.03f, -12f, 162f, -90f, -0.07f, 0.08f, 28f, 3f, 18f, 1f, 0f, 0f, 0f);   // взвод: пауза перед ударом
                    p.KMirror(0.43f, 0.18f, 0.04f, 0.36f, -6f, 70f, -90f, 0.1f, 0.1f, 0f, 8f, 4f, 8f, 0.12f, 0.048f, 0.3f);   // таз уже развернулся к врагу и тянет руки, шаг вперёд
                    p.KMirror(0.5f, -0.1f, 0f, 0.5f, 0f, -40f, -90f, 0.2f, 0.11f, -22f, 10f, -10f, 10f, 0.168f, 0.067f, 0.5f);   // клинок прошёл через центр на уровне груди
                    p.KMirror(0.64f, -0.24f, -0.06f, 0.3f, 4f, -105f, -90f, 0.22f, 0.12f, -30f, 11f, -20f, 11f, 0.168f, 0.067f, 0.6f);   // проводка вправо, корпус докручивается
                    p.KMirror(0.86f, -0.04f, -0.3f, 0.32f, -10f, -25f, -30f, 0.06f, 0.08f, -16f, 8f, -6f, 8f, 0.03f, 0.012f, 0.1f);   // шаг назад, клинок возвращается в чудан
                    p.KReady(1.08f);
                    break;
                case AttackType.Rising:      // кири-агэ: от правого бедра вверх-влево по диагонали
                    p.label = "СНИЗУ";
                    p.windupTime = 0.32f; p.strikeTime = 0.14f; p.followTime = 0.12f; p.recoveryTime = 0.44f;
                    //        t      руки x,y,z              клинок p,y,r      таз F,D        таз yaw,pitch  грудь yaw,pitch  стопы F,R,пятка
                    p.KReady(0f);
                    p.K(0.14f, 0.18f, -0.38f, 0.14f, 20f, 60f, -120f, -0.03f, 0.09f, 8f, 8f, 4f, 8f, 0f, 0f, 0f);   // клинок опускается к правому бедру
                    p.K(0.28f, 0.24f, -0.4f, 0.02f, 38f, 125f, -100f, -0.06f, 0.12f, 22f, 10f, 12f, 10f, 0f, 0f, 0f);   // низко за правым бедром, остриё назад-вниз
                    p.K(0.32f, 0.25f, -0.4f, -0.01f, 40f, 132f, -100f, -0.07f, 0.12f, 24f, 10f, 14f, 10f, 0f, 0f, 0f);   // взвод
                    p.K(0.39f, 0.1f, -0.22f, 0.38f, 10f, 50f, -110f, 0.12f, 0.11f, 0f, 8f, 2f, 6f, 0.132f, 0.053f, 0.3f);   // таз и шаг ведут, клинок идёт вверх по диагонали
                    p.K(0.46f, -0.12f, 0.1f, 0.46f, -38f, -40f, -120f, 0.2f, 0.1f, -20f, 6f, -10f, 2f, 0.168f, 0.067f, 0.5f);   // вверху слева
                    p.K(0.58f, -0.18f, 0.2f, 0.36f, -55f, -70f, -125f, 0.22f, 0.1f, -24f, 6f, -14f, 0f, 0.168f, 0.067f, 0.55f);   // проводка
                    p.K(0.8f, 0f, -0.2f, 0.32f, -20f, -20f, -20f, 0.07f, 0.08f, -14f, 8f, -6f, 7f, 0.03f, 0.012f, 0.1f);   // возврат
                    p.KReady(1.02f);
                    break;
                case AttackType.LegSweep:      // аси-барай: присед и низкая дуга по голеням
                    p.label = "ПО НОГАМ";
                    p.windupTime = 0.34f; p.strikeTime = 0.15f; p.followTime = 0.12f; p.recoveryTime = 0.48f;
                    //        t      руки x,y,z              клинок p,y,r      таз F,D        таз yaw,pitch  грудь yaw,pitch  стопы F,R,пятка
                    p.KReady(0f);
                    p.K(0.16f, 0.18f, -0.4f, 0.12f, 20f, 70f, -60f, -0.03f, 0.12f, 8f, 10f, 6f, 12f, 0f, 0f, 0f);   // клинок вниз-вправо, колени сгибаются
                    p.K(0.3f, 0.3f, -0.44f, -0.02f, 30f, 140f, -85f, -0.06f, 0.18f, 24f, 12f, 14f, 16f, 0f, 0f, 0f);   // низкий взвод за правой ногой
                    p.K(0.34f, 0.31f, -0.45f, -0.05f, 30f, 148f, -88f, -0.07f, 0.19f, 26f, 12f, 16f, 16f, 0f, 0f, 0f);   // пауза
                    p.K(0.42f, 0.14f, -0.48f, 0.34f, 34f, 60f, -88f, 0.1f, 0.26f, 0f, 16f, 4f, 28f, 0.132f, 0.053f, 0.3f);   // присед и шаг, клинок у голеней
                    p.K(0.49f, -0.1f, -0.5f, 0.4f, 34f, -40f, -88f, 0.18f, 0.28f, -20f, 18f, -10f, 30f, 0.168f, 0.067f, 0.5f);   // рез по ногам
                    p.K(0.61f, -0.22f, -0.46f, 0.26f, 32f, -100f, -88f, 0.2f, 0.28f, -26f, 18f, -16f, 30f, 0.168f, 0.067f, 0.55f);   // проводка
                    p.K(0.85f, -0.02f, -0.4f, 0.32f, 0f, -20f, -30f, 0.06f, 0.12f, -14f, 10f, -6f, 10f, 0.03f, 0.012f, 0.1f);   // подъём в стойку
                    p.KReady(1.09f);
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
                    p.K(0.12f, 0.06f, -0.18f, 0.32f, -48f, 1f, 0f, -0.04f, 0.06f, -10f, 6f, -2f, 4f, 0f, 0f, 0f);      // вес уходит на заднюю ногу, руки поднимают клинок
                    p.K(0.26f, 0.04f, 0.3f, 0.26f, -120f, 2f, 0f, -0.07f, 0.05f, -8f, 4f, 0f, 0f, 0f, 0f, 0f);      // дзё-дан: руки над лбом и впереди, клинок над головой
                    p.K(0.36f, 0.04f, 0.33f, 0.25f, -140f, 2f, 0f, -0.08f, 0.05f, -10f, 3f, 0f, -3f, 0f, 0f, 0f);      // взвод: остриё за головой, пауза перед ударом
                    p.K(0.45f, 0.03f, 0.12f, 0.46f, -40f, 0f, 0f, 0.14f, 0.09f, -12f, 8f, -2f, 9f, 0.144f, 0.058f, 0.4f); // таз и шаг уже впереди, руки разгоняют клинок
                    p.K(0.49f, 0.03f, -0.1f, 0.54f, 10f, -1f, 0f, 0.21f, 0.11f, -13f, 10f, -2f, 12f, 0.18f, 0.072f, 0.6f); // контакт: руки вытянуты, вес на передней ноге, спина прямая
                    p.K(0.64f, 0.03f, -0.28f, 0.46f, 42f, -2f, 0f, 0.24f, 0.12f, -14f, 11f, -2f, 15f, 0.18f, 0.072f, 0.7f); // проводка: клинок продолжает вниз и гасится
                    p.K(0.88f, 0.05f, -0.36f, 0.34f, -6f, -1f, 0f, 0.08f, 0.08f, -15f, 9f, -4f, 9f, 0.036f, 0.014f, 0.15f); // шаг назад, клинок поднимается в чудан
                    p.KReady(1.10f);
                    break;
            }
            return p;
        }
    }
}
