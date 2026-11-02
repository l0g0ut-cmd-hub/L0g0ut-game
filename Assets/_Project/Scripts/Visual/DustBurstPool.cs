using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// One reusable copy of the EXISTING soft arena dust/snow burst effect.
    /// This is intentionally not a new look: the same particle shape, velocity,
    /// palette, rotation and fade are retained. Only the allocation pattern changes.
    /// Owned by Juice's scene hierarchy; no static objects survive scene unload.
    /// </summary>
    public sealed class DustBurstPool : MonoBehaviour
    {
        ParticleSystem system;
        bool busy;

        public bool Busy { get { return busy; } }

        public static DustBurstPool Create(Transform owner, Material material)
        {
            if (material == null) return null;
            GameObject go = new GameObject("DustPuff_Pooled");
            go.transform.SetParent(owner, false);
            DustBurstPool result = go.AddComponent<DustBurstPool>();
            result.Setup(material);
            go.SetActive(false);
            return result;
        }

        void Setup(Material material)
        {
            system = gameObject.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.gravityModifier = -0.02f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Callback;
            main.maxParticles = 80;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var emission = system.emission;
            emission.rateOverTime = 0f;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 70f;
            shape.radius = 0.12f;

            var color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 1.3f)));

            ParticleSystemRenderer renderer = GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public void Burst(Vector3 point, Vector3 direction, int count, bool snow)
        {
            if (system == null) return;
            busy = true;
            gameObject.SetActive(true);
            transform.position = point;
            Vector3 aim = direction.sqrMagnitude > 0.0001f
                ? direction.normalized * 0.8f + Vector3.up * 0.6f
                : Vector3.up;
            transform.rotation = Quaternion.LookRotation(aim);

            var main = system.main;
            main.startColor = snow
                ? new Color(0.8f, 0.85f, 0.94f, 0.42f)
                : new Color(0.38f, 0.34f, 0.3f, 0.3f);
            system.Clear(true);
            system.Play(true);
            system.Emit(Mathf.Clamp(count, 1, 64));
        }

        void OnParticleSystemStopped()
        {
            if (!busy) return;
            busy = false;
            gameObject.SetActive(false);
        }
    }
}
