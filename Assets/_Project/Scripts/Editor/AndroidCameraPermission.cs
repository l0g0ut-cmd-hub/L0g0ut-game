#if UNITY_ANDROID
using System.IO;
using UnityEditor.Android;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Редактор персонажей снимает лицо камерой (WebCamTexture): разрешение CAMERA — явно в манифест сборки
    /// (без него запрос доступа молча отклоняется), камера при этом не обязательна для установки.
    /// </summary>
    public class AndroidCameraPermission : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder { get { return 100; } }
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string m = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(m)) return;
            string x = File.ReadAllText(m);
            string add = "";
            if (!x.Contains("android.permission.CAMERA")) add += "\n    <uses-permission android:name=\"android.permission.CAMERA\" />";
            if (!x.Contains("android.hardware.camera\"")) add += "\n    <uses-feature android:name=\"android.hardware.camera\" android:required=\"false\" />";
            if (!x.Contains("android.hardware.camera.front")) add += "\n    <uses-feature android:name=\"android.hardware.camera.front\" android:required=\"false\" />";
            if (add.Length == 0) return;
            int i = x.IndexOf("<application");
            if (i < 0) return;
            File.WriteAllText(m, x.Insert(i, add.TrimStart('\n') + "\n    "));
        }
    }
}
#endif
