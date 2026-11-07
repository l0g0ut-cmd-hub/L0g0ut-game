using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Снаряжение героя (IMGUI, виртуальная высота 540): слева — уровень, опыт и пять характеристик с очками, сводка бонусов
    /// и изученные приёмы; в середине — шесть надетых предметов; справа — сумка и карточка выбранного предмета
    /// (надеть/снять, улучшить у кузнеца за монеты, продать, прочесть свиток). Плюс полоса «Добыча» на экране итогов матча.
    /// Значки и рамки — из атласов (UI/items 4×4, UI/frame_0…3, UI/stat_*), пока их нет — простые плашки.
    /// </summary>
    public class InventoryScreen : MonoBehaviour
    {
        void Awake()
        {
            useGUILayout = false;   // GUILayout нигде не используется: без лишнего прохода Layout IMGUI вдвое дешевле
        }

        public static bool Open { get; private set; }
        static InventoryScreen inst;
        Item sel; int page; float openedAt;
        bool runeModal; float runeModalAt;   // всплывающий выбор руны — отдельно от карточки, чтобы не обрезать эффекты предмета
        readonly List<Item> visibleBag = new List<Item>(64);
        int tab;                // 0 — снаряжение, 1 — прокачка
        int[] pending;          // распределение очков, пока не принято
        static readonly Color Gold = new Color(0.86f, 0.70f, 0.38f, 1f);
        static readonly Color Ink = new Color(0.95f, 0.9f, 0.8f, 1f);
        static readonly string[] Abbr = { "КЛ", "ЦБ", "ОД", "МС", "АМ", "ТЫ", "СВ" };

        public static void Ensure() { if (inst == null) inst = new GameObject("InventoryScreen").AddComponent<InventoryScreen>(); }
        public static void Show(int tab = 0) { Ensure(); Open = true; inst.openedAt = Time.unscaledTime; inst.sel = null; inst.page = 0; inst.tab = tab; inst.pending = Profile.StatsCopy(); }
        /// <summary>Снаряжение открыто у кузнеца (на карте или в лагере): только здесь можно улучшать предметы и ставить руны.</summary>
        public static bool AtForge { get { return Profile.CostMul < 1f; } }
        /// <summary>Снаряжение открыто у торговца: только здесь предметы можно продать.</summary>
        public static bool AtMarket;
        public static void Hide() { Open = false; Profile.CostMul = 1f; AtMarket = false; if (DuelManager.Instance != null) DuelManager.Instance.ApplyProfile(); }

        void OnGUI()
        {
            // No post-fight loot banner: rewards already go directly to Profile.
            // Avoid an IMGUI layout pass entirely while the inventory is closed.
            if (!Open) return;
            GUI.depth = -20;   // поверх HUD и кнопок боя
            Matrix4x4 old = UiKit.Begin();
            DrawScreen();
            GUI.matrix = old;
        }

        // ---------------- значок предмета ----------------

        static void SheetFrame(Rect r, Texture2D t, int frame, int cols)
        {
            float u = 1f / cols;
            GUI.DrawTextureWithTexCoords(r, t, new Rect(frame * u, 0f, u, 1f));
        }

        static void DrawItem(Rect r, Item it, bool selected)
        {
            int rar = it != null ? it.rarity : 0;
            Texture2D fr = UiKit.Tex("UI/frame_" + rar);
            Color rc = Profile.RarityColors[rar];
            UiKit.Rect(r, new Color(0.04f, 0.035f, 0.035f, it != null ? 0.85f : 0.55f));   // пустая ячейка — легче, видно фон
            if (it != null)
            {
                // A low-opacity radial tint gives rarity a visual identity without replacing the item art.
                float glow = rar >= 3 ? 0.15f + 0.045f * Mathf.Sin(Time.unscaledTime * 2f + r.x * 0.01f) : 0.075f;
                UiKit.Disc(r.center, r.width * 0.43f, new Color(rc.r, rc.g, rc.b, glow));
            }
            if (it != null)
            {
                bool second = it.icon >= 16, third = it.icon >= 52, fourth = it.icon >= 88;
                Texture2D atlas = UiKit.Tex(fourth ? "UI/items4" : third ? "UI/items3" : second ? "UI/items2" : "UI/items");
                Rect ir = new Rect(r.x + r.width * 0.15f, r.y + r.height * 0.15f, r.width * 0.7f, r.height * 0.7f);   // внутри проёма рамки
                Texture2D setAtlas = it.set >= 0 && it.slot <= 3 ? UiKit.Tex("UI/set_items") : null;
                if (setAtlas != null)
                {
                    // комплекты кланов: атлас 4×3 — строка клана, столбец ячейки (клинок, цуба, облачение, маска)
                    GUI.DrawTextureWithTexCoords(ir, setAtlas, new Rect(it.slot * 0.25f, 1f - (it.set + 1) / 3f, 0.25f, 1f / 3f));
                }
                else if (atlas != null)
                {
                    int n = second ? 6 : 4, k = fourth ? it.icon - 88 : third ? it.icon - 52 : second ? it.icon - 16 : it.icon;
                    float u = 1f / n;
                    GUI.DrawTextureWithTexCoords(ir, atlas, new Rect((k % n) * u, 1f - (k / n + 1) * u, u, u));
                }
                else
                {
                    UiKit.Disc(ir.center, ir.width * 0.42f, new Color(rc.r * 0.35f, rc.g * 0.35f, rc.b * 0.35f, 1f));
                    UiKit.Text(ir, Abbr[Mathf.Clamp(it.slot, 0, 6)], ir.height * 0.36f, rc);
                }
                if (it.rune >= 0)
                {
                    Texture2D rt = UiKit.Tex("UI/runes");
                    if (rt != null) GUI.DrawTextureWithTexCoords(new Rect(r.x + 2f, r.yMax - r.height * 0.36f - 2f, r.width * 0.36f, r.height * 0.36f), rt,
                        new Rect((it.rune % 4) * 0.25f, it.rune < 4 ? 0.5f : 0f, 0.25f, 0.5f));
                }
                if (it.level > 0)
                {
                    UiKit.Rect(new Rect(r.xMax - 31f, r.y + 3f, 28f, 17f), new Color(0.045f, 0.035f, 0.025f, 0.72f));
                    UiKit.Text(new Rect(r.xMax - 30f, r.y + 2f, 28f, 16f), "+" + it.level, 12f, Gold, TextAnchor.UpperRight);
                }
                if (Profile.IsEquipped(it)) UiKit.Disc(new Vector2(r.x + 8f, r.y + 8f), 4f, Gold);
            }
            // рамки редкости (UI/frame_0…3): железо, сталь, золото с фиолетовым, кроваво-золотая с когтями
            Texture2D sh = rar >= 3 && fr == null ? UiKit.Tex("UI/shine_frame") : null;
            if (sh != null)
            {
                // легендарное: золотая рамка, по которой время от времени пробегает блик
                float cyc = (Time.unscaledTime * 0.4f + r.x * 0.003f) % 1f;
                int sf = cyc < 0.82f ? 0 : Mathf.Min(3, 1 + (int)((cyc - 0.82f) / 0.06f));
                SheetFrame(r, sh, sf, 4);
            }
            else if (fr != null) GUI.DrawTexture(r, fr);
            else
            {
                Color bc = it != null ? rc : new Color(0.35f, 0.32f, 0.3f);
                UiKit.Rect(new Rect(r.x, r.y, r.width, 2f), bc); UiKit.Rect(new Rect(r.x, r.yMax - 2f, r.width, 2f), bc);
                UiKit.Rect(new Rect(r.x, r.y, 2f, r.height), bc); UiKit.Rect(new Rect(r.xMax - 2f, r.y, 2f, r.height), bc);
            }
            if (selected)
            {
                Color rim = new Color(1f, 0.87f, 0.57f, 0.95f);
                UiKit.Rect(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, 3f), rim);
                UiKit.Rect(new Rect(r.x - 3f, r.yMax, r.width + 6f, 3f), rim);
                UiKit.Rect(new Rect(r.x - 3f, r.y, 3f, r.height), rim);
                UiKit.Rect(new Rect(r.xMax, r.y, 3f, r.height), rim);
            }
        }

        /// <summary>Кнопка-доска (для карты похода и других экранов).</summary>
        public static bool Button(Rect r, string label, bool enabled = true, bool red = false) { return Btn(r, label, enabled, red); }
        /// <summary>Значок предмета в рамке редкости.</summary>
        static readonly Color Dimmed = new Color(0.62f, 0.58f, 0.52f);
        public static void ItemIcon(Rect r, Item it) { DrawItem(r, it, false); }

        static bool Btn(Rect r, string label, bool enabled = true, bool red = false)
        {
            return VisualIdentity.KitButton(r, label, enabled, red);
        }

        // ---------------- экран снаряжения ----------------

        void DrawScreen()
        {
            float W = UiKit.Width;
            UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, 0.86f));
            Rect panel = new Rect(12f, 10f, W - 24f, 520f);
            VisualIdentity.KitArmory(panel, 1f);   // оружейная самурая (рамка, катаны, доспех справа) — снизу под ячейками

            // шапка: вкладки, монеты, «готово»
            if (Btn(new Rect(48f, 26f, 180f, 36f), "СНАРЯЖЕНИЕ", true, tab == 0)) tab = 0;
            if (Btn(new Rect(238f, 26f, 180f, 36f), "ПРОКАЧКА" + (Profile.Points > 0 ? "  +" + Profile.Points : ""), true, tab == 1)) { tab = 1; pending = Profile.StatsCopy(); }
            if (Btn(new Rect(428f, 26f, 144f, 36f), "ПУТЬ", true, tab == 2)) tab = 2;
            Texture2D coin = UiKit.Tex("UI/coins");
            float gx = W - 330f;
            if (coin != null) GUI.DrawTexture(new Rect(gx, 26f, 36f, 36f), coin); else UiKit.Disc(new Vector2(gx + 18f, 44f), 12f, Gold);
            UiKit.Text(new Rect(gx + 40f, 24f, 140f, 40f), Profile.Gold + " МОН", 20f, Gold, TextAnchor.MiddleLeft);
            if (Btn(new Rect(W - 178f, 26f, 130f, 36f), "ГОТОВО")) { Hide(); return; }

            VisualIdentity.Divider(new Rect(40f, 68f, W - 80f, 1f), 0.43f);
            if (tab == 1) { DrawUpgrade(W); return; }
            if (tab == 2) { DrawPath(W); return; }
            Rect stats = new Rect(40f, 76f, 284f, 432f);
            Rect equipped = new Rect(334f, 76f, 232f, 432f);
            Rect bag = new Rect(578f, 76f, W - 618f, 432f);
            // подложки полупрозрачные: сквозь них видна оружейная (раньше почти чёрные — закрывали фон, особенно доспех справа)
            VisualIdentity.Inset(stats, null, 0.62f);
            VisualIdentity.Inset(equipped, null, 0.5f);
            VisualIdentity.Inset(bag, null, 0.32f);
            DrawStats(new Rect(stats.x + 8f, stats.y + 4f, stats.width - 16f, stats.height - 8f));
            DrawEquipped(new Rect(equipped.x + 8f, equipped.y + 4f, equipped.width - 16f, equipped.height - 8f));
            DrawBag(new Rect(bag.x + 8f, bag.y + 4f, bag.width - 16f, bag.height - 8f));
            if (runeModal) DrawRuneModal(W);
        }

        /// <summary>Выбор руны поверх всего экрана: полный набор значков и подписей, ничего не обрезается.</summary>
        void DrawRuneModal(float W)
        {
            if (sel == null || !Profile.Items.Contains(sel) || sel.slot != (int)Slot.Blade) { runeModal = false; return; }
            float a = Mathf.Clamp01((Time.unscaledTime - runeModalAt) / 0.2f);
            UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0.015f, 0.012f, 0.012f, 0.65f * a));
            const float w = 420f, h = 300f;
            Rect r = new Rect((W - w) * 0.5f, (540f - h) * 0.5f + (1f - a) * 20f, w, h);
            VisualIdentity.KitSurface(r, a);
            UiKit.Text(new Rect(r.x + 16f, r.y + 10f, r.width - 32f, 26f), "ВЫБЕРИ РУНУ", 17f, Gold, TextAnchor.MiddleLeft);
            UiKit.Text(new Rect(r.x + 16f, r.y + 34f, r.width - 32f, 16f), "Кузнец вставит за " + Profile.RuneCost + " мон", 11f, Dimmed, TextAnchor.MiddleLeft);
            Texture2D rt = UiKit.Tex("UI/runes");
            int n = Profile.RuneNames.Length, cols = 4;
            float rs = 64f, gap = 10f;
            float gridW = cols * rs + (cols - 1) * gap, gx = r.x + (r.width - gridW) * 0.5f, gy = r.y + 58f;
            for (int k = 0; k < n; k++)
            {
                Rect rr = new Rect(gx + (k % cols) * (rs + gap), gy + (k / cols) * (rs + gap), rs, rs);
                if (sel.rune == k) UiKit.Rect(new Rect(rr.x - 3f, rr.y - 3f, rr.width + 6f, rr.height + 6f), new Color(1f, 0.85f, 0.4f, 0.9f));
                if (rt != null) GUI.DrawTextureWithTexCoords(rr, rt, new Rect((k % 4) * 0.25f, k < 4 ? 0.5f : 0f, 0.25f, 0.5f));
                if (GUI.Button(rr, GUIContent.none, GUIStyle.none) && Profile.Gold >= Profile.RuneCost) { Profile.SetRune(sel, k); runeModal = false; }
            }
            UiKit.Text(new Rect(r.x + 16f, gy + 2f * (rs + gap) + 4f, r.width - 32f, 36f),
                sel.rune >= 0 ? "«" + Profile.RuneNames[sel.rune] + "»: " + Profile.RuneHints[sel.rune] : "коснись значка, чтобы вставить руну",
                11.5f, new Color(0.7f, 0.85f, 1f), TextAnchor.UpperLeft, false);
            if (Btn(new Rect(r.x + r.width - 110f, r.yMax - 44f, 94f, 32f), "ЗАКРЫТЬ")) runeModal = false;
        }

        void DrawStats(Rect a)
        {
            UiKit.Text(new Rect(a.x, a.y, a.width, 26f), "УРОВЕНЬ " + Profile.Level + (Profile.Cycle > 0 ? "   ·   КРУГ " + (Profile.Cycle + 1) : ""), 20f, Ink, TextAnchor.MiddleLeft);
            Rect xb = new Rect(a.x, a.y + 30f, a.width, 10f);
            VisualIdentity.AccentBar(xb, VisualIdentity.Brass,
                Profile.Xp / (float)Mathf.Max(1, Profile.XpToNext));
            UiKit.Text(new Rect(a.x, a.y + 42f, a.width, 16f), "ОПЫТ " + Profile.Xp + " / " + Profile.XpToNext, 11f, new Color(0.8f, 0.76f, 0.68f), TextAnchor.MiddleLeft);
            int pts = Profile.Points;
            UiKit.Text(new Rect(a.x, a.y + 42f, a.width, 16f), pts > 0 ? "ОЧКОВ: " + pts : "", 13f, Gold, TextAnchor.MiddleRight);
            for (int i = 0; i < Profile.StatCount; i++)
            {
                float y = a.y + 66f + i * 46f;
                Texture2D g = UiKit.Tex(Profile.StatGlyph[i]);
                Rect gr = new Rect(a.x, y + 2f, 34f, 34f);
                if (g != null) { Color o = GUI.color; GUI.color = new Color(0.95f, 0.85f, 0.6f, 1f); GUI.DrawTexture(gr, g); GUI.color = o; }
                else { UiKit.Disc(gr.center, 15f, new Color(0.3f, 0.2f, 0.1f)); UiKit.Text(gr, Profile.StatNames[i].Substring(0, 1), 16f, Gold); }
                UiKit.Text(new Rect(a.x + 42f, y, 170f, 20f), Profile.StatNames[i], 15f, Ink, TextAnchor.MiddleLeft);
                // подсказка не вылезает за колонку: длинные («−4% к откату рывка, +1,5% к скорости») — чуть мельче
                float hw = a.width - 50f, hs = Mathf.Min(10f, hw / Mathf.Max(1f, Profile.StatHint[i].Length * 0.52f));
                UiKit.Text(new Rect(a.x + 42f, y + 20f, hw, 16f), Profile.StatHint[i], hs, new Color(0.75f, 0.72f, 0.65f), TextAnchor.MiddleLeft, false);
                UiKit.Text(new Rect(a.x + 190f, y, 50f, 22f), Profile.Stat(i).ToString(), 20f, Gold, TextAnchor.MiddleRight);
                if (pts > 0 && Btn(new Rect(a.x + 252f, y + 2f, 36f, 32f), "+")) { Profile.AddPoint(i); pending = Profile.StatsCopy(); }
            }
            // сводка
            Profile.Bonus b = Profile.B;
            float sy = a.y + 300f;
            string[] lines =
            {
                "Урон ×" + b.dmgMul.ToString("0.00") + "   ·   Здоровье " + (100 + b.hp),
                "Равновесие врага ×" + b.postureMul.ToString("0.00") + "   ·   Броня " + Mathf.RoundToInt((1f - b.armorMul) * 100f) + "%",
                "Отражение ×" + b.parryMul.ToString("0.00") + "   ·   Дух ×" + b.kiGainMul.ToString("0.00"),
                "Рывок ×" + b.dashCdMul.ToString("0.00") + "   ·   Глотков " + (2 + b.gourds) + (b.crit > 0f ? "   ·   Точный " + Mathf.RoundToInt(b.crit * 100f) + "%" : ""),
            };
            for (int i = 0; i < lines.Length; i++) UiKit.Text(new Rect(a.x, sy + i * 16f, a.width + 20f, 16f), lines[i], 11f, new Color(0.85f, 0.82f, 0.74f), TextAnchor.MiddleLeft, false);
            // приёмы
            float ty = sy + 72f;
            UiKit.Text(new Rect(a.x, ty, a.width, 18f), "ПРИЁМЫ" + (Profile.TechCount == 0 ? ": свитки выпадают в бою" : ""), 13f, Gold, TextAnchor.MiddleLeft);
            int k = 0;
            for (int t = 0; t < 6; t++)
            {
                if (!Profile.Has((Tech)t)) continue;
                Texture2D card = UiKit.Tex("UI/tech_" + t);
                float cx0 = a.x + (k % 2) * 150f, cy0 = ty + 20f + (k / 2) * 16f;
                if (card != null) GUI.DrawTexture(new Rect(cx0, cy0, 15f, 15f), card);
                UiKit.Text(new Rect(cx0 + (card != null ? 18f : 0f), cy0, 132f, 16f), Profile.TechNames[t], 10.5f, Ink, TextAnchor.MiddleLeft, false);
                k++;
            }
        }

        // ---------------- прокачка: распределить и перераспределить очки ----------------

        void DrawUpgrade(float W)
        {
            if (pending == null || pending.Length != Profile.StatCount) pending = Profile.StatsCopy();
            int total = Profile.TotalPoints, used = 0;
            foreach (int v in pending) used += v;
            int free = total - used;
            int[] cur = Profile.StatsCopy();
            bool changed = false; for (int i = 0; i < cur.Length; i++) if (cur[i] != pending[i]) changed = true;

            float lx = 44f, lw = Mathf.Min(560f, W * 0.56f);   // внутри золотой рамки оружейной
            UiKit.Text(new Rect(lx, 72f, lw, 28f), "УРОВЕНЬ " + Profile.Level + "   ·   ОПЫТ " + Profile.Xp + " / " + Profile.XpToNext, 17f, Ink, TextAnchor.MiddleLeft);
            Rect xb = new Rect(lx, 100f, lw, 8f);
            VisualIdentity.AccentBar(xb, VisualIdentity.Brass,
                Profile.Xp / (float)Mathf.Max(1, Profile.XpToNext));
            float pulse = free > 0 ? 1f + 0.05f * Mathf.Sin(Time.unscaledTime * 5f) : 1f;
            UiKit.Text(new Rect(lx, 110f, lw, 30f), "СВОБОДНЫХ ОЧКОВ: " + free + "   из " + total, 19f * pulse, free > 0 ? new Color(1f, 0.82f, 0.35f) : new Color(0.8f, 0.76f, 0.68f), TextAnchor.MiddleLeft);
            for (int i = 0; i < Profile.StatCount; i++)
            {
                float y = 146f + i * 64f;
                Rect row = new Rect(lx, y, lw, 60f);
                VisualIdentity.RowPanel(row);   // утопленная строка с золотой кромкой и уголками
                Texture2D g = UiKit.Tex(Profile.StatGlyph[i]);
                Rect gr = new Rect(lx + 6f, y + 4f, 52f, 52f);
                if (g != null) { Color o = GUI.color; GUI.color = new Color(0.95f, 0.82f, 0.55f, 1f); GUI.DrawTexture(gr, g); GUI.color = o; }
                UiKit.Text(new Rect(lx + 66f, y + 4f, 260f, 24f), Profile.StatNames[i], 19f, Ink, TextAnchor.MiddleLeft);
                UiKit.Text(new Rect(lx + 66f, y + 30f, lw - 250f, 22f), Profile.StatHint[i], 11.5f, new Color(0.78f, 0.74f, 0.66f), TextAnchor.MiddleLeft, false);
                bool up = pending[i] > cur[i], down = pending[i] < cur[i];
                string val = pending[i] == cur[i] ? cur[i].ToString() : cur[i] + " → " + pending[i];
                UiKit.Text(new Rect(lx + lw - 200f, y + 10f, 90f, 40f), val, 22f, up ? new Color(0.55f, 0.95f, 0.45f) : down ? new Color(1f, 0.55f, 0.45f) : Gold, TextAnchor.MiddleRight);
                if (Btn(new Rect(lx + lw - 102f, y + 9f, 44f, 42f), "−", pending[i] > 0)) pending[i]--;
                if (Btn(new Rect(lx + lw - 52f, y + 9f, 44f, 42f), "+", free > 0)) pending[i]++;
            }
            // справа — что изменится
            float rx = lx + lw + 22f, rw = W - rx - 46f;
            Rect rp = new Rect(rx, 76f, rw, 382f);
            VisualIdentity.Inset(rp, null, 0.55f);
            VisualIdentity.Heading(new Rect(rx + 12f, 82f, rw - 24f, 30f),
                changed ? "ЧТО ИЗМЕНИТСЯ" : "ИТОГ");
            Profile.Bonus a = Profile.Compute(cur), b = Profile.Compute(pending);
            string[] names = { "Урон", "Здоровье", "Урон по равновесию", "Накопление духа", "Окно отражения", "Откат рывка", "Скорость" };
            float[] av = { a.dmgMul, 100 + a.hp, a.postureMul, a.kiGainMul, a.parryMul, a.dashCdMul, a.moveMul };
            float[] bv = { b.dmgMul, 100 + b.hp, b.postureMul, b.kiGainMul, b.parryMul, b.dashCdMul, b.moveMul };
            bool[] lowerBetter = { false, false, false, false, false, true, false };
            for (int k = 0; k < names.Length; k++)
            {
                float y = 120f + k * 46f;   // под заголовком «ИТОГ», не на нём
                UiKit.Text(new Rect(rx + 12f, y, rw - 24f, 18f), names[k], 12.5f, new Color(0.82f, 0.78f, 0.7f), TextAnchor.MiddleLeft, false);
                bool better = lowerBetter[k] ? bv[k] < av[k] - 1e-4f : bv[k] > av[k] + 1e-4f, worse = lowerBetter[k] ? bv[k] > av[k] + 1e-4f : bv[k] < av[k] - 1e-4f;
                string txt = Mathf.Abs(bv[k] - av[k]) < 1e-4f ? Fmt(av[k], k) : Fmt(av[k], k) + "  →  " + Fmt(bv[k], k);
                UiKit.Text(new Rect(rx + 12f, y + 17f, rw - 24f, 20f), txt, 15f, better ? new Color(0.55f, 0.95f, 0.45f) : worse ? new Color(1f, 0.55f, 0.45f) : Ink, TextAnchor.MiddleLeft);
            }
            // кнопки: перераспределение бесплатно, в любой момент
            float by = 464f, bw = (W - 140f - 20f) / 3f;   // в стороне от угловых орнаментов рамки
            if (Btn(new Rect(70f, by, bw, 40f), "СБРОСИТЬ ВСЁ", used > 0, true)) for (int i = 0; i < pending.Length; i++) pending[i] = 0;
            if (Btn(new Rect(80f + bw, by, bw, 40f), "ОТМЕНА", changed)) pending = Profile.StatsCopy();
            if (Btn(new Rect(90f + bw * 2f, by, bw, 40f), "ПРИНЯТЬ", changed)) { Profile.SetStats(pending); pending = Profile.StatsCopy(); if (DuelManager.Instance != null) DuelManager.Instance.ApplyProfile(); }
        }

        // ---------------- «Путь»: горная тропа, печати ступеней, враг в полный рост ----------------

        static readonly string[] EnemyFull = { "UI/enemy_full_fallen", "UI/enemy_full_shadow", "UI/enemy_full_oni", "UI/enemy_full_shogun" };
        static readonly Vector2[] PathPts = { new Vector2(0.47f, 0.9f), new Vector2(0.36f, 0.66f), new Vector2(0.6f, 0.5f), new Vector2(0.55f, 0.24f) };

        void DrawPath(float W)
        {
            float lw = Mathf.Min(560f, W * 0.55f);
            Rect pr = new Rect(44f, 76f, lw, 432f);
            Texture2D pan = UiKit.Tex("UI/path_panorama");
            if (pan != null) GUI.DrawTexture(pr, pan, ScaleMode.ScaleAndCrop); else UiKit.Rect(pr, new Color(0.1f, 0.1f, 0.12f));
            VisualIdentity.RowPanel(new Rect(pr.x, pr.y, pr.width, 36f), 0.97f);
            UiKit.Text(new Rect(pr.x + 12f, pr.y + 2f, pr.width - 24f, 30f), "ПУТЬ КЛИНКА" + (Profile.Cycle > 0 ? "   ·   КРУГ " + (Profile.Cycle + 1) : "") + "   ·   ПРОЙДЕН РАЗ: " + Records.Cleared, 15f, Gold, TextAnchor.MiddleLeft);
            int cur = Ladder.Index;
            // фонари вдоль тропы
            Texture2D lan = UiKit.Tex("UI/path_lantern");
            for (int i = 0; i < PathPts.Length; i++)
            {
                Vector2 c = new Vector2(pr.x + PathPts[i].x * pr.width, pr.y + PathPts[i].y * pr.height);
                if (lan != null && i < PathPts.Length - 1) { Vector2 m = (c + new Vector2(pr.x + PathPts[i + 1].x * pr.width, pr.y + PathPts[i + 1].y * pr.height)) * 0.5f; GUI.DrawTexture(new Rect(m.x - 14f, m.y - 40f, 28f, 44f), lan); }
                Texture2D md = UiKit.Tex("UI/medal_" + i);
                bool done = i < cur, now = i == cur;
                float s = now ? 70f * (1f + 0.06f * Mathf.Sin(Time.unscaledTime * 4f)) : 56f;
                Color o = GUI.color;
                GUI.color = done ? Color.white : now ? Color.white : new Color(0.45f, 0.42f, 0.42f, 0.9f);
                if (md != null) GUI.DrawTexture(new Rect(c.x - s * 0.5f, c.y - s * 0.5f, s, s), md); else UiKit.Disc(c, s * 0.45f, new Color(0.6f, 0.1f, 0.08f));
                GUI.color = o;
                if (done) UiKit.Disc(new Vector2(c.x + s * 0.32f, c.y - s * 0.32f), 6f, new Color(0.55f, 0.9f, 0.45f));   // пройдено
                // имя ступени на тёмной плашке: серым по горам не читалось
                Rect nr = new Rect(c.x + s * 0.5f + 4f, c.y - 10f, 200f, 20f);
                float nw = Ladder.Stages[i].name.Length * (now ? 13f : 11f) * 0.68f + 14f;
                VisualIdentity.RowPanel(new Rect(nr.x - 8f, nr.y - 3f, nw + 4f, 26f), 0.95f);
                UiKit.Text(nr, Ladder.Stages[i].name, now ? 13f : 11f, now ? Gold : new Color(0.93f, 0.89f, 0.8f, done ? 1f : 0.85f), TextAnchor.MiddleLeft);
            }
            // справа — текущий противник
            float rx = pr.xMax + 20f, rw = W - rx - 46f;
            Rect rp = new Rect(rx, 76f, rw, 432f);
            VisualIdentity.KitSurface(rp, 0.95f);   // враг на панели в стиле игры (картинки врагов теперь с прозрачным фоном)
            Texture2D ef = UiKit.Tex(EnemyFull[Mathf.Clamp(cur, 0, EnemyFull.Length - 1)]);
            if (ef != null)
            {
                float h = 284f, w = h * ef.width / Mathf.Max(1f, ef.height);
                if (w > rw - 20f) { w = rw - 20f; h = w * ef.height / Mathf.Max(1f, ef.width); }
                GUI.DrawTexture(new Rect(rx + (rw - w) * 0.5f, 88f, w, h), ef);   // внутри рамки панели
            }
            Ladder.Stage st = Ladder.Current;
            UiKit.Text(new Rect(rx + 10f, 378f, rw - 20f, 26f), "СЛЕДУЮЩИЙ: " + st.name, 17f, Gold);
            UiKit.Text(new Rect(rx + 10f, 404f, rw - 20f, 18f), st.title, 12f, new Color(0.85f, 0.82f, 0.75f));
            string[] traits = { "обычный бой — учись отражать", "уходит в дым за спину, финтит", "бьёт по земле — перепрыгивай волну", "три фазы: залпы духа, затем дым" };
            UiKit.Text(new Rect(rx + 10f, 424f, rw - 20f, 18f), traits[Mathf.Clamp(cur, 0, 3)], 11.5f, new Color(1f, 0.65f, 0.45f));
            UiKit.Text(new Rect(rx + 10f, 446f, rw - 20f, 18f), "жизней: " + Mathf.Max(1, st.marks) + "   ·   здоровье ×" + (st.hpMul * Profile.CycleHp).ToString("0.0") + "   ·   урон ×" + (st.dmgMul * Profile.CycleDmg).ToString("0.0"), 11f, new Color(0.8f, 0.78f, 0.72f));
            UiKit.Text(new Rect(rx + 10f, 470f, rw - 20f, 36f), Ladder.IsLast ? "награда: добыча не ниже редкой, после победы — новый круг" : "победа открывает следующую ступень", 11f, new Color(0.75f, 0.72f, 0.66f));
        }

        static string Fmt(float v, int kk) { return kk == 1 ? Mathf.RoundToInt(v).ToString() : (v >= 1f ? "+" : "−") + Mathf.RoundToInt(Mathf.Abs(v - 1f) * 100f) + "%"; }

        // ---- отклик надевания (UiFx): вещь «впечатывается» в ячейку — отскок, вспышка цвета редкости, кольцо, всплывает её свойство
        int equipFxSlot = -1; float equipFxAt = -10f; Item equipFxItem;
        float learnFxAt = -10f, upFxAt = -10f;

        void DrawEquipFx(Rect r, Item it, float age)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color rc = it != null ? Profile.RarityColors[Mathf.Clamp(it.rarity, 0, 3)] : Color.white;
            Vector2 c = r.center;
            if (age < UiFx.Ring.Length)
                UiFx.Draw(new Rect(c.x - r.width * 0.85f, c.y + r.height * 0.18f, r.width * 1.7f, r.width * 1.7f / 3f), UiFx.Ring, age, new Color(rc.r, rc.g, rc.b, 0.9f));
            UiFx.Sheet fl = it != null && it.rarity >= 3 ? UiFx.FlashGold : UiFx.FlashWhite;
            if (age < fl.Length)
                UiFx.Draw(UiFx.Around(c, r.width * (1.2f + 0.25f * (it != null ? it.rarity : 0))), fl, age, new Color(Mathf.Lerp(1f, rc.r, 0.5f), Mathf.Lerp(1f, rc.g, 0.5f), Mathf.Lerp(1f, rc.b, 0.5f), 0.7f), 0.65f);
            // кромка ячейки вспыхивает
            float k = Mathf.Clamp01(1f - age / 0.5f);
            if (k > 0f)
            {
                Color oc = GUI.color; GUI.color = new Color(rc.r, rc.g, rc.b, 0.85f * k);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2f), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(r.x, r.yMax - 2f, r.width, 2f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.x, r.y, 2f, r.height), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(r.xMax - 2f, r.y, 2f, r.height), Texture2D.whiteTexture);
                GUI.color = oc;
            }
            // всплывает главное свойство вещи
            if (it != null && it.mods.Count > 0 && age < 1.6f)
            {
                float u = age / 1.6f, a = Mathf.Clamp01(u / 0.12f) * Mathf.Clamp01((1f - u) / 0.35f);
                Mod m = it.mods[0];
                UiKit.Text(new Rect(r.x - 40f, r.y - 8f - u * 34f, r.width + 80f, 20f), Profile.ModText((ModId)m.stat, it.Value(m)), 12.5f, new Color(0.6f, 1f, 0.5f, a));
            }
        }

        void DrawEquipped(Rect a)
        {
            VisualIdentity.Heading(new Rect(a.x, a.y, a.width, 32f), "НАДЕТО");
            for (int s = 0; s < Profile.SlotCount; s++)
            {
                float x = a.x + (s % 2) * 110f + 6f, y = a.y + 30f + (s / 2) * 136f;
                Rect r = new Rect(x, y, 96f, 96f);
                Item it = Profile.Equipped(s);
                float fxAge = s == equipFxSlot && it == equipFxItem ? Time.unscaledTime - equipFxAt : 99f;
                float pop = UiFx.Pop(fxAge, 0.14f);
                Rect pr = pop != 1f ? new Rect(r.center.x - r.width * pop * 0.5f, r.center.y - r.height * pop * 0.5f, r.width * pop, r.height * pop) : r;
                DrawItem(pr, it, it != null && it == sel);
                if (fxAge < 2f) DrawEquipFx(r, it, fxAge);
                // пустая ячейка — золотой силуэт того, что в неё надевается (клинок, цуба, облачение, маска, амулет, тыква)
                Texture2D sil = it == null && s < 6 ? UiKit.Tex("UI/slot_silhouettes") : null;
                if (sil != null)
                {
                    Color o = GUI.color; GUI.color = new Color(1f, 1f, 1f, 0.34f);
                    GUI.DrawTextureWithTexCoords(new Rect(r.x + r.width * 0.18f, r.y + r.height * 0.18f, r.width * 0.64f, r.height * 0.64f), sil, new Rect(s / 6f, 0f, 1f / 6f, 1f));
                    GUI.color = o;
                }
                UiKit.Text(new Rect(x - 12f, y + 98f, 120f, 18f), Profile.SlotNames[s], 11f, new Color(0.8f, 0.76f, 0.68f));
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) sel = it;
            }
        }

        void DrawBag(Rect a)
        {
            List<Item> all = Profile.Items;
            List<Item> bag = visibleBag;
            bag.Clear();
            foreach (Item it in all) if (!Profile.IsEquipped(it)) bag.Add(it);
            const float cell = 58f, gap = 6f;
            int cols = Mathf.Max(3, Mathf.FloorToInt((a.width + gap) / (cell + gap)));
            int per = cols * 4, pages = Mathf.Max(1, Mathf.CeilToInt(bag.Count / (float)per));
            page = Mathf.Clamp(page, 0, pages - 1);
            VisualIdentity.Heading(new Rect(a.x, a.y, a.width, 32f),
                "СУМКА  " + all.Count + " / " + Profile.BagSize);
            if (pages > 1)
            {
                if (Btn(new Rect(a.xMax - 92f, a.y, 40f, 26f), "<", page > 0)) page--;
                if (Btn(new Rect(a.xMax - 44f, a.y, 40f, 26f), ">", page < pages - 1)) page++;
            }
            for (int i = 0; i < per; i++)
            {
                int idx = page * per + i;
                Rect r = new Rect(a.x + (i % cols) * (cell + gap), a.y + 30f + (i / cols) * (cell + gap), cell, cell);
                Item it = idx < bag.Count ? bag[idx] : null;
                DrawItem(r, it, it != null && it == sel);
                if (it != null && GUI.Button(r, GUIContent.none, GUIStyle.none)) sel = it;
            }
            if (bag.Count == 0) UiKit.Text(new Rect(a.x, a.y + 110f, a.width, 24f), "пусто — добыча выпадает за победы", 12f, new Color(0.7f, 0.66f, 0.6f));
            DrawCard(new Rect(a.x, a.y + 30f + 4f * (cell + gap) + 6f, a.width, a.height - (30f + 4f * (cell + gap) + 6f)));   // по высоте сумки, не за рамку
        }

        void DrawCard(Rect a)
        {
            if (sel != null && !Profile.Items.Contains(sel)) sel = null;
            VisualIdentity.Inset(a, null, 0.6f);
            // кузнец улучшил вещь — короткая золотая вспышка у названия (кольцо и вспышка у свитка убраны — смотрелись огненным кольцом не к месту)
            float fxA = Time.unscaledTime - upFxAt;
            if (fxA < UiFx.FlashGold.Length)
                UiFx.Draw(UiFx.Around(new Vector2(a.x + 40f, a.y + 20f), 90f), UiFx.FlashGold, fxA, new Color(1f, 1f, 1f, 0.55f), 0.6f);
            if (sel == null)
            {
                UiKit.Text(a, AtForge ? "КУЗНИЦА: ВЫБЕРИ ПРЕДМЕТ ДЛЯ УЛУЧШЕНИЯ" : AtMarket ? "ТОРГОВЕЦ: ВЫБЕРИ ПРЕДМЕТ НА ПРОДАЖУ" : "ВЫБЕРИ ПРЕДМЕТ", 13f, AtForge || AtMarket ? new Color(1f, 0.62f, 0.32f) : VisualIdentity.Subdued);
                return;
            }
            Item it = sel;
            Color rc = Profile.RarityColors[it.rarity];
            UiKit.Rect(new Rect(a.x + 5f, a.y + 4f, 3f, 32f),
                new Color(rc.r, rc.g, rc.b, 0.92f));
            UiKit.Text(new Rect(a.x + 14f, a.y + 4f, a.width - 24f, 22f),
                it.name + (it.level > 0 ? "  +" + it.level : ""), 16f, rc, TextAnchor.MiddleLeft);
            UiKit.Text(new Rect(a.x + 14f, a.y + 24f, a.width - 24f, 16f),
                Profile.SlotNames[it.slot] + "  ·  " + Profile.RarityNames[it.rarity],
                10.5f, VisualIdentity.Subdued, TextAnchor.MiddleLeft, false);
            float y = a.y + 42f;
            if (it.slot == (int)Slot.Scroll)
            {
                int t = Mathf.Clamp(it.tech, 0, 5);
                Texture2D card = UiKit.Tex("UI/tech_" + t);
                float tx = a.x + 10f;
                if (card != null) { GUI.DrawTexture(new Rect(a.x + 10f, y, 64f, 64f), card); tx = a.x + 82f; }
                UiKit.Text(new Rect(tx, y, a.xMax - tx - 10f, 16f), "Приём: " + Profile.TechNames[t], 12f, Ink, TextAnchor.MiddleLeft, false);
                UiKit.Text(new Rect(tx, y + 18f, a.xMax - tx - 10f, 32f), Profile.TechHint[t], 11f, new Color(0.85f, 0.82f, 0.74f), TextAnchor.UpperLeft, false);
            }
            else
            {
                Item cur = Profile.Equipped(it.slot);
                foreach (Mod m in it.mods)
                {
                    float v = it.Value(m);
                    // сравнение с надетым: лучше — зелёным, хуже — красноватым
                    Color mc = Ink;
                    if (cur != null && cur != it)
                    {
                        float cv = 0f; foreach (Mod cm in cur.mods) if (cm.stat == m.stat) cv += cur.Value(cm);
                        mc = v > cv + 1e-4f ? new Color(0.55f, 0.9f, 0.45f) : v < cv - 1e-4f ? new Color(0.95f, 0.55f, 0.45f) : Ink;
                    }
                    UiKit.Text(new Rect(a.x + 10f, y, a.width - 20f, 15f), Profile.ModText((ModId)m.stat, v), 11.5f, mc, TextAnchor.MiddleLeft, false);
                    y += 15f;
                }
                // комплект клана: сколько надето и что даёт
                if (it.set >= 0)
                {
                    int have = Profile.SetPieces(it.set);
                    Color sc = new Color(0.95f, 0.78f, 0.35f);
                    UiKit.Text(new Rect(a.x + 10f, y + 2f, a.width - 20f, 15f), "Комплект " + Profile.SetNames[it.set] + ": " + have + "/4", 11.5f, sc, TextAnchor.MiddleLeft, false); y += 16f;
                    UiKit.Text(new Rect(a.x + 16f, y, a.width - 26f, 15f), "2: " + Profile.SetBonus2[it.set], 11f, have >= 2 ? sc : Dimmed, TextAnchor.MiddleLeft, false); y += 15f;
                    UiKit.Text(new Rect(a.x + 16f, y, a.width - 26f, 15f), "4: " + Profile.SetBonus4[it.set], 11f, have >= 4 ? sc : Dimmed, TextAnchor.MiddleLeft, false); y += 15f;
                }
                // руна клинка: у кузнеца (на карте или в лагере) — вставить или заменить
                if (it.slot == (int)Slot.Blade)
                {
                    if (it.rune >= 0)
                    {
                        UiKit.Text(new Rect(a.x + 10f, y + 2f, a.width - 20f, 15f), "Руна «" + Profile.RuneNames[it.rune] + "»: " + Profile.RuneHints[it.rune], 11.5f, new Color(0.7f, 0.85f, 1f), TextAnchor.MiddleLeft, false);
                        y += 17f;
                    }
                    if (Profile.CostMul < 1f)
                    {
                        // сетка значков рун раньше рисовалась прямо тут и у предметов с несколькими эффектами
                        // вылезала за карточку (обрезалась кнопками снизу) — теперь это отдельное окно
                        string label = (it.rune >= 0 ? "ЗАМЕНИТЬ РУНУ: " : "ВСТАВИТЬ РУНУ: ") + Profile.RuneCost + " мон";
                        if (Btn(new Rect(a.x + 10f, y + 2f, a.width - 20f, 26f), label, Profile.Gold >= Profile.RuneCost))
                        { runeModal = true; runeModalAt = Time.unscaledTime; }
                        y += 30f;
                    }
                }
            }
            // кнопки
            float bw = (a.width - 30f) / 3f, by = a.yMax - 40f;
            if (it.slot == (int)Slot.Scroll) { if (Btn(new Rect(a.x + 10f, by, AtForge ? bw : AtMarket ? (a.width - 25f) * 0.5f : a.width - 20f, 34f), "ПРОЧЕСТЬ")) { Profile.Learn(it); UiFx.Play("ui_scroll", 0.9f); learnFxAt = Time.unscaledTime; sel = null; return; } }
            else if (Btn(new Rect(a.x + 10f, by, AtForge ? bw : AtMarket ? (a.width - 25f) * 0.5f : a.width - 20f, 34f), Profile.IsEquipped(it) ? "СНЯТЬ" : "НАДЕТЬ"))
            {
                bool wasOn = Profile.IsEquipped(it);
                Profile.Equip(it);
                if (!wasOn && Profile.IsEquipped(it)) { UiFx.EquipSound(it.slot); equipFxSlot = it.slot; equipFxAt = Time.unscaledTime; equipFxItem = it; }
                else UiFx.Play(it.slot == (int)Slot.Garb ? "ui_cloth" : "ui_leather", 0.5f);   // снято — тише
            }
            // улучшение — только у кузнеца; в обычном снаряжении две кнопки во всю ширину
            if (AtForge)
            {
                bool canUp = it.slot != (int)Slot.Scroll && it.level < Profile.MaxItemLevel;
                int cost = Profile.UpgradeCost(it);
                if (Btn(new Rect(a.x + 15f + bw, by, AtMarket ? bw : bw * 2f + 5f, 34f), canUp ? "УЛУЧШИТЬ " + cost : "МАКС.", canUp && Profile.Gold >= cost, true)) { int lv = it.level; Profile.Upgrade(it); if (it.level > lv) { UiFx.Play("clang", 0.8f, 0.05f); upFxAt = Time.unscaledTime; } }   // молот по клинку
                if (AtMarket && Btn(new Rect(a.x + 20f + bw * 2f, by, bw, 34f), "ПРОДАТЬ " + Profile.SellPrice(it), true)) { Profile.Sell(it); sel = null; }
            }
            else if (AtMarket)
            {
                // продать — только у торговца
                float hw = (a.width - 25f) * 0.5f;
                if (Btn(new Rect(a.x + 15f + hw, by, hw, 34f), "ПРОДАТЬ " + Profile.SellPrice(it), true, true)) { Profile.Sell(it); sel = null; }
            }
        }

    }
}
