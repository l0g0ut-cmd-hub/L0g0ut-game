using UnityEngine;

namespace BladePath
{
    /// <summary>HUD дуэли: полоски здоровья, имена, «БОЙ!», цифры урона, итоговый экран. Только рисует; кнопки обрабатывает PrototypeInput.</summary>
    public class DuelHud : MonoBehaviour
    {
        void Awake()
        {
            useGUILayout = false;   // GUILayout нигде не используется: без лишнего прохода Layout IMGUI вдвое дешевле
        }

        public DuelManager duel;
        public string playerName = "РОНИН";
        public string enemyName = "ПОВЕРЖЕННЫЙ";

        static readonly Color Frame = new Color(0.04f, 0.03f, 0.03f, 0.92f);
        static readonly Color Gold = VisualIdentity.Brass;

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

        void OnDestroy()
        {
            if (hooked && duel != null)
                duel.Damaged -= OnDamaged;
            hooked = false;
            splats.Clear();
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

            // Camera blood belongs to the previous exchange, never the next
            // round's opening. Reset it before drawing the intro HUD.
            if (duel.State == DuelState.Intro) splats.Clear();
            DrawSplats();

            float barW = Mathf.Clamp(W * 0.5f - 80f - 110f, 200f, 400f);
            float cx = W * 0.5f;
            SideDuel sdl = SideDuel.Instance;
            if (sdl != null && sdl.Takeover && sdl.ally != null)   // герой пал — слева соратник, который ведёт бой
                DrawBar(new Rect(cx - 80f - barW, 22f, barW, 14f), sdl.ally, false, "СОРАТНИК · " + Profile.AllyTierNames[SideDuel.Tier], new Color(0.3f, 0.7f, 0.45f), sdl.AllyHp01, "UI/portrait_ronin");
            else DrawBar(new Rect(cx - 80f - barW, 22f, barW, 14f), duel.player, false,
                JourneyChoice.Names[JourneyChoice.Hero], new Color(0.15f, 0.55f, 0.85f));
            DrawBar(new Rect(cx + 80f, 22f, barW, 14f), duel.enemy, true, Ladder.Current.name, new Color(0.75f, 0.2f, 0.85f));
            // спутник героя: маленький портрет под портретом героя, кольцо — откат налёта
            if (Profile.Companion >= 0)
            {
                Texture2D ct = UiKit.Tex("UI/companion_" + (Profile.Companion == 0 ? "raven" : Profile.Companion == 1 ? "wolf" : "snake"));
                Vector2 cc = new Vector2(cx - 80f - barW - 66f + 34f, 70f);
                float rk = duel.CompanionReady01;
                UiKit.Disc(cc, 17f, new Color(0f, 0f, 0f, 0.6f));
                if (ct != null) UiKit.Image(cc, 15f, ct, rk >= 0.98f ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f));
                for (int k = 0; k < 24; k++)
                {
                    float ang = -Mathf.PI * 0.5f + k / 24f * Mathf.PI * 2f;
                    bool lit = k < Mathf.RoundToInt(rk * 24f);
                    UiKit.Disc(cc + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 17.5f, 1.6f, lit ? new Color(1f, 0.85f, 0.45f, 0.95f) : new Color(0f, 0f, 0f, 0.35f));
                }
            }
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
                    v.y = Mathf.Max(v.y, 136f);   // надписи над головой не заплывают на полоски здоровья и духа
                    Color c = p.color; c.a = Mathf.Clamp01(1.6f - age * 1.6f);
                    float sz = p.size * (1f + Mathf.Max(0f, 0.25f - age) * 2f);
                    UiKit.Text(new Rect(v.x - 60f, v.y - 20f, 120f, 40f), p.text, sz, c);
                }
            }

            // шкалы духа (ки) под полосками здоровья
            DrawKi(new Rect(cx - 80f - barW * 0.55f, 86f, barW * 0.55f, 10f), duel.player, false);   // толще: в новой рамке тонкую полосу духа не было видно
            DrawKi(new Rect(cx + 80f, 86f, barW * 0.55f, 10f), duel.enemy, true);

            // равновесие (как в Sekiro): у врага — над головой, у игрока — внизу по центру; полоса растёт от середины к краям
            if (cam != null && duel.State == DuelState.Fighting)
            {
                Vector3 ehp = duel.enemy.transform.position + Vector3.up * 2.05f;
                Vector3 esp = cam.WorldToScreenPoint(ehp);
                // над головой врага, но не выше полосок духа (y 88..94) и подписей: когда враг близко к камере,
                // голова уходит под верх экрана, и полоса ложилась прямо на «ДУХ»
                if (esp.z > 0f) DrawPosture(new Vector2(Mathf.Clamp(esp.x / UiKit.Scale, 80f, W - 80f), Mathf.Max((Screen.height - esp.y) / UiKit.Scale, 128f)), 130f, duel.enemy);
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
                    DrawBrush("UI/ru_danger", new Vector2(g.x / UiKit.Scale, (Screen.height - g.y) / UiKit.Scale), sz * 0.55f, a, 1f);
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
                            DrawBrush(ew.stance == 1 ? "UI/ru_high" : "UI/ru_low", sc2, 54f, sal * 0.92f, 1f);
                            if (stanceShown <= 3)
                                UiKit.Text(new Rect(sc2.x - 160f, sc2.y + 18f, 320f, 22f), ew.stance == 1 ? "ЖДИ УДАР СВЕРХУ" : "ЖДИ УДАР СНИЗУ И ПО НОГАМ", 14f, new Color(1f, 0.85f, 0.7f, sal));
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
                        Texture2D bsl = UiKit.Tex("UI/brush_slash");
                        if (da < 1.2f && bsl != null)
                        {
                            // кисть прочерчивает кадр по диагонали через точку удара (8 кадров за 0,3 с), потом брызги и потёки (ещё 8)
                            int bf = da < 0.3f ? (int)(da / 0.3f * 8f) : Mathf.Min(15, 8 + (int)((da - 0.3f) / 0.6f * 8f));
                            Vector2 bc = new Vector2(g.x / UiKit.Scale, (Screen.height - g.y) / UiKit.Scale);
                            float bs = 360f;
                            Color boc = GUI.color; GUI.color = new Color(0.9f, 0.35f, 0.3f, Mathf.Clamp01((1.2f - da) / 0.4f));
                            GUI.DrawTextureWithTexCoords(new Rect(bc.x - bs * 0.5f, bc.y - bs * 0.5f, bs, bs), bsl, new Rect((bf % 4) * 0.25f, 0.75f - (bf / 4) * 0.25f, 0.25f, 0.25f));
                            GUI.color = boc;
                        }
                        else if (da < 1.2f) UiKit.Ink("FX/ink_slash", new Vector2(g.x / UiKit.Scale, (Screen.height - g.y) / UiKit.Scale), 330f, new Color(0.5f, 0.03f, 0.03f, 0.85f * Mathf.Clamp01((1.2f - da) / 0.6f)), Mathf.Clamp01(da / 0.12f));
                        DrawBrush("UI/ru_deathblow", new Vector2(g.x / UiKit.Scale, (Screen.height - g.y) / UiKit.Scale - 70f), 95f * pop, Mathf.Clamp01((1.9f - da) / 0.45f), Mathf.Clamp01(da / 0.22f));
                    }
                }
            }

            // таймер раунда (выключен в настройках — в медальоне знак бесконечности из двух колечек)
            Vector2 tc = new Vector2(cx, 28f);
            Texture2D tmed = UiKit.Tex("UI/Kit/hud_timer");
            if (tmed != null) GUI.DrawTexture(new Rect(tc.x - 37f, tc.y - 35f, 74f, 70f), tmed);
            else { UiKit.Disc(tc, 30f, Frame); UiKit.Ring(tc, 30f, Gold); }
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
            if (!string.IsNullOrEmpty(duel.AnnounceText) && duel.AnnounceAge < annLife && duel.State == DuelState.Fighting)   // в итогах раунда не ложится на заголовок
            {
                float age = duel.AnnounceAge;
                float aa = Mathf.Clamp01(age / 0.1f) * Mathf.Clamp01((annLife - age) / 0.4f);
                float sc = 1f + Mathf.Max(0f, 0.2f - age) * 2f;
                Color ac = duel.AnnounceColor; ac.a = aa;
                // лента-объявление (UI/Kit): чёрная, для опасного (красно-оранжевого) — кроваво-красная
                bool danger = duel.AnnounceColor.r > 0.8f && duel.AnnounceColor.g < 0.55f;
                Texture2D ban = UiKit.Tex(danger ? "UI/Kit/hud_banner_red" : "UI/Kit/hud_banner");
                // ширина ленты — по длине надписи: текст помещается в ровную середину (рваные концы ~20% с каждой стороны);
                // длинная надпись («РАВНОВЕСИЕ СЛОМЛЕНО — ДОБЕЙ!») — мельче шрифт, а не вылезает за ленту
                float afs = 38f, atw = duel.AnnounceText.Length * afs * 0.7f, inner = (W - 40f) * 0.6f;
                if (atw > inner) { afs *= inner / atw; atw = inner; }
                if (ban != null)
                {
                    float bw = Mathf.Min(W - 40f, atw / 0.6f + 40f), bh = 74f * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(age / 0.12f));
                    UiKit.Slice(new Rect(cx - bw * 0.5f, 194f - bh * 0.5f, bw, bh), ban, bh / ban.height, 0.2f, 0.08f, 0.5f, 0f, new Color(1f, 1f, 1f, aa));
                }
                else UiKit.Rect(new Rect(0f, 168f, W, hasSub ? 78f : 52f), new Color(0f, 0f, 0f, 0.35f * aa));
                UiKit.Text(new Rect(0f, 166f, W, 56f), duel.AnnounceText, afs * sc, ac);
                if (hasSub) UiKit.Text(new Rect(0f, 232f, W, 26f), duel.AnnounceSub, 17f, new Color(1f, 0.88f, 0.75f, aa * Mathf.Clamp01((age - 0.25f) / 0.2f)));
            }

            // серии ударов
            // Brush-ink combo counters under the life bars, kept between the ki bars
            // and the announcement band (see ComboMaxH) so they never hide the fight.
            DrawCombo(duel.player, new Vector2(70f, 121f), TextAnchor.MiddleLeft);
            DrawCombo(duel.enemy, new Vector2(W - 70f, 121f), TextAnchor.MiddleRight);

            // счёт раундов: по два ромба под полосками
            DrawPips(new Vector2(cx - 92f, 64f), duel.PlayerWins, duel.NeedWins, false);
            DrawPips(new Vector2(cx + 92f, 64f), duel.EnemyWins, duel.NeedWins, true);

            // «БОЙ!»
            if (duel.State == DuelState.Intro)
            {
                bool deciding = duel.PlayerWins == duel.NeedWins - 1 && duel.EnemyWins == duel.NeedWins - 1;
                float ta = Mathf.Clamp01(1f - duel.StateTime / duel.IntroLength * 0.9f);
                UiKit.Text(new Rect(cx - 300f, 98f, 600f, 28f), deciding ? "РЕШАЮЩИЙ РАУНД" : "РАУНД", 20f, new Color(0.95f, 0.9f, 0.8f, ta));   // номер — цифрой в круге ниже
                if (duel.Round == 1)
                {
                    // враг в полный рост справа — представление противника, как у боссов
                    Texture2D ef = UiKit.Tex(EnemyFullArt[Mathf.Clamp(Ladder.Index, 0, EnemyFullArt.Length - 1)]);
                    if (ef != null)
                    {
                        float ia = Mathf.Clamp01(duel.StateTime / 0.5f) * Mathf.Clamp01((duel.IntroLength - duel.StateTime) / 0.5f);
                        float h = 380f, w = h * ef.width / Mathf.Max(1f, ef.height), slide = (1f - Mathf.Clamp01(duel.StateTime / 0.6f)) * 60f;
                        Color oc = GUI.color; GUI.color = new Color(1f, 1f, 1f, 0.92f * ia);
                        GUI.DrawTexture(new Rect(W - w - 30f + slide, 120f, w, h), ef);
                        GUI.color = oc;
                    }
                    // мазок туши под титулом врага (как имя босса в Sekiro): кисть проводит его слева направо
                    DrawBrush("UI/brush_stroke", new Vector2(cx, 318f), 120f, 0.88f * ta, Mathf.Clamp01(duel.StateTime / 0.45f));
                    UiKit.Text(new Rect(cx - 400f, 288f, 800f, 34f), "ПУТЬ " + (Ladder.Index + 1) + " / " + Ladder.Count + (Profile.Cycle > 0 ? "  ·  КРУГ " + (Profile.Cycle + 1) : "") + "  ·  " + Ladder.Current.name, 22f, new Color(Gold.r, Gold.g, Gold.b, ta));
                    UiKit.Text(new Rect(cx - 400f, 318f, 800f, 24f), Ladder.Current.title, 15f, new Color(0.9f, 0.88f, 0.82f, ta * 0.9f));
                    int mk = duel.MaxMarks(duel.enemy);
                    if (mk > 1) UiKit.Text(new Rect(cx - 400f, 342f, 800f, 24f), "ЖИЗНЕЙ: " + mk + " — СЛОМИ И ДОБЕЙ " + mk + " РАЗА", 15f, new Color(1f, 0.45f, 0.3f, ta * 0.95f));
                    // время суток и погода матча — строкой под титулом
                    Weather wz = Weather.Instance;
                    if (wz != null)
                    {
                        bool sak = Weather.LeafMode == 2;
                        string wt = wz.Raining ? "НОЧЬ · ЛИВЕНЬ" : wz.Snowing ? (wz.HeavySnow ? "НОЧЬ · СНЕГОПАД" : "НОЧЬ · ПЕРВЫЙ СНЕГ") : wz.Foggy ? "НОЧЬ · ТУМАН" : wz.Dawn ? (sak ? "РАССВЕТ · ЦВЕТЁТ САКУРА" : "РАССВЕТ В ТУМАНЕ") : wz.Dusk ? (sak ? "ЗАКАТ · ЦВЕТЁТ САКУРА" : "ЗАКАТ") : wz.Daytime ? (sak ? "ВЕСНА · ЦВЕТЁТ САКУРА" : "СОЛНЕЧНЫЙ ДЕНЬ") : wz.BloodMoon ? "НОЧЬ · КРОВАВАЯ ЛУНА" : "ЯСНАЯ НОЧЬ";
                        UiKit.Text(new Rect(cx - 400f, mk > 1 ? 366f : 342f, 800f, 22f), wt, 13f, new Color(0.8f, 0.82f, 0.86f, ta * 0.8f));
                    }
                }
                float t = duel.StateTime / duel.IntroLength;
                // первая половина: номер раунда тушью (壱 弐 参) в багровом круге энсо; вторая — 始 («начало») кистью за «БОЙ!»
                const float split = 0.5f;
                Vector2 rc = new Vector2(cx, 200f);   // круг 125…275: между «РАУНД» и титулом врага
                if (t < split)
                {
                    float rAl = Mathf.Clamp01(t / 0.06f) * Mathf.Clamp01((split - t) / 0.07f);
                    float rv = Mathf.Clamp01(t / 0.16f);
                    UiKit.Ink("UI/enso", rc, 150f * (1.06f - 0.06f * rv), new Color(0.72f, 0.1f, 0.08f, 0.9f * rAl), rv);
                    // номер раунда — цифрой кистью (раньше иероглифы 壱 弐 参)
                    if (t > 0.05f) UiKit.BrushNumber(rc, duel.Round.ToString(), 84f, rAl, TextAnchor.MiddleCenter);
                }
                else
                {
                    float t2 = (t - split) / (1f - split);
                    float a = t2 < 0.6f ? 1f : Mathf.Clamp01((1f - t2) * 2.5f);
                    float sc = 1f + (1f - Mathf.Clamp01(t2 * 4f)) * 0.8f;
                    // «НАЧАЛИ!» кистью — само по себе, без второго слова поверх
                    UiKit.Ink("UI/ru_begin", rc, 120f * sc, new Color(1f, 1f, 1f, a), Mathf.Clamp01(t2 * 5f));
                }
            }

            // выхват: бойцы замерли; по знаку 斬 — жми удар
            if (duel.IaiWaiting)
            {
                bool cued = duel.IaiCued;
                UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, cued ? 0.15f : 0.35f));
                if (!cued) UiKit.Text(new Rect(cx - 300f, 150f, 600f, 40f), "ВЫХВАТ", 34f, new Color(0.95f, 0.9f, 0.8f, 0.9f));
                UiKit.Text(new Rect(cx - 400f, 196f, 800f, 26f), cued ? "" : "жми удар, как только появится знак — не раньше", 15f, new Color(0.85f, 0.82f, 0.75f, 0.85f));
                if (cued)
                {
                    float k = Mathf.Clamp01((Time.unscaledTime - duel.IaiCueAt) / 0.08f);
                    DrawBrush("UI/ru_cut", new Vector2(cx, 200f), 115f * (1.25f - 0.25f * k), 1f, 1f);
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
                // победа в бою — надписью кистью из атласа («ПОБЕДА»), остальное (раунд, ничья, путь пройден, поражение) — шрифтом
                bool brushWin = duel.MatchOver && won && !duel.LastDraw && title == "ПОБЕДА" && UiKit.Tex("UI/ru_victory") != null;
                if (!duel.TimedOut && !duel.LastDraw && !brushWin)
                {
                    float gt = Mathf.Clamp01((duel.StateTime - 0.35f) / 0.5f);
                    if (gt > 0f) DrawBrush("UI/brush_stroke_red", new Vector2(cx, 172f), 120f * (1.08f - 0.08f * gt), (won ? 0.55f : 0.75f) * gt, Mathf.Clamp01(gt * 1.6f));   // мазок без слов — заголовок уже написан
                }
                if (sub.Length > 0) UiKit.Text(new Rect(cx - 300f, 98f, 600f, 30f), sub, 24f, new Color(1f, 0.85f, 0.35f, t));   // под полосками духа, над заголовком
                if (brushWin)
                {
                    float gt = Mathf.Clamp01((duel.StateTime - 0.35f) / 0.45f);
                    DrawBrush("UI/ru_victory", new Vector2(cx, 165f), 140f * (1.1f - 0.1f * gt), t, Mathf.Clamp01(gt * 1.4f));   // кисть входит мазком слева направо
                }
                else UiKit.Text(new Rect(cx - 400f, 110f, 800f, 120f), title, duel.MatchOver ? 90f : 64f, c);
                if (duel.MatchOver) DrawStats(cx, 240f, t);
                if (duel.MatchOver && SideDuel.Enabled && Profile.Ally >= 0 && Profile.AllyDown)
                    UiKit.Text(new Rect(cx - 300f, 352f, 600f, 22f), "СОРАТНИК ПАЛ — ЕГО ВЫХОДИТ ТРАВНИЦА", 14f, new Color(1f, 0.45f, 0.35f, t));
                if (!duel.MatchOver) UiKit.Text(new Rect(cx - 300f, 250f, 600f, 40f), duel.PlayerWins + " : " + duel.EnemyWins, 34f, new Color(1f, 1f, 1f, t));
                UiKit.Rect(new Rect(cx - 200f, 232f, 400f, 2f), new Color(c.r, c.g, c.b, 0.8f * t));
            }
            // 回生: павший игрок может подняться (раз за матч) — 死, кнопка воскрешения и тающий отсчёт
            if (duel.ResurrectOffered)
            {
                float oa = duel.ResurrectOfferAge;
                float k = Mathf.Clamp01(oa / 0.6f);
                UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0.05f, 0f, 0f, 0.55f * k));
                DrawBrush("UI/ru_death", new Vector2(cx, 175f), 125f, 0.85f * k, Mathf.Clamp01(oa / 0.5f));
                if (oa > 0.6f)
                {
                    float left = Mathf.Clamp01(1f - oa / DuelManager.ResurrectWindow);
                    float pulse = 1f + 0.05f * Mathf.Sin(Time.unscaledTime * 6f);
                    Rect br = new Rect(cx - 170f, 330f, 340f, 120f);
                    UiKit.Rect(new Rect(br.x, br.y, br.width, br.height), new Color(0f, 0f, 0f, 0.5f));
                    DrawBrush("UI/ru_revive", new Vector2(cx, br.y + 46f), 62f * pulse, 1f, 1f);
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
                    if (g.z > 0f) DrawBrush("UI/ru_revive", new Vector2(g.x / UiKit.Scale, (Screen.height - g.y) / UiKit.Scale), 64f, Mathf.Clamp01((1.6f - ra) / 0.5f), Mathf.Clamp01(ra / 0.2f));
                }
            }

            // линии скорости тушью по краям экрана — выхват, супер-удар, добивание, удар с III ступени накопления
            float sl = 0f;
            if (duel.IaiCued) sl = 0.9f;
            float slIai = Time.unscaledTime - duel.IaiResultAt; if (slIai < 0.45f) sl = Mathf.Max(sl, 0.9f * (1f - slIai / 0.45f));
            if (duel.SuperActive) sl = Mathf.Max(sl, 0.8f);
            if (duel.DeathblowActive) sl = Mathf.Max(sl, 0.7f);
            float slRel = Time.unscaledTime - ChargeSystem.BigReleaseAt; if (slRel < 0.55f) sl = Mathf.Max(sl, 0.85f * (1f - slRel / 0.55f));
            Texture2D slt = sl > 0.01f ? UiKit.Tex("UI/speed_lines") : null;
            if (slt != null)
            {
                int sf = (int)(Time.unscaledTime * 24f) % 4;   // дрожание: 4 варианта по кругу
                float slk = 1.04f + 0.03f * Mathf.Sin(Time.unscaledTime * 40f);
                Color slc = GUI.color; GUI.color = new Color(1f, 1f, 1f, sl);
                GUI.DrawTextureWithTexCoords(new Rect(W * 0.5f - W * slk * 0.5f, 270f - 540f * slk * 0.5f, W * slk, 540f * slk), slt, new Rect((sf % 2) * 0.5f, 0.5f - (sf / 2) * 0.5f, 0.5f, 0.5f));
                GUI.color = slc;
            }

            // затемнение между раундами
            float fade = 0f;
            float nr = duel.NextRoundIn;
            if (nr >= 0f && nr < 0.45f) fade = 1f - nr / 0.45f;
            if (duel.State == DuelState.Intro) fade = Mathf.Max(fade, 1f - Mathf.Clamp01(duel.StateTime / (duel.Round > 1 ? 0.4f : 0.8f)));
            if (fade > 0.001f)
            {
                // смена раунда — клякса туши: растёт из капли и заливает экран; на новом раунде распадается брызгами
                Texture2D ink = UiKit.Tex("UI/ink_splash");
                bool opening = duel.State == DuelState.Intro;
                if (ink != null)
                {
                    int fr = opening ? (fade > 0.55f ? 2 : 3) : (fade < 0.3f ? 0 : fade < 0.6f ? 1 : 2);
                    float sz = W * (opening ? 1.2f + 1.4f * (1f - fade) : 0.35f + 1.5f * fade);
                    Color oc = GUI.color; GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(fade * 1.6f));
                    GUI.DrawTextureWithTexCoords(new Rect(W * 0.5f - sz * 0.5f, 270f - sz * 0.5f, sz, sz), ink, new Rect(fr * 0.25f, 0f, 0.25f, 1f));
                    GUI.color = oc;
                    UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, Mathf.Clamp01((fade - 0.55f) / 0.45f)));
                }
                else UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, fade));
            }

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
            if (st2 > 0f && UiKit.Tex(seal + "_ru") != null)
            {
                seal += "_ru";
                Vector2 sc2 = new Vector2(cx - 262f, y + 24f);
                Matrix4x4 m0 = GUI.matrix;
                GUIUtility.RotateAroundPivot(-7f, sc2 * UiKit.Scale);
                DrawGlyph(seal, sc2, 80f * (1f + 0.8f * (1f - st2) * (1f - st2)), st2);
                GUI.matrix = m0;
            }
            Vector2 gc = new Vector2(cx + 262f, y + 24f);
            if (UiKit.Tex(tex + "_ru") != null) DrawGlyph(tex + "_ru", gc, 86f * (1f + 0.7f * (1f - stamp) * (1f - stamp)), stamp);   // оценка — звёзды в печати (раньше 秀 優 良 可)
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
            // Understated danger notches when a fighter is close to losing
            // posture. Drawn outside the existing ornate bar frame; no extra
            // lettering or modal flashes competing with sword choreography.
            if (!broken && k > 0.84f && DuelManager.Gfx == 1)
            {
                float danger = Mathf.InverseLerp(0.84f, 1f, k);
                float breathe = 0.68f + 0.20f *
                    Mathf.Sin(Time.unscaledTime * 5f);
                Color warning = new Color(1f, 0.43f, 0.25f,
                    danger * breathe * 0.55f);
                UiKit.Rect(new Rect(r.x - 4f, r.y - 2f, 2f, h + 4f), warning);
                UiKit.Rect(new Rect(r.xMax + 2f, r.y - 2f, 2f, h + 4f), warning);
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
            Texture2D kf = UiKit.Tex("UI/Kit/hud_ki");
            Rect kiWin = KiWinKit;
            if (kf == null) { kf = UiKit.Tex("UI/bar_ki"); kiWin = KiWin; }
            // окно внутри рамки — заливка идёт ровно туда
            Rect win = (kf != null) ? kiWin : new Rect(0f, 0f, 1f, 1f);
            float fillX = r.x + r.width * win.x;
            float fillW = r.width * win.width;
            float fillY = r.y + r.height * win.y;
            float fillH = r.height * win.height;
            // тёмный фон только внутри окна
            if (kf == null) UiKit.Rect(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), Frame);
            else UiKit.Rect(new Rect(fillX, fillY, fillW, fillH), new Color(0.02f, 0.02f, 0.03f, 1f));
            float k = f.Ki01;
            float w = fillW * k;
            bool full = f.CanEnergy;
            Color c = full ? new Color(0.35f, 0.75f, 1f, 1f) : new Color(0.25f, 0.45f, 0.7f, 0.9f);
            if (full) c = Color.Lerp(c, Color.white, 0.25f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f));
            UiKit.Rect(mirror ? new Rect(fillX, fillY, w, fillH) : new Rect(fillX + fillW - w, fillY, w, fillH), c);
            // отметка стоимости шара
            float m = fillW * (f.energyCost / Mathf.Max(1f, f.kiMax));
            UiKit.Rect(new Rect(mirror ? fillX + m : fillX + fillW - m, fillY - 1f, 1.5f, fillH + 2f), new Color(1f, 1f, 1f, 0.6f));
            DrawFrame(r, kf, kiWin, mirror);
            UiKit.Text(new Rect(mirror ? r.xMax + 24f : r.x - 54f, r.y - 6f, 30f, 18f), "ДУХ", 9f, new Color(0.7f, 0.85f, 1f, 0.9f), mirror ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, false);
        }

        // ---- Серия ударов: кисточные цифры на брызге туши, подпись на красном мазке ----
        // Три ступени: 2–4 «СЕРИЯ» (золото), 5–9 «ВИХРЬ КЛИНКА» (огонь, 斬), 10+ «БУРЯ СТАЛИ» (алый, свечение).
        // Цифра подпрыгивает на каждом ударе; при переходе на ступень выше — всплеск туши.
        // Вся серия держится между полоской духа (y 94) и полосой объявлений (y 168),
        // чтобы не закрывать бой и баннеры: ComboMaxH — предел высоты цифр.
        const float ComboMaxH = 66f;
        static readonly string[] ComboTitles = { "СЕРИЯ", "ВИХРЬ КЛИНКА", "БУРЯ СТАЛИ" };
        readonly int[] comboTier = new int[2];
        readonly float[] comboTierAt = { -10f, -10f };

        static int ComboGlyph(char ch) { return ch >= '0' && ch <= '9' ? ch - '0' : ch == '×' ? 10 : -1; }

        static void DrawComboDigits(Texture2D t, string s, float x, float cy, float h, Color col, float grow)
        {
            if (t == null) return;
            float gw = h * 0.66f, step = h * 0.56f;
            Color o = GUI.color; GUI.color = col;
            for (int i = 0; i < s.Length; i++)
            {
                int g = ComboGlyph(s[i]);
                if (g < 0) continue;
                Rect uv = new Rect((g % 4) * 0.25f, 1f - (g / 4 + 1) / 3f, 0.25f, 1f / 3f);
                GUI.DrawTextureWithTexCoords(new Rect(x + i * step - grow, cy - h * 0.5f - grow,
                    gw + grow * 2f, h + grow * 2f), t, uv);
            }
            GUI.color = o;
        }

        static void DrawSplashFrame(Texture2D t, int frame, Vector2 c, float size, Color col)
        {
            if (t == null) return;
            Color o = GUI.color; GUI.color = col;
            GUI.DrawTextureWithTexCoords(new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size), t,
                new Rect(Mathf.Clamp(frame, 0, 3) * 0.25f, 0f, 0.25f, 1f));
            GUI.color = o;
        }

        void DrawCombo(FighterController f, Vector2 at, TextAnchor anchor)
        {
            bool right = anchor == TextAnchor.MiddleRight;
            int side = right ? 1 : 0;
            int hits = duel.State == DuelState.Fighting ? duel.Combo(f) : 0;
            float age = duel.ComboAge(f);
            if (hits < 2 || age > 1.6f) { comboTier[side] = 0; return; }

            int tier = hits < 5 ? 0 : hits < 10 ? 1 : 2;
            if (tier > comboTier[side]) comboTierAt[side] = Time.unscaledTime;
            comboTier[side] = tier;
            float fade = Mathf.Clamp01((1.6f - age) / 0.4f);
            Color accent = tier == 0 ? new Color(0.96f, 0.81f, 0.47f)
                : tier == 1 ? new Color(1f, 0.50f, 0.20f) : new Color(1f, 0.24f, 0.16f);

            string s = "×" + hits;
            float pop = 1f + 0.18f * Mathf.Exp(-age * 14f);      // прыжок цифры на каждом ударе
            float h = (tier == 0 ? 52f : tier == 1 ? 58f : ComboMaxH) * pop;
            float cy = at.y + 10f;
            float total = h * 0.56f * (s.Length - 1) + h * 0.66f;
            float x0 = right ? at.x - total : at.x;
            Vector2 numC = new Vector2(x0 + total * 0.5f, cy);

            // брызга туши за числом
            Texture2D splash = UiKit.Tex("UI/ink_splash");
            DrawSplashFrame(splash, 2, numC, h * 1.9f, new Color(0.03f, 0.015f, 0.015f, 0.8f * fade));

            // мазок кисти под подписью
            const float labelW = 140f;
            float lx = right ? x0 - 8f - labelW : x0 + total + 8f;
            Texture2D stroke = UiKit.Tex("UI/brush_stroke_red");
            if (stroke != null)
            {
                Color o = GUI.color;
                GUI.color = tier == 0 ? new Color(0.45f, 0.30f, 0.22f, 0.85f * fade)
                    : new Color(1f, 1f, 1f, (tier == 2 ? 0.9f : 0.78f) * fade);
                Rect br = new Rect(lx - 18f, cy - 22f, labelW + 36f, 44f);
                GUI.DrawTextureWithTexCoords(br, stroke, right ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f));
                GUI.color = o;
            }

            // всплеск при переходе на новую ступень
            float up = Time.unscaledTime - comboTierAt[side];
            if (up < 0.35f && tier > 0)
                DrawSplashFrame(splash, (int)(up / 0.35f * 4f), numC, h * 2.6f,
                    new Color(0.55f, 0.05f, 0.04f, (1f - up / 0.35f) * fade));

            Texture2D white = UiKit.Tex("UI/brush_digits_white");
            if (white == null)
            {
                UiKit.Text(new Rect(x0 - 10f, cy - 20f, total + 20f, 40f), s, 30f,
                    new Color(accent.r, accent.g, accent.b, fade));
            }
            else
            {
                if (tier > 0)
                {
                    float breathe = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 6f);
                    DrawComboDigits(UiKit.Tex("UI/brush_digits_glow"), s, x0, cy, h,
                        new Color(accent.r, accent.g, accent.b, (tier == 2 ? 0.75f : 0.4f) * breathe * fade), h * 0.12f);
                }
                DrawComboDigits(UiKit.Tex("UI/brush_digits"), s, x0 + 2.5f, cy + 3f, h,
                    new Color(0f, 0f, 0f, 0.9f * fade), 1.5f);
                DrawComboDigits(white, s, x0, cy, h, new Color(accent.r, accent.g, accent.b, fade), 0f);
            }

            TextAnchor ta = right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            UiKit.Text(new Rect(lx, cy - 20f, labelW, 22f), ComboTitles[tier], 17f,
                new Color(0.98f, 0.93f, 0.83f, fade), ta);
            UiKit.Text(new Rect(lx, cy + 2f, labelW, 16f), "ударов подряд", 11f,
                new Color(0.92f, 0.84f, 0.72f, fade), ta);
        }

        void DrawPips(Vector2 c, int wins, int total, bool mirror)
        {
            for (int i = 0; i < total; i++)
            {
                Vector2 p = new Vector2(c.x + (mirror ? 1f : -1f) * i * 26f, c.y);
                Texture2D pip = UiKit.Tex(i < wins ? "UI/Kit/hud_pip_full" : "UI/Kit/hud_pip_empty");
                if (pip != null) { GUI.DrawTexture(new Rect(p.x - 12f, p.y - 12f, 24f, 24f), pip); continue; }
                UiKit.Disc(p, 9f, VisualIdentity.InsetColor);
                UiKit.Disc(p, 6.5f, i < wins ? VisualIdentity.BrassLight : VisualIdentity.Raised);
                UiKit.Ring(p, 9f, new Color(Gold.r, Gold.g, Gold.b, 0.8f), true);
                if (i < wins)
                    UiKit.Disc(p + new Vector2(-2f, -2f), 2f,
                        new Color(1f, 0.97f, 0.82f, 0.72f));
            }
        }

        // окно заливки внутри рамки bar_hp.png (476x53): x 42..440, y 19..34 — см. tools/extract_ref.py
        // окна заливки в рамках UI/Kit (замерено по тёмной середине): hud_hp_l 578x160 — x 88..524, y 56..107; hud_ki 467x76 — x 34..421, y 14..59
        static readonly Rect HpWinKit = new Rect(88f / 578f, 56f / 160f, 436f / 578f, 51f / 160f);
        static readonly Rect KiWinKit = new Rect(34f / 467f, 14f / 76f, 387f / 467f, 45f / 76f);
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

        void DrawBar(Rect r, FighterController f, bool mirror, string name, Color portrait, float hpOverride = -1f, string portraitOverride = null)
        {
            float hp = hpOverride >= 0f ? hpOverride : duel.Hp01(f), tr = hpOverride >= 0f ? hpOverride : duel.Trail01(f);
            float since = duel.SinceHit(f);
            Texture2D frame = UiKit.Tex("UI/Kit/hud_hp_l");   // единый стиль: золото с шипами, массивный край — снаружи
            Rect hpWin = HpWinKit;
            if (frame == null) { frame = UiKit.Tex("UI/bar_hp"); hpWin = HpWin; }
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
            DrawFrame(r, frame, hpWin, mirror);

            // портрет за наружным концом рамки
            // кольцо с шипами крупнее старого ободка: портрет чуть меньше и ниже, чтобы шипы не срезались краем экрана
            float px = mirror ? r.xMax + 60f : r.x - 60f;
            Vector2 pc = new Vector2(px, r.y + r.height * 0.5f + 17f);
            // портреты: герой — снимок модели героя (CI), враг — по облику: самурай в красном или ронин
            string pp;
            // Each traveller's approved selection card contains a dedicated
            // circular face inset. Reuse it here so Akiko/Ren never show Ronin.
            // Dedicated round face portraits (UI/hero_face_N) come first; the
            // card inset remains a fallback if a face file is ever missing.
            if (portraitOverride != null) pp = portraitOverride;
            else if (!mirror)
            {
                pp = JourneyChoice.Face(JourneyChoice.Hero);
                if (UiKit.Tex(pp) == null) pp = JourneyChoice.Portrait(JourneyChoice.Hero);
            }
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
                if (!mirror && pp.StartsWith("UI/selection/hero_"))
                {
                    // Top-left face inset of each original full-body hero card.
                    // The crop covers the circular portrait, not the small body.
                    Rect target = new Rect(pc.x - 27f, pc.y - 27f, 54f, 54f);
                    Color previous = GUI.color;
                    GUI.color = tint;
                    GUI.DrawTextureWithTexCoords(target, pt,
                        new Rect(0.035f, 0.742f, 0.365f, 0.250f));
                    GUI.color = previous;
                    DrawPortraitRing(pc, false);
                }
                else
                {
                    UiKit.Image(pc, 27f, pt, tint);
                    DrawPortraitRing(pc, mirror);   // золотое кольцо с шипами; у врага — с кроваво-красными вставками
                }
            }
            else
            {
                UiKit.Disc(pc, 24f, Frame);
                UiKit.Disc(pc, 20f, portrait * 0.55f);
                UiKit.Ring(pc, 24f, Gold);
            }

            UiKit.Text(new Rect(mirror ? r.xMax - 260f : r.x, r.y + 34f, 260f, 18f),
                name, 13f, VisualIdentity.Parchment,
                mirror ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
            UiKit.Rect(new Rect(mirror ? r.xMax - 90f : r.x,
                r.y + 55f, 90f, 1f),
                new Color(Gold.r, Gold.g, Gold.b, 0.28f));
        }

        static readonly string[] EnemyFullArt = { "UI/enemy_full_fallen", "UI/enemy_full_shadow", "UI/enemy_full_oni", "UI/enemy_full_shogun" };
        static readonly string[] EnemyPortraits = { "UI/portrait_fallen", "UI/portrait_shadow", "UI/portrait_oni", "UI/portrait_shogun" };

        /// <summary>Кольцо поверх круглого портрета (радиус лица 32): дыра в текстуре — ~0,33 (герой) и ~0,30 (враг) ширины.</summary>
        static void DrawPortraitRing(Vector2 pc, bool enemy)
        {
            Texture2D t = UiKit.Tex(enemy ? "UI/Kit/hud_ring_enemy" : "UI/Kit/hud_ring_hero");
            if (t == null) { if (!enemy) UiKit.Ring(pc, 32f, Gold, true); return; }
            float w = 28f / (enemy ? 0.30f : 0.33f), h = w * t.height / t.width;
            GUI.DrawTexture(new Rect(pc.x - w * 0.5f, pc.y - h * 0.5f, w, h), t);
        }

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
