#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DomeProjection.Editor
{
    [InitializeOnLoad]
    internal static class DomeProjectionProjectSetup
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";

        static DomeProjectionProjectSetup()
        {
            EditorApplication.delayCall += EnsureProjectSettings;
        }

        private static void EnsureProjectSettings()
        {
            PlayerSettings.productName = "Dome Projection Player";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;

            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.All(scene => scene.path != MainScenePath))
            {
                scenes.Insert(0, new EditorBuildSettingsScene(MainScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                Debug.Log("Added Dome Projection Main scene to Build Settings.");
            }
        }
    }
}
#endif

