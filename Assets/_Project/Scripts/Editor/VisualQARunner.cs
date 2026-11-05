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

    public sealed class VisualScreenshotDriver : MonoBehaviour
    {
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        MainMenuController menu;

        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(2f);
            menu = FindFirstObjectByType<MainMenuController>();
            if (menu == null)
            {
                VisualQARunner.Fail("MainMenuController not found in running scene");
                VisualQARunner.Finished = true;
                yield break;
            }
            // Video frame may differ on Linux (codec availability). Capture menu
            // nonetheless; source-video pixels remain untouched by QA harness.
            yield return Capture("main_menu");
            for (int difficulty = 0; difficulty < 3; difficulty++)
            {
                for (int fog = 0; fog < 2; fog++)
                {
                    Set("newJourneyStep", 1);
                    Set("chosenDifficulty", difficulty);
                    JourneyChoice.FogEnabled = fog == 1;
                    yield return Capture("difficulty_" + difficulty + "_fog_" + fog);
                }
            }
            for (int hero = 0; hero < 3; hero++)
            {
                Set("newJourneyStep", 2);
                Set("chosenHero", hero);
                yield return Capture("traveller_" + hero);
            }
            Set("newJourneyStep", 0);
            VisualQARunner.Info("END screenshots=10 failures=" + VisualQARunner.Failures);
            VisualQARunner.Finished = true;
        }

        void Set(string field, object value)
        {
            FieldInfo f = typeof(MainMenuController).GetField(field, Private);
            if (f == null) { VisualQARunner.Fail("Missing field: " + field); return; }
            f.SetValue(menu, value);
        }

        IEnumerator Capture(string name)
        {
            // Render 3 frames after state change to let OnGUI draw the new state.
            for (int i=0; i<3; i++) yield return new WaitForEndOfFrame();
            string path = Path.Combine(VisualQARunner.DirectoryPath, name + ".png");
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 8f;
            while ((!File.Exists(path) || new FileInfo(path).Length < 8192) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!File.Exists(path) || new FileInfo(path).Length < 8192)
            {
                VisualQARunner.Fail("Missing/blank screenshot: " + name + "; graphics=" + SystemInfo.graphicsDeviceType);
                yield break;
            }
            // A valid PNG can still be totally black. Check sampled image pixels.
            byte[] bytes = File.ReadAllBytes(path);
            Texture2D readback = new Texture2D(2, 2, TextureFormat.RGB24, false);
            bool valid = readback.LoadImage(bytes, false);
            if (!valid)
            {
                VisualQARunner.Fail("Invalid PNG: " + name);
                Destroy(readback);
                yield break;
            }
            Color baseColor = readback.GetPixel(0, 0);
            bool diverse = false;
            for (int y = 1; y <= 4 && !diverse; y++)
                for (int x = 1; x <= 4; x++)
                {
                    Color c = readback.GetPixel(readback.width*x/5, readback.height*y/5);
                    if (Mathf.Abs(c.r-baseColor.r)+Mathf.Abs(c.g-baseColor.g)+Mathf.Abs(c.b-baseColor.b) > .12f)
                    { diverse = true; break; }
                }
            if (!diverse) VisualQARunner.Fail("Screenshot is a flat/black frame: " + name);
            VisualQARunner.Info("SHOT " + name + " " + readback.width + "x" + readback.height +
                " bytes=" + bytes.Length + " diverse=" + diverse + " graphics=" + SystemInfo.graphicsDeviceType);
            Destroy(readback);
        }
    }
}
#endif