#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Tools → Blade Path → Build Main Menu Scene.
    /// Собирает сцену меню из кода: камера + MainMenuController с постерами. Сам ролик лежит в
    /// Assets/StreamingAssets/menu_bg.mp4 и играется через VideoPlayer по URL.
    /// </summary>
    public static class MainMenuBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/MainMenu.unity";
        const string VideoPath = "Assets/StreamingAssets/menu_bg.mp4";
        const string PosterFirst = "Assets/_Project/Art/menu_poster_first.jpg";
        const string PosterLast = "Assets/_Project/Art/menu_poster_last.jpg";

        [MenuItem("Tools/Blade Path/Build Main Menu Scene")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Scenes")) AssetDatabase.CreateFolder("Assets/_Project", "Scenes");
            if (!File.Exists(VideoPath)) Debug.LogWarning("BladePath: нет " + VideoPath + " — меню покажет только постер");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject cam = new GameObject("Main Camera");
            cam.tag = "MainCamera";
            Camera c = cam.AddComponent<Camera>();
            cam.AddComponent<AudioListener>();
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = Color.black;

            GameObject go = new GameObject("MainMenu");
            MainMenuController menu = go.AddComponent<MainMenuController>();
            menu.posterFirst = AssetDatabase.LoadAssetAtPath<Texture2D>(PosterFirst);
            menu.posterLast = AssetDatabase.LoadAssetAtPath<Texture2D>(PosterLast);
            if (menu.posterFirst == null || menu.posterLast == null) Debug.LogWarning("BladePath: постеры меню не найдены в Assets/_Project/Art");

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("Blade Path: сцена меню собрана → " + ScenePath);
        }
    }
}
#endif
