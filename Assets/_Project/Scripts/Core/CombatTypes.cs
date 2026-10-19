using UnityEngine;

namespace BladePath
{
    /// <summary>Зоны тела (раздел 5.5 ТЗ).</summary>
    public enum BodyZoneId { Head, Torso, ArmLeft, ArmRight, LegLeft, LegRight }

    /// <summary>Фазы удара (раздел 5.2 ТЗ). Контакт возможен только в Strike и FollowThrough.</summary>
    public enum SwingPhase { Idle, Windup, Strike, FollowThrough, Recovery }

    /// <summary>Подтверждённый контакт клинка с зоной тела. Создаётся только BladeTrajectorySampler.</summary>
    public struct BladeContact
    {
        public WeaponController weapon;   // чей клинок
        public BodyZone zone;             // в какую зону попал
        public Vector3 point;             // точка контакта (мир)
        public Vector3 normal;            // нормаль поверхности в точке контакта
        public Vector3 bladeVelocity;     // скорость точки клинка в момент контакта, м/с
        public float bladeT;              // положение вдоль клинка: 0 = основание, 1 = кончик
        public int swingId;               // номер удара
        public bool fromOverlap;          // true: клинок уже был внутри коллайдера (каст его не видит), нормаль оценочная
    }
}
