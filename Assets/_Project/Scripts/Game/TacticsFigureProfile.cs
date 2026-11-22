using UnityEngine;
using System.Collections.Generic;

namespace BladePath
{
    // Generated with the Android importer, the board camera angle and the game's material.
    public sealed class TacticsFigureProfile : ScriptableObject
    {
        public float scale;
        public Vector3 offset;
        public float stride;
        public float[] headingScale;
        public AnimationClip advance;

        public sealed class CombatClips
        {
            public readonly string idle, walk, attack, die, hit, win;

            public CombatClips(Animation anim)
            {
                var names = new List<string>();
                foreach (AnimationState state in anim) names.Add(state.name);
                string first = names.Count > 0 ? names[0] : null;
                die = Take(names, "dying", "dead", "death", "fall", "knock");
                hit = Take(names, "reaction", "hit");
                win = Take(names, "victory", "cheer", "howl", "idle1");
                idle = Take(names, "combat_stance", "dog_idle", "idle", "stance") ?? first;
                walk = Take(names, "walk", "canter", "run");
                attack = Take(names, "shot", "shoot", "slash", "thrust", "spell", "cast", "attack",
                    "combo", "jump", "bite", "strike", "swing", "punch", "kick", "fire");
            }

            static string Take(List<string> names, params string[] keys)
            {
                foreach (string key in keys)
                {
                    int index = names.FindIndex(name => name.Substring(name.LastIndexOf('|') + 1)
                        .IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0);
                    if (index < 0) continue;
                    string result = names[index];
                    names.RemoveAt(index);
                    return result;
                }
                return null;
            }
        }

        float HeadingFactor(float yaw)
        {
            float adjustment = 1f;
            if (headingScale != null && headingScale.Length > 1)
            {
                float index = Mathf.Repeat(yaw,360f) * headingScale.Length / 360f;
                int first = Mathf.FloorToInt(index) % headingScale.Length;
                adjustment = Mathf.Lerp(headingScale[first],headingScale[(first+1)%headingScale.Length],index-Mathf.Floor(index));
            }
            return adjustment;
        }

        public float StrideAt(float yaw)
        {
            return stride * HeadingFactor(yaw) / HeadingFactor(90f);
        }

        public void Fit(Transform mount, float yaw = 0f)
        {
            float adjustment = HeadingFactor(yaw);
            mount.localScale = Vector3.one * (scale * adjustment);
            mount.localPosition = offset * adjustment;
        }

        public static void Dress(GameObject figure, Material material)
        {
            foreach (Renderer renderer in figure.GetComponentsInChildren<Renderer>(true))
            {
                var skin = renderer as SkinnedMeshRenderer;
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;
                var materials = new Material[mesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.allowOcclusionWhenDynamic = false;
                if (skin != null)
                {
                    skin.updateWhenOffscreen = true;
                    skin.forceMatrixRecalculationPerRender = true;
                }
            }
        }
    }
}
