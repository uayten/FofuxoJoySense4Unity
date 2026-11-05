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
    /// The value is not in the state. A report holds one sample, and a frame can span many or none, so the device holds
    /// each sample's speed until the next one and sets here the turn over the frame's time. The control still sits on
    /// the gyro's bytes, which change with every report, so that actions
    /// bound to it are notified. Read it every frame through an action; what is not read is gone, so a menu leaves
    /// nothing behind for the camera.
    /// </remarks>
    [Preserve]
    public class MotionLookControl : InputControl<Vector2>
    {
        /// <summary>
        /// The usage every controller with motion gives this control, so that the binding <c>*/{GyroLook}</c> works
        /// for all of them without naming a model.
        /// </summary>
        public const string GyroLookUsage = "GyroLook";

        internal Vector2 frameTurn;

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
