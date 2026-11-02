using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Pooled single-particle flashes for sparks, parries and shockwaves.
    /// Previously each glint created a GameObject, ParticleSystem, Gradient,
    /// curves and renderer, then destroyed everything a fraction of a second later.
    /// This version preserves the visual envelope while reusing renderers.
    /// The pool is scene owned and is discarded when the arena unloads.
    /// </summary>
    public sealed class CinematicFlashPool : MonoBehaviour
    {
        const int MaxEffects = 30;
        readonly List<Entry> entries = new List<Entry>(MaxEffects);
        static AnimationCurve growCurve;
        static AnimationCurve flashCurve;
        static Gradient fadeGradient;

        sealed class Entry
        {
            public GameObject gameObject;
            public ParticleSystem particle;
            public ParticleSystemRenderer renderer;
            public float releaseAt;
            public bool busy;
        }

        static void EnsureCurves()
        {
            if (growCurve != null) return;
            growCurve = new AnimationCurve(
                new Keyframe(0f, 0.15f, 0f, 4f), new Keyframe(1f, 1f, 0f, 0f));
            flashCurve = new AnimationCurve(
                new Keyframe(0f, 0.55f, 0f, 5f), new Keyframe(0.25f, 1f),
                new Keyframe(1f, 0.75f));
            fadeGradient = new Gradient();
            fadeGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(0.85f, 0.3f),
                        new GradientAlphaKey(0f, 1f) });
        }

        Entry Get()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                Entry item = entries[i];
                if (!item.busy && item.particle != null) return item;
            }
            if (entries.Count >= MaxEffects) return null;

            GameObject obj = new GameObject("FlashVFX_Pooled");
            obj.transform.SetParent(transform, false);
            ParticleSystem ps = obj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.05f;
            main.startSpeed = 0f;
            main.maxParticles = 1;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.stopAction = ParticleSystemStopAction.None;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;

            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, flashCurve);

            var lifetimeColor = ps.colorOverLifetime;
            lifetimeColor.enabled = true;
            lifetimeColor.color = fadeGradient;

            ParticleSystemRenderer renderer = obj.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = -10f;
            Entry entry = new Entry { gameObject = obj, particle = ps, renderer = renderer };
            entries.Add(entry);
            return entry;
        }

        public void Emit(Vector3 position, float size, Color tint, float life,
                         Material material, float rotation, bool grow)
        {
            if (material == null || size <= 0f) return;
            EnsureCurves();
            Entry entry = Get();
            if (entry == null) return;

            entry.busy = true;
            entry.gameObject.SetActive(true);
            entry.gameObject.transform.position = position;
            entry.renderer.sharedMaterial = material;

            ParticleSystem ps = entry.particle;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = Mathf.Max(0.035f, life);
            main.startSize = size;
            main.startRotation = rotation * Mathf.Deg2Rad;
            main.startColor = tint;

            var sz = ps.sizeOverLifetime;
            sz.size = new ParticleSystem.MinMaxCurve(1f, grow ? growCurve : flashCurve);
            entry.releaseAt = Time.unscaledTime + Mathf.Max(0.035f, life) + 0.04f;
            ps.Play(true);
            ps.Emit(1);
        }

        void Update()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry item = entries[i];
                if (!item.busy || now < item.releaseAt) continue;
                if (item.particle != null)
                    item.particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                item.busy = false;
                if (item.gameObject != null) item.gameObject.SetActive(false);
            }
        }
    }
}
