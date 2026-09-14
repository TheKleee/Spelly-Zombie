using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SpellyZombie
{
    /// The picture of a map the lobby shows beside its MAP row: taken from
    /// the Scene view exactly as it is framed, with the game camera's own
    /// look, saved under the scene's name. Listing it on the Lobby's
    /// Collection Manager is the other item in the same menu.
    public static class MapPictureTools
    {
        public const string Folder = "Assets/_Game/Art/2D/Map Pictures";
        const int Width = 1280, Height = 720;

        [MenuItem("Spelly Zombie/Maps/Take Map Picture (as the Scene view is framed)")]
        static void Take()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null || view.camera == null)
            {
                Debug.LogWarning("[SpellyZombie] Open a Scene view and frame the map first.");
                return;
            }
            string scene = EditorSceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(scene))
            {
                Debug.LogWarning("[SpellyZombie] Save the scene first - the picture takes its name.");
                return;
            }

            var go = new GameObject("~MapPictureCamera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            // the scene's own camera, active or not (a map keeps it inactive in play)
            Camera main = null;
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c != null && c.GetComponentInParent<SimpleFPSController>() == null
                    && (main == null || c.CompareTag("MainCamera"))) main = c;
            if (main != null)
            {
                cam.CopyFrom(main); // the game's look: sky, fog, layers, clip planes
                cam.GetUniversalAdditionalCameraData().renderPostProcessing =
                    main.GetUniversalAdditionalCameraData().renderPostProcessing;
            }
            var eye = view.camera.transform;
            cam.transform.SetPositionAndRotation(eye.position, eye.rotation);
            cam.fieldOfView = view.camera.fieldOfView;
            cam.orthographic = view.camera.orthographic;
            cam.orthographicSize = view.camera.orthographicSize;

            var rt = new RenderTexture(Width, Height, 24);
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                Directory.CreateDirectory(Folder);
                string path = $"{Folder}/{scene}.png";
                File.WriteAllBytes(path, tex.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                Debug.Log($"[SpellyZombie] Map picture saved: {path}. Open the Lobby, run " +
                          "Spelly Zombie/Maps/Add Missing Map Pictures To Collection Manager, save the Lobby.");
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(go);
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
        }
    }
}
