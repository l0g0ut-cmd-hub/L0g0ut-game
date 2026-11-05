#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BladePath.EditorTools
{
    public sealed class VisualScreenshotDriver : MonoBehaviour
    {
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        MainMenuController menu;

        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(2f);
            menu = FindFirstObjectByType<MainMenuController>();
            // StudioSplash obscures/blocks the menu for 8-10 seconds. QA must
            // bypass it or ten identical splash screenshots look like success.
            StudioSplash splash = FindFirstObjectByType<StudioSplash>();
            if (splash != null) Destroy(splash.gameObject);
            if (menu != null) menu.enabled = true;
            yield return new WaitForSecondsRealtime(2f);
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
