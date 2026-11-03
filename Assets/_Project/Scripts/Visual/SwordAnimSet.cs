using System;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Мокап ударов (Mixamo Great Sword Pack), перенесённый на скелет конкретной модели при сборке (SwordAnimBaker).
    /// Корпус и руки целиком из клипа, меч — в правой кисти, левая кисть ставится на рукоять.
    /// Поворот каждой кости хранится в системе бойца (+Z — к противнику): в игре кость = поворот бойца × значение.
    /// </summary>
    public class SwordAnimSet : ScriptableObject
    {
        /// <summary>Кости в порядке «родитель раньше ребёнка»: Spine, Chest, Shoulder.L, UpperArm.L, Forearm.L, Hand.L, Shoulder.R, UpperArm.R, Forearm.R, Hand.R.</summary>
        public static readonly string[] BoneNames = { "Spine", "Chest", "Shoulder.L", "UpperArm.L", "Forearm.L", "Hand.L", "Shoulder.R", "UpperArm.R", "Forearm.R", "Hand.R" };
        public const int HandL = 5, HandR = 9, ForearmL = 4, ForearmR = 8, UpperArmL = 3, UpperArmR = 7;

        [Serializable]
        public class Clip
        {
            public string name;
            public int type = -1;        // AttackType удара или −1 (стойка, блок)
            public int frames;           // кадров (30 к/с)
            public float[] marks;        // кадры: начало замаха, начало удара, конец удара, конец проводки, конец восстановления
            public Quaternion[] rot;     // [кадр × кости]
        }

        public string[] bonePaths;       // путь от корня модели (пустая строка — кости нет)
        public Clip[] clips;
        public int idle = -1, block = -1;
        public int blockHold = 12;       // кадр блока, в котором поза держится
        public Vector3 palmR, palmL;     // центр кулака в системе кисти (локальная точка)
        public Vector3 bladeAxisR;       // направление клинка в системе правой кисти
        public Vector3 bladeEdgeR;       // направление кромки в системе правой кисти

        public int Bones { get { return BoneNames.Length; } }

        public Clip ForAttack(AttackType t)
        {
            if (clips == null) return null;
            foreach (Clip c in clips) if (c.type == (int)t) return c;
            return null;
        }

        /// <summary>Поза кадра f (дробный, с интерполяцией) в систему бойца.</summary>
        public void Sample(Clip c, float f, Quaternion[] outRot)
        {
            int n = Bones;
            f = Mathf.Clamp(f, 0f, c.frames - 1);
            int a = Mathf.FloorToInt(f), b = Mathf.Min(a + 1, c.frames - 1);
            float u = f - a;
            for (int i = 0; i < n; i++) outRot[i] = Quaternion.Slerp(c.rot[a * n + i], c.rot[b * n + i], u);
        }

        /// <summary>Кадр петли (стойка дышит): f по кругу, шов сглажен.</summary>
        public void SampleLoop(Clip c, float f, Quaternion[] outRot)
        {
            int n = Bones, len = c.frames;
            f = Mathf.Repeat(f, len);
            int a = Mathf.FloorToInt(f) % len, b = (a + 1) % len;
            float u = f - Mathf.Floor(f);
            for (int i = 0; i < n; i++) outRot[i] = Quaternion.Slerp(c.rot[a * n + i], c.rot[b * n + i], u);
        }
    }
}
