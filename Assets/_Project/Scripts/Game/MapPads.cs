using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Площадки под домики и стоянки у мест на карте (сгенерировано tools/map_extend/gen_pads.py из tools/map_pads.json).
    /// x, y — место (доли широкой карты), z, w — основание домика. Площадки выбраны глазами по каждой карте: только открытая
    /// земля (не вода, лава, пропасть, мост, лес, не поверх нарисованных домов и друг друга). Нет площадки — домика нет.
    /// </summary>
    public static class MapPads
    {
        public const float Size = 74f;   // домик в точках карты (площадки проверены и при этом размере)
        static readonly Vector4[][] Pads =
        {
            new[] { new Vector4(0.1324f, 0.585f, 0.167f, 0.5996f), new Vector4(0.1766f, 0.885f, 0.1766f, 0.9873f), new Vector4(0.3384f, 0.615f, 0.3728f, 0.6299f), new Vector4(0.306f, 0.52f, 0.3406f, 0.5342f), new Vector4(0.3943f, 0.55f, 0.4287f, 0.5645f), new Vector4(0.356f, 0.462f, 0.3215f, 0.4766f), new Vector4(0.4208f, 0.445f, 0.392f, 0.5283f), new Vector4(0.462f, 0.395f, 0.4621f, 0.4971f), new Vector4(0.7034f, 0.5996f, 0.7379f, 0.6143f), new Vector4(0.7234f, 0.7812f, 0.6946f, 0.8643f), new Vector4(0.7839f, 0.5781f, 0.7494f, 0.5928f), new Vector4(0.1942f, 0.636f, 0.1943f, 0.7383f) },
            new[] { new Vector4(0.1601f, 0.555f, 0.1946f, 0.5693f), new Vector4(0.203f, 0.805f, 0.2318f, 0.8877f), new Vector4(0.1148f, 0.415f, 0.0862f, 0.498f), new Vector4(0.3884f, 0.37f, 0.4172f, 0.3262f), new Vector4(0.4296f, 0.465f, 0.4582f, 0.5479f), new Vector4(0.4473f, 0.35f, 0.4816f, 0.3643f), new Vector4(0.5238f, 0.43f, 0.5525f, 0.3857f), new Vector4(0.764f, 0.5039f, 0.7927f, 0.5869f) },
            new[] { new Vector4(0.3208f, 0.455f, 0.3528f, 0.4697f), new Vector4(0.3438f, 0.58f, 0.3705f, 0.6631f), new Vector4(0.3929f, 0.42f, 0.3663f, 0.5029f), new Vector4(0.4256f, 0.6f, 0.4522f, 0.6826f), new Vector4(0.4911f, 0.468f, 0.4643f, 0.5508f), new Vector4(0.4693f, 0.585f, 0.4959f, 0.668f), new Vector4(0.5844f, 0.6465f, 0.5577f, 0.6025f), new Vector4(0.6757f, 0.6094f, 0.7076f, 0.624f), new Vector4(0.7069f, 0.7715f, 0.7389f, 0.7861f), new Vector4(0.7496f, 0.5469f, 0.7815f, 0.5615f), new Vector4(0.859f, 0.7207f, 0.827f, 0.7354f), new Vector4(0.4365f, 0.432f, 0.4632f, 0.5146f) },
        };
        /// <summary>Основание домика у места (доли карты) или false, если там домик не ставят.</summary>
        public static bool Get(int region, float x, float y, out Vector2 at)
        {
            Vector4[] list = Pads[Mathf.Clamp(region, 0, Pads.Length - 1)];
            for (int i = 0; i < list.Length; i++)
                if (Mathf.Abs(list[i].x - x) < 0.003f && Mathf.Abs(list[i].y - y) < 0.004f) { at = new Vector2(list[i].z, list[i].w); return true; }
            at = Vector2.zero; return false;
        }
        public static bool Has(int region, float x, float y) { Vector2 a; return Get(region, x, y, out a); }
    }
}
