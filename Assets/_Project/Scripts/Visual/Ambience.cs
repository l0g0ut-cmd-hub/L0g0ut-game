using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Общая очередь звуков окружения: цикады, птицы, коршун и сова, кот, карпы, бамбук, колокольчик, метла — по одному,
    /// с паузами тишины между ними (раньше каждый источник жил своим таймером, и звуки шли залпом, сплошной канонадой).
    /// Claim — попросить слово: если сейчас звучит другой или ещё не прошла пауза, отказ (источник попробует позже).
    /// Extend — звук, привязанный к видимому событию (вспорхнула птица, испуганный кот, удар грома): играет сразу,
    /// но сдвигает очередь, чтобы следующий подождал. Фоновые петли (сверчки, дождь, шелест, сутра) — вне очереди.
    /// </summary>
    public static class Ambience
    {
        static float freeAt;

        public static bool Free { get { return Time.time >= freeAt; } }

        /// <summary>Занять эфир на dur секунд и паузу после (minGap…maxGap). false — сейчас нельзя.</summary>
        public static bool Claim(float dur, float minGap = 3f, float maxGap = 8f)
        {
            float now = Time.time;
            if (now < freeAt) return false;
            freeAt = now + dur + Random.Range(minGap, maxGap);
            return true;
        }

        /// <summary>Внеочередной звук длиной dur: очередь ждёт его окончания и короткую паузу.</summary>
        public static void Extend(float dur, float gap = 2.5f)
        {
            freeAt = Mathf.Max(freeAt, Time.time + dur + gap);
        }
    }
}
