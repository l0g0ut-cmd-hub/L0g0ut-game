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
        static int rounds, bet, mood, betIdx;
        static readonly int[] BetMul = { 1, 2, 4, 8, 16 };   // ставка — кратная базовой (по области)          // mood: 0 спокоен, 1 хохочет (ты проиграл), 2 в ярости (ты выиграл)
        static float stAt, moodAt, shakeAt = -9f;
        static string say = ""; static float sayAt;
        static Vector2 headPos = new Vector2(480f, 160f);
        public const int MaxRounds = 3;
        /// <summary>Игорный дом на карте: ставки втрое выше, пять партий (тот же стол и тот же жулик).</summary>
        public static bool HighStakes;
        static int Rounds { get { return HighStakes ? 5 : MaxRounds; } }

        static readonly string[] SayIdle =
        {
            "Ну чё, самурай хуев, чёт или нечет? Или ты только железкой махать горазд, блядь?",
            "Ставь, ёпта. Мёртвым бабки ни к чему, а ты, судя по ебалу, скоро сдохнешь.",
            "Я честный, сука, как монах. Монах тоже мухлевал — его, блядь, в колодце и нашли.",
            "Ик… Бросай уже, нахуй, ночь не резиновая, а сакэ само себя не выжрет.",
            "Прошлый герой тут проебал меч, штаны и честь. За честь, правда, хуй что дали.",
            "Кости не пиздят. Пизжу я. Но кости — нет. Почти.",
            "Хули вылупился? Ставь или съебись нахуй к своим демонам.",
            "Батю моего зарезали за этим столом. Хороший был мужик, но мухлевал хуёво.",
            "Не ссы, дырявый. Проебёшь — почку продашь. У вас, блядь, их две.",
        };
        static readonly string[] SayWin =
        {
            "Ха-ха-ха! Сука, обожаю, когда великие воины ноют, как бляди!",
            "Монетки — к монеткам, а лохи — к хуям. Всё по-честному, блядь.",
            "Ещё разок? Отыграешься… хуй там плавал, но давай, давай.",
            "Спасибо за сакэ, самурай! Помянем твой кошелёк, пиздец ему.",
            "Вот он, путь воина: прямиком в жопу и нищету, нахуй!",
            "Ой бля, не могу… Ты и в бою так же везуч? Тогда тебя к утру, сука, закопают.",
            "Меч оставь себе — им удобно вешаться. А денежки, блядь, мои.",
            "Меня любят кости, любят бабы, а тебя, уёбок, любит только смерть.",
        };
        static readonly string[] SayLose =
        {
            "Тьфу, блядь! Везучий ты пёс, ебать тебя в сраку!",
            "Это кости кривые, сука, а не я! Кто их, нахуй, точил?!",
            "Ну погоди, пидор… Ночью в переулке договорим.",
            "Ещё раз! Сейчас я тебя, ёб твою мать, по миру пущу!",
            "Да ёбаный же ты нахуй, как так-то, блядь?!",
            "Забирай, подавись, мудила. Пусть тебя на них и похоронят.",
            "Это был разогрев, понял, блядь?! Разогрев, сука!",
            "Чтоб тебя демоны жрали медленно, с солью и через жопу…",
        };
        static readonly string[] SayBump =
        {
            "Ой, бля! Локтем задел… бывает, хули ты.",
            "Ик! Стол шатается, видишь? Пол, сука, кривой!",
            "Тц, муха села, ебучая. Не считается, перебрасываем!",
            "Хули смотришь? Землетрясение. Духи бесятся, блядь.",
            "Я нихуя не трогал. Это рука. Она сама. Она в говно.",
        };

        public static int RoundsLeft { get { return Rounds - rounds; } }

        /// <summary>Вход за стол (новый визит в трактир — партии заново).</summary>
        public static void Enter(bool newVisit, bool highStakes = false)
        {
            HighStakes = highStakes;
            if (newVisit) rounds = 0;
            st = St.Bet; mood = 0; moodAt = Time.unscaledTime; bumped = false;
            Say(SayIdle[Random.Range(0, SayIdle.Length)]);
            TavernDiceTable.Ensure();
        }

        static void Leave() { TavernDiceTable.Release(); }
        /// <summary>Перенос реплики по словам (UiKit.Text не переносит сам).</summary>
        static string WrapSay(string s, int max)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(); int col = 0;
            foreach (string w in s.Split(' '))
            {
                if (col > 0 && col + 1 + w.Length > max) { sb.Append('\n'); col = 0; }
                else if (col > 0) { sb.Append(' '); col++; }
                sb.Append(w); col += w.Length;
            }
            return sb.ToString();
        }

        static void Say(string s) { say = s; sayAt = Time.unscaledTime; }
        static void SetMood(int m) { mood = m; moodAt = Time.unscaledTime; }

        /// <summary>Рисует экран; true — игрок встал из-за стола.</summary>
        public static bool Draw(bool ok, float W, Color gold, Color ink, Color dim)
        {
            TavernDiceTable t = TavernDiceTable.Ensure();
            float now = Time.unscaledTime;
            int baseBet = (25 + 15 * Campaign.Region) * (HighStakes ? 3 : 1);
            if (st == St.Bet) bet = baseBet * BetMul[Mathf.Clamp(betIdx, 0, BetMul.Length - 1)];   // во время броска ставка не меняется

            // тряска экрана от удара кулаком / локтем
            float sk = Mathf.Clamp01(1f - (now - shakeAt) / 0.35f);
            Vector2 shake = sk > 0f ? new Vector2(Mathf.Sin(now * 90f), Mathf.Cos(now * 77f)) * 5f * sk : Vector2.zero;

            // ---- фон: стол
            Texture2D bg = UiKit.Tex("UI/dice_bg");
            Rect full = new Rect(shake.x - 6f, shake.y - 6f, W + 12f, 552f);
            // дальний край стола на арте — 41,7% высоты сверху; жулик сидит за ним
            float edgeY = 262f, imgH = 552f;
            if (bg != null) { DrawCover(full, bg); edgeY = CoverY(full, bg, 0.417f, out imgH); }
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
                float h = bg != null ? imgH * 0.46f : 300f, w = h * (rogue != null ? rogue.width / (float)rogue.height : 1f);
                float bottom = bg != null ? edgeY + h * 0.16f : 268f;   // низ листа (локти) уходит за край стола
                Rect rr = new Rect(W * 0.5f - w * 0.5f + rage + shake.x, bottom - h - breathe - laugh + shake.y, w, h + breathe);
                if (rogue != null) GUI.DrawTexture(rr, rogue, ScaleMode.StretchToFill, true);
                headPos = new Vector2(rr.center.x, rr.y + rr.height * 0.22f);   // лицо жулика — сюда смотрит хвостик пузыря
            }
            // столешница поверх жулика: он сидит ЗА столом, а не растёт из него
            if (bg != null) DrawCoverFrom(full, bg, edgeY);
            else UiKit.Rect(new Rect(full.x, 262f, full.width, 10f), new Color(0.12f, 0.07f, 0.04f));

            // ---- кости (RenderTexture поверх стола, во весь экран 16:9 по ширине)
            if (t.RT != null && st != St.Bet)
            {
                float rw = W + 12f, rh = rw * 9f / 16f;
                GUI.DrawTexture(new Rect(shake.x - 6f, 552f - rh + shake.y, rw, rh), t.RT, ScaleMode.StretchToFill, true);
            }

            // ---- реплика жулика: чёрный пузырь с белым текстом, хвостик к его голове, текст печатается
            if (say.Length > 0)
            {
                float age = now - sayAt, life = 3.2f + say.Length * 0.045f;
                float a = Mathf.Clamp01(age / 0.12f) * Mathf.Clamp01((sayAt + life - now) / 0.4f);
                if (a > 0f)
                {
                    float pop = 1f + 0.12f * Mathf.Exp(-age * 14f) * Mathf.Cos(age * 40f);
                    float bw = Mathf.Min(330f, W * 0.5f - 60f);
                    string txt = WrapSay(say, 36);
                    int lines = txt.Split('\n').Length;
                    float bh = 22f + lines * 17f;
                    Vector2 head = new Vector2(headPos.x + 40f, headPos.y + 10f);
                    float bx = Mathf.Min(head.x + 24f, W - bw - 12f), by = Mathf.Max(64f, head.y - bh - 18f);
                    Rect b = new Rect(bx, by, bw, bh);
                    Vector2 c = b.center;
                    b = new Rect(c.x - bw * pop * 0.5f, c.y - bh * pop * 0.5f, bw * pop, bh * pop);
                    Color fill = new Color(0.03f, 0.025f, 0.03f, 0.93f * a), rim = new Color(0.95f, 0.88f, 0.7f, 0.85f * a);
                    // хвостик: сужающийся клин от нижнего левого края пузыря к голове
                    Vector2 t0 = new Vector2(b.x + 34f, b.yMax - 2f), t1 = head;
                    for (int i = 0; i < 9; i++)
                    {
                        float k = i / 8f;
                        Vector2 p = Vector2.Lerp(t0, t1, k);
                        float r = Mathf.Lerp(9f, 1.5f, k);
                        GUI.DrawTexture(new Rect(p.x - r, p.y - r, r * 2f, r * 2f), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, fill, 0f, r);
                    }
                    GUI.DrawTexture(b, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, fill, 0f, 14f);
                    GUI.DrawTexture(b, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, rim, 1.5f, 14f);
                    int shown = Mathf.Clamp(Mathf.FloorToInt(age * 55f), 0, say.Length);
                    UiKit.Text(new Rect(b.x + 14f, b.y + 6f, b.width - 28f, b.height - 12f), txt.Substring(0, Mathf.Min(shown, txt.Length)), 13f, new Color(1f, 1f, 1f, a), TextAnchor.MiddleLeft, false);
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
                        else if (Profile.Gold < bet) UiKit.Text(new Rect(0f, y - 84f, W, 24f), "Не хватает монет на ставку", 14f, new Color(1f, 0.6f, 0.45f));
                        // выбор ставки: − / + (кратно базовой; больше, чем есть монет, не поднять)
                        if (RoundsLeft > 0)
                        {
                            float sy = y - 50f, cw = 190f;
                            Rect lab = new Rect(W * 0.5f - cw * 0.5f, sy, cw, 40f);
                            GUI.DrawTexture(lab, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(0.03f, 0.025f, 0.03f, 0.85f), 0f, 10f);
                            UiKit.Text(lab, "СТАВКА: " + bet + " МОН", 15f, gold);
                            bool canUp = betIdx < BetMul.Length - 1 && Profile.Gold >= baseBet * BetMul[betIdx + 1] * 2;   // ставка — не больше половины кошелька
                            if (VisualIdentity.KitButton(new Rect(lab.x - 52f, sy, 46f, 40f), "−", ok && betIdx > 0)) { betIdx--; Haptics.Pulse(15); }
                            if (VisualIdentity.KitButton(new Rect(lab.xMax + 6f, sy, 46f, 40f), "+", ok && canUp)) { betIdx++; Haptics.Pulse(15); }
                            while (betIdx > 0 && Profile.Gold < baseBet * BetMul[betIdx] * 2) betIdx--;   // монет стало меньше — ставка опускается сама
                        }
                        for (int k = 0; k < 2; k++)
                            if (VisualIdentity.KitButton(new Rect(W * 0.5f - bw - 6f + k * (bw + 12f), y, bw, 48f), k == 0 ? "ЧЁТ" : "НЕЧЕТ", can, k == 0))
                            {
                                betEven = k == 0; bumped = false; st = St.Throw; stAt = now; SetMood(0);
                                t.Throw(); Haptics.Pulse(25);
                                if (Juice.Instance != null) Juice.Instance.Taiko(0.5f, 1.6f);
                            }
                        if (VisualIdentity.KitButton(new Rect(16f, 540f - 58f, 150f, 42f), "ВСТАТЬ", ok)) { Leave(); return true; }
                        break;
                    }
                case St.Throw:
                case St.Bump:
                    if (t.Settled && t.StillFor > 0.35f)
                    {
                        bool even = (t.D1 + t.D2) % 2 == 0;
                        bool wouldWin = even == betEven;
                        // жульничество: на твоём выигрыше иногда «задевает локтем» стол — один раз за бросок
                        if (wouldWin && !bumped && Random.value < 0.22f + 0.06f * betIdx)   // на крупной ставке жулик толкает стол охотнее (до 46%)
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
                            if (VisualIdentity.KitButton(new Rect(W * 0.5f - bw - 6f, 540f - 58f, bw, 44f), RoundsLeft > 0 ? "ЕЩЁ ПАРТИЮ" : "ХВАТИТ", ok, true))
                            { st = St.Bet; SetMood(0); if (RoundsLeft > 0) Say(SayIdle[Random.Range(0, SayIdle.Length)]); }
                            if (VisualIdentity.KitButton(new Rect(W * 0.5f + 6f, 540f - 58f, bw, 44f), "ВСТАТЬ", ok)) { Leave(); return true; }
                        }
                        break;
                    }
            }
            return false;
        }

        const float CoverTop = 0.15f;   // доля обрезки по высоте, уходящая сверху

        /// <summary>Экранная Y точки картинки (доля высоты сверху) при отрисовке DrawCover; imgH — полная высота картинки на экране.</summary>
        static float CoverY(Rect r, Texture2D tex, float frac, out float imgH)
        {
            float ta = tex.width / (float)tex.height, ra = r.width / r.height;
            if (ta > ra) { imgH = r.height; return r.y + frac * r.height; }
            float vf = ta / ra; imgH = r.height / vf;
            return r.y + (frac - (1f - vf) * CoverTop) / vf * r.height;
        }

        /// <summary>Та же картинка, что DrawCover, но только ниже экранной Y (передняя часть стола).</summary>
        static void DrawCoverFrom(Rect r, Texture2D tex, float fromY)
        {
            Rect uv = CoverUV(r, tex);
            float k = Mathf.Clamp01((fromY - r.y) / r.height);   // доля сверху
            Rect sub = new Rect(uv.x, uv.y, uv.width, uv.height * (1f - k));   // texcoords снизу вверх
            GUI.DrawTextureWithTexCoords(new Rect(r.x, fromY, r.width, r.yMax - fromY), tex, sub);
        }

        static Rect CoverUV(Rect r, Texture2D tex)
        {
            float ta = tex.width / (float)tex.height, ra = r.width / r.height;
            return ta > ra ? new Rect((1f - ra / ta) * 0.5f, 0f, ra / ta, 1f) : new Rect(0f, (1f - ta / ra) * (1f - CoverTop), 1f, ta / ra);
        }

        /// <summary>Картинка «по размеру с обрезкой» (как cover в CSS).</summary>
        static void DrawCover(Rect r, Texture2D tex)
        {
            // на широких экранах картинка режется по высоте: сверху чуть-чуть (голова жулика), остальное — снизу (стол большой)
            Rect uv = CoverUV(r, tex);
            GUI.DrawTextureWithTexCoords(r, tex, uv);
        }
    }
}
