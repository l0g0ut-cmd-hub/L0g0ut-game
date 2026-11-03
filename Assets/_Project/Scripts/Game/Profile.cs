using System;
using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>Ячейка снаряжения.</summary>
    public enum Slot { Blade, Guard, Garb, Mask, Charm, Gourd, Scroll }

    /// <summary>Свойство предмета. Проценты — долями (0,08 = +8 %), здоровье/равновесие/глотки — единицами.</summary>
    public enum ModId { Damage, MaxHp, PostureDmg, Armor, KiGain, Parry, DashCd, MoveSpeed, Heal, Gourd, PostureMax, GoldFind, Bleed, Crit }

    /// <summary>Приём из свитка: открывается навсегда.</summary>
    public enum Tech { Riposte, SecondWind, Bloodletting, Thunder, StoneGuard, SteelThirst }

    [Serializable] public class Mod { public int stat; public float value; }

    [Serializable]
    public class Item
    {
        public int id, slot, rarity, level, icon, tech = -1;
        public int set = -1;    // комплект клана: 0 Ворон, 1 Волк, 2 Змея (клинок, цуба, облачение, маска)
        public int rune = -1;   // руна в клинке (вставляет кузнец): Profile.RuneNames
        public string name;
        public List<Mod> mods = new List<Mod>();
        public float Value(Mod m) { return m.value * (1f + 0.12f * level); }   // улучшение у кузнеца: +12 % к каждому свойству за уровень
    }

    /// <summary>Итог матча: что получено (показывается на экране итогов).</summary>
    public class Reward
    {
        public int gold, xp, levels, points;
        public List<Item> items = new List<Item>();
        public int soldForSpace;   // не влезло в сумку — продано
        public bool cycleUp;
    }

    /// <summary>
    /// Путь самурая между боями: уровень и опыт, пять характеристик (力 сила, 体 стойкость, 気 дух, 速 ловкость, 技 мастерство),
    /// монеты, сумка с предметами и надетое снаряжение (клинок, цуба, облачение, маска, амулет, тыква), приёмы из свитков,
    /// круг «Пути» (после последнего врага — заново, враги крепче, добыча богаче). Всё хранится в PlayerPrefs (JSON).
    /// </summary>
    public static class Profile
    {
        [Serializable]
        class Data
        {
            public int level = 1, xp, points, gold, cycle, nextId = 1, techMask;
            public int[] stats = new int[5];
            public int[] equipped = { -1, -1, -1, -1, -1, -1 };
            public int companion = -1;   // спутник: 0 ворон, 1 волк, 2 змея (−1 — нет)
            public List<Item> items = new List<Item>();
        }

        public const int StatCount = 5, SlotCount = 6, BagSize = 30;
        public static readonly string[] StatNames = { "СИЛА", "СТОЙКОСТЬ", "ДУХ", "ЛОВКОСТЬ", "МАСТЕРСТВО" };
        public static readonly string[] StatHint = { "+4% к урону", "+8 к здоровью", "+8% к накоплению духа", "−4% к откату рывка, +1,5% к скорости", "+5% к окну отражения и урону по равновесию" };
        public static readonly string[] StatGlyph = { "UI/stat_str", "UI/stat_vit", "UI/stat_spi", "UI/stat_agi", "UI/stat_tec" };
        public static readonly string[] SlotNames = { "КЛИНОК", "ЦУБА", "ОБЛАЧЕНИЕ", "МАСКА", "АМУЛЕТ", "ТЫКВА", "СВИТОК" };
        public static readonly string[] RarityNames = { "ОБЫЧНЫЙ", "ДОБРОТНЫЙ", "РЕДКИЙ", "ЛЕГЕНДАРНЫЙ" };
        public static readonly Color[] RarityColors = { new Color(0.82f, 0.8f, 0.76f), new Color(0.55f, 0.85f, 0.45f), new Color(0.45f, 0.72f, 1f), new Color(1f, 0.75f, 0.25f) };
        public static readonly string[] TechNames = { "ОТВЕТНЫЙ УДАР", "ВТОРОЕ ДЫХАНИЕ", "КРОВОПУСКАНИЕ", "УДАР ГРОМА", "КАМЕННАЯ СТОЙКА", "ЖАЖДА СТАЛИ" };
        public static readonly string[] TechHint =
        {
            "контратака после отражения — ×2 вместо ×1,5",
            "ещё один глоток из тыквы за бой",
            "твои удары пускают кровь: 3 урона в секунду 3 с",
            "удар духа наносит на 50% больше",
            "блок копит на 30% меньше урона по равновесию",
            "каждое попадание лечит на 8% нанесённого урона"
        };

        static Data d;
        static Data D { get { if (d == null) Load(); return d; } }

        static void Load()
        {
            string js = PlayerPrefs.GetString("profile_v1", "");
            try { d = string.IsNullOrEmpty(js) ? null : JsonUtility.FromJson<Data>(js); } catch { d = null; }
            if (d == null) d = new Data();
            if (d.stats == null || d.stats.Length != StatCount) d.stats = new int[StatCount];
            if (d.equipped == null || d.equipped.Length != SlotCount) d.equipped = new[] { -1, -1, -1, -1, -1, -1 };
            if (d.items == null) d.items = new List<Item>();
            dirty = true;
        }

        public static void Save() { PlayerPrefs.SetString("profile_v1", JsonUtility.ToJson(D)); PlayerPrefs.Save(); dirty = true; }

        public static int Level { get { return D.level; } }
        public static int Xp { get { return D.xp; } }
        public static int XpToNext { get { return 80 + 40 * (D.level - 1); } }
        public static int Points { get { return D.points; } }
        public static int Gold { get { return D.gold; } }
        public static int Cycle { get { return D.cycle; } }
        public static int Stat(int i) { return D.stats[i]; }
        public static List<Item> Items { get { return D.items; } }
        public static bool Has(Tech t) { return (D.techMask & (1 << (int)t)) != 0; }
        public static int TechCount { get { int n = 0; for (int i = 0; i < 6; i++) if ((D.techMask & (1 << i)) != 0) n++; return n; } }

        public static Item Equipped(int slot)
        {
            int id = D.equipped[slot];
            if (id < 0) return null;
            foreach (Item it in D.items) if (it.id == id) return it;
            return null;
        }
        public static bool IsEquipped(Item it) { return it != null && it.slot < SlotCount && D.equipped[it.slot] == it.id; }

        public static bool AddPoint(int stat)
        {
            if (D.points <= 0) return false;
            D.points--; D.stats[stat]++; Save(); return true;
        }

        public static void Equip(Item it)
        {
            if (it == null || it.slot >= SlotCount) return;
            D.equipped[it.slot] = IsEquipped(it) ? -1 : it.id;
            Save();
        }

        public static int SellPrice(Item it) { return Mathf.RoundToInt(15f * (1f + it.rarity * 2f) * (1f + it.level * 0.5f) * (1f + 0.3f * D.cycle)); }
        public static float CostMul = 1f;   // у кузнеца на карте — полцены
        public static int UpgradeCost(Item it) { return Mathf.RoundToInt(50f * (it.level + 1) * (1f + it.rarity) * CostMul); }
        public static int BuyPrice(Item it) { return SellPrice(it) * 3; }

        // ---------------- карта похода: моны, опыт, предметы без боя ----------------

        public static bool Spend(int v) { if (v < 0 || D.gold < v) return false; D.gold -= v; Save(); return true; }
        public static void AddGold(int v) { D.gold = Mathf.Max(0, D.gold + v); Save(); }
        /// <summary>Опыт вне боя; возвращает, сколько уровней взято.</summary>
        public static int AddXp(int v)
        {
            D.xp += Mathf.Max(0, v); int lv = 0;
            while (D.xp >= XpToNext) { D.xp -= XpToNext; D.level++; D.points += 2; lv++; }
            Save(); return lv;
        }
        public static void GivePoint() { D.points++; Save(); }
        /// <summary>Предмет в сумку (пустой слот — сразу надеть). Сумка полна — продаётся; false.</summary>
        public static bool Give(Item it)
        {
            if (it == null) return false;
            if (D.items.Count >= BagSize) { D.gold += SellPrice(it); Save(); return false; }
            it.id = D.nextId++; D.items.Add(it);
            if (it.slot < SlotCount && D.equipped[it.slot] < 0) D.equipped[it.slot] = it.id;
            Save(); return true;
        }
        /// <summary>Свиток с ещё не выученным приёмом (null — все выучены).</summary>
        public static Item NewScroll() { return TechCount < 6 ? ScrollItem() : null; }
        /// <summary>Тайник на карте: моны, немного опыта, предмет (иногда два).</summary>
        public static Reward Treasure(int stage)
        {
            Reward r = new Reward();
            float cyc = 1f + 0.4f * D.cycle;
            r.gold = Mathf.RoundToInt((35 + 20 * stage) * cyc * B.goldMul * UnityEngine.Random.Range(0.85f, 1.25f));
            r.xp = Mathf.RoundToInt((20 + 10 * stage) * cyc);
            int n = 1 + (UnityEngine.Random.value < 0.35f ? 1 : 0);
            for (int i = 0; i < n; i++) AddLoot(r, Roll(stage, 40, false));
            D.gold += r.gold; D.xp += r.xp;
            while (D.xp >= XpToNext) { D.xp -= XpToNext; D.level++; D.points += 2; r.levels++; r.points += 2; }
            Save();
            return r;
        }
        public const int MaxItemLevel = 5;

        public static void Sell(Item it)
        {
            if (it == null) return;
            if (IsEquipped(it)) D.equipped[it.slot] = -1;
            D.gold += SellPrice(it);
            D.items.Remove(it);
            Save();
        }

        public static bool Upgrade(Item it)
        {
            if (it == null || it.slot == (int)Slot.Scroll || it.level >= MaxItemLevel) return false;
            int c = UpgradeCost(it);
            if (D.gold < c) return false;
            D.gold -= c; it.level++; Save(); return true;
        }

        /// <summary>Прочесть свиток: приём открыт навсегда, свиток исчезает.</summary>
        public static void Learn(Item it)
        {
            if (it == null || it.slot != (int)Slot.Scroll || it.tech < 0) return;
            D.techMask |= 1 << it.tech;
            D.items.Remove(it);
            Save();
        }

        // ---------------- бонусы снаряжения и характеристик ----------------

        public struct Bonus
        {
            public float dmgMul, postureMul, armorMul, kiGainMul, kiRegenMul, parryMul, dashCdMul, moveMul, healMul, goldMul, bleedDps, crit, blockPostureMul;
            public float lifesteal;   // руна «Вампиризм»: доля урона, возвращаемая здоровьем
            public int hp, gourds, postureMax;
        }
        static Bonus cache; static bool dirty = true;

        public static float ModSum(ModId id)
        {
            float s = 0f;
            for (int i = 0; i < SlotCount; i++)
            {
                Item it = Equipped(i);
                if (it == null) continue;
                foreach (Mod m in it.mods) if (m.stat == (int)id) s += it.Value(m);
            }
            return s;
        }

        public static Bonus B
        {
            get
            {
                if (!dirty) return cache;
                dirty = false;
                cache = Compute(D.stats);
                return cache;
            }
        }

        /// <summary>Все очки: свободные + вложенные (для перераспределения).</summary>
        public static int TotalPoints { get { int s = D.points; foreach (int v in D.stats) s += v; return s; } }
        public static int[] StatsCopy() { return (int[])D.stats.Clone(); }

        /// <summary>Принять новое распределение (сумма не больше всех очков; остаток — свободные).</summary>
        public static bool SetStats(int[] st)
        {
            if (st == null || st.Length != StatCount) return false;
            int sum = 0; foreach (int v in st) { if (v < 0) return false; sum += v; }
            int total = TotalPoints;
            if (sum > total) return false;
            D.stats = (int[])st.Clone(); D.points = total - sum; Save();
            return true;
        }

        /// <summary>Бонусы при данных характеристиках и надетом снаряжении (для предпросмотра в «Прокачке»).</summary>
        public static Bonus Compute(int[] st)
        {
            {
                Bonus b = new Bonus();
                b.dmgMul = 1f + 0.04f * st[0] + ModSum(ModId.Damage);
                b.hp = 8 * st[1] + Mathf.RoundToInt(ModSum(ModId.MaxHp));
                b.kiGainMul = 1f + 0.08f * st[2] + ModSum(ModId.KiGain);
                b.kiRegenMul = 1f + 0.05f * st[2] + ModSum(ModId.KiGain) * 0.5f;
                b.dashCdMul = Mathf.Max(0.5f, 1f - 0.04f * st[3] - ModSum(ModId.DashCd));
                b.moveMul = Mathf.Min(1.35f, 1f + 0.015f * st[3] + ModSum(ModId.MoveSpeed));
                b.parryMul = Mathf.Min(2f, 1f + 0.05f * st[4] + ModSum(ModId.Parry));
                b.postureMul = 1f + 0.05f * st[4] + ModSum(ModId.PostureDmg);
                b.armorMul = 1f - Mathf.Min(0.5f, ModSum(ModId.Armor));
                b.healMul = 1f + ModSum(ModId.Heal);
                b.goldMul = 1f + ModSum(ModId.GoldFind);
                b.bleedDps = ModSum(ModId.Bleed) + (Has(Tech.Bloodletting) ? 3f : 0f);
                b.crit = Mathf.Min(0.5f, ModSum(ModId.Crit));
                b.gourds = Mathf.RoundToInt(ModSum(ModId.Gourd)) + (Has(Tech.SecondWind) ? 1 : 0);
                b.postureMax = Mathf.RoundToInt(ModSum(ModId.PostureMax));
                b.blockPostureMul = Has(Tech.StoneGuard) ? 0.7f : 1f;
                // комплекты кланов (2 и 4 предмета) и руна клинка
                for (int set = 0; set < 3; set++)
                {
                    int c = SetPieces(set);
                    if (set == 0) { if (c >= 2) b.crit = Mathf.Min(0.5f, b.crit + 0.08f); if (c >= 4) b.dmgMul += 0.15f; }
                    if (set == 1) { if (c >= 2) b.hp += 20; if (c >= 4) b.armorMul *= 0.9f; }
                    if (set == 2) { if (c >= 2) b.bleedDps += 2f; if (c >= 4) { b.postureMul += 0.12f; b.kiGainMul += 0.1f; } }
                }
                Item blade = Equipped((int)Slot.Blade);
                switch (blade != null ? blade.rune : -1)
                {
                    case 0: b.bleedDps += 2.5f; break;
                    case 1: b.dmgMul += 0.06f; break;
                    case 2: b.bleedDps += 1.2f; b.postureMul += 0.05f; break;
                    case 3: b.lifesteal += 0.06f; break;
                    case 4: b.parryMul = Mathf.Min(2f, b.parryMul + 0.15f); break;
                    case 5: b.crit = Mathf.Min(0.5f, b.crit + 0.06f); break;
                    case 6: b.postureMul += 0.12f; break;
                    case 7: b.goldMul += 0.2f; break;
                }
                // Starting identities grant small, legible differences:
                // Dzin: flexible sword technique; Akiko: survivability;
                // Ren: nimble movement. Existing equipment/stat rules remain.
                switch (JourneyChoice.Hero)
                {
                    case 0: b.dmgMul *= 1.03f; break;
                    case 1: b.hp += 6; b.armorMul *= 0.93f; break;
                    case 2:
                        b.moveMul = Mathf.Min(1.38f, b.moveMul * 1.06f);
                        b.dashCdMul *= 0.94f;
                        break;
                }
                return b;
            }
        }

        /// <summary>Враги на втором и дальше кругах «Пути» крепче.</summary>
        public static float CycleHp { get { return 1f + 0.35f * D.cycle; } }
        public static float CycleDmg { get { return 1f + 0.22f * D.cycle; } }

        // ---------------- награда за бой ----------------

        public static Reward LastReward { get; private set; }
        public static float LastRewardAt { get; private set; } = -10f;

        public static Reward Grant(bool won, int stage, int score, bool perfect, bool last, bool rareDrop = false, float goldMul = 1f, bool noLoot = false)
        {
            Reward r = new Reward();
            float cyc = 1f + 0.4f * D.cycle;
            if (won)
            {
                r.xp = Mathf.RoundToInt((60 + 30 * stage + Mathf.Clamp(score, 0, 120) * 0.5f) * cyc);
                r.gold = Mathf.RoundToInt((30 + 20 * stage) * cyc * B.goldMul * UnityEngine.Random.Range(0.85f, 1.2f));
                int n = 1 + (UnityEngine.Random.value < 0.35f ? 1 : 0) + (perfect || score >= 95 ? 1 : 0);
                r.gold = Mathf.RoundToInt(r.gold * goldMul);
                if (!noLoot)   // реванш на пройденном месте — без добычи
                {
                    for (int i = 0; i < n; i++) AddLoot(r, Roll(stage, score, (last || rareDrop) && i == 0));
                    // свиток с приёмом: с шансом, у последнего врага — чаще
                    if (TechCount < 6 && UnityEngine.Random.value < (last ? 0.6f : 0.22f)) AddLoot(r, ScrollItem());
                }
                if (last) { D.cycle++; r.cycleUp = true; }
            }
            else
            {
                r.xp = Mathf.RoundToInt((20 + 10 * stage) * cyc);
                r.gold = Mathf.RoundToInt((8 + 4 * stage) * cyc);
            }
            D.gold += r.gold;
            D.xp += r.xp;
            while (D.xp >= XpToNext) { D.xp -= XpToNext; D.level++; D.points += 2; r.levels++; r.points += 2; }
            Save();
            LastReward = r; LastRewardAt = Time.unscaledTime;
            return r;
        }

        static void AddLoot(Reward r, Item it)
        {
            if (D.items.Count >= BagSize) { int p = SellPrice(it); D.gold += p; r.soldForSpace += p; return; }
            it.id = D.nextId++;
            D.items.Add(it);
            r.items.Add(it);
            // пустая ячейка — сразу надеть (первые предметы не надо искать в сумке)
            if (it.slot < SlotCount && D.equipped[it.slot] < 0) D.equipped[it.slot] = it.id;
        }

        // ---------------- генерация предметов ----------------

        static readonly string[][] Bases =
        {
            new[] { "Катана", "Тати", "Утигатана", "Вакидзаси" },
            new[] { "Цуба" },
            new[] { "Хаори", "Кимоно", "Нагрудник до", "Доспех" },
            new[] { "Мэнпо", "Маска они" },
            new[] { "Омамори", "Талисман", "Оберег" },
            new[] { "Тыква", "Хётан" },
        };
        static readonly string[] EpUncommon = { "Речной камень", "Старая сосна", "Закалка", "Иней", "Тихий шаг", "Ржавый лист" };
        static readonly string[] EpRare = { "Лунный серп", "Журавль", "Багровый лист", "Северный ветер", "Волна Канагавы", "Чёрный тигр", "Пепел храма" };
        static readonly string[] EpLegend = { "Мурамаса", "Кусанаги", "Слеза дракона", "Тысяча журавлей", "Сердце бури", "Последний сёгун" };

        static readonly ModId[][] Pools =
        {
            new[] { ModId.Damage, ModId.Crit, ModId.Bleed, ModId.PostureDmg },     // клинок
            new[] { ModId.Parry, ModId.PostureMax, ModId.Armor },                    // цуба
            new[] { ModId.MaxHp, ModId.Armor, ModId.MoveSpeed },                     // облачение
            new[] { ModId.PostureDmg, ModId.Damage, ModId.KiGain },                  // маска
            new[] { ModId.KiGain, ModId.GoldFind, ModId.DashCd, ModId.Heal },        // амулет
            new[] { ModId.Heal, ModId.MaxHp, ModId.Gourd },                          // тыква
        };

        static float Range(ModId m, int rarity, float power)
        {
            float k = (1f + 0.55f * rarity) * power;
            switch (m)
            {
                case ModId.Damage: return UnityEngine.Random.Range(0.04f, 0.08f) * k;
                case ModId.Crit: return UnityEngine.Random.Range(0.03f, 0.06f) * k;
                case ModId.Bleed: return UnityEngine.Random.Range(0.8f, 1.5f) * k;
                case ModId.PostureDmg: return UnityEngine.Random.Range(0.05f, 0.1f) * k;
                case ModId.Parry: return UnityEngine.Random.Range(0.06f, 0.12f) * k;
                case ModId.PostureMax: return Mathf.Round(UnityEngine.Random.Range(8f, 15f) * k);
                case ModId.Armor: return UnityEngine.Random.Range(0.03f, 0.06f) * k;
                case ModId.MaxHp: return Mathf.Round(UnityEngine.Random.Range(8f, 16f) * k);
                case ModId.MoveSpeed: return UnityEngine.Random.Range(0.02f, 0.045f) * k;
                case ModId.KiGain: return UnityEngine.Random.Range(0.06f, 0.12f) * k;
                case ModId.GoldFind: return UnityEngine.Random.Range(0.08f, 0.15f) * k;
                case ModId.DashCd: return UnityEngine.Random.Range(0.04f, 0.08f) * k;
                case ModId.Heal: return UnityEngine.Random.Range(0.08f, 0.15f) * k;
                case ModId.Gourd: return 1f;
            }
            return 0f;
        }

        static T Pick<T>(T[] a) { return a[UnityEngine.Random.Range(0, a.Length)]; }

        /// <summary>Новый предмет: редкость зависит от ступени, круга и оценки боя; у последнего врага — не ниже редкого.</summary>
        // ---------------- комплекты кланов, руны, спутники ----------------

        public static readonly string[] SetNames = { "клана Ворона", "клана Волка", "клана Змеи" };
        static readonly string[] SetBases = { "Катана", "Цуба", "Доспех", "Мэнпо" };
        public static readonly string[] SetBonus2 = { "+8% к шансу точного удара", "+20 к здоровью", "кровотечение +2 ед/с" };
        public static readonly string[] SetBonus4 = { "+15% к урону", "−10% получаемого урона", "+12% к урону по равновесию, +10% к накоплению духа" };
        /// <summary>Сколько предметов комплекта надето (клинок, цуба, облачение, маска).</summary>
        public static int SetPieces(int set)
        {
            int c = 0;
            for (int i = 0; i < 4; i++) { Item it = Equipped(i); if (it != null && it.set == set) c++; }
            return c;
        }

        public static readonly string[] RuneNames = { "КРОВОТЕЧЕНИЕ", "ОГОНЬ", "ЯД", "ВАМПИРИЗМ", "ЛЁД", "ГРОМ", "ЛОМКА СТОЙКИ", "УДАЧА" };
        public static readonly string[] RuneHints = { "кровотечение +2,5 ед/с", "+6% к урону", "кровотечение +1,2 ед/с, +5% по равновесию", "6% урона возвращается здоровьем",
                                                      "+15% к окну отражения", "+6% к шансу точного удара", "+12% к урону по равновесию", "+20% монет за бой" };
        public static int RuneCost { get { return Mathf.RoundToInt(120f * (1f + 0.3f * D.cycle) * CostMul); } }
        public static bool SetRune(Item it, int rune)
        {
            if (it == null || it.slot != (int)Slot.Blade || rune < 0 || rune >= RuneNames.Length || it.rune == rune) return false;
            if (!Spend(RuneCost)) return false;
            it.rune = rune; Save();
            return true;
        }

        public const int CompanionRaven = 0, CompanionWolf = 1, CompanionSnake = 2;
        public static readonly string[] CompanionNames = { "ВОРОН", "ВОЛК", "ЗМЕЯ" };
        public static readonly string[] CompanionHints =
        {
            "Разведчик: туман войны отступает на шаг дальше. В бою раз в 20 с пикирует на врага и сбивает замах.",
            "Боец: в бою раз в 15 с кусает врага — урон и удар по равновесию.",
            "Яд: в бою раз в 12 с жалит врага — яд и равновесие врага восстанавливается вдвое медленнее 5 с.",
        };
        public static readonly string[] CompanionHintsNoFog =
        {
            "Разведчик: в бою раз в 20 с пикирует на врага и сбивает замах (урон и удар по равновесию).",
            "Боец: в бою раз в 15 с кусает врага — урон и удар по равновесию.",
            "Яд: в бою раз в 12 с жалит врага — яд и равновесие врага восстанавливается вдвое медленнее 5 с.",
        };
        /// <summary>Описание спутника с учётом того, включён ли туман войны.</summary>
        public static string CompanionHint(int c, bool fogEnabled)
        {
            if (c < 0 || c >= CompanionHints.Length) return "";
            return fogEnabled ? CompanionHints[c] : CompanionHintsNoFog[c];
        }
        public static int Companion { get { return D.companion; } }
        public static void SetCompanion(int c) { D.companion = Mathf.Clamp(c, -1, 2); Save(); }

        public static Item Roll(int stage, int score, bool bossDrop)
        {
            float shift = stage * 0.6f + D.cycle * 1.2f + Mathf.Clamp(score, 0, 120) / 60f;
            float[] w = { Mathf.Max(8f, 60f - shift * 9f), 28f + shift * 2f, 10f + shift * 4f, 2f + shift * 2f };
            float sum = w[0] + w[1] + w[2] + w[3], x = UnityEngine.Random.value * sum;
            int rarity = 0;
            while (rarity < 3 && x > w[rarity]) { x -= w[rarity]; rarity++; }
            if (bossDrop) rarity = Mathf.Max(rarity, 2);
            int slot = UnityEngine.Random.Range(0, SlotCount);
            Item made = Make(slot, rarity, 1f + 0.22f * stage + 0.4f * D.cycle);
            // предмет комплекта клана: не ниже «добротного», только клинок, цуба, облачение, маска
            if (slot <= (int)Slot.Mask && rarity >= 1 && UnityEngine.Random.value < (bossDrop ? 0.35f : 0.14f))
            {
                made.set = UnityEngine.Random.Range(0, 3);
                made.name = SetBases[slot] + " " + SetNames[made.set];
            }
            return made;
        }

        public static Item Make(int slot, int rarity, float power)
        {
            Item it = new Item { slot = slot, rarity = rarity };
            string b = Pick(Bases[slot]);
            it.name = rarity == 0 ? b : b + " «" + (rarity == 1 ? Pick(EpUncommon) : rarity == 2 ? Pick(EpRare) : Pick(EpLegend)) + "»";
            List<ModId> pool = new List<ModId>(Pools[slot]);
            if (rarity < 2) pool.Remove(ModId.Gourd);   // лишний глоток — только у редких тыкв
            int count = Mathf.Min(pool.Count, 1 + rarity);
            // главное свойство ячейки — всегда первым
            ModId first = Pools[slot][0];
            pool.Remove(first);
            it.mods.Add(new Mod { stat = (int)first, value = Range(first, rarity, power) });
            for (int i = 1; i < count && pool.Count > 0; i++)
            {
                ModId m = pool[UnityEngine.Random.Range(0, pool.Count)]; pool.Remove(m);
                it.mods.Add(new Mod { stat = (int)m, value = Range(m, rarity, power) * 0.7f });
            }
            it.icon = IconFor(slot, rarity);
            return it;
        }

        static Item ScrollItem()
        {
            List<int> left = new List<int>();
            for (int i = 0; i < 6; i++) if ((D.techMask & (1 << i)) == 0) left.Add(i);
            bool already = false;
            int t = left[UnityEngine.Random.Range(0, left.Count)];
            foreach (Item x in D.items) if (x.slot == (int)Slot.Scroll && x.tech == t) already = true;
            if (already && left.Count > 1) { left.Remove(t); t = left[UnityEngine.Random.Range(0, left.Count)]; }
            return new Item { slot = (int)Slot.Scroll, rarity = 2, tech = t, name = "Свиток «" + TechNames[t].Substring(0, 1) + TechNames[t].Substring(1).ToLower() + "»", icon = Pick(ScrollIcons) };
        }

        /// <summary>
        /// Значок: 0–15 — UI/items (4×4: клинки, цубы, облачение, амулеты, маска, тыквы, свиток, монеты),
        /// 16–51 — UI/items2 (6×6: ещё 6 клинков, 6 цуб, 6 облачений, 6 масок, амулеты и тыквы, свитки, ценности).
        /// По ячейке и редкости — случайный из подходящих, чтобы добыча не повторялась.
        /// </summary>
        static readonly int[][][] Icons =
        {
            // 0–15 — UI/items (4×4), 16–51 — UI/items2 (6×6), 52–87 — UI/items3 (6×6)
            new[] { new[] { 0, 16, 21, 52 }, new[] { 3, 21, 19, 17, 53, 57 }, new[] { 1, 17, 18, 19, 54, 57 }, new[] { 2, 20, 18, 55, 56 } },   // клинок
            new[] { new[] { 4, 27, 58 }, new[] { 4, 22, 26, 58, 59 }, new[] { 5, 23, 24, 60, 63 }, new[] { 5, 25, 24, 61, 62 } },               // цуба
            new[] { new[] { 6, 28, 33, 30, 64 }, new[] { 6, 29, 30, 66, 67 }, new[] { 7, 31, 29, 65, 68 }, new[] { 7, 32, 69 } },               // облачение
            new[] { new[] { 39, 11, 70, 75 }, new[] { 34, 36, 11, 72 }, new[] { 35, 38, 36, 11, 71, 73 }, new[] { 37, 38, 74 } },               // маска
            new[] { new[] { 8, 9, 41, 76, 77 }, new[] { 8, 9, 42, 79, 80 }, new[] { 10, 40, 42, 78 }, new[] { 43, 10, 81 } },                   // амулет
            new[] { new[] { 12, 45, 83 }, new[] { 12, 44, 83 }, new[] { 13, 44, 45, 82 }, new[] { 13, 84 } },                                   // тыква
        };
        static readonly int[] ScrollIcons = { 14, 46, 47, 48, 85, 86, 87 };

        static int IconFor(int slot, int rarity)
        {
            if (slot >= SlotCount) return Pick(ScrollIcons);
            return Pick(Icons[slot][Mathf.Clamp(rarity, 0, 3)]);
        }

        /// <summary>Строка свойства для интерфейса: «+12% урона».</summary>
        public static string ModText(ModId m, float v)
        {
            switch (m)
            {
                case ModId.Damage: return "+" + Pct(v) + " к урону";
                case ModId.MaxHp: return "+" + Mathf.RoundToInt(v) + " к здоровью";
                case ModId.PostureDmg: return "+" + Pct(v) + " к урону по равновесию";
                case ModId.Armor: return "−" + Pct(v) + " получаемого урона";
                case ModId.KiGain: return "+" + Pct(v) + " к накоплению духа";
                case ModId.Parry: return "+" + Pct(v) + " к окну отражения";
                case ModId.DashCd: return "−" + Pct(v) + " к откату рывка";
                case ModId.MoveSpeed: return "+" + Pct(v) + " к скорости";
                case ModId.Heal: return "+" + Pct(v) + " к лечению тыквы";
                case ModId.Gourd: return "+" + Mathf.RoundToInt(v) + " глоток из тыквы";
                case ModId.PostureMax: return "+" + Mathf.RoundToInt(v) + " к запасу равновесия";
                case ModId.GoldFind: return "+" + Pct(v) + " монет за бой";
                case ModId.Bleed: return "кровотечение " + v.ToString("0.0") + " ед/с";
                case ModId.Crit: return "+" + Pct(v) + " к шансу точного удара";
            }
            return "";
        }
        static string Pct(float v) { return Mathf.RoundToInt(v * 100f) + "%"; }

        /// <summary>Сбросить весь путь (новая игра с нуля).</summary>
        public static void Wipe() { d = new Data(); Save(); }
    }
}
