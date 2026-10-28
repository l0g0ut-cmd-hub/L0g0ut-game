using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>Каналы позы всего тела (одинаковы для ключей атаки и для стойки).</summary>
    public static class PoseCh
    {
        public const int HX = 0, HY = 1, HZ = 2;          // руки: смещение от середины плеч в системе бойца, м (x вправо, y вверх, z вперёд)
        public const int EP = 3, EY = 4, ER = 5;          // клинок: pitch (+ остриё вниз), yaw (+ вправо), roll, град.
        public const int PF = 6, PD = 7;                  // таз: вынос вперёд, м; опускание, м (+ ниже)
        public const int HYAW = 8, HPIT = 9;              // таз: поворот (− правый бок вперёд), наклон вперёд, град.
        public const int CYAW = 10, CPIT = 11;            // грудь (поверх таза): поворот, наклон вперёд, град.
        public const int FF = 12, RF = 13, HEEL = 14;     // передняя / задняя стопа: смещение вперёд, м; подъём пятки задней ноги 0…1
        public const int N = 15;
    }

    /// <summary>
    /// Ключевые позы атаки во времени и монотонная кубическая интерполяция (Steffen) по каждому каналу:
    /// движение идёт через ключи с сохранением скорости (клинок не «останавливается» в каждой точке, как при lerp между двумя позами),
    /// но без перелётов за ключевые значения; на экстремумах (верх замаха, конец проводки) скорость ровно ноль — естественная «зависка».
    /// </summary>
    public class PoseTrack
    {
        readonly List<float> t = new List<float>();
        readonly List<float[]> v = new List<float[]>();
        float[][] m;   // касательные (ед./с)

        public int Count { get { return t.Count; } }
        public float Duration { get { return t.Count > 0 ? t[t.Count - 1] : 0f; } }
        public float TimeAt(int i) { return t[i]; }

        public void Add(float time, float[] values)
        {
            float[] c = new float[PoseCh.N];
            for (int i = 0; i < PoseCh.N && i < values.Length; i++) c[i] = values[i];
            t.Add(time); v.Add(c); m = null;
        }

        void Build()
        {
            int n = t.Count;
            m = new float[n][];
            for (int k = 0; k < n; k++)
            {
                m[k] = new float[PoseCh.N];
                if (k == 0 || k == n - 1) continue;   // начало и конец — стойка, скорость ноль
                float h0 = t[k] - t[k - 1], h1 = t[k + 1] - t[k];
                for (int c = 0; c < PoseCh.N; c++)
                {
                    float d0 = (v[k][c] - v[k - 1][c]) / h0, d1 = (v[k + 1][c] - v[k][c]) / h1;
                    if (d0 * d1 <= 0f) { m[k][c] = 0f; continue; }
                    float p = (d0 * h1 + d1 * h0) / (h0 + h1);
                    m[k][c] = (Mathf.Sign(d0) + Mathf.Sign(d1)) * Mathf.Min(Mathf.Abs(d0), Mathf.Min(Mathf.Abs(d1), 0.5f * Mathf.Abs(p)));
                }
            }
        }

        /// <summary>Значения каналов [from, to) в момент time.</summary>
        public void Sample(float time, float[] outv, int from, int to)
        {
            if (m == null) Build();
            int n = t.Count;
            if (n == 0) return;
            if (time <= t[0]) { for (int c = from; c < to; c++) outv[c] = v[0][c]; return; }
            if (time >= t[n - 1]) { for (int c = from; c < to; c++) outv[c] = v[n - 1][c]; return; }
            int k = 0;
            while (k < n - 2 && time > t[k + 1]) k++;
            float h = t[k + 1] - t[k];
            float s = (time - t[k]) / h, s2 = s * s, s3 = s2 * s;
            float a = 2f * s3 - 3f * s2 + 1f, b = s3 - 2f * s2 + s, cc = -2f * s3 + 3f * s2, d = s3 - s2;
            for (int c = from; c < to; c++)
                outv[c] = a * v[k][c] + b * h * m[k][c] + cc * v[k + 1][c] + d * h * m[k + 1][c];
        }
    }
}
