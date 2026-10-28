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

        void OnGUI()
        {
            if (duel == null || duel.player == null || duel.enemy == null) return;
            Matrix4x4 old = UiKit.Begin();
            float W = UiKit.Width;

            float barW = Mathf.Min(W * 0.36f, 420f);
            float cx = W * 0.5f;
            DrawBar(new Rect(cx - 52f - barW, 16f, barW, 20f), duel.player, false, playerName, new Color(0.15f, 0.55f, 0.85f));
            DrawBar(new Rect(cx + 52f, 16f, barW, 20f), duel.enemy, true, enemyName, new Color(0.75f, 0.2f, 0.85f));

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

            // «БОЙ!»
            if (duel.State == DuelState.Intro)
            {
                float t = duel.StateTime / duel.introSeconds;
                float a = t < 0.75f ? 1f : Mathf.Clamp01((1f - t) * 4f);
                float sc = 1f + (1f - Mathf.Clamp01(t * 3f)) * 0.8f;
                UiKit.Text(new Rect(cx - 300f, 150f, 600f, 110f), "БОЙ!", 78f * sc, new Color(Gold.r, Gold.g, Gold.b, a));
            }

            // итог
            if (duel.State == DuelState.PlayerWon || duel.State == DuelState.PlayerLost)
            {
                float t = Mathf.Clamp01((duel.StateTime - 0.5f) / 0.8f);
                bool won = duel.State == DuelState.PlayerWon;
                UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, 0.55f * t));
                Color c = won ? Gold : new Color(0.85f, 0.15f, 0.12f);
                c.a = t;
                UiKit.Text(new Rect(cx - 400f, 110f, 800f, 120f), won ? "ПОБЕДА" : "ПОРАЖЕНИЕ", 90f, c);
                UiKit.Rect(new Rect(cx - 200f, 232f, 400f, 2f), new Color(c.r, c.g, c.b, 0.8f * t));
            }
            GUI.matrix = old;
        }

        void DrawBar(Rect r, FighterController f, bool mirror, string name, Color portrait)
        {
            float hp = duel.Hp01(f), tr = duel.Trail01(f);
            float since = duel.SinceHit(f);
            // рамка + подложка
            UiKit.Rect(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), Frame);
            UiKit.Rect(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), new Color(0.55f, 0.43f, 0.22f, 0.9f));
            UiKit.Rect(r, new Color(0.08f, 0.02f, 0.02f, 1f));
            // шлейф и текущее
            DrawFill(r, tr, mirror, new Color(0.95f, 0.75f, 0.3f, 0.95f));
            Color main = hp > 0.3f ? new Color(0.78f, 0.1f, 0.1f) : new Color(0.95f, 0.18f, 0.08f);
            if (since < 0.12f) main = Color.white;
            DrawFill(r, hp, mirror, main);
            // блик
            DrawFill(new Rect(r.x, r.y, r.width, r.height * 0.35f), hp, mirror, new Color(1f, 1f, 1f, 0.16f));
            // деления
            for (int i = 1; i < 4; i++) UiKit.Rect(new Rect(r.x + r.width * i / 4f - 0.5f, r.y, 1f, r.height), new Color(0f, 0f, 0f, 0.45f));

            // портрет
            float px = mirror ? r.xMax + 28f : r.x - 28f;
            Vector2 pc = new Vector2(px, r.y + r.height * 0.5f);
            UiKit.Disc(pc, 24f, Frame);
            UiKit.Disc(pc, 20f, portrait * 0.55f);
            UiKit.Ring(pc, 24f, Gold);
            UiKit.Text(new Rect(pc.x - 20f, pc.y - 18f, 40f, 36f), mirror ? "П" : "Р", 22f, new Color(1f, 1f, 1f, 0.95f), TextAnchor.MiddleCenter, false);

            UiKit.Text(new Rect(mirror ? r.xMax - 260f : r.x, r.yMax + 3f, 260f, 18f), name, 13f, new Color(0.9f, 0.85f, 0.7f, 0.95f), mirror ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
            UiKit.Text(new Rect(mirror ? r.x : r.xMax - 80f, r.yMax + 3f, 80f, 18f), Mathf.CeilToInt(duel.Hp(f)) + "", 13f, new Color(1f, 1f, 1f, 0.75f), mirror ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);
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
