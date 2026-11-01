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
        public static bool Open { get; private set; }
        static InventoryScreen inst;
        Item sel; int page; float openedAt;
        int tab;                // 0 — снаряжение, 1 — прокачка
        int[] pending;          // распределение очков, пока не принято
        static readonly Color Gold = new Color(0.86f, 0.70f, 0.38f, 1f);
        static readonly Color Ink = new Color(0.95f, 0.9f, 0.8f, 1f);
        static readonly string[] Abbr = { "КЛ", "ЦБ", "ОД", "МС", "АМ", "ТЫ", "СВ" };

        public static void Ensure() { if (inst == null) inst = new GameObject("InventoryScreen").AddComponent<InventoryScreen>(); }
        public static void Show(int tab = 0) { Ensure(); Open = true; inst.openedAt = Time.unscaledTime; inst.sel = null; inst.page = 0; inst.tab = tab; inst.pending = Profile.StatsCopy(); }
        public static void Hide() { Open = false; if (DuelManager.Instance != null) DuelManager.Instance.ApplyProfile(); }

        void OnGUI()
        {
            GUI.depth = -20;   // поверх HUD и кнопок боя
            Matrix4x4 old = UiKit.Begin();
            if (Open) DrawScreen(); else DrawLootStrip();
            GUI.matrix = old;
        }

        // ---------------- значок предмета ----------------

        static void DrawItem(Rect r, Item it, bool selected)
        {
            int rar = it != null ? it.rarity : 0;
            Texture2D fr = UiKit.Tex("UI/frame_" + rar);
            Color rc = Profile.RarityColors[rar];
            UiKit.Rect(r, new Color(0.04f, 0.035f, 0.035f, 0.92f));
            if (it != null)
            {
                bool second = it.icon >= 16;
                Texture2D atlas = UiKit.Tex(second ? "UI/items2" : "UI/items");
                Rect ir = new Rect(r.x + r.width * 0.15f, r.y + r.height * 0.15f, r.width * 0.7f, r.height * 0.7f);   // внутри проёма рамки
                if (atlas != null)
                {
                    int n = second ? 6 : 4, k = second ? it.icon - 16 : it.icon;
                    float u = 1f / n;
                    GUI.DrawTextureWithTexCoords(ir, atlas, new Rect((k % n) * u, 1f - (k / n + 1) * u, u, u));
                }
                else
                {
                    UiKit.Disc(ir.center, ir.width * 0.42f, new Color(rc.r * 0.35f, rc.g * 0.35f, rc.b * 0.35f, 1f));
                    UiKit.Text(ir, Abbr[Mathf.Clamp(it.slot, 0, 6)], ir.height * 0.36f, rc);
                }
                if (it.level > 0) UiKit.Text(new Rect(r.xMax - 30f, r.y + 2f, 28f, 16f), "+" + it.level, 12f, Gold, TextAnchor.UpperRight);
                if (Profile.IsEquipped(it)) UiKit.Disc(new Vector2(r.x + 8f, r.y + 8f), 4f, Gold);
            }
            if (fr != null) GUI.DrawTexture(r, fr);
            else
            {
                Color bc = it != null ? rc : new Color(0.35f, 0.32f, 0.3f);
                UiKit.Rect(new Rect(r.x, r.y, r.width, 2f), bc); UiKit.Rect(new Rect(r.x, r.yMax - 2f, r.width, 2f), bc);
                UiKit.Rect(new Rect(r.x, r.y, 2f, r.height), bc); UiKit.Rect(new Rect(r.xMax - 2f, r.y, 2f, r.height), bc);
            }
            if (selected) { UiKit.Rect(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, 3f), Ink); UiKit.Rect(new Rect(r.x - 3f, r.yMax, r.width + 6f, 3f), Ink); UiKit.Rect(new Rect(r.x - 3f, r.y, 3f, r.height), Ink); UiKit.Rect(new Rect(r.xMax, r.y, 3f, r.height), Ink); }
        }

        static bool Btn(Rect r, string label, bool enabled = true, bool red = false)
        {
            Texture2D plank = UiKit.Tex(red ? "UI/plank_red" : "UI/plank");
            Color o = GUI.color;
            if (plank != null) { GUI.color = enabled ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.8f); GUI.DrawTexture(r, plank, ScaleMode.StretchToFill); GUI.color = o; }
            else
            {
                UiKit.Rect(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), Gold * (enabled ? 0.8f : 0.35f));
                UiKit.Rect(r, red ? new Color(0.35f, 0.08f, 0.06f, 1f) : new Color(0.09f, 0.07f, 0.07f, 0.97f));
            }
            UiKit.Text(r, label, Mathf.Min(17f, r.height * 0.46f), enabled ? Ink : new Color(0.6f, 0.58f, 0.55f));
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // ---------------- экран снаряжения ----------------

        void DrawScreen()
        {
            float W = UiKit.Width;
            UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, 0.82f));
            Rect panel = new Rect(12f, 10f, W - 24f, 520f);
            UiKit.Rect(new Rect(panel.x - 2f, panel.y - 2f, panel.width + 4f, panel.height + 4f), Gold * 0.6f);
            UiKit.Rect(panel, new Color(0.055f, 0.045f, 0.045f, 0.98f));
            UiKit.Paper(panel, new Color(0.2f, 0.16f, 0.12f, 0.35f));
            UiKit.Corners(panel, 54f);

            // шапка: вкладки, монеты, «готово»
            if (Btn(new Rect(30f, 18f, 190f, 36f), "СНАРЯЖЕНИЕ", true, tab == 0)) tab = 0;
            if (Btn(new Rect(230f, 18f, 190f, 36f), "ПРОКАЧКА" + (Profile.Points > 0 ? "  +" + Profile.Points : ""), true, tab == 1)) { tab = 1; pending = Profile.StatsCopy(); }
            Texture2D coin = UiKit.Tex("UI/coins");
            float gx = W - 330f;
            if (coin != null) GUI.DrawTexture(new Rect(gx, 18f, 36f, 36f), coin); else UiKit.Disc(new Vector2(gx + 18f, 36f), 12f, Gold);
            UiKit.Text(new Rect(gx + 40f, 16f, 140f, 40f), Profile.Gold + " МОН", 20f, Gold, TextAnchor.MiddleLeft);
            if (Btn(new Rect(W - 160f, 18f, 130f, 36f), "ГОТОВО")) { Hide(); return; }

            if (tab == 1) { DrawUpgrade(W); return; }
            DrawStats(new Rect(28f, 64f, 292f, 456f));
            DrawEquipped(new Rect(332f, 64f, 232f, 456f));
            DrawBag(new Rect(578f, 64f, W - 578f - 30f, 456f));
        }

        void DrawStats(Rect a)
        {
            UiKit.Text(new Rect(a.x, a.y, a.width, 26f), "УРОВЕНЬ " + Profile.Level + (Profile.Cycle > 0 ? "   ·   КРУГ " + (Profile.Cycle + 1) : ""), 20f, Ink, TextAnchor.MiddleLeft);
            Rect xb = new Rect(a.x, a.y + 30f, a.width, 10f);
            UiKit.Rect(xb, new Color(0f, 0f, 0f, 0.6f));
            UiKit.Rect(new Rect(xb.x, xb.y, xb.width * Mathf.Clamp01(Profile.Xp / (float)Profile.XpToNext), xb.height), new Color(0.75f, 0.6f, 0.3f));
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
                UiKit.Text(new Rect(a.x + 42f, y + 20f, 220f, 16f), Profile.StatHint[i], 10f, new Color(0.75f, 0.72f, 0.65f), TextAnchor.MiddleLeft, false);
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
                UiKit.Text(new Rect(a.x + (k % 2) * 150f, ty + 20f + (k / 2) * 16f, 150f, 16f), Profile.TechNames[t], 10.5f, Ink, TextAnchor.MiddleLeft, false);
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

            float lx = 30f, lw = Mathf.Min(560f, W * 0.56f);
            UiKit.Text(new Rect(lx, 62f, lw, 28f), "УРОВЕНЬ " + Profile.Level + "   ·   ОПЫТ " + Profile.Xp + " / " + Profile.XpToNext, 17f, Ink, TextAnchor.MiddleLeft);
            Rect xb = new Rect(lx, 92f, lw, 8f);
            UiKit.Rect(xb, new Color(0f, 0f, 0f, 0.6f));
            UiKit.Rect(new Rect(xb.x, xb.y, xb.width * Mathf.Clamp01(Profile.Xp / (float)Profile.XpToNext), xb.height), new Color(0.75f, 0.6f, 0.3f));
            float pulse = free > 0 ? 1f + 0.05f * Mathf.Sin(Time.unscaledTime * 5f) : 1f;
            UiKit.Text(new Rect(lx, 104f, lw, 30f), "СВОБОДНЫХ ОЧКОВ: " + free + "   из " + total, 19f * pulse, free > 0 ? new Color(1f, 0.82f, 0.35f) : new Color(0.8f, 0.76f, 0.68f), TextAnchor.MiddleLeft);
            for (int i = 0; i < Profile.StatCount; i++)
            {
                float y = 142f + i * 66f;
                Rect row = new Rect(lx, y, lw, 60f);
                UiKit.Rect(row, new Color(0f, 0f, 0f, 0.3f));
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
            float rx = lx + lw + 22f, rw = W - rx - 32f;
            Rect rp = new Rect(rx, 62f, rw, 396f);
            UiKit.Rect(rp, new Color(0f, 0f, 0f, 0.35f));
            UiKit.Text(new Rect(rx + 12f, 68f, rw - 24f, 26f), changed ? "ЧТО ИЗМЕНИТСЯ" : "ИТОГ", 16f, Gold, TextAnchor.MiddleLeft);
            Profile.Bonus a = Profile.Compute(cur), b = Profile.Compute(pending);
            string[] names = { "Урон", "Здоровье", "Урон по равновесию", "Накопление духа", "Окно отражения", "Откат рывка", "Скорость" };
            float[] av = { a.dmgMul, 100 + a.hp, a.postureMul, a.kiGainMul, a.parryMul, a.dashCdMul, a.moveMul };
            float[] bv = { b.dmgMul, 100 + b.hp, b.postureMul, b.kiGainMul, b.parryMul, b.dashCdMul, b.moveMul };
            bool[] lowerBetter = { false, false, false, false, false, true, false };
            for (int k = 0; k < names.Length; k++)
            {
                float y = 102f + k * 40f;
                UiKit.Text(new Rect(rx + 12f, y, rw - 24f, 18f), names[k], 12.5f, new Color(0.82f, 0.78f, 0.7f), TextAnchor.MiddleLeft, false);
                bool better = lowerBetter[k] ? bv[k] < av[k] - 1e-4f : bv[k] > av[k] + 1e-4f, worse = lowerBetter[k] ? bv[k] > av[k] + 1e-4f : bv[k] < av[k] - 1e-4f;
                string txt = Mathf.Abs(bv[k] - av[k]) < 1e-4f ? Fmt(av[k], k) : Fmt(av[k], k) + "  →  " + Fmt(bv[k], k);
                UiKit.Text(new Rect(rx + 12f, y + 17f, rw - 24f, 20f), txt, 15f, better ? new Color(0.55f, 0.95f, 0.45f) : worse ? new Color(1f, 0.55f, 0.45f) : Ink, TextAnchor.MiddleLeft);
            }
            // кнопки: перераспределение бесплатно, в любой момент
            float by = 470f, bw = (W - 64f - 20f) / 3f;
            if (Btn(new Rect(32f, by, bw, 42f), "СБРОСИТЬ ВСЁ", used > 0, true)) for (int i = 0; i < pending.Length; i++) pending[i] = 0;
            if (Btn(new Rect(42f + bw, by, bw, 42f), "ОТМЕНА", changed)) pending = Profile.StatsCopy();
            if (Btn(new Rect(52f + bw * 2f, by, bw, 42f), "ПРИНЯТЬ", changed)) { Profile.SetStats(pending); pending = Profile.StatsCopy(); if (DuelManager.Instance != null) DuelManager.Instance.ApplyProfile(); }
        }

        static string Fmt(float v, int kk) { return kk == 1 ? Mathf.RoundToInt(v).ToString() : (v >= 1f ? "+" : "−") + Mathf.RoundToInt(Mathf.Abs(v - 1f) * 100f) + "%"; }

        void DrawEquipped(Rect a)
        {
            UiKit.Text(new Rect(a.x, a.y, a.width, 26f), "НАДЕТО", 16f, Gold);
            for (int s = 0; s < Profile.SlotCount; s++)
            {
                float x = a.x + (s % 2) * 120f + 8f, y = a.y + 30f + (s / 2) * 136f;
                Rect r = new Rect(x, y, 96f, 96f);
                Item it = Profile.Equipped(s);
                DrawItem(r, it, it != null && it == sel);
                UiKit.Text(new Rect(x - 12f, y + 98f, 120f, 18f), Profile.SlotNames[s], 11f, new Color(0.8f, 0.76f, 0.68f));
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) sel = it;
            }
        }

        void DrawBag(Rect a)
        {
            List<Item> all = Profile.Items;
            List<Item> bag = new List<Item>();
            foreach (Item it in all) if (!Profile.IsEquipped(it)) bag.Add(it);
            const float cell = 58f, gap = 6f;
            int cols = Mathf.Max(3, Mathf.FloorToInt((a.width + gap) / (cell + gap)));
            int per = cols * 4, pages = Mathf.Max(1, Mathf.CeilToInt(bag.Count / (float)per));
            page = Mathf.Clamp(page, 0, pages - 1);
            UiKit.Text(new Rect(a.x, a.y, a.width, 26f), "СУМКА  " + all.Count + " / " + Profile.BagSize, 16f, Gold, TextAnchor.MiddleLeft);
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
            DrawCard(new Rect(a.x, a.y + 30f + 4f * (cell + gap) + 6f, a.width, 456f - (30f + 4f * (cell + gap) + 6f)));
        }

        void DrawCard(Rect a)
        {
            if (sel != null && !Profile.Items.Contains(sel)) sel = null;
            UiKit.Rect(a, new Color(0f, 0f, 0f, 0.35f));
            if (sel == null) { UiKit.Text(a, "выбери предмет", 13f, new Color(0.7f, 0.66f, 0.6f)); return; }
            Item it = sel;
            Color rc = Profile.RarityColors[it.rarity];
            UiKit.Text(new Rect(a.x + 10f, a.y + 4f, a.width - 20f, 22f), it.name + (it.level > 0 ? "  +" + it.level : ""), 16f, rc, TextAnchor.MiddleLeft);
            UiKit.Text(new Rect(a.x + 10f, a.y + 24f, a.width - 20f, 16f), Profile.SlotNames[it.slot] + "  ·  " + Profile.RarityNames[it.rarity], 10.5f, new Color(0.78f, 0.74f, 0.66f), TextAnchor.MiddleLeft, false);
            float y = a.y + 42f;
            if (it.slot == (int)Slot.Scroll)
            {
                int t = Mathf.Clamp(it.tech, 0, 5);
                UiKit.Text(new Rect(a.x + 10f, y, a.width - 20f, 16f), "Приём: " + Profile.TechNames[t], 12f, Ink, TextAnchor.MiddleLeft, false);
                UiKit.Text(new Rect(a.x + 10f, y + 16f, a.width - 20f, 16f), Profile.TechHint[t], 11f, new Color(0.85f, 0.82f, 0.74f), TextAnchor.MiddleLeft, false);
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
            }
            // кнопки
            float bw = (a.width - 30f) / 3f, by = a.yMax - 40f;
            if (it.slot == (int)Slot.Scroll) { if (Btn(new Rect(a.x + 10f, by, bw, 34f), "ПРОЧЕСТЬ")) { Profile.Learn(it); sel = null; return; } }
            else if (Btn(new Rect(a.x + 10f, by, bw, 34f), Profile.IsEquipped(it) ? "СНЯТЬ" : "НАДЕТЬ")) Profile.Equip(it);
            bool canUp = it.slot != (int)Slot.Scroll && it.level < Profile.MaxItemLevel;
            int cost = Profile.UpgradeCost(it);
            if (Btn(new Rect(a.x + 15f + bw, by, bw, 34f), canUp ? "КУЗНЕЦ " + cost : "МАКС.", canUp && Profile.Gold >= cost)) Profile.Upgrade(it);
            if (Btn(new Rect(a.x + 20f + bw * 2f, by, bw, 34f), "ПРОДАТЬ " + Profile.SellPrice(it), true, true)) { Profile.Sell(it); sel = null; }
        }

        // ---------------- полоса «Добыча» на экране итогов матча ----------------

        void DrawLootStrip()
        {
            DuelManager dm = DuelManager.Instance;
            Reward r = Profile.LastReward;
            if (dm == null || r == null || !dm.MatchOver || Profile.LastRewardAt < dm.MatchStartedAt) return;
            if (dm.State != DuelState.PlayerWon && dm.State != DuelState.PlayerLost) return;
            float t = Time.unscaledTime - Profile.LastRewardAt - 1.4f;
            if (t <= 0f) return;
            float W = UiKit.Width, cx = W * 0.5f, a = Mathf.Clamp01(t / 0.35f);
            Rect p = new Rect(cx - 350f, 366f, 700f, 106f);
            UiKit.Rect(p, new Color(0.02f, 0.015f, 0.015f, 0.82f * a));
            UiKit.Rect(new Rect(p.x, p.y, p.width, 2f), new Color(Gold.r, Gold.g, Gold.b, 0.7f * a));
            Texture2D chest = UiKit.Tex(r.items.Count > 0 ? "UI/chest_open" : "UI/chest_closed");
            float tx = p.x + 14f;
            if (chest != null) { Color oc = GUI.color; GUI.color = new Color(1f, 1f, 1f, a); GUI.DrawTexture(new Rect(p.x + 6f, p.y + 8f, 90f, 90f), chest); GUI.color = oc; tx = p.x + 104f; }
            UiKit.Text(new Rect(tx, p.y + 4f, 280f, 22f), "ДОБЫЧА", 16f, new Color(Gold.r, Gold.g, Gold.b, a), TextAnchor.MiddleLeft);
            string line = "+" + r.gold + " МОН   ·   +" + r.xp + " ОПЫТА";
            if (r.soldForSpace > 0) line += "   ·   сумка полна: продано за " + r.soldForSpace;
            UiKit.Text(new Rect(tx, p.y + 26f, 360f, 18f), line, 12f, new Color(Ink.r, Ink.g, Ink.b, a), TextAnchor.MiddleLeft, false);
            if (r.levels > 0)
            {
                float pulse = 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 5f);
                Texture2D up = UiKit.Tex("UI/levelup");
                if (up != null) GUI.DrawTexture(new Rect(tx - 2f, p.y + 44f, 24f, 24f), up);
                UiKit.Text(new Rect(tx + (up != null ? 24f : 0f), p.y + 46f, 340f, 22f), "НОВЫЙ УРОВЕНЬ " + Profile.Level + "  ·  +" + r.points + " ОЧКА", 15f * pulse, new Color(1f, 0.82f, 0.35f, a), TextAnchor.MiddleLeft);
            }
            if (r.cycleUp) UiKit.Text(new Rect(tx, p.y + 70f, 380f, 18f), "КРУГ " + (Profile.Cycle + 1) + ": ВРАГИ КРЕПЧЕ, ДОБЫЧА БОГАЧЕ", 11.5f, new Color(1f, 0.55f, 0.4f, a), TextAnchor.MiddleLeft, false);
            // предметы появляются по одному
            for (int i = 0; i < r.items.Count && i < 4; i++)
            {
                float it = Mathf.Clamp01((t - 0.3f - i * 0.3f) / 0.25f);
                if (it <= 0f) continue;
                float s = 64f * (1f + 0.4f * (1f - it) * (1f - it));
                Vector2 c = new Vector2(p.xMax - 50f - (r.items.Count - 1 - i) * 74f, p.y + 46f);
                Color o = GUI.color; GUI.color = new Color(1f, 1f, 1f, it * a);
                DrawItem(new Rect(c.x - s * 0.5f, c.y - s * 0.5f, s, s), r.items[i], false);
                GUI.color = o;
                string nm = r.items[i].name; if (nm.Length > 14) nm = nm.Substring(0, 13) + "…";
                UiKit.Text(new Rect(c.x - 40f, c.y + 34f, 80f, 14f), nm, 9f, new Color(Profile.RarityColors[r.items[i].rarity].r, Profile.RarityColors[r.items[i].rarity].g, Profile.RarityColors[r.items[i].rarity].b, it * a), TextAnchor.MiddleCenter, false);
            }
        }
    }
}
