using UnityEngine;

namespace BladePath
{
    /// <summary>HUD дуэли: полоски здоровья, имена, «БОЙ!», цифры урона, итоговый экран. Только рисует; кнопки обрабатывает PrototypeInput.</summary>
    public class DuelHud : MonoBehaviour
    {
        public DuelManager duel;
        public string playerName = "РОНИН";
        public string enemyName = "ПОВЕРЖЕННЫЙ";

        static readonly Color Frame = new Color(0.04f, 0.03f, 0.03f, 0.92f);
        static readonly Color Gold = new Color(0.86f, 0.70f, 0.38f, 1f);

        // кровь «на объективе», когда игроку прилетает сильный удар
        struct Splat { public Vector2 p; public float size, born, rot; }
        readonly System.Collections.Generic.List<Splat> splats = new System.Collections.Generic.List<Splat>();
        bool hooked;

        void OnDamaged(FighterController f, float dmg)
        {
            if (duel == null || f != duel.player || dmg < 14f) return;
            int n = dmg >= 24f ? 6 : 3;
            float W = UiKit.Width;
            for (int i = 0; i < n; i++)
            {
                Splat s = new Splat();
                // ближе к краям экрана, чтобы не закрывать бой
                float side = Random.value < 0.5f ? Random.Range(0f, 0.25f) : Random.Range(0.75f, 1f);
                s.p = new Vector2(side * W, Random.Range(60f, 480f));
                s.size = Random.Range(40f, 110f) * (dmg >= 24f ? 1.3f : 1f);
                s.born = Time.unscaledTime; s.rot = Random.Range(0f, 360f);
                splats.Add(s);
            }
            if (splats.Count > 24) splats.RemoveRange(0, splats.Count - 24);
        }

        void DrawSplats()
        {
            for (int i = splats.Count - 1; i >= 0; i--)
            {
                Splat s = splats[i];
                float age = Time.unscaledTime - s.born;
                if (age > 2.2f) { splats.RemoveAt(i); continue; }
                float a = Mathf.Clamp01((2.2f - age) / 1.2f) * 0.75f;
                float drip = age * 14f;   // медленно стекает
                Matrix4x4 m = GUI.matrix;
                GUI.matrix = m * Matrix4x4.TRS(new Vector3(s.p.x, s.p.y + drip, 0f), Quaternion.Euler(0f, 0f, s.rot), Vector3.one);
                GUI.color = new Color(0.45f, 0f, 0.02f, a);
                Texture2D bt = UiKit.Tex("FX/blood_splat");
                GUI.DrawTexture(new Rect(-s.size * 0.5f, -s.size * 0.5f, s.size, s.size), bt != null ? bt : SoftSprites.Splat);
                GUI.matrix = m;
            }
            GUI.color = Color.white;
        }

        PortraitCam portraitP, portraitE;
        bool portraitsTried;
        static Texture2D portraitMask;

        void OnGUI()
        {
            if (duel == null || duel.player == null || duel.enemy == null) return;
            if (!hooked) { duel.Damaged += OnDamaged; hooked = true; Breadcrumbs.Mark("HUD: первый OnGUI"); }
            if (!portraitsTried && Event.current.type == EventType.Repaint)
            {
                portraitsTried = true;
                // рисованные портреты из референса (UI/portrait_*); живые камеры — только если картинок нет
                if (UiKit.Tex("UI/portrait_ronin") == null)
                {
                    portraitP = PortraitCam.Create(duel.player, new Color(0.08f, 0.14f, 0.22f));
                    portraitE = PortraitCam.Create(duel.enemy, new Color(0.2f, 0.06f, 0.18f));
                }
            }
            Matrix4x4 old = UiKit.Begin();
            float W = UiKit.Width;

            // кинематографичная виньетка; при низком здоровье игрока она пульсирует красным
            if (vignette == null) vignette = MakeVignette();
            Color vc = new Color(0f, 0f, 0f, 0.55f);
            float low = duel.Hp01(duel.player);
            if (low < 0.3f && duel.State == DuelState.Fighting)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.Lerp(9f, 5f, low / 0.3f));
                vc = Color.Lerp(vc, new Color(0.55f, 0f, 0f, 0.85f), (1f - low / 0.3f) * 0.5f + pulse * 0.35f);
            }
            GUI.color = vc;
            GUI.DrawTexture(new Rect(0f, 0f, W, 540f), vignette);
            GUI.color = Color.white;

            DrawSplats();

            float barW = Mathf.Clamp(W * 0.5f - 80f - 110f, 200f, 400f);
            float cx = W * 0.5f;
            DrawBar(new Rect(cx - 80f - barW, 22f, barW, 14f), duel.player, false, playerName, new Color(0.15f, 0.55f, 0.85f));
            DrawBar(new Rect(cx + 80f, 22f, barW, 14f), duel.enemy, true, Ladder.Current.name, new Color(0.75f, 0.2f, 0.85f));
            // метки жизни врага (как у боссов Sekiro): энсо под полосой — сколько ещё добиваний; снятая метка гаснет
            int mm = duel.MaxMarks(duel.enemy);
            if (mm > 1)
            {
                int left = duel.Marks(duel.enemy);
                for (int i = 0; i < mm; i++)
                {
                    Vector2 mc = new Vector2(cx + 80f + barW - 12f - i * 26f, 50f);
                    bool alive = i < left;
                    Color o = GUI.color; GUI.color = alive ? Color.white : new Color(0.35f, 0.3f, 0.3f, 0.45f);
                    Texture2D mt = UiKit.Tex("UI/deathblow_mark");
                    if (mt != null) GUI.DrawTexture(new Rect(mc.x - 13f, mc.y - 13f, 26f, 26f), mt);
                    GUI.color = o;
                }
            }

            // красная вспышка по краям, когда бьют игрока
            float since = duel.SinceHit(duel.player);
            if (since < 0.5f)
            {
                float a = (0.5f - since) / 0.5f * 0.35f;
                Color c = new Color(0.7f, 0f, 0f, a);
                UiKit.Rect(new Rect(0f, 0f, W, 70f), c);
                UiKit.Rect(new Rect(0f, 470f, W, 70f), c);
                UiKit.Rect(new Rect(0f, 0f, 60f, 540f), c);
                UiKit.Rect(new Rect(W - 60f, 0f, 60f, 540f), c);
            }

            // цифры урона
            Camera cam = Camera.main;
            if (cam != null)
            {
                for (int i = 0; i < duel.Popups.Count; i++)
                {
                    DuelManager.Popup p = duel.Popups[i];
                    float age = Time.unscaledTime - p.born;
                    Vector3 sp = cam.WorldToScreenPoint(p.world);
                    if (sp.z < 0f) continue;
                    Vector2 v = new Vector2(sp.x / UiKit.Scale, (Screen.height - sp.y) / UiKit.Scale - age * 46f);
                    Color c = p.color; c.a = Mathf.Clamp01(1.6f - age * 1.6f);
                    float sz = p.size * (1f + Mathf.Max(0f, 0.25f - age) * 2f);
                    UiKit.Text(new Rect(v.x - 60f, v.y - 20f, 120f, 40f), p.text, sz, c);
                }
            }

            // шкалы духа (ки) под полосками здоровья
            DrawKi(new Rect(cx - 80f - barW * 0.55f, 88f, barW * 0.55f, 6f), duel.player, false);
            DrawKi(new Rect(cx + 80f, 88f, barW * 0.55f, 6f), duel.enemy, true);

            // равновесие (как в Sekiro): у врага — над головой, у игрока — внизу по центру; полоса растёт от середины к краям
            if (cam != null && duel.State == DuelState.Fighting)
            {
                Vector3 ehp = duel.enemy.transform.position + Vector3.up * 2.05f;
                Vector3 esp = cam.WorldToScreenPoint(ehp);
                if (esp.z > 0f) DrawPosture(new Vector2(esp.x / UiKit.Scale, (Screen.height - esp.y) / UiKit.Scale), 130f, duel.enemy);
                DrawPosture(new Vector2(cx, 470f), 230f, duel.player);
            }

            // 危 над тем, кто начал опасную атаку; 斬 на месте добивания
            if (cam != null)
            {
                foreach (System.Collections.Generic.KeyValuePair<FighterController, float> kv in duel.PerilAt)
                {
                    float age = Time.unscaledTime - kv.Value;
                    if (kv.Key == null || age > 0.9f) continue;
                    Vector3 g = cam.WorldToScreenPoint(kv.Key.transform.position + Vector3.up * 2.6f);
                    if (g.z < 0f) continue;
                    float a = Mathf.Clamp01(age / 0.06f) * Mathf.Clamp01((0.9f - age) / 0.25f);
                    float sz = 74f * (1f + 0.35f * Mathf.Max(0f, 0.15f - age) / 0.15f);
                    DrawGlyph("UI/kiken", new Vector2(g.x / UiKit.Scale, (Screen.height - g.y) / UiKit.Scale), sz, a);
                }
                // стойка врага сменилась: 八相 (хассо) / 脇構 (вакигамаэ) над головой на 1,6 с — по стойке читается удар;
                // первые разы за запуск — ещё и подпись, чем он из неё бьёт
                WeaponController ew = duel.enemy != null ? duel.enemy.weapon : null;
                if (ew != null && duel.State == DuelState.Fighting)
                {
                    if (ew.stance != lastStance) { lastStance = ew.stance; stanceAt = Time.unscaledTime; if (ew.stance != 0) stanceShown++; }
                    float sa = Time.unscaledTime - stanceAt;
                    if (ew.stance != 0 && sa < 1.6f)
                    {
                        Vector3 sgp = cam.WorldToScreenPoint(duel.enemy.transform.position + Vector3.up * 2.5f);
                        if (sgp.z > 0f)
                        {
                            float sal = Mathf.Clamp01(sa / 0.15f) * Mathf.Clamp01((1.6f - sa) / 0.4f);
                            Vector2 sc2 = new Vector2(sgp.x / UiKit.Scale, (Screen.height - sgp.y) / UiKit.Scale);
                            DrawGlyphWide(ew.stance == 1 ? "UI/hasso" : "UI/waki", sc2, 92f, sal * 0.92f);
                            if (stanceShown <= 3)
                                UiKit.Text(new Rect(sc2.x - 160f, sc2.y + 18f, 320f, 22f), ew.stance == 1 ? "ХАССО — ЖДИ УДАР СВЕРХУ" : "ВАКИГАМАЭ — СНИЗУ И ПО НОГАМ", 14f, new Color(1f, 0.85f, 0.7f, sal));
                        }
                    }
                }
                // метка добивания (как красная точка Sekiro): красное энсо на груди «поплывшего» врага, пульсирует и медленно вращается
                FighterController br = duel.enemy;
                if (br != null && br.PostureBroken && !br.Dead && !duel.DeathblowActive)
                {
                    if (markSince < 0f) markSince = Time.unscaledTime;
                    float ma = Time.unscaledTime - markSince;
                    Vector3 g = cam.WorldToScreenPoint(br.transform.position + Vector3.up * 1.22f);
                    if (g.z > 0f)
                    {
                        float pulse = 1f + 0.09f * Mathf.Sin(Time.unscaledTime * 7f) + 0.5f * Mathf.Max(0f, 0.18f - ma) / 0.18f;
                        Vector2 c = new Vector2(g.x / UiKit.Scale, (Screen.height - g.y) / UiKit.Scale);
                        Matrix4x4 m0 = GUI.matrix;
                        GUIUtility.RotateAroundPivot(-Time.unscaledTime * 25f, c * UiKit.Scale);
                        DrawGlyph("UI/deathblow_mark", c, 62f * pulse, Mathf.Clamp01(ma / 0.12f));
                        GUI.matrix = m0;
                    }
                }
                else markSince = -1f;
                float da = Time.unscaledTime - duel.DeathblowAt;
                if (da < 1.9f)
                {
                    // 忍殺: кисть проводит знак слева направо за 0,22 с, лёгкий «удар» масштабом, держится и гаснет
                    Vector3 g = cam.WorldToScreenPoint(duel.DeathblowPos);
                    if (g.z > 0f)
                    {
                        float pop = 1f + 0.18f * Mathf.Max(0f, 0.22f - da) / 0.22f;
                        // косой мазок туши через точку удара — кисть «рассекает» кадр за 0,12 с, тёмно-багровый, тает за секунду
                        if (da < 1.2f) UiKit.Ink("FX/ink_slash", new Vector2(g.x / UiKit.Scale, (Screen.height - g.y) / UiKit.Scale), 330f, new Color(0.5f, 0.03f, 0.03f, 0.85f * Mathf.Clamp01((1.2f - da) / 0.6f)), Mathf.Clamp01(da / 0.12f));
                        DrawBrush("UI/deathblow", new Vector2(g.x / UiKit.Scale, (Screen.height - g.y) / UiKit.Scale - 70f), 170f * pop, Mathf.Clamp01((1.9f - da) / 0.45f), Mathf.Clamp01(da / 0.22f));
                    }
                }
            }

            // таймер раунда (выключен в настройках — в медальоне знак бесконечности из двух колечек)
            Vector2 tc = new Vector2(cx, 28f);
            UiKit.Disc(tc, 30f, Frame);
            UiKit.Ring(tc, 30f, Gold);
            if (DuelManager.TimerOn)
            {
                int secs = Mathf.CeilToInt(duel.TimeLeft);
                bool hurry = secs <= 10 && duel.State == DuelState.Fighting;
                float tp = hurry ? 1f + 0.12f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f)) : 1f;
                UiKit.Text(new Rect(tc.x - 30f, tc.y - 22f, 60f, 44f), secs.ToString(), 26f * tp, hurry ? new Color(1f, 0.3f, 0.2f) : Color.white, TextAnchor.MiddleCenter, false);
            }
            else { UiKit.Ring(tc + new Vector2(-6.5f, 0f), 7.5f, Gold, true); UiKit.Ring(tc + new Vector2(6.5f, 0f), 7.5f, Gold, true); }

            // супер-удар: затемнение, плашка с названием, вспышка при ударе
            if (duel.SuperActive)
            {
                float st = duel.SuperTime;
                bool mine = duel.SuperBy == duel.player;
                if (st < DuelManager.SuperWindup)
                {
                    float k = Mathf.Clamp01(st / 0.15f);
                    UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0.03f, 0.5f * k));
                    // мазок туши кистью через экран (вместо плоской цветной плашки) и надпись цветом владельца
                    float reveal = Mathf.Clamp01(st / 0.22f);
                    Color glow = mine ? new Color(0.45f, 0.8f, 1f) : new Color(0.85f, 0.45f, 1f);
                    UiKit.Rect(new Rect(0f, 228f, W, 74f), new Color(glow.r * 0.12f, glow.g * 0.08f, glow.b * 0.15f, 0.45f * k));
                    DrawBrush(mine ? "UI/brush_stroke" : "UI/brush_stroke_red", new Vector2(W * 0.5f, 265f), 150f, 0.95f * k, reveal);   // удар духа врага — багровым мазком
                    float ta = Mathf.Clamp01((st - 0.12f) / 0.12f);
                    UiKit.Text(new Rect(0f, 222f, W, 86f), "УДАР ДУХА", 60f * (1f + 0.25f * (1f - ta)), new Color(Mathf.Lerp(1f, glow.r, 0.35f), Mathf.Lerp(1f, glow.g, 0.35f), Mathf.Lerp(1f, glow.b, 0.35f), ta));
                }
                else
                {
                    float f = 1f - Mathf.Clamp01((st - DuelManager.SuperWindup) / 0.3f);
                    if (duel.SuperLanded) UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(1f, 1f, 1f, 0.8f * f * f));
                }
            }

            // объявления
            bool hasSub = !string.IsNullOrEmpty(duel.AnnounceSub);
            float annLife = hasSub ? 2.6f : 1.4f;   // с подсказкой — дольше, чтобы успеть прочесть
            if (!string.IsNullOrEmpty(duel.AnnounceText) && duel.AnnounceAge < annLife && duel.State != DuelState.Intro)
            {
                float age = duel.AnnounceAge;
                float aa = Mathf.Clamp01(age / 0.1f) * Mathf.Clamp01((annLife - age) / 0.4f);
                float sc = 1f + Mathf.Max(0f, 0.2f - age) * 2f;
                Color ac = duel.AnnounceColor; ac.a = aa;
                UiKit.Rect(new Rect(0f, 168f, W, hasSub ? 78f : 52f), new Color(0f, 0f, 0f, 0.35f * aa));
                UiKit.Text(new Rect(0f, 166f, W, 56f), duel.AnnounceText, 40f * sc, ac);
                if (hasSub) UiKit.Text(new Rect(0f, 214f, W, 28f), duel.AnnounceSub, 18f, new Color(1f, 0.88f, 0.75f, aa * Mathf.Clamp01((age - 0.25f) / 0.2f)));
            }

            // серии ударов
            DrawCombo(duel.player, new Vector2(70f, 150f), TextAnchor.MiddleLeft);
            DrawCombo(duel.enemy, new Vector2(W - 70f, 150f), TextAnchor.MiddleRight);

            // счёт раундов: по два ромба под полосками
            DrawPips(new Vector2(cx - 92f, 64f), duel.PlayerWins, duel.winsToMatch, false);
            DrawPips(new Vector2(cx + 92f, 64f), duel.EnemyWins, duel.winsToMatch, true);

            // «БОЙ!»
            if (duel.State == DuelState.Intro)
            {
                bool deciding = duel.PlayerWins == duel.winsToMatch - 1 && duel.EnemyWins == duel.winsToMatch - 1;
                float ta = Mathf.Clamp01(1f - duel.StateTime / duel.IntroLength * 0.9f);
                UiKit.Text(new Rect(cx - 300f, 110f, 600f, 40f), deciding ? "РЕШАЮЩИЙ РАУНД" : "РАУНД " + duel.Round, 26f, new Color(0.95f, 0.9f, 0.8f, ta));
                if (duel.Round == 1)
                {
                    // мазок туши под титулом врага (как имя босса в Sekiro): кисть проводит его слева направо
                    DrawBrush("UI/brush_stroke", new Vector2(cx, 292f), 120f, 0.88f * ta, Mathf.Clamp01(duel.StateTime / 0.45f));
                    UiKit.Text(new Rect(cx - 400f, 262f, 800f, 34f), "ПУТЬ " + (Ladder.Index + 1) + " / " + Ladder.Count + (Profile.Cycle > 0 ? "  ·  КРУГ " + (Profile.Cycle + 1) : "") + "  ·  " + Ladder.Current.name, 22f, new Color(Gold.r, Gold.g, Gold.b, ta));
                    UiKit.Text(new Rect(cx - 400f, 292f, 800f, 24f), Ladder.Current.title, 15f, new Color(0.9f, 0.88f, 0.82f, ta * 0.9f));
                    int mk = duel.MaxMarks(duel.enemy);
                    if (mk > 1) UiKit.Text(new Rect(cx - 400f, 316f, 800f, 24f), "ЖИЗНЕЙ: " + mk + " — СЛОМИ И ДОБЕЙ " + mk + " РАЗА", 15f, new Color(1f, 0.45f, 0.3f, ta * 0.95f));
                    // время суток и погода матча — строкой под титулом
                    Weather wz = Weather.Instance;
                    if (wz != null)
                    {
                        bool sak = Weather.LeafMode == 2;
                        string wt = wz.Raining ? "НОЧЬ · ЛИВЕНЬ" : wz.Snowing ? (wz.HeavySnow ? "НОЧЬ · СНЕГОПАД" : "НОЧЬ · ПЕРВЫЙ СНЕГ") : wz.Foggy ? "НОЧЬ · ТУМАН" : wz.Dawn ? (sak ? "РАССВЕТ · ЦВЕТЁТ САКУРА" : "РАССВЕТ В ТУМАНЕ") : wz.Dusk ? (sak ? "ЗАКАТ · ЦВЕТЁТ САКУРА" : "ЗАКАТ") : wz.Daytime ? (sak ? "ВЕСНА · ЦВЕТЁТ САКУРА" : "СОЛНЕЧНЫЙ ДЕНЬ") : wz.BloodMoon ? "НОЧЬ · КРОВАВАЯ ЛУНА" : "ЯСНАЯ НОЧЬ";
                        UiKit.Text(new Rect(cx - 400f, mk > 1 ? 340f : 316f, 800f, 22f), wt, 13f, new Color(0.8f, 0.82f, 0.86f, ta * 0.8f));
                    }
                }
                float t = duel.StateTime / duel.IntroLength;
                // первая половина: номер раунда тушью (壱 弐 参) в багровом круге энсо; вторая — 始 («начало») кистью за «БОЙ!»
                const float split = 0.5f;
                Vector2 rc = new Vector2(cx, 205f);
                if (t < split)
                {
                    float rAl = Mathf.Clamp01(t / 0.06f) * Mathf.Clamp01((split - t) / 0.07f);
                    float rv = Mathf.Clamp01(t / 0.16f);
                    UiKit.Ink("UI/enso", rc, 190f * (1.06f - 0.06f * rv), new Color(0.72f, 0.1f, 0.08f, 0.9f * rAl), rv);
                    string rg = duel.Round == 1 ? "UI/ink_r1" : duel.Round == 2 ? "UI/ink_r2" : duel.Round == 3 ? "UI/ink_r3" : null;
                    if (rg != null) UiKit.Ink(rg, rc, 116f, new Color(0.96f, 0.92f, 0.84f, rAl), Mathf.Clamp01((t - 0.05f) / 0.14f));
                }
                else
                {
                    float t2 = (t - split) / (1f - split);
                    float a = t2 < 0.6f ? 1f : Mathf.Clamp01((1f - t2) * 2.5f);
                    float sc = 1f + (1f - Mathf.Clamp01(t2 * 4f)) * 0.8f;
                    UiKit.Ink("UI/ink_hajime", rc, 210f * (1.12f - 0.12f * Mathf.Clamp01(t2 * 3f)), new Color(0.7f, 0.08f, 0.06f, 0.6f * a), Mathf.Clamp01(t2 * 5f));
                    UiKit.Text(new Rect(cx - 300f, 150f, 600f, 110f), "БОЙ!", 78f * sc, new Color(Gold.r, Gold.g, Gold.b, a));
                }
            }

            // итог
            if (duel.State == DuelState.PlayerWon || duel.State == DuelState.PlayerLost)
            {
                // кадр удара KO: на мгновение экран в красно-чёрных тонах
                if (duel.StateTime < 0.14f && !duel.TimedOut)
                {
                    float kf = 1f - duel.StateTime / 0.14f;
                    UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0.35f, 0f, 0f, 0.55f * kf));
                }
                float t = Mathf.Clamp01((duel.StateTime - 0.5f) / 0.8f);
                bool won = duel.State == DuelState.PlayerWon;
                UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, 0.55f * t));
                Color c = won ? Gold : new Color(0.85f, 0.15f, 0.12f);
                c.a = t;
                string title = duel.MatchOver ? (won ? (Ladder.IsLast ? "ПУТЬ ПРОЙДЕН" : "ПОБЕДА") : "ПОРАЖЕНИЕ") : (won ? "РАУНД ВЫИГРАН" : "РАУНД ПРОИГРАН");
                if (duel.LastDraw) { title = "НИЧЬЯ"; c = new Color(0.85f, 0.85f, 0.9f, t); }
                string sub = duel.TimedOut ? "ВРЕМЯ ВЫШЛО" : "";
                if (duel.Perfect && !duel.LastDraw) sub = "ИДЕАЛЬНО";
                // знак кистью за заголовком: 死 — поражение (как экран смерти Sekiro), 勝 — победа; проявляется мазком слева направо
                if (!duel.TimedOut && !duel.LastDraw)
                {
                    float gt = Mathf.Clamp01((duel.StateTime - 0.35f) / 0.5f);
                    if (gt > 0f) DrawBrush(won ? "UI/sho" : "UI/shi", new Vector2(cx, 172f), 330f * (1.08f - 0.08f * gt), 0.62f * gt, Mathf.Clamp01(gt * 1.6f));
                }
                if (sub.Length > 0) UiKit.Text(new Rect(cx - 300f, 70f, 600f, 44f), sub, 30f, new Color(1f, 0.85f, 0.35f, t));
                UiKit.Text(new Rect(cx - 400f, 110f, 800f, 120f), title, duel.MatchOver ? 90f : 64f, c);
                if (duel.MatchOver) DrawStats(cx, 240f, t);
                if (!duel.MatchOver) UiKit.Text(new Rect(cx - 300f, 250f, 600f, 40f), duel.PlayerWins + " : " + duel.EnemyWins, 34f, new Color(1f, 1f, 1f, t));
                UiKit.Rect(new Rect(cx - 200f, 232f, 400f, 2f), new Color(c.r, c.g, c.b, 0.8f * t));
            }
            // 回生: павший игрок может подняться (раз за матч) — 死, кнопка воскрешения и тающий отсчёт
            if (duel.ResurrectOffered)
            {
                float oa = duel.ResurrectOfferAge;
                float k = Mathf.Clamp01(oa / 0.6f);
                UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0.05f, 0f, 0f, 0.55f * k));
                DrawBrush("UI/shi", new Vector2(cx, 175f), 300f, 0.85f * k, Mathf.Clamp01(oa / 0.5f));
                if (oa > 0.6f)
                {
                    float left = Mathf.Clamp01(1f - oa / DuelManager.ResurrectWindow);
                    float pulse = 1f + 0.05f * Mathf.Sin(Time.unscaledTime * 6f);
                    Rect br = new Rect(cx - 170f, 330f, 340f, 120f);
                    UiKit.Rect(new Rect(br.x, br.y, br.width, br.height), new Color(0f, 0f, 0f, 0.5f));
                    DrawBrush("UI/kaisei", new Vector2(cx, br.y + 48f), 92f * pulse, 1f, 1f);
                    UiKit.Text(new Rect(br.x, br.y + 86f, br.width, 28f), "ВОСКРЕСНУТЬ", 22f, new Color(0.95f, 0.9f, 0.8f, 1f));
                    UiKit.Rect(new Rect(br.x, br.yMax + 6f, br.width * left, 4f), new Color(0.9f, 0.15f, 0.1f, 0.9f));
                    if (GUI.Button(br, GUIContent.none, GUIStyle.none)) duel.Resurrect();
                }
            }
            float ra = Time.unscaledTime - duel.ResurrectedAt;
            if (ra < 1.6f)
            {
                // вспышка (прячет подъём тела) и знак 回生 над героем
                if (ra < 0.3f) UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(1f, 0.95f, 0.9f, 0.85f * (1f - ra / 0.3f)));
                if (cam != null && duel.player != null)
                {
                    Vector3 g = cam.WorldToScreenPoint(duel.player.transform.position + Vector3.up * 2.2f);
                    if (g.z > 0f) DrawBrush("UI/kaisei", new Vector2(g.x / UiKit.Scale, (Screen.height - g.y) / UiKit.Scale), 120f, Mathf.Clamp01((1.6f - ra) / 0.5f), Mathf.Clamp01(ra / 0.2f));
                }
            }

            // затемнение между раундами
            float fade = 0f;
            float nr = duel.NextRoundIn;
            if (nr >= 0f && nr < 0.45f) fade = 1f - nr / 0.45f;
            if (duel.State == DuelState.Intro) fade = Mathf.Max(fade, 1f - Mathf.Clamp01(duel.StateTime / (duel.Round > 1 ? 0.4f : 0.8f)));
            if (fade > 0.001f) UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, fade));

            DrawTutorial(W);
            GUI.matrix = old;
        }

        // подсказки первого боя (один раз)
        static readonly string[] Tips =
        {
            "ДЖОЙСТИК СЛЕВА — ДВИЖЕНИЕ, СВАЙП ПО ЭКРАНУ — КАМЕРА",
            "КНОПКИ СПРАВА — УДАРЫ. НАЖМИ ЕЩЁ ВО ВРЕМЯ УДАРА — ПОЙДЁТ СЕРИЯ",
            "ЩИТ — БЛОК. БЛОК В ПОСЛЕДНИЙ МИГ — ОТРАЖЕНИЕ. ЧАСТЫЕ НАЖАТИЯ НЕ ОТРАЖАЮТ",
            "ЖЁЛТАЯ ШКАЛА — РАВНОВЕСИЕ. СЛОМАЙ ЕГО ВРАГУ — И ОДИН УДАР УБЬЁТ",
            "КРАСНЫЙ ЗНАК НАД ВРАГОМ — БЛОК НЕ ДЕРЖИТ: ВЫПАД — РЫВОК НАВСТРЕЧУ, ПОДСЕЧКА — ПРЫЖОК",
            "ПОПАДАНИЯ КОПЯТ ДУХ. ПОЛНЫЙ ДУХ — УДАР ДУХА. ДВА РАУНДА — ПОБЕДА"
        };
        float tutStart = -1f;
        int tutDone = -1;

        void DrawTutorial(float W)
        {
            if (tutDone < 0) tutDone = PlayerPrefs.GetInt("tut_v2", 0);
            if (tutDone == 1) return;
            if (duel.State != DuelState.Fighting || duel.Round != 1) { if (tutStart >= 0f && duel.State != DuelState.Fighting) Finish(); return; }
            if (tutStart < 0f) tutStart = Time.unscaledTime;
            float t = Time.unscaledTime - tutStart;
            int i = Mathf.FloorToInt(t / 3.6f);
            if (i >= Tips.Length) { Finish(); return; }
            float lt = t - i * 3.6f;
            float a = Mathf.Clamp01(lt / 0.25f) * Mathf.Clamp01((3.6f - lt) / 0.3f);
            Rect r = new Rect(W * 0.5f - 380f, 112f, 760f, 30f);
            UiKit.Rect(r, new Color(0f, 0f, 0f, 0.55f * a));
            UiKit.Rect(new Rect(r.x, r.yMax - 2f, r.width * Mathf.Clamp01(lt / 3.6f), 2f), new Color(Gold.r, Gold.g, Gold.b, a));
            UiKit.Text(r, Tips[i], 14f, new Color(1f, 0.95f, 0.85f, a));
        }

        void Finish() { tutDone = 1; PlayerPrefs.SetInt("tut_v2", 1); }

        void DrawStats(float cx, float y, float a)
        {
            DuelManager.Stats p = duel.StatsOf(duel.player), e = duel.StatsOf(duel.enemy);
            string[] names = { "УРОН", "ПОПАДАНИЯ", "МАКС. СЕРИЯ", "ПАРИРОВАНИЯ" };
            int[] pv = { p.damage, p.hits, p.maxCombo, p.parries };
            int[] ev = { e.damage, e.hits, e.maxCombo, e.parries };
            for (int i = 0; i < names.Length; i++)
            {
                float ry = y + i * 14f;
                Color pc = pv[i] >= ev[i] ? Gold : new Color(0.85f, 0.85f, 0.85f);
                Color ec = ev[i] >= pv[i] ? Gold : new Color(0.85f, 0.85f, 0.85f);
                pc.a = a; ec.a = a;
                UiKit.Text(new Rect(cx - 170f, ry, 80f, 14f), pv[i].ToString(), 13f, pc, TextAnchor.MiddleRight, false);
                UiKit.Text(new Rect(cx - 80f, ry, 160f, 14f), names[i], 11f, new Color(0.85f, 0.8f, 0.7f, a), TextAnchor.MiddleCenter, false);
                UiKit.Text(new Rect(cx + 90f, ry, 80f, 14f), ev[i].ToString(), 13f, ec, TextAnchor.MiddleLeft, false);
            }
            // оценка боя кистью, как отметка учителя каллиграфии: 秀 / 優 / 良 / 可 — «печать» опускается на лист
            int score = (duel.PlayerWonMatch ? 40 : 0) + p.parries * 6 + p.maxCombo * 4 + p.hits * 2 + p.blocks + p.supers * 3 - e.hits * 5;
            string tex = score >= 95 ? "UI/rank_s" : score >= 70 ? "UI/rank_a" : score >= 45 ? "UI/rank_b" : "UI/rank_c";
            string word = score >= 95 ? "ПРЕВОСХОДНО" : score >= 70 ? "ОТЛИЧНО" : score >= 45 ? "ХОРОШО" : "ДОСТОЙНО";
            float stamp = Mathf.Clamp01(a * 1.4f);
            // гербы над колонками: перья — герой, томоэ — противник
            UiKit.Image(new Vector2(cx - 196f, y + 28f), 16f, UiKit.Tex("UI/kamon_feather"), new Color(1f, 1f, 1f, a));
            UiKit.Image(new Vector2(cx + 196f, y + 28f), 16f, UiKit.Tex("UI/kamon_tomoe"), new Color(1f, 1f, 1f, a));
            // печать итога матча слева (зеркально оценке): 勝 — победа, 敗 — поражение, 終 — весь путь пройден; чуть наискось, как оттиск
            string seal = !duel.PlayerWonMatch ? "UI/seal_hai" : (Ladder.IsLast ? "UI/seal_owari" : "UI/seal_sho");
            float st2 = Mathf.Clamp01(a * 1.4f - 0.15f);
            if (st2 > 0f)
            {
                Vector2 sc2 = new Vector2(cx - 262f, y + 24f);
                Matrix4x4 m0 = GUI.matrix;
                GUIUtility.RotateAroundPivot(-7f, sc2 * UiKit.Scale);
                DrawGlyph(seal, sc2, 80f * (1f + 0.8f * (1f - st2) * (1f - st2)), st2);
                GUI.matrix = m0;
            }
            Vector2 gc = new Vector2(cx + 262f, y + 24f);
            DrawGlyph(tex, gc, 86f * (1f + 0.7f * (1f - stamp) * (1f - stamp)), stamp);
            UiKit.Text(new Rect(gc.x - 70f, gc.y + 40f, 140f, 16f), word, 11f, new Color(0.9f, 0.82f, 0.68f, a), TextAnchor.MiddleCenter, false);
        }

        /// <summary>Квадрат с прозрачным кругом в центре: превращает квадратный портрет в круглый.</summary>
        static Texture2D MakeMask()
        {
            const int n = 96;
            Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            Color[] px = new Color[n * n];
            float c = (n - 1) * 0.5f, R = n * 0.5f - 3f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(d - R + 0.5f));
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        static Texture2D vignette;
        static Texture2D MakeVignette()
        {
            const int n = 128;
            Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            Color[] px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x / (n - 1f)) * 2f - 1f, dy = (y / (n - 1f)) * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy * 1.1f);
                    float a = Mathf.Clamp01((d - 0.62f) / 0.65f);
                    px[y * n + x] = new Color(1f, 1f, 1f, a * a);
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        /// <summary>Знак кистью с проявлением слева направо (reveal 0…1), высота h, ширина — по пропорциям текстуры.</summary>
        static void DrawBrush(string tex, Vector2 c, float h, float alpha, float reveal)
        {
            Texture2D t = UiKit.Tex(tex);
            if (t == null || alpha <= 0.01f || reveal <= 0.001f) return;
            float w = h * t.width / Mathf.Max(1f, t.height);
            reveal = 1f - (1f - reveal) * (1f - reveal);   // кисть быстро входит и замедляется к концу
            Color o = GUI.color; GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTextureWithTexCoords(new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w * reveal, h), t, new Rect(0f, 0f, reveal, 1f));
            GUI.color = o;
        }

        float markSince = -1f;

        int lastStance; float stanceAt = -10f; static int stanceShown;

        /// <summary>Широкий знак (два кандзи, текстура 2:1): ширина w, высота w/2.</summary>
        static void DrawGlyphWide(string tex, Vector2 c, float w, float alpha)
        {
            Texture2D t = UiKit.Tex(tex);
            if (t == null || alpha <= 0.01f) return;
            Color o = GUI.color; GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(c.x - w * 0.5f, c.y - w * 0.25f, w, w * 0.5f), t);
            GUI.color = o;
        }

        static void DrawGlyph(string tex, Vector2 c, float size, float alpha)
        {
            Texture2D t = UiKit.Tex(tex);
            if (t == null || alpha <= 0.01f) return;
            Color o = GUI.color; GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size), t);
            GUI.color = o;
        }

        void DrawPosture(Vector2 c, float w, FighterController f)
        {
            float k = f.Posture01;
            bool broken = f.PostureBroken;
            if (k < 0.01f && !broken) return;
            float h = 7f;
            Rect r = new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w, h);
            Texture2D pf = UiKit.Tex("UI/posture_frame");
            UiKit.Rect(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), new Color(0f, 0f, 0f, 0.7f));
            if (pf == null) UiKit.Rect(new Rect(c.x - 1f, r.y - 4f, 2f, h + 8f), new Color(1f, 0.85f, 0.4f, 0.5f));   // засечка середины
            Color col = Color.Lerp(new Color(1f, 0.82f, 0.25f), new Color(1f, 0.45f, 0.1f), Mathf.InverseLerp(0.4f, 0.75f, k));
            col = Color.Lerp(col, new Color(1f, 0.12f, 0.08f), Mathf.InverseLerp(0.75f, 1f, k));
            if (broken) { k = 1f; col = new Color(1f, 0.1f, 0.06f, 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 10f))); }
            float fw = w * k;
            UiKit.Rect(new Rect(c.x - fw * 0.5f, r.y, fw, h), col);
            // удар по равновесию — полоса на миг вспыхивает
            float ph = f.SincePostureHit;
            if (ph < 0.18f && !broken) UiKit.Rect(new Rect(c.x - fw * 0.5f, r.y, fw, h), new Color(1f, 1f, 0.9f, 0.7f * (1f - ph / 0.18f)));
            if (k > 0.75f || broken) UiKit.Rect(new Rect(c.x - fw * 0.5f, r.y - 1f, fw, 1.5f), new Color(1f, 1f, 0.8f, 0.6f));
            // рамка тушью с ромбом посередине (окно рамки в текстуре: x 6…506, y 22…42 из 512×64); на грани слома — краснеет
            if (pf != null)
            {
                float sx = w / 500f, sy = h / 20f;
                Color o = GUI.color;
                GUI.color = broken ? new Color(1f, 0.35f, 0.25f, 1f) : Color.Lerp(Color.white, new Color(1f, 0.55f, 0.35f), Mathf.InverseLerp(0.7f, 1f, k));
                GUI.DrawTexture(new Rect(r.x - 6f * sx, r.y - 22f * sy, 512f * sx, 64f * sy), pf);
                GUI.color = o;
            }
            if (broken && f != duel.enemy)   // у врага метка добивания — энсо на груди
            {
                // знак добивания: красная точка, как в Sekiro
                Vector2 m = new Vector2(c.x, r.y - 22f);
                float pul = 1f + 0.18f * Mathf.Sin(Time.unscaledTime * 9f);
                UiKit.Disc(m, 13f * pul, new Color(0.9f, 0.05f, 0.04f, 0.35f));
                UiKit.Disc(m, 8f * pul, new Color(1f, 0.12f, 0.08f, 0.95f));
                UiKit.Ring(m, 11f * pul, new Color(1f, 0.6f, 0.5f, 0.8f), true);
            }
        }

        void DrawKi(Rect r, FighterController f, bool mirror)
        {
            Texture2D kf = UiKit.Tex("UI/bar_ki");
            if (kf == null) UiKit.Rect(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), Frame);
            else UiKit.Rect(r, new Color(0.02f, 0.02f, 0.03f, 1f));
            float k = f.Ki01;
            float w = r.width * k;
            bool full = f.CanEnergy;
            Color c = full ? new Color(0.35f, 0.75f, 1f, 1f) : new Color(0.25f, 0.45f, 0.7f, 0.9f);
            if (full) c = Color.Lerp(c, Color.white, 0.25f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f));
            UiKit.Rect(mirror ? new Rect(r.x, r.y, w, r.height) : new Rect(r.xMax - w, r.y, w, r.height), c);
            // отметка стоимости шара
            float m = r.width * (f.energyCost / Mathf.Max(1f, f.kiMax));
            UiKit.Rect(new Rect(mirror ? r.x + m : r.xMax - m, r.y - 1f, 1.5f, r.height + 2f), new Color(1f, 1f, 1f, 0.6f));
            DrawFrame(r, kf, KiWin, mirror);
            UiKit.Text(new Rect(mirror ? r.xMax + 24f : r.x - 54f, r.y - 6f, 30f, 18f), "ДУХ", 9f, new Color(0.7f, 0.85f, 1f, 0.9f), mirror ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, false);
        }

        void DrawCombo(FighterController f, Vector2 at, TextAnchor anchor)
        {
            int n = duel.Combo(f);
            float age = duel.ComboAge(f);
            if (n < 2 || age > 1.6f) return;
            float a = Mathf.Clamp01((1.6f - age) * 2.5f);
            float pop = 1f + Mathf.Max(0f, 0.2f - age) * 2.5f;
            string word = (n % 10 >= 2 && n % 10 <= 4 && (n % 100 < 12 || n % 100 > 14)) ? "УДАРА" : "УДАРОВ";
            Rect r = anchor == TextAnchor.MiddleLeft ? new Rect(at.x, at.y - 40f, 300f, 80f) : new Rect(at.x - 300f, at.y - 40f, 300f, 80f);
            UiKit.Text(r, n.ToString(), 64f * pop, new Color(1f, 0.82f, 0.3f, a), anchor);
            Rect r2 = new Rect(r.x, r.y + 52f, r.width, 30f);
            UiKit.Text(r2, word + " ПОДРЯД", 16f, new Color(1f, 0.95f, 0.85f, a), anchor);
        }

        void DrawPips(Vector2 c, int wins, int total, bool mirror)
        {
            for (int i = 0; i < total; i++)
            {
                Vector2 p = new Vector2(c.x + (mirror ? 1f : -1f) * i * 26f, c.y);
                UiKit.Disc(p, 9f, Frame);
                UiKit.Disc(p, 6.5f, i < wins ? Gold : new Color(0.2f, 0.17f, 0.12f, 0.9f));
                UiKit.Ring(p, 9f, new Color(Gold.r, Gold.g, Gold.b, 0.8f), true);
            }
        }

        // окно заливки внутри рамки bar_hp.png (476x53): x 42..440, y 19..34 — см. tools/extract_ref.py
        static readonly Rect HpWin = new Rect(42f / 476f, 19f / 53f, 398f / 476f, 15f / 53f);
        static readonly Rect KiWin = new Rect(38f / 422f, 17f / 47f, 361f / 422f, 13f / 47f);

        /// <summary>Рамка-текстура вокруг прямоугольника заливки r (окно win в долях текстуры). Для правой полоски — зеркально.</summary>
        static void DrawFrame(Rect r, Texture2D t, Rect win, bool mirror)
        {
            if (t == null) return;
            float fw = r.width / win.width, fh = r.height / win.height;
            float fx = mirror ? r.xMax - (win.x + win.width) * fw : r.x - win.x * fw;
            Rect fr = new Rect(fx, r.y - win.y * fh, fw, fh);
            if (mirror) GUI.DrawTextureWithTexCoords(fr, t, new Rect(1f, 0f, -1f, 1f));
            else GUI.DrawTexture(fr, t);
        }

        void DrawBar(Rect r, FighterController f, bool mirror, string name, Color portrait)
        {
            float hp = duel.Hp01(f), tr = duel.Trail01(f);
            float since = duel.SinceHit(f);
            Texture2D frame = UiKit.Tex("UI/bar_hp");
            if (frame == null)
            {
                UiKit.Rect(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), Frame);
                UiKit.Rect(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), new Color(0.55f, 0.43f, 0.22f, 0.9f));
            }
            UiKit.Rect(r, new Color(0.05f, 0.01f, 0.01f, 1f));
            // шлейф и текущее
            DrawFill(r, tr, mirror, new Color(0.95f, 0.75f, 0.3f, 0.95f));
            Color main = hp > 0.3f ? new Color(0.72f, 0.07f, 0.08f) : new Color(0.95f, 0.16f, 0.08f);
            if (since < 0.12f) main = Color.white;
            DrawFill(r, hp, mirror, main);
            DrawFill(new Rect(r.x, r.y, r.width, r.height * 0.4f), hp, mirror, new Color(1f, 0.6f, 0.55f, 0.18f));   // блик
            DrawFill(new Rect(r.x, r.yMax - r.height * 0.3f, r.width, r.height * 0.3f), hp, mirror, new Color(0f, 0f, 0f, 0.25f));
            DrawFrame(r, frame, HpWin, mirror);

            // портрет за наружным концом рамки
            float px = mirror ? r.xMax + 66f : r.x - 66f;
            Vector2 pc = new Vector2(px, r.y + r.height * 0.5f + 4f);
            // портреты: герой — снимок модели героя (CI), враг — по облику: самурай в красном или ронин
            string pp;
            if (!mirror) pp = UiKit.Tex("UI/portrait_hero") != null ? "UI/portrait_hero" : "UI/portrait_ronin";
            else
            {
                // у каждого врага «Пути» свой портрет (Поверженный, Тень клана, Кровавый они, Проклятый сёгун); нет — по облику модели
                int li = Mathf.Clamp(Ladder.Index, 0, EnemyPortraits.Length - 1);
                if (UiKit.Tex(EnemyPortraits[li]) != null) pp = EnemyPortraits[li];
                else { EnemySkins sk = f.GetComponent<EnemySkins>(); pp = sk != null && sk.Active == 1 ? "UI/portrait_ronin" : "UI/portrait_undead"; }
            }
            Texture2D pt = UiKit.Tex(pp);
            if (pt != null)
            {
                Color tint = since < 0.18f ? new Color(1f, 0.55f, 0.5f) : Color.white;
                if (f.Dead) tint = new Color(0.4f, 0.4f, 0.4f);
                UiKit.Image(pc, 32f, pt, tint);
            }
            else
            {
                UiKit.Disc(pc, 24f, Frame);
                UiKit.Disc(pc, 20f, portrait * 0.55f);
                UiKit.Ring(pc, 24f, Gold);
            }

            UiKit.Text(new Rect(mirror ? r.xMax - 260f : r.x, r.y + 34f, 260f, 18f), name, 13f, new Color(0.9f, 0.85f, 0.7f, 0.95f), mirror ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
        }

        static readonly string[] EnemyPortraits = { "UI/portrait_fallen", "UI/portrait_shadow", "UI/portrait_oni", "UI/portrait_shogun" };

        static void DrawFill(Rect r, float k, bool mirror, Color c)
        {
            float w = r.width * Mathf.Clamp01(k);
            if (w < 0.5f) return;
            Rect f = mirror ? new Rect(r.x, r.y, w, r.height) : new Rect(r.xMax - w, r.y, w, r.height);   // дальний от центра край неподвижен? нет: убывает к краю экрана
            // игрок: полоска растёт от центра к краю (убывает к краю экрана); враг — зеркально
            f = mirror ? new Rect(r.x, r.y, w, r.height) : new Rect(r.xMax - w, r.y, w, r.height);
            UiKit.Rect(f, c);
        }
    }
}
