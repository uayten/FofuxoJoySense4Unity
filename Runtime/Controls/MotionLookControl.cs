using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Scripting;

namespace Fofuxo.JoySense.Controls
{
    /// <summary>
    /// How far a controller turned during the current input update, in degrees, for turning a camera: x is to the right,
    /// y is upwards, like a mouse delta.
    /// </summary>
    /// <remarks>
    /// The value is not in the state. A report holds one sample and a frame spans many, so the device adds them up and
    /// sets the total here. The control still sits on the gyro's bytes, which change with every report, so that actions
    /// bound to it are notified. Read it every frame through an action; what is not read is gone, so a menu leaves
    /// nothing behind for the camera.
    /// </remarks>
    [Preserve]
    public class MotionLookControl : InputControl<Vector2>
    {
        internal Vector2 frameTurn;

        /// <summary>The time that <see cref="frameTurn"/> covers, for turning it back into a speed.</summary>
        internal float frameSeconds;

        // A control has to have a size of its own before the device's layout places it.
        public MotionLookControl()
        {
            m_StateBlock.sizeInBits = 16;
            m_StateBlock.format = InputStateBlock.FormatBit;
        }

        public override unsafe Vector2 ReadUnprocessedValueFromState(void* statePtr)
        {
            return frameTurn;
        }

        public override unsafe float EvaluateMagnitude(void* statePtr)
        {
            return frameTurn.magnitude;
        }
    }
}
