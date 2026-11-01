using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Reusable impact sparks. One ParticleSystem per concurrent impact rather than a
    /// newly allocated ParticleSystem, gradients, curves and GameObject on every clash.
    /// Lives under Juice (scene-owned); stale references are skipped after scene unload.
    /// </summary>
    public sealed class ImpactSparkPool : MonoBehaviour
    {
        const int MaxConcurrent = 18;
        static readonly Stack<ImpactSparkPool> free = new Stack<ImpactSparkPool>(MaxConcurrent);
        static int created;
        static Transform groundPlane;
        ParticleSystem particles;
        bool playing;

        public static void Emit(Vector3 pos, Vector3 normal, int count, float power, Material material, Transform parent)
        {
            if (material == null || count <= 0) return;
            ImpactSparkPool effect = null;
            while (free.Count > 0 && effect == null) effect = free.Pop();
            if (effect == null)
            {
                if (created >= MaxConcurrent) return;
                GameObject go = new GameObject("PooledImpactSparks");
                if (parent != null) go.transform.SetParent(parent, false);
                effect = go.AddComponent<ImpactSparkPool>();
                effect.Setup(material);
                created++;
            }
            else if (effect.particles == null) return;
            effect.gameObject.SetActive(true);
            effect.transform.position = pos;
            effect.transform.rotation = Quaternion.LookRotation(normal.sqrMagnitude > 1e-5f ? normal.normalized : Vector3.up);
            var main = effect.particles.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f * power, 8.5f * power);
            effect.playing = true;
            effect.particles.Clear(true);
            effect.particles.Play(true);
            effect.particles.Emit(Mathf.Clamp(count, 1, 32));
        }

        void Setup(Material material)
        {
            particles = gameObject.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = false;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.010f, 0.026f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 8.5f);
            main.startColor = Color.white;
            main.gravityModifier = 1.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Callback;
            main.maxParticles = 32;

            var emission = particles.emission;
            emission.rateOverTime = 0f;
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 55f;
            shape.radius = 0.02f;
            var limit = particles.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = 2.2f;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = true;
            var color = particles.colorOverLifetime;
            color.enabled = true;
            Gradient heat = new Gradient();
            heat.SetKeys(
                new[] {
                    new GradientColorKey(new Color(1f, 0.97f, 0.82f), 0f),
                    new GradientColorKey(new Color(1f, 0.78f, 0.35f), 0.18f),
                    new GradientColorKey(new Color(1f, 0.34f, 0.07f), 0.55f),
                    new GradientColorKey(new Color(0.34f, 0.035f, 0.015f), 1f)
                },
                new[] {
                    new GradientAlphaKey(0.95f, 0f),
                    new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(0.75f, 0.5f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = heat;
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.35f)));

            if (groundPlane == null)
            {
                GameObject floor = new GameObject("SparksCollisionPlane");
                groundPlane = floor.transform;
                groundPlane.position = new Vector3(0f, 0.01f, 0f);
                // Scene-owned: do not keep collision planes alive between arenas.
                if (Juice.Instance != null) groundPlane.SetParent(Juice.Instance.transform, true);
            }
            var collision = particles.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.Planes;
            collision.SetPlane(0, groundPlane);
            collision.bounce = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            collision.dampen = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
            collision.lifetimeLoss = 0.25f;
            collision.radiusScale = 0.3f;

            ParticleSystemRenderer rend = GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = material;
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.035f;
            rend.lengthScale = 2.2f;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
        }

        void OnParticleSystemStopped()
        {
            if (!playing) return;
            playing = false;
            gameObject.SetActive(false);
            free.Push(this);
        }

        void OnDestroy()
        {
            if (created > 0) created--;
        }
    }
}
