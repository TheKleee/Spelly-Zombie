using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// A stand-in for FxLibrary > Pot Beacon until the real one is made: soft
    /// gold streaks rising from the pot in a column about 22 m tall, so it shows
    /// over the roofs. Swap the prefab or the slot any time.
    public static class PotBeaconMaker
    {
        const string PrefabPath = "Assets/_Game/Prefabs/FX_PotBeacon.prefab";
        const string GlowPath = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/cfxr proc glow soft add.mat";

        [MenuItem("Spelly Zombie/Particles/Make Placeholder Pot Beacon")]
        static void Make()
        {
            var lib = FxLibrary.I;
            if (lib == null) { Debug.LogError("[SpellyZombie] No FxLibrary asset in Resources."); return; }
            var glow = AssetDatabase.LoadAssetAtPath<Material>(GlowPath);
            if (glow == null) { Debug.LogError("[SpellyZombie] Placeholder Pot Beacon: missing " + GlowPath); return; }
            if (lib.PotBeacon != null && !EditorUtility.DisplayDialog("Pot Beacon",
                    "The slot already holds " + lib.PotBeacon.name + ". Replace it with the placeholder?", "Replace", "Keep"))
                return;

            var go = new GameObject("FX_PotBeacon");
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = 2.5f;
            main.startSpeed = 9f;
            main.startSize = new ParticleSystem.MinMaxCurve(1f, 1.6f);
            main.startColor = new Color(1f, 0.82f, 0.4f, 0.85f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 300;

            var emission = ps.emission;
            emission.rateOverTime = 50f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 1f;
            shape.radius = 0.4f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // the cone's axis points up

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f),
                        new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 4f;
            r.velocityScale = 0.15f;
            r.sharedMaterial = glow;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            if (prefab == null) { Debug.LogError("[SpellyZombie] Could not save " + PrefabPath); return; }
            Undo.RecordObject(lib, "Placeholder Pot Beacon");
            lib.PotBeacon = prefab;
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(prefab);
            Debug.Log("[SpellyZombie] FxLibrary > Pot Beacon now holds the placeholder " + PrefabPath);
        }
    }
}
