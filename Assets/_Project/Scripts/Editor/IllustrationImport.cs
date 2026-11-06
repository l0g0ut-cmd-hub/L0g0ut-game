using UnityEditor;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Вертикальные иллюстрации окон (места на карте, новые события) — не степени двойки (504×760, 758×1018).
    /// По умолчанию Unity растягивает такие текстуры до ближайшей степени двойки (504×760 → 512×512),
    /// и картинка в окне выходила сплющенной. Здесь они импортируются в своём настоящем размере.
    /// </summary>
    public class IllustrationImport : AssetPostprocessor
    {
        public override uint GetVersion() { return 1; }

        static bool IsIllustration(string path)
        {
            if (!path.StartsWith("Assets/_Project/Resources/UI/")) return false;
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name.StartsWith("place_")) return true;
            if (name.StartsWith("event_"))
            {
                int n;
                return int.TryParse(name.Substring(6), out n) && n >= 12;
            }
            return false;
        }

        void OnPreprocessTexture()
        {
            if (!IsIllustration(assetPath)) return;
            TextureImporter ti = (TextureImporter)assetImporter;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.mipmapEnabled = false;
            ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        }
    }
}
