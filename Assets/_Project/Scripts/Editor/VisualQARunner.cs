#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Captures the ACTUAL Unity IMGUI output in Play Mode (not an art-only
    /// mock-up), using the same menu component, assets and game camera as APK.
    /// If a CI runner has no framebuffer, screenshot verification fails loudly.
    /// </summary>
    [InitializeOnLoad]
    public static class VisualQARunner
    {
        const string Key = "BladePath.VisualQA.Active";
        const string StartedKey = "BladePath.VisualQA.Started";
        const double TimeoutMinutes = 18;
        static bool hooked;
        public static bool Finished;
        public static int Failures;

        static VisualQARunner() { if (SessionState.GetBool(Key, false)) Hook(); }

        public static void Run()
        {
            try
            {
                Directory.CreateDirectory(OutDir);
                File.WriteAllText(Path.Combine(OutDir, "status.txt"), "STARTED: Unity Play Mode visual QA\n");
                MainMenuBuilder.Build();
                EditorSceneManager.OpenScene(MainMenuBuilder.ScenePath, OpenSceneMode.Single);
                Finished = false;
                Failures = 0;
                SessionState.SetBool(Key, true);
                SessionState.SetString(StartedKey, DateTime.UtcNow.Ticks.ToString());
                Hook();
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogError("VISUAL_QA_SETUP_FAILURE " + e);
                Fail("Setup: " + e);
                SessionState.SetBool(Key, false);
                EditorApplication.Exit(2);
            }
        }

        static string OutDir { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "../build/visual-runtime")); } }

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Watch;
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
            // ScreenCapture includes IMGUI; Camera.Render / RenderTexture does NOT.
            // Open the Game view when possible; in batchmode this can be absent.
            try
            {
                Type t = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                if (t != null)
                {
                    EditorWindow w = EditorWindow.GetWindow(t, false, "Game", false);
                    if (w != null) { w.position = new Rect(0, 0, 1824, 862); w.Focus(); }
                }
            }
            catch (Exception e) { Debug.LogWarning("VISUAL_QA_GAMEVIEW " + e.Message); }
            Screen.SetResolution(1824, 862, false);
            new GameObject("Visual QA Screenshot Driver").AddComponent<VisualScreenshotDriver>();
        }

        static void Watch()
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (Finished)
            {
                SessionState.SetBool(Key, false);
                Debug.Log("VISUAL_QA_DONE failures=" + Failures);
                EditorApplication.Exit(Failures == 0 ? 0 : 2);
                return;
            }
            long ticks;
            if (long.TryParse(SessionState.GetString(StartedKey, ""), out ticks) &&
                (DateTime.UtcNow - new DateTime(ticks)).TotalMinutes > TimeoutMinutes)
            {
                Fail("Timeout waiting for game frames");
                Finished = true;
            }
        }

        internal static void Fail(string detail)
        {
            Failures++;
            try
            {
                Directory.CreateDirectory(OutDir);
                File.AppendAllText(Path.Combine(OutDir, "status.txt"), "FAIL: " + detail + "\n");
            }
            catch { }
            Debug.LogError("VISUAL_QA_FAIL " + detail);
        }

        internal static void Info(string detail)
        {
            try { File.AppendAllText(Path.Combine(OutDir, "status.txt"), detail + "\n"); }
            catch { }
            Debug.Log("VISUAL_QA " + detail);
        }

        internal static string DirectoryPath { get { return OutDir; } }
    }

}
#endif
