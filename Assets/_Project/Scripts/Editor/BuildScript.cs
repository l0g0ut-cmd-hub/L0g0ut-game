#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Сборка Android-APK без участия человека (для CI): собирает тестовую сцену этапа 1,
    /// выставляет настройки проигрывателя и собирает debug-APK.
    ///
    /// Вызывается через -executeMethod BladePath.EditorTools.BuildScript.BuildAndroid.
    /// GameCI передаёт -customBuildPath (полный путь к .apk) и -customBuildName.
    /// В лог пишется «Build succeeded!» — по этой строке GameCI подтверждает успех.
    /// </summary>
    public static class BuildScript
    {
        public static void BuildAndroid()
        {
            try
            {
                string name = GetArg("-customBuildName");
                if (string.IsNullOrEmpty(name)) name = "BladePath";
                string path = GetArg("-customBuildPath");
                if (string.IsNullOrEmpty(path)) path = "build/Android/" + name + ".apk";
                else if (!path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(path, name + ".apk");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));

                Debug.Log("BladePath: сборка APK → " + path);

                PrototypeSceneBuilder.Build();     // сцена создаётся из кода, в репозитории её нет
                ConfigurePlayer();

                BuildPlayerOptions opts = new BuildPlayerOptions();
                opts.scenes = new string[] { PrototypeSceneBuilder.ScenePath };
                opts.locationPathName = path;
                opts.target = BuildTarget.Android;
                opts.targetGroup = BuildTargetGroup.Android;
                // Development: Debug.isDebugBuild = true, иначе отладочная графика отключается
                opts.options = BuildOptions.Development;

                BuildReport report = BuildPipeline.BuildPlayer(opts);
                BuildSummary sum = report.summary;
                Debug.Log("BladePath: результат=" + sum.result + ", ошибок=" + sum.totalErrors + ", размер=" + sum.totalSize + " байт");

                if (sum.result == BuildResult.Succeeded)
                {
                    Debug.Log("Build succeeded!");
                    EditorApplication.Exit(0);
                }
                else
                {
                    Debug.LogError("BladePath: сборка не удалась: " + sum.result);
                    EditorApplication.Exit(2);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("BladePath: исключение в сборке: " + e);
                EditorApplication.Exit(1);
            }
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "BladePath";
            PlayerSettings.productName = "Blade Path";
            PlayerSettings.bundleVersion = "0.1.0";

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.bladepath.prototype");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;

            // горизонтальная ориентация (ТЗ, раздел 4)
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            EditorUserBuildSettings.buildAppBundle = false;                 // APK, не AAB
            EditorUserBuildSettings.androidBuildType = AndroidBuildType.Debug;
            PlayerSettings.Android.useCustomKeystore = false;               // стандартный debug-ключ: ставится с телефона без подписи

            ForceLegacyInputManager();
        }

        /// <summary>
        /// Этап 1 использует старый Input Manager и IMGUI-кнопки. Если проект создан с «новым» вводом,
        /// IMGUI без пакета Input System не получает касаний — переключаем явно.
        /// </summary>
        static void ForceLegacyInputManager()
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) { Debug.LogWarning("BladePath: ProjectSettings.asset не найден, ввод не переключён"); return; }
            SerializedObject so = new SerializedObject(assets[0]);
            SerializedProperty p = so.FindProperty("activeInputHandler");
            if (p == null) { Debug.LogWarning("BladePath: свойство activeInputHandler не найдено"); return; }
            p.intValue = 0; // 0 = Input Manager (Old), 1 = Input System Package, 2 = Both
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
#endif
