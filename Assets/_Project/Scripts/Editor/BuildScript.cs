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
    /// выставляет настройки проигрывателя и собирает debug-APK (сцены: главное меню → бой).
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

                // Сцены создаются из кода, в репозитории их нет. Порядок: меню (индекс 0), потом бой.
                MainMenuBuilder.Build();
                PrototypeSceneBuilder.Build();
                PoseProbe.Run(PrototypeSceneBuilder.ScenePath);   // диагностика позы скелета в Unity (build/probe.txt → ветка apk-latest)
                string[] scenes = new string[] { MainMenuBuilder.ScenePath, PrototypeSceneBuilder.ScenePath };
                EditorBuildSettings.scenes = new EditorBuildSettingsScene[]
                {
                    new EditorBuildSettingsScene(scenes[0], true),
                    new EditorBuildSettingsScene(scenes[1], true),
                };
                ConfigurePlayer(name);

                BuildPlayerOptions opts = new BuildPlayerOptions();
                opts.scenes = scenes;
                opts.locationPathName = path;
                opts.target = BuildTarget.Android;
                opts.targetGroup = BuildTargetGroup.Android;
                // Релизная сборка: быстрее, без плашки Development Build и без консоли ошибок поверх игры.
                // Отладочная графика боя включается флагом CombatDebugVisualizer.forceEnableInRelease, если понадобится.
                opts.options = BuildOptions.None;
                // фототур экранов (Debug/QATour.cs) — только в тестовой сборке под эмулятор; в обычный APK не попадает
                if (name.IndexOf("VisualEmulator", StringComparison.OrdinalIgnoreCase) >= 0)
                    opts.extraScriptingDefines = new[] { "BLADEPATH_QATOUR" };

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

        static void ConfigurePlayer(string buildName)
        {
            PlayerSettings.companyName = "BladePath";
            PlayerSettings.productName = "Путь клинка";
            PlayerSettings.bundleVersion = "0.2.0";
            PlayerSettings.Android.bundleVersionCode = 2;
            SetIcon();

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.bladepath.prototype");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            // Keep production APK ARM64-only. The opt-in emulator QA build
            // uses x86_64 so the standard Linux emulator can INSTALL and RUN it.
            bool emulatorQA = buildName.IndexOf("VisualEmulator", StringComparison.OrdinalIgnoreCase) >= 0;
            PlayerSettings.Android.targetArchitectures = emulatorQA
                ? AndroidArchitecture.X86_64 : AndroidArchitecture.ARM64;
            Debug.Log("BladePath Android ABI=" + (emulatorQA ? "x86_64 (QA only)" : "arm64-v8a (production)"));
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;

            // горизонтальная ориентация (ТЗ, раздел 4)
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            // окно сразу во весь экран и под вырезом камеры: иначе на старте Android сначала даёт окно
            // с системными полосами, через мгновение убирает их — ширина меняется, и меню с самураем
            // (оно центрируется по окну) съезжает на пару пикселей в первую секунду
            PlayerSettings.Android.startInFullscreen = true;
            PlayerSettings.Android.renderOutsideSafeArea = true;

            EditorUserBuildSettings.buildAppBundle = false;                 // APK, не AAB
            EditorUserBuildSettings.androidBuildType = AndroidBuildType.Release;   // подпись всё равно debug-ключом (без своего keystore)
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
            // скиннинг на GPU: две модели по 40–75 тыс. вершин на CPU телефона съедали кадры (38–54 FPS)
            SerializedProperty gs = so.FindProperty("gpuSkinning");
            if (gs != null) gs.boolValue = true;
            SerializedProperty md = so.FindProperty("meshDeformation");
            if (md != null) { md.intValue = 2; Debug.Log("BladePath: meshDeformation = GPU (batched)"); }   // 0 CPU, 1 GPU, 2 GPU batched
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Иконка приложения (tools/gen_icon.py → Assets/_Project/Art/icon.png).</summary>
        static void SetIcon()
        {
            const string p = "Assets/_Project/Art/icon.png";
            try
            {
                TextureImporter ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti != null && (ti.textureCompression != TextureImporterCompression.Uncompressed || ti.npotScale != TextureImporterNPOTScale.None))
                {
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.npotScale = TextureImporterNPOTScale.None;
                    ti.mipmapEnabled = false;
                    ti.SaveAndReimport();
                }
                Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (icon == null) { Debug.LogWarning("BladePath: иконка не найдена"); return; }
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new Texture2D[] { icon }, IconKind.Any);
            }
            catch (Exception e) { Debug.LogWarning("BladePath: иконка не выставлена: " + e.Message); }
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
