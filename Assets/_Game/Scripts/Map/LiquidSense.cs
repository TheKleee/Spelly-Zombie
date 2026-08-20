using UnityEngine;

namespace SpellyZombie
{
    /// Rides the camera: dipping under a liquid's surface tints the world
    /// with that liquid's own color via fog - blue in water, red in magma,
    /// dark in space. Restores the scene's real fog on surfacing.
    public class LiquidSense : MonoBehaviour
    {
        bool _under, _hadFog;
        Color _fogColor;
        float _fogDensity;
        FogMode _fogMode;
        Camera _cam;
        CameraClearFlags _clear;
        Color _bg;

        void Awake() => _cam = GetComponent<Camera>();

        void LateUpdate()
        {
            var liquid = LiquidBiome.At(transform.position);
            bool under = liquid != null;

            if (under && !_under)
            {
                _hadFog = RenderSettings.fog;
                _fogColor = RenderSettings.fogColor;
                _fogDensity = RenderSettings.fogDensity;
                _fogMode = RenderSettings.fogMode;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Exponential;
                if (_cam != null)
                {
                    // skyboxes ignore fog: paint the clear color instead
                    _clear = _cam.clearFlags;
                    _bg = _cam.backgroundColor;
                    _cam.clearFlags = CameraClearFlags.SolidColor;
                }
            }
            else if (!under && _under)
            {
                RenderSettings.fog = _hadFog;
                RenderSettings.fogColor = _fogColor;
                RenderSettings.fogDensity = _fogDensity;
                RenderSettings.fogMode = _fogMode;
                if (_cam != null)
                {
                    _cam.clearFlags = _clear;
                    _cam.backgroundColor = _bg;
                }
            }

            if (under)
            {
                Color c = liquid.Tint; c.a = 1f;
                RenderSettings.fogColor = c;
                RenderSettings.fogDensity = 0.08f;
                if (_cam != null) _cam.backgroundColor = c;
            }
            _under = under;
        }
    }
}
