using UnityEngine;

namespace SpellyZombie
{
    /// Central tuning constants for the drawing / seal system.
    /// Everything gameplay-feel lives here so balancing is one file.
    public static class DrawingConfig
    {
        // ---- Pen / stroke capture ----
        public const float DrawRange = 8f;           // max raycast distance of the pen
        public const float NodeSpacing = 0.02f;      // min world distance between nodes — fine enough for palm-sized runes
        public const float SurfaceOffset = 0.008f;   // lift ink off the surface to avoid z-fighting
        public const float MaxStrokeJump = 0.35f;    // hit point jumping further than this in one frame ends the stroke
        public const int MinStrokeNodes = 4;         // strokes shorter than this are discarded as accidental clicks
        public const float InkWidth = 0.012f;        // line renderer width
        public const float DrawSmoothingTime = 0.025f; // hand-jitter smoothing time constant, seconds (0 = raw input)
        public const float DrawLookSensitivityScale = 0.35f; // camera sensitivity multiplier while the pen is down
        public const float EraseRadius = 0.08f;      // debug eraser size

        // ---- Seal closure / integrity ----
        // Cross-stroke endpoint link distance: "the ink has to actually touch".
        public const float CloseThreshold = 0.05f;
        public const float SelfCloseFraction = 0.06f;// self close: threshold = fraction of stroke length...
        public const float SelfCloseMin = 0.03f;     // ...clamped to [SelfCloseMin, CloseThreshold]
        public const float BreakDistance = 0.12f;    // adjacent loop nodes drifting further apart than this open the seal
        public const int MinLoopNodes = 8;
        public const float MinLoopPerimeter = 0.18f; // palm-sized seals are legal (~6cm triangle)
        public const int MaxLoopStrokes = 6;         // DFS depth cap when chaining strokes into one seal

        // ---- Seal shape -> duration ----
        public const float DurationPerEdge = 0.1f;   // triangle = 0.3s
        public const int CircleEdges = 360;          // perfect circle counts as 360 edges = 36s
        public const float CircleMaxVariance = 0.07f;// coefficient of variation of radius below this = circle
        public const int CircleMinCorners = 8;       // and it must not be an obvious low-corner polygon
        public const float RdpEpsilonFactor = 0.015f;// RDP epsilon as fraction of the loop's bounding diagonal
        public const float MinCornerAngle = 20f;     // degrees of direction change required to count as an edge corner

        // ---- Detection / recognition ----
        public const float DetectInterval = 0.12f;   // how often the seal detector rescans stroke endpoints
        public const float MinRuneScore = 0.65f;     // $1 score below this = rune fizzles (unrecognized)

        // ---- Persistent ink (characters & weapons) ----
        public const float ReArmDistance = 0.10f;    // a spent loop must open this far before it can fire again
        public const int MaxEnvironmentStrokes = 300;// oldest unsealed world ink fades beyond this (perf cap)

        /// Self-closure distance for a single stroke scales with the stroke's own
        /// size: a 20cm rune keeps its 4cm gap open, while a 2m loop still snaps
        /// shut when the pen comes back within 5cm of the start.
        public static float SelfCloseThreshold(float strokeLength)
        {
            return Mathf.Clamp(strokeLength * SelfCloseFraction, SelfCloseMin, CloseThreshold);
        }
    }
}
