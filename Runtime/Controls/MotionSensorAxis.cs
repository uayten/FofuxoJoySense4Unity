using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Scripting;

namespace Fofuxo.JoySense.Controls
{
    /// <summary>
    /// One axis of a motion sensor, read from a raw little-endian 16-bit word through a per-device scale and bias.
    /// </summary>
    /// <remarks>
    /// The Input System's own 16-bit format reads as a normalised -1..1, which is not what a sensor means. The bias is
    /// the gyro's drift, which the device keeps working out while it runs, so it is set on the control at runtime.
    /// </remarks>
    [Preserve]
    public class MotionSensorAxis : AxisControl
    {
        /// <summary>The value of one raw count. Negative for an axis whose word runs the other way.</summary>
        public float sensorScale { get; set; } = 1f;

        /// <summary>Subtracted from the scaled value.</summary>
        public float sensorBias { get; set; }

        public override unsafe float ReadUnprocessedValueFromState(void* statePtr)
        {
            var bytes = (byte*)statePtr + stateBlock.byteOffset;
            var raw = (short)(bytes[0] | (bytes[1] << 8));
            return Preprocess(raw * sensorScale - sensorBias);
        }

        // Every read must come through the scale above.
        protected override FourCC CalculateOptimizedControlDataType()
        {
            return InputStateBlock.FormatInvalid;
        }
    }
}
