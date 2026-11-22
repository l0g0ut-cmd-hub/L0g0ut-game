using UnityEngine;

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

        public void Fit(Transform mount, float yaw = 0f)
        {
            float adjustment = 1f;
            if (headingScale != null && headingScale.Length > 1)
            {
                float index = Mathf.Repeat(yaw,360f) * headingScale.Length / 360f;
                int first = Mathf.FloorToInt(index) % headingScale.Length;
                adjustment = Mathf.Lerp(headingScale[first],headingScale[(first+1)%headingScale.Length],index-Mathf.Floor(index));
            }
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
