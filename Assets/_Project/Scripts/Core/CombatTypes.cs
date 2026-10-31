using UnityEngine;

namespace BladePath
{
    /// <summary>Зоны тела (раздел 5.5 ТЗ).</summary>
    public enum BodyZoneId { Head, Torso, ArmLeft, ArmRight, LegLeft, LegRight }

    /// <summary>Фазы удара (раздел 5.2 ТЗ). Контакт клинка возможен только в Strike и FollowThrough.</summary>
    public enum SwingPhase { Idle, Windup, Strike, FollowThrough, Recovery }

    /// <summary>Типы атак прототипа. JumpStrike выбирается автоматически, если боец в воздухе. Thrust — укол вперёд; Slash / SlashBack — горизонтальный рез слева направо / справа налево.</summary>
    public enum AttackType { Overhead, Rising, LegSweep, JumpStrike, Energy, Thrust, Slash, SlashBack, Deathblow, Mikiri, DeathblowKesa, DeathblowNeck, DeathblowRising, Charge, Salute, DeathblowPlunge }

    /// <summary>Подтверждённый контакт с зоной тела (клинок или снаряд).</summary>
    public struct BladeContact
    {
        public WeaponController weapon;   // чей клинок / чей снаряд
        public BodyZone zone;             // в какую зону попал
        public Vector3 point;             // точка контакта (мир)
        public Vector3 normal;            // нормаль поверхности в точке контакта
        public Vector3 bladeVelocity;     // скорость точки клинка (или снаряда) в момент контакта, м/с
        public float bladeT;              // положение вдоль клинка: 0 = основание, 1 = кончик
        public int swingId;               // номер удара
        public bool fromOverlap;          // true: клинок уже был внутри коллайдера, нормаль оценочная
    }
}
