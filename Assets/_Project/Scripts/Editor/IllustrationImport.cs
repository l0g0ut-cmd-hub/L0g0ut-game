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
        public override uint GetVersion() { return 3; }

        static bool IsIllustration(string path)
        {
            if (!path.StartsWith("Assets/_Project/Resources/UI/")) return false;
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name.StartsWith("place_")) return true;
            if (name.StartsWith("fx_ui_") || name == "fx_campfire" || name == "fx_mist" || name == "map_daytime") return true;   // листы кадров — без растяжения до степени двойки (кадры не мылятся)
            if (name.StartsWith("event_"))
            {
                int n;
                return int.TryParse(name.Substring(6), out n) && n >= 12;
            }
            return false;
        }

        /// <summary>Длинные записи (музыка, подложки карты, дождь) — потоком с диска, а не распакованными в память
        /// (2 минуты стерео — это ~20 МБ ОЗУ). Короткие удары и шаги — как по умолчанию, без задержки.</summary>
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/_Project/Resources/Audio/")) return;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (!(name.StartsWith("music_") || name.StartsWith("amb_") || name.EndsWith("_loop"))) return;
            AudioImporter ai = (AudioImporter)assetImporter;
            AudioImporterSampleSettings st = ai.defaultSampleSettings;
            st.loadType = UnityEngine.AudioClipLoadType.Streaming;
            st.compressionFormat = UnityEngine.AudioCompressionFormat.Vorbis;
            st.quality = 0.6f;
            ai.defaultSampleSettings = st;
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
