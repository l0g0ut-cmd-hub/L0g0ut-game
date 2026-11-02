using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Runtime switching among three genuine in-project FBX characters.
    /// All are prepared in the Unity editor build; only the chosen hero is
    /// active, avoiding duplicate Animator/IK/cloth work during combat.
    /// </summary>
    public sealed class HeroSkins : MonoBehaviour
    {
        public GameObject[] models = new GameObject[0];
        public int Active { get; private set; }

        void Awake() { Apply(HeroRoster.Selected); }

        public void Apply(int index)
        {
            if (models == null || models.Length == 0) return;
            index = Mathf.Clamp(index, 0, models.Length - 1);
            if (models[index] == null)
            {
                for (int i = 0; i < models.Length; i++)
                    if (models[i] != null) { index = i; break; }
            }
            for (int i = 0; i < models.Length; i++)
                if (models[i] != null) models[i].SetActive(i == index);
            Active = index;
            HeroRigDriver driver = models[index] != null
                ? models[index].GetComponent<HeroRigDriver>() : null;
            if (driver != null) driver.ApplyStanceOpen();
            FighterController fighter = GetComponent<FighterController>();
            if (fighter != null) fighter.displayName = HeroRoster.Names[index];
        }
    }
}
