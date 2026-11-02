using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// The three existing, fully rigged FBX heroes live in the built scene.
    /// Selects one before their Start/IK runs; enemy model switching is untouched.
    /// Dzin: Ronin. Akiko: red-armoured Samurai2 prototype. Ren: Hero3.
    /// Akiko currently uses the available red-armoured base FBX; portrait and
    /// combat specialization are distinct, and the rig is ready for a female FBX.
    /// </summary>
    public sealed class PlayerSkins : MonoBehaviour
    {
        public GameObject[] models;
        public int Active { get; private set; }

        void Start()
        {
            // The editor builder adds this component before assigning models.
            // Awake runs immediately on AddComponent in the editor, when its
            // model array is still empty. Start applies the saved choice after
            // deserialization and works in the Android player as well.
            int index = PlayerPrefs.GetInt("journey_started", 0) == 1
                ? JourneyChoice.Hero : 2; // preserve legacy Hero3 appearance
            Apply(index);
        }

        public void Apply(int index)
        {
            if (models == null || models.Length == 0) return;
            index = Mathf.Clamp(index, 0, models.Length - 1);
            GameObject selected = models[index];
            if (selected == null)
                for (int i = 0; i < models.Length; i++)
                    if (models[i] != null) { selected = models[i]; break; }
            // Multiple entries may reference the same fallback model. Select
            // by object reference instead of overwriting its active state.
            for (int i = 0; i < models.Length; i++)
                if (models[i] != null) models[i].SetActive(models[i] == selected);
            Active = index;
            if (models[index] != null)
            {
                HeroRigDriver rig = models[index].GetComponent<HeroRigDriver>();
                if (rig != null) rig.ApplyStanceOpen();
            }
        }
    }
}
