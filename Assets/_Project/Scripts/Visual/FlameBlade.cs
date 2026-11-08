using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Пламя клинка. Главное — покадровый лист FX/blade_fire (сделан владельцем в ChatGPT по нашему промту: 4×8 = 32 кадра,
    /// огонь вдоль прямой с раскалённой сердцевиной, бесшовный цикл): лента натянута вдоль лезвия, повёрнута к камере и всегда
    /// горит «вверх» по экрану, все 32 кадра по кругу. Ниже — прежние слои (язычки-частицы на взмахе, нити сердцевины), они
    /// остаются запасным вариантом, если листа нет.
    /// (История 2026-11-08: четыре костра-спрайта — плохо; одно свечение стали — «клинок просто меняет цвет».)
    /// Теперь по всей длине клинка горит живое пламя: множество маленьких языков огня (частицы с листом FX/brazier_fire, все 8 кадров
    /// за жизнь языка) рождаются вдоль лезвия, лижут вверх и тают; в мировом пространстве — на взмахе огонь тянется шлейфом.
    /// Сталь под огнём лишь чуть накалена (слабое свечение шейдера BladePath/Blade), с клинка срываются искры-угольки
    /// (частицы в мировом пространстве — на взмахе тянутся следом), тёплый неяркий свет, след удара — огненный (trail_fire).
    /// Ignite/Extinguish зовёт DuelManager; в последние 1,5 с накал спадает и гаснет сам.
    /// </summary>
    public class FlameBlade : MonoBehaviour
    {
        WeaponController weapon;
        float until;
        bool on;
        Renderer[] steel = new Renderer[0];
        MaterialPropertyBlock mpb;
        ParticleSystem embers, flames;
        LineRenderer coreOuter, coreInner;
        // лента пламени из листа FX/blade_fire
        GameObject ribbonGo; Mesh ribbon; Material ribbonMat; MeshRenderer ribbonR;
        // 4 полосы: кадр и следующий с наплывом (плавно, без рывков между 32 кадрами) + зеркальный слой со сдвигом фазы —
        // огонь не повторяется одним и тем же рисунком
        readonly Vector3[] rv = new Vector3[16]; readonly Vector2[] ruv = new Vector2[16]; readonly Color[] rcol = new Color[16];
        const int SheetCols = 4, SheetRows = 8, SheetFrames = 32;
        const float SheetFps = 16f;
        const float CoreV = 44f / 128f, CoreU0 = 45f / 512f, CoreU1 = 447f / 512f;   // замер листа: сердцевина в кадре   // раскалённая сердцевина вдоль лезвия: оранжевый ореол и бело-жёлтая нить поверх огня
        Light glow;
        Color trailWas; string trailTexWas; bool trailSwapped;
        static readonly Color Hot = new Color(1f, 0.36f, 0.08f);

        public static void Ignite(WeaponController w, float seconds)
        {
            if (w == null || w.bladeRoot == null) return;
            FlameBlade fb = w.GetComponent<FlameBlade>();
            if (fb == null) fb = w.gameObject.AddComponent<FlameBlade>();
            fb.weapon = w; fb.until = Time.unscaledTime + seconds;
            fb.Build();
        }

        public static void Extinguish(WeaponController w)
        {
            if (w == null) return;
            FlameBlade fb = w.GetComponent<FlameBlade>();
            if (fb != null) fb.Clear();
        }

        void Build()
        {
            if (on) return;   // уже горит — продлили время
            on = true;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            // только стальное лезвие (как кровь на клинке в Juice): не рукоять, не цуба
            List<Renderer> list = new List<Renderer>();
            foreach (Renderer r in weapon.bladeRoot.GetComponentsInChildren<Renderer>(true))
                if (r is MeshRenderer && r.sharedMaterial != null && r.sharedMaterial.HasProperty("_EmissionColor") && r.sharedMaterial.name.Contains("Steel")) list.Add(r);
            steel = list.ToArray();
            embers = MakeEmbers();
            if (!MakeRibbon())
            {
                // листа нет — прежний огонь из язычков и нитей
                flames = MakeFlames();
                coreOuter = MakeCore("FlameBladeCoreGlow", 0.075f, new Color(1f, 0.45f, 0.1f, 0.55f), 2);
                coreInner = MakeCore("FlameBladeCore", 0.024f, new Color(1f, 0.95f, 0.75f, 1f), 3);
            }
            GameObject lo = new GameObject("FlameBladeLight");
            lo.transform.position = BladePoint(0.6f);   // в мировых координатах, каждый кадр (LateUpdate)
            glow = lo.AddComponent<Light>();
            glow.type = LightType.Point; glow.color = new Color(1f, 0.5f, 0.2f); glow.range = 2f; glow.intensity = 0.8f;
            WeaponTrail tr = WeaponTrail.Of(weapon);
            if (tr != null && !trailSwapped)
            {
                trailWas = tr.color; trailTexWas = tr.Tex; trailSwapped = true;
                tr.SetLook(new Color(1f, 0.55f, 0.2f), "trail_fire");
            }
        }

        /// <summary>Пламя вдоль всего лезвия: ~130 маленьких анимированных языков огня в секунду.</summary>
        ParticleSystem MakeFlames()
        {
            Texture2D tex = Resources.Load<Texture2D>("FX/brazier_fire");
            Shader add = Shader.Find("Legacy Shaders/Particles/Additive");
            if (tex == null || add == null) return null;
            GameObject g = new GameObject("FlameBladeFire");
            g.transform.SetParent(weapon.bladeRoot, false);
            g.transform.localPosition = Vector3.forward * (weapon.bladeLength * 0.55f);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = true; main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.11f, 0.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.55f, 0.8f), new Color(1f, 0.55f, 0.3f, 0.7f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // на взмахе пламя срывается и тянется шлейфом
            main.maxParticles = 160;
            main.gravityModifier = -0.35f;   // языки лижут вверх
            var em = ps.emission; em.rateOverTime = 80f;
            // по всей длине лезвия (от хабаки до острия), тонкой полосой
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.04f, 0.03f, weapon.bladeLength * 0.92f);   // языки по обе стороны лезвия
            var tsa = ps.textureSheetAnimation; tsa.enabled = true; tsa.numTilesX = 4; tsa.numTilesY = 2;
            tsa.animation = ParticleSystemAnimationType.WholeSheet;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));   // все 8 кадров за жизнь языка
            tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 0f);
            tsa.cycleCount = 1;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.35f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new GradientColorKey[] { new GradientColorKey(new Color(1f, 0.9f, 0.65f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0.15f), 0.45f), new GradientColorKey(new Color(0.85f, 0.18f, 0.05f), 1f) },
                       new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.12f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.12f; noise.frequency = 2.2f;
            ParticleSystemRenderer r = g.GetComponent<ParticleSystemRenderer>();
            Material m = new Material(add);
            m.mainTexture = tex;
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.55f, 0.5f, 0.45f, 0.5f));
            r.material = m;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.pivot = new Vector3(0f, 0.32f, 0f);   // основание языка — на лезвии (на кадрах огонь стоит у нижнего края)
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.sortingOrder = 1;
            ps.Play();
            return ps;
        }

        bool MakeRibbon()
        {
            Texture2D tex = Resources.Load<Texture2D>("FX/blade_fire");
            Shader add = Shader.Find("Legacy Shaders/Particles/Additive");
            if (tex == null || add == null) return false;
            ribbonGo = new GameObject("FlameBladeRibbon");
            ribbon = new Mesh(); ribbon.MarkDynamic();
            ribbon.vertices = rv; ribbon.uv = ruv;
            ribbon.triangles = new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5, 5, 6, 7, 8, 10, 9, 9, 10, 11, 12, 14, 13, 13, 14, 15 };
            ribbonGo.AddComponent<MeshFilter>().sharedMesh = ribbon;
            ribbonR = ribbonGo.AddComponent<MeshRenderer>();
            ribbonMat = new Material(add); ribbonMat.mainTexture = tex;
            if (ribbonMat.HasProperty("_TintColor")) ribbonMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            ribbonR.sharedMaterial = ribbonMat;
            ribbonR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ribbonR.receiveShadows = false;
            ribbonR.sortingOrder = 2;
            UpdateRibbon(1f);
            return true;
        }

        /// <summary>Лента вдоль лезвия: сердцевина листа ложится на сталь, огонь — в сторону «верха» экрана.</summary>
        void UpdateRibbon(float k)
        {
            if (ribbon == null || weapon == null || weapon.bladeRoot == null) return;
            Camera cam = Camera.main;
            Transform br = weapon.bladeRoot;
            float len = weapon.bladeLength;
            Vector3 a = br.position + br.forward * (len * 0.03f), b = br.position + br.forward * (len * 1.0f);
            Vector3 d = b - a; float core = d.magnitude; if (core < 1e-4f) return; d /= core;
            Vector3 view = cam != null ? (cam.transform.position - (a + b) * 0.5f).normalized : Vector3.back;
            Vector3 side = Vector3.Cross(d, view);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.up; side.Normalize();
            Vector3 up = cam != null ? cam.transform.up : Vector3.up;
            if (Vector3.Dot(side, up) < 0f) side = -side;   // огонь — всегда к верху экрана
            // кадр листа: по ширине сердцевина занимает CoreU0…CoreU1, по высоте лежит на CoreV снизу
            float w = core / (CoreU1 - CoreU0), h = w * (128f / 512f) * 1.35f * (0.85f + 0.15f * k);   // языки выше, как на ориентире владельца
            float ft = Time.unscaledTime * SheetFps;
            int f0 = (int)ft; float kf = ft - f0; kf = kf * kf * (3f - 2f * kf);
            // основной слой: кадр f и f+1 с наплывом
            Quad(0, a, d, side, w, h, f0 % SheetFrames, false, k * (1f - kf));
            Quad(4, a, d, side, w, h, (f0 + 1) % SheetFrames, false, k * kf);
            // зеркальный слой: на полфазы вперёд, ниже и слабее
            int g0 = f0 + 13;
            Quad(8, a, d, side, w, h * 0.85f, g0 % SheetFrames, true, 0.45f * k * (1f - kf));
            Quad(12, a, d, side, w, h * 0.85f, (g0 + 1) % SheetFrames, true, 0.45f * k * kf);
            ribbon.vertices = rv; ribbon.uv = ruv; ribbon.colors = rcol;
            ribbon.RecalculateBounds();
        }

        void Quad(int i, Vector3 a, Vector3 d, Vector3 side, float w, float h, int f, bool mirror, float alpha)
        {
            // зеркальная полоса — та же сердцевина на стали, только кадр отражён по длине (U)
            Vector3 left = a - d * (w * (mirror ? 1f - CoreU1 : CoreU0)), right = left + d * w;
            Vector3 down = -side * (h * CoreV), top = side * (h * (1f - CoreV));
            rv[i] = left + down; rv[i + 1] = right + down; rv[i + 2] = left + top; rv[i + 3] = right + top;
            float u0 = (f % SheetCols) / (float)SheetCols, v0 = 1f - (f / SheetCols + 1) / (float)SheetRows;
            float du = 1f / SheetCols, dv = 1f / SheetRows;
            float ua = mirror ? u0 + du : u0, ub = mirror ? u0 : u0 + du;
            ruv[i] = new Vector2(ua, v0); ruv[i + 1] = new Vector2(ub, v0); ruv[i + 2] = new Vector2(ua, v0 + dv); ruv[i + 3] = new Vector2(ub, v0 + dv);
            Color c = new Color(1f, 1f, 1f, alpha);
            rcol[i] = rcol[i + 1] = rcol[i + 2] = rcol[i + 3] = c;
        }

        /// <summary>Светящаяся нить вдоль лезвия (поверх огня): мягкая текстура, ширина в метрах, повёрнута к камере.</summary>
        LineRenderer MakeCore(string name, float width, Color c, int order)
        {
            Shader add = Shader.Find("Legacy Shaders/Particles/Additive");
            if (add == null) return null;
            GameObject g = new GameObject(name);
            g.transform.SetParent(weapon.bladeRoot, false);
            LineRenderer lr = g.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPosition(0, Vector3.forward * (weapon.bladeLength * 0.04f));
            lr.SetPosition(1, Vector3.forward * (weapon.bladeLength * 0.99f));
            lr.widthCurve = new AnimationCurve(new Keyframe(0f, width * 0.7f), new Keyframe(0.15f, width), new Keyframe(0.85f, width), new Keyframe(1f, width * 0.35f));
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            Material m = new Material(add);
            m.mainTexture = SoftSprites.Disc;
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            lr.material = m;
            lr.startColor = lr.endColor = c;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sortingOrder = order;
            return lr;
        }

        ParticleSystem MakeEmbers()
        {
            // искры ставятся на клинок в мировых координатах каждый кадр (LateUpdate): у bladeRoot бывает свой масштаб,
            // и дочерний объект со смещением в локальных единицах уезжал в сторону — искры шли «рядом», а не от меча
            GameObject g = new GameObject("FlameBladeEmbers");
            g.transform.SetPositionAndRotation(BladePoint(0.55f), weapon.bladeRoot.rotation);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = true; main.duration = 1f;
            main.useUnscaledTime = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.45f), new Color(1f, 0.45f, 0.12f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // на взмахе искры остаются в воздухе следом
            main.maxParticles = 120;
            main.gravityModifier = -0.15f;   // тянутся вверх, как от жара
            var em = ps.emission; em.rateOverTime = 45f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.02f, 0.02f, weapon.bladeLength * 0.8f);
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.4f, 0.15f), 1f) },
                       new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.25f; noise.frequency = 1.6f;
            ParticleSystemRenderer r = g.GetComponent<ParticleSystemRenderer>();
            Shader add = Shader.Find("Legacy Shaders/Particles/Additive");
            Material m = new Material(add != null ? add : Shader.Find("Sprites/Default"));
            m.mainTexture = SoftSprites.Disc;
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            r.material = m;
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.04f; r.lengthScale = 1.2f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        Vector3 BladePoint(float t) { Transform br = weapon.bladeRoot; return br.position + br.forward * (weapon.bladeLength * t); }

        void SetGlow(Color c)
        {
            for (int i = 0; i < steel.Length; i++)
            {
                Renderer r = steel[i];
                if (r == null) continue;
                r.GetPropertyBlock(mpb);   // кровь на клинке (Juice) живёт в том же блоке — читаем и дописываем
                mpb.SetColor("_EmissionColor", c);
                r.SetPropertyBlock(mpb);
            }
        }

        void Clear()
        {
            if (!on) return;
            on = false;
            if (mpb != null) SetGlow(Color.black);
            steel = new Renderer[0];
            if (flames != null)
            {
                flames.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                flames.transform.SetParent(null, true);
                Destroy(flames.gameObject, 0.6f);
            }
            flames = null;
            if (embers != null)
            {
                // искры не обрываются — дотлевают
                embers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                embers.transform.SetParent(null, true);
                Destroy(embers.gameObject, 1f);
            }
            embers = null;
            if (glow != null) Destroy(glow.gameObject);
            glow = null;
            if (ribbonGo != null) Destroy(ribbonGo);
            if (ribbon != null) Destroy(ribbon);
            if (ribbonMat != null) Destroy(ribbonMat);
            ribbonGo = null; ribbon = null; ribbonMat = null; ribbonR = null;
            if (coreOuter != null) Destroy(coreOuter.gameObject);
            if (coreInner != null) Destroy(coreInner.gameObject);
            coreOuter = coreInner = null;
            WeaponTrail tr = WeaponTrail.Of(weapon);
            if (tr != null && trailSwapped) tr.SetLook(trailWas, trailTexWas);
            trailSwapped = false;
            until = 0f;
        }

        void Update()
        {
            if (!on) return;
            float left = until - Time.unscaledTime;
            if (left <= 0f) { Clear(); return; }
            float k = Mathf.Clamp01(left / 1.5f);   // к концу накал спадает
            float t = Time.unscaledTime;
            float flick = 0.8f + 0.2f * Mathf.PerlinNoise(t * 7f, 0.3f) + 0.08f * Mathf.Sin(t * 23f);
            SetGlow(Hot * (0.4f * k * flick));   // сталь лишь накалена — горит пламя, а не «оранжевая палка»
            if (coreOuter != null) { Color c = new Color(1f, 0.45f, 0.1f, 0.55f * k * flick); coreOuter.startColor = coreOuter.endColor = c; }
            if (coreInner != null) { Color c = new Color(1f, 0.95f, 0.75f, k * (0.85f + 0.15f * flick)); coreInner.startColor = coreInner.endColor = c; }
            if (embers != null) { var em = embers.emission; em.rateOverTime = 45f * k; }
            if (flames != null) { var fe = flames.emission; fe.rateOverTime = 80f * k; }
            if (glow != null) glow.intensity = 0.8f * k * flick;
        }

        void LateUpdate()
        {
            if (!on || weapon == null || weapon.bladeRoot == null) return;
            // после анимации кадра: лента, искры и свет — точно на клинке
            if (embers != null) embers.transform.SetPositionAndRotation(BladePoint(0.55f), weapon.bladeRoot.rotation);
            if (glow != null) glow.transform.position = BladePoint(0.6f);
            if (ribbon != null) UpdateRibbon(Mathf.Clamp01((until - Time.unscaledTime) / 1.5f));
        }

        void OnDestroy() { Clear(); }
    }
}
