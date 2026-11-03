using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Чо-хан в трактире «Чёрный фонарь» отдельным экраном: напротив за столом — пьяный жулик, ты ставишь на чёт или нечет
    /// и бросаешь кости (настоящая физика, TavernDiceTable), он радуется твоему проигрышу и бесится от выигрыша.
    /// Если ты выигрываешь, жулик иногда «случайно» задевает стол локтем — кость перекатывается.
    /// Арт: UI/dice_bg (стол), UI/dice_rogue_0..2 (спокоен / хохочет / в ярости, PNG с прозрачностью); без него — запасной вид.
    /// Три партии за визит.
    /// </summary>
    public static class TavernDiceGame
    {
        enum St { Bet, Throw, Bump, Result }
        static St st;
        static bool betEven, win, bumped;
        static int rounds, bet, mood;          // mood: 0 спокоен, 1 хохочет (ты проиграл), 2 в ярости (ты выиграл)
        static float stAt, moodAt, shakeAt = -9f;
        static string say = ""; static float sayAt;
        public const int MaxRounds = 3;

        static readonly string[] SayIdle = { "Ну, самурай? Чёт или нечет?", "Кости сегодня ко мне благосклонны…", "Ставь, не робей. Я честный… почти.", "Ик… Бросай уже, ночь не резиновая." };
        static readonly string[] SayWin = { "Ха-ха-ха! Кости любят меня!", "Монетки — к монеткам!", "Ещё разок? Отыграешься… наверное.", "Спасибо за сакэ, самурай!" };
        static readonly string[] SayLose = { "Тьфу! Везучий пёс…", "Это кости кривые, не я!", "Ну погоди у меня…", "Ещё раз! Сейчас я тебя…" };
        static readonly string[] SayBump = { "Ой! Локтем задел… бывает.", "Ик! Стол шатается, видишь?", "Тц, муха села. Не считается!" };

        public static int RoundsLeft { get { return MaxRounds - rounds; } }

        /// <summary>Вход за стол (новый визит в трактир — партии заново).</summary>
        public static void Enter(bool newVisit)
        {
            if (newVisit) rounds = 0;
            st = St.Bet; mood = 0; moodAt = Time.unscaledTime; bumped = false;
            Say(SayIdle[Random.Range(0, SayIdle.Length)]);
            TavernDiceTable.Ensure();
        }

        static void Leave() { TavernDiceTable.Release(); }
        static void Say(string s) { say = s; sayAt = Time.unscaledTime; }
        static void SetMood(int m) { mood = m; moodAt = Time.unscaledTime; }

        /// <summary>Рисует экран; true — игрок встал из-за стола.</summary>
        public static bool Draw(bool ok, float W, Color gold, Color ink, Color dim)
        {
            TavernDiceTable t = TavernDiceTable.Ensure();
            float now = Time.unscaledTime;
            bet = 25 + 15 * Campaign.Region;

            // тряска экрана от удара кулаком / локтем
            float sk = Mathf.Clamp01(1f - (now - shakeAt) / 0.35f);
            Vector2 shake = sk > 0f ? new Vector2(Mathf.Sin(now * 90f), Mathf.Cos(now * 77f)) * 5f * sk : Vector2.zero;

            // ---- фон: стол
            Texture2D bg = UiKit.Tex("UI/dice_bg");
            Rect full = new Rect(shake.x - 6f, shake.y - 6f, W + 12f, 552f);
            if (bg != null) DrawCover(full, bg);
            else
            {
                Texture2D tav = UiKit.Tex("UI/tavern");
                if (tav != null) GUI.DrawTextureWithTexCoords(new Rect(full.x, full.y, full.width, 300f), tav, new Rect(0.52f, 0.35f, 0.3f, 0.45f));
                UiKit.Rect(new Rect(0f, 0f, W, 300f), new Color(0f, 0f, 0f, 0.45f));
                for (int i = 0; i < 24; i++)   // запасной стол: тёмное дерево с полосами
                    UiKit.Rect(new Rect(full.x, 270f + i * 11.5f, full.width, 12f), Color.Lerp(new Color(0.22f, 0.13f, 0.07f), new Color(0.32f, 0.2f, 0.11f), Mathf.PerlinNoise(i * 0.7f, 0.3f)));
                UiKit.Rect(new Rect(full.x, 262f, full.width, 10f), new Color(0.12f, 0.07f, 0.04f));
            }

            // ---- жулик: дыхание; хохот — подпрыгивает; ярость — дрожит и бьёт кулаком
            Texture2D rogue = UiKit.Tex("UI/dice_rogue_" + mood);
            if (rogue == null && mood != 0) rogue = UiKit.Tex("UI/dice_rogue_0");
            {
                float mt = now - moodAt;
                float breathe = Mathf.Sin(now * 1.6f) * 2.5f;
                float laugh = mood == 1 ? Mathf.Abs(Mathf.Sin(mt * 11f)) * 7f * Mathf.Clamp01(2.2f - mt) : 0f;
                float rage = mood == 2 ? Mathf.Sin(mt * 47f) * 3f * Mathf.Clamp01(1.6f - mt) : 0f;
                float h = 300f, w = h * (rogue != null ? rogue.width / (float)rogue.height : 1f);
                Rect rr = new Rect(W * 0.5f - w * 0.5f + rage + shake.x, 268f - h - breathe - laugh + shake.y, w, h + breathe);
                if (rogue != null) GUI.DrawTexture(rr, rogue, ScaleMode.StretchToFill, true);
            }
            // край стола перед жуликом (на запасном фоне; на арте стол уже есть)
            if (bg == null) UiKit.Rect(new Rect(full.x, 262f, full.width, 10f), new Color(0.12f, 0.07f, 0.04f));

            // ---- кости (RenderTexture поверх стола, во весь экран 16:9 по ширине)
            if (t.RT != null && st != St.Bet)
            {
                float rw = W + 12f, rh = rw * 9f / 16f;
                GUI.DrawTexture(new Rect(shake.x - 6f, 552f - rh + shake.y, rw, rh), t.RT, ScaleMode.StretchToFill, true);
            }

            // ---- реплика жулика
            if (say.Length > 0)
            {
                float a = Mathf.Clamp01((now - sayAt) / 0.15f) * Mathf.Clamp01((sayAt + 4.5f - now) / 0.5f);
                if (a > 0f)
                {
                    Rect b = new Rect(W * 0.5f + 120f, 40f, Mathf.Min(300f, W * 0.5f - 140f), 54f);
                    UiKit.Rect(b, new Color(0.96f, 0.91f, 0.8f, 0.92f * a));
                    UiKit.Rect(new Rect(b.x - 12f, b.y + 30f, 14f, 8f), new Color(0.96f, 0.91f, 0.8f, 0.92f * a));
                    UiKit.Text(new Rect(b.x + 10f, b.y + 4f, b.width - 20f, b.height - 8f), "«" + say + "»", 13f, new Color(0.15f, 0.08f, 0.05f, a), TextAnchor.MiddleLeft, false);
                }
            }

            // ---- шапка: монеты, ставка, партии
            UiKit.Text(new Rect(16f, 10f, 400f, 26f), "ЧО-ХАН · ЧЁТ ИЛИ НЕЧЕТ", 18f, gold, TextAnchor.MiddleLeft);
            UiKit.Text(new Rect(16f, 36f, 400f, 20f), Profile.Gold + " мон   ·   ставка " + bet + "   ·   партий: " + RoundsLeft, 12.5f, ink, TextAnchor.MiddleLeft);

            // ---- ход игры
            switch (st)
            {
                case St.Bet:
                    {
                        bool can = ok && RoundsLeft > 0 && Profile.Gold >= bet;
                        float bw = 170f, y = 540f - 70f;
                        if (RoundsLeft <= 0) UiKit.Text(new Rect(0f, y - 34f, W, 24f), "Жулик сгребает кости: «На сегодня хватит, самурай»", 14f, dim);
                        else if (Profile.Gold < bet) UiKit.Text(new Rect(0f, y - 34f, W, 24f), "Не хватает монет на ставку", 14f, new Color(1f, 0.6f, 0.45f));
                        for (int k = 0; k < 2; k++)
                            if (VisualIdentity.Button(new Rect(W * 0.5f - bw - 6f + k * (bw + 12f), y, bw, 48f), k == 0 ? "ЧЁТ" : "НЕЧЕТ", can, k == 0))
                            {
                                betEven = k == 0; bumped = false; st = St.Throw; stAt = now; SetMood(0);
                                t.Throw(); Haptics.Pulse(25);
                                if (Juice.Instance != null) Juice.Instance.Taiko(0.5f, 1.6f);
                            }
                        if (VisualIdentity.Button(new Rect(16f, 540f - 58f, 150f, 42f), "ВСТАТЬ", ok)) { Leave(); return true; }
                        break;
                    }
                case St.Throw:
                case St.Bump:
                    if (t.Settled && t.StillFor > 0.35f)
                    {
                        bool even = (t.D1 + t.D2) % 2 == 0;
                        bool wouldWin = even == betEven;
                        // жульничество: на твоём выигрыше иногда «задевает локтем» стол — один раз за бросок
                        if (wouldWin && !bumped && Random.value < 0.22f)
                        {
                            bumped = true; st = St.Bump; shakeAt = now; SetMood(1);
                            Say(SayBump[Random.Range(0, SayBump.Length)]);
                            t.Bump(Random.Range(0, 2)); Haptics.Pulse(40);
                            break;
                        }
                        win = wouldWin; rounds++;
                        Profile.AddGold(win ? bet : -bet);
                        st = St.Result; stAt = now;
                        if (win) { SetMood(2); shakeAt = now + 0.12f; Say(SayLose[Random.Range(0, SayLose.Length)]); Haptics.Pulse(60); }
                        else { SetMood(1); Say(SayWin[Random.Range(0, SayWin.Length)]); }
                    }
                    else UiKit.Text(new Rect(0f, 540f - 60f, W, 30f), st == St.Bump ? "…кость катится снова" : "кости катятся…", 16f, dim);
                    break;
                case St.Result:
                    {
                        float a = Mathf.Clamp01((now - stAt) / 0.3f);
                        int sum = t.D1 + t.D2; bool even = sum % 2 == 0;
                        UiKit.Text(new Rect(0f, 540f - 132f, W, 40f), t.D1 + " + " + t.D2 + " = " + sum + "  ·  " + (even ? "ЧЁТ" : "НЕЧЕТ"), 30f, new Color(gold.r, gold.g, gold.b, a));
                        UiKit.Text(new Rect(0f, 540f - 94f, W, 30f), win ? "Твоя взяла: +" + bet + " мон" : "Проиграл " + bet + " мон", 20f,
                            win ? new Color(0.6f, 0.95f, 0.6f, a) : new Color(1f, 0.55f, 0.45f, a));
                        if (now - stAt > 1.2f)
                        {
                            float bw = 200f;
                            if (VisualIdentity.Button(new Rect(W * 0.5f - bw - 6f, 540f - 58f, bw, 44f), RoundsLeft > 0 ? "ЕЩЁ ПАРТИЮ" : "ХВАТИТ", ok, true))
                            { st = St.Bet; SetMood(0); if (RoundsLeft > 0) Say(SayIdle[Random.Range(0, SayIdle.Length)]); }
                            if (VisualIdentity.Button(new Rect(W * 0.5f + 6f, 540f - 58f, bw, 44f), "ВСТАТЬ", ok)) { Leave(); return true; }
                        }
                        break;
                    }
            }
            return false;
        }

        /// <summary>Картинка «по размеру с обрезкой» (как cover в CSS).</summary>
        static void DrawCover(Rect r, Texture2D tex)
        {
            float ta = tex.width / (float)tex.height, ra = r.width / r.height;
            Rect uv = ta > ra ? new Rect((1f - ra / ta) * 0.5f, 0f, ra / ta, 1f) : new Rect(0f, (1f - ta / ra) * 0.5f, 1f, ta / ra);
            GUI.DrawTextureWithTexCoords(r, tex, uv);
        }
    }
}
