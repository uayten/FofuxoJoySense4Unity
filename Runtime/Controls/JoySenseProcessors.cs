using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Scripting;

namespace Fofuxo.JoySense.Controls
{
    /// <summary>
    /// A radial stick dead zone for one binding, so that each function can have its own.
    /// </summary>
    /// <remarks>
    /// Unlike the Input System's Stick Deadzone, it takes its values literally: there, a minimum of 0 means "use the
    /// global setting". Sticks reach bindings after the global dead zone, so a value below it only has an effect after
    /// <see cref="JoySenseSupport.RemoveGlobalStickDeadzone"/>.
    /// </remarks>
    [Preserve]
    public class JoySenseStickDeadzoneProcessor : InputProcessor<Vector2>
    {
        /// <summary>Below this deflection the stick reads as centred.</summary>
        public float min = 0.125f;

        /// <summary>From this deflection on the stick reads as fully tilted.</summary>
        public float max = 0.925f;

        public override Vector2 Process(Vector2 value, InputControl control)
        {
            var magnitude = value.magnitude;
            if (magnitude <= min)
            {
                return Vector2.zero;
            }

            var adjusted = max > min ? Mathf.Clamp01((magnitude - min) / (max - min)) : 1f;
            return value * (adjusted / magnitude);
        }
    }

    /// <summary>
    /// A dead zone for a <see cref="MotionLookControl"/>: turning slower than this counts as no turn, so a hand held
    /// still does not shake the camera. Faster turning loses this much speed, so nothing jumps at the threshold.
    /// </summary>
    [Preserve]
    public class JoySenseGyroDeadzoneProcessor : InputProcessor<Vector2>
    {
        public float degreesPerSecond = 1f;

        public override Vector2 Process(Vector2 value, InputControl control)
        {
            if (!(control is MotionLookControl look) || look.frameSeconds <= 0f)
            {
                return value;
            }

            var speed = value.magnitude / look.frameSeconds;
            return speed > degreesPerSecond ? value * ((speed - degreesPerSecond) / speed) : Vector2.zero;
        }
    }
}
