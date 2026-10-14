using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Skyfall.EditorTools
{
    /// Creates the game scene (one GameObject with the Game component) and puts it first in the build list.
    /// Run from the menu: Skyfall > Set Up Game Scene.
    public static class SkyfallSetup
    {
        public const string ScenePath = "Assets/Skyfall/Scenes/Skyfall.unity";

        [MenuItem("Skyfall/Set Up Game Scene")]
        public static void SetupScene()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Skyfall/Scenes"))
                AssetDatabase.CreateFolder("Assets/Skyfall", "Scenes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Game");
            go.AddComponent<Game>();

            // Saved with fog on so exponential-fog shader variants are kept in builds.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.0045f;
            RenderSettings.ambientMode = AmbientMode.Trilight;

            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("Skyfall: game scene created at " + ScenePath + " and added to the build settings.");
        }

        /// Opens the game scene if it exists (used when launching the editor from the command line).
        public static void OpenScene()
        {
            if (System.IO.File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
