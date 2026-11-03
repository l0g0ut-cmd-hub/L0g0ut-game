#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Автотест боя в CI без сборки APK (workflow qa.yml): game-ci build с --manual-exit вызывает Run(),
    /// сцена боя собирается как для APK, редактор входит в Play Mode, CombatQA гоняет проверки,
    /// затем редактор закрывается сам. Результат — build/qa (qa.txt, кадры) → ветка qa-results.
    /// Состояние переживает перезагрузку домена при входе в Play Mode через SessionState.
    /// </summary>
    [InitializeOnLoad]
    public static class QARunner
    {
        const string Key = "BladePathQA", StartKey = "BladePathQA.start";
        const double TimeoutMin = 25.0;
        static bool hooked;

        static QARunner() { if (SessionState.GetBool(Key, false)) Hook(); }

        public static void Run()
        {
            try
            {
                Debug.Log("QA: сборка сцен");
                MainMenuBuilder.Build();
                PrototypeSceneBuilder.Build();
                EditorSceneManager.OpenScene(PrototypeSceneBuilder.ScenePath, OpenSceneMode.Single);
                SessionState.SetBool(Key, true);
                SessionState.SetString(StartKey, DateTime.UtcNow.Ticks.ToString());
                Hook();
                Debug.Log("QA: вход в Play Mode");
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogError("QA: исключение до Play Mode: " + e);
                Write("QA|setup|исключение " + e.Message.Replace('\n', ' ') + "|FAIL\n");
                EditorApplication.Exit(0);
            }
        }

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Watch;
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            Debug.Log("QA: Play Mode, запуск CombatQA");
            CombatQA.OutDir = OutDir();
            new GameObject("CombatQA").AddComponent<CombatQA>();
        }

        static void Watch()
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (CombatQA.Finished)
            {
                SessionState.SetBool(Key, false);
                Debug.Log("QA: готово, провалов " + CombatQA.Fails);
                Debug.Log("Build succeeded!");
                EditorApplication.Exit(0);
                return;
            }
            long ticks;
            if (long.TryParse(SessionState.GetString(StartKey, ""), out ticks) && (DateTime.UtcNow - new DateTime(ticks)).TotalMinutes > TimeoutMin)
            {
                SessionState.SetBool(Key, false);
                Debug.LogError("QA: тайм-аут " + TimeoutMin + " мин");
                Write("QA|timeout|тест не закончился за " + TimeoutMin + " мин|FAIL\n");
                EditorApplication.Exit(0);
            }
        }

        static string OutDir() { return Path.GetFullPath(Path.Combine(Application.dataPath, "../build/qa")); }

        static void Write(string s)
        {
            try { Directory.CreateDirectory(OutDir()); File.AppendAllText(Path.Combine(OutDir(), "qa.txt"), s); } catch { }
        }
    }
}
#endif
