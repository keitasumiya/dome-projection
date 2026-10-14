using UnityEngine;

namespace DomeProjection
{
    public static class DomeProjectionBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartApplication()
        {
            if (Object.FindFirstObjectByType<DomeProjectionApp>() != null) return;

            var root = new GameObject("Dome Projection App");
            Object.DontDestroyOnLoad(root);
            root.AddComponent<DomeProjectionApp>();
        }
    }
}

