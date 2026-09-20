using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpellyZombie
{
    /// Writes every game controller's clips with their state names to
    /// StreamingAssets (AnimNames): on entering Play and before a build. The
    /// file changes only when a controller does.
    public class AnimNamesMirror : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => Write();

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.ExitingEditMode) Write();
            };
        }

        static void Write()
        {
            var paths = new SortedSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { "Assets/_Game" }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            // the players' moves, wherever their controller lives
            foreach (var guid in AssetDatabase.FindAssets("t:CharacterLibrary"))
            {
                var lib = AssetDatabase.LoadAssetAtPath<CharacterLibrary>(AssetDatabase.GUIDToAssetPath(guid));
                if (lib != null && lib.AnimController is AnimatorController ac) paths.Add(AssetDatabase.GetAssetPath(ac));
            }

            var table = new AnimNames.Table();
            foreach (var path in paths)
            {
                var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (ac == null) continue;
                var states = new Dictionary<AnimationClip, string>();
                foreach (var layer in ac.layers) Walk(layer.stateMachine, states);
                var entry = new AnimNames.Controller { name = ac.name };
                foreach (var clip in ac.animationClips)
                    entry.clips.Add(new AnimNames.Clip
                    {
                        name = clip == null ? "" : states.TryGetValue(clip, out var n) ? n : clip.name,
                        length = clip != null ? clip.length : 0f,
                    });
                table.controllers.Add(entry);
            }

            string json = JsonUtility.ToJson(table, true);
            string dir = Path.Combine(Application.dataPath, "StreamingAssets");
            string file = Path.Combine(dir, AnimNames.FileName);
            if (File.Exists(file) && File.ReadAllText(file) == json) return;
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, json);
            Debug.Log($"[SpellyZombie] animation names written: {table.controllers.Count} controllers -> {AnimNames.FileName}");
        }

        static void Walk(AnimatorStateMachine sm, Dictionary<AnimationClip, string> states)
        {
            if (sm == null) return;
            foreach (var cs in sm.states)
                if (cs.state != null) Name(cs.state.motion, cs.state.name, states);
            foreach (var sub in sm.stateMachines) Walk(sub.stateMachine, states);
        }

        /// A clip takes the first state that plays it; a blend tree's clips its state's name and their place in it.
        static void Name(Motion m, string name, Dictionary<AnimationClip, string> states)
        {
            if (m is AnimationClip clip)
            {
                if (!states.ContainsKey(clip)) states[clip] = name;
                return;
            }
            if (m is BlendTree tree)
            {
                var kids = tree.children;
                for (int i = 0; i < kids.Length; i++)
                    Name(kids[i].motion, kids.Length > 1 ? name + " " + (i + 1) : name, states);
            }
        }
    }
}
