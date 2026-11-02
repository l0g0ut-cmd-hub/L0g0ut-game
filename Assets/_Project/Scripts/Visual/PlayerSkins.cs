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

        void Awake()
        {
            int index = PlayerPrefs.GetInt("journey_started", 0) == 1
                ? JourneyChoice.Hero : 2; // keep the existing Hero3 appearance on legacy saves
            Apply(index);
        }

        public void Apply(int index)
        {
            if (models == null || models.Length == 0) return;
            index = Mathf.Clamp(index, 0, models.Length - 1);
            for (int i = 0; i < models.Length; i++)
                if (models[i] != null) models[i].SetActive(i == index);
            Active = index;
            if (models[index] != null)
            {
                HeroRigDriver rig = models[index].GetComponent<HeroRigDriver>();
                if (rig != null) rig.ApplyStanceOpen();
            }
        }
    }
}
