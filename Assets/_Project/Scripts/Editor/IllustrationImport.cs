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
        public override uint GetVersion() { return 5; }

        static bool IsIllustration(string path)
        {
            if (!path.StartsWith("Assets/_Project/Resources/UI/")) return false;
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name.StartsWith("place_")) return true;
            // картины в окнах карты (2026-11-09): враги и NPC 683×1024 уходили в 512×1024 — окно строилось по этим размерам,
            // и картина выходила сплюснутой по ширине на четверть; фоны привала, игорного дома, таверны, конца области — тоже
            if (name.StartsWith("enemy_full_") || name.StartsWith("npc_") || name.StartsWith("region_clear_") || name == "camp" || name == "dice_bg" || name == "tavern") return true;
            if (name.StartsWith("fx_ui_") || name == "fx_campfire" || name == "fx_mist" || name == "map_daytime" || name.StartsWith("shop_") || name == "items4" || name == "items5" || name == "items6") return true;   // листы кадров — без растяжения до степени двойки (кадры не мылятся)
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
            // широкие карты областей (2610…2815 × 1024): без растяжения до степени двойки и без ужатия до 2048 —
            // иначе картина мылится по горизонтали (MapGeom)
            if (assetPath.StartsWith("Assets/_Project/Resources/UI/map_wide_"))
            {
                TextureImporter tw = (TextureImporter)assetImporter;
                tw.npotScale = TextureImporterNPOTScale.None;
                tw.mipmapEnabled = false;
                tw.wrapMode = UnityEngine.TextureWrapMode.Clamp;
                tw.maxTextureSize = 4096;
                return;
            }
            if (!IsIllustration(assetPath)) return;
            TextureImporter ti = (TextureImporter)assetImporter;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.mipmapEnabled = false;
            ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        }
    }
}
