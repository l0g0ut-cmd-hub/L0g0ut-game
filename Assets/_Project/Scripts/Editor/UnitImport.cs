using UnityEditor;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Бойцы пошагового боя (Resources/Units/*.fbx): модели Meshy со своим скелетом и клипами из библиотеки Meshy
    /// (стойка, ходьба, удар/выстрел, получил удар, смерть, победа) — легаси-анимация, клипы играет Tactics по именам.
    /// </summary>
    public class UnitImport : AssetPostprocessor
    {
        public override uint GetVersion() { return 1; }
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith("Assets/_Project/Resources/Units/")) return;
            ModelImporter mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Legacy;
            mi.importAnimation = true;
            mi.generateAnimations = ModelImporterGenerateAnimations.GenerateAnimations;
            mi.isReadable = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;   // материал ставит Tactics (текстура Meshy рядом)
            mi.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
        }
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/_Project/Resources/Units/")) return;
            ((TextureImporter)assetImporter).maxTextureSize = 1024;
        }
    }
}
