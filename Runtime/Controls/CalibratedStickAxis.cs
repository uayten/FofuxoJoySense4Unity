using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Scripting;

namespace Fofuxo.JoySense.Controls
{
    /// <summary>
    /// The calibration of one stick axis, in the controller's raw counts.
    /// </summary>
    /// <remarks>
    /// Nintendo sticks are not centred at the middle of their range, and travel a different distance each way, so
    /// one axis is three points rather than a centre and a scale. A value maps to -1..0 between
    /// <see cref="min"/> and <see cref="center"/>, and to 0..1 between <see cref="center"/> and
    /// <see cref="max"/>, and is clamped outside them.
    /// </remarks>
    public readonly struct AxisCalibration
    {
        public readonly int min;
        public readonly int center;
        public readonly int max;

        /// <summary>Whether raw counts grow towards negative values instead of positive ones.</summary>
        public readonly bool reversed;

        public AxisCalibration(int min, int center, int max, bool reversed = false)
        {
            this.min = min;
            this.center = center;
            this.max = max;
            this.reversed = reversed;
        }

        public AxisCalibration Reversed(bool value) => new AxisCalibration(min, center, max, value);

        public float Apply(int raw)
        {
            float value;
            if (raw >= center)
            {
                value = max > center ? (float)(raw - center) / (max - center) : 0f;
            }
            else
            {
                value = center > min ? -(float)(center - raw) / (center - min) : 0f;
            }

            if (value > 1f)
            {
                value = 1f;
            }
            else if (value < -1f)
            {
                value = -1f;
            }

            return reversed ? -value : value;
        }

        // Reads an unsigned value of up to 16 bits at the control's place in the state. Sticks are packed as
        // 12-bit values that straddle byte boundaries, which the Input System's own formats do not calibrate.
        internal static unsafe int ReadRaw(InputStateBlock block, void* statePtr)
        {
            var bytes = (byte*)statePtr + block.byteOffset;
            var word = bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
            return (int)((word >> (int)block.bitOffset) & ((1u << (int)block.sizeInBits) - 1));
        }
    }

    /// <summary>
    /// The calibration of both axes of one stick.
    /// </summary>
    public readonly struct StickCalibration
    {
        public readonly AxisCalibration x;
        public readonly AxisCalibration y;

        public StickCalibration(AxisCalibration x, AxisCalibration y)
        {
            this.x = x;
            this.y = y;
        }
    }

    /// <summary>
    /// A stick axis read from raw packed counts through a per-device <see cref="AxisCalibration"/>.
    /// </summary>
    /// <remarks>
    /// The calibration is a property of the physical controller, read from it once it connects, so it is set on
    /// the control at runtime rather than fixed in the layout.
    /// </remarks>
    [Preserve]
    public class CalibratedStickAxis : AxisControl
    {
        public AxisCalibration calibration { get; set; }

        public override unsafe float ReadUnprocessedValueFromState(void* statePtr)
        {
            return Preprocess(calibration.Apply(AxisCalibration.ReadRaw(stateBlock, statePtr)));
        }

        // The packed format has no fast path; every read must come through the calibration above.
        protected override FourCC CalculateOptimizedControlDataType()
        {
            return InputStateBlock.FormatInvalid;
        }
    }

    /// <summary>
    /// One direction of a stick (up, down, left or right) as a button, through the same calibration as its axis.
    /// </summary>
    [Preserve]
    public class CalibratedStickButton : ButtonControl
    {
        public AxisCalibration calibration { get; set; }

        public override unsafe float ReadUnprocessedValueFromState(void* statePtr)
        {
            return Preprocess(calibration.Apply(AxisCalibration.ReadRaw(stateBlock, statePtr)));
        }

        protected override FourCC CalculateOptimizedControlDataType()
        {
            return InputStateBlock.FormatInvalid;
        }
    }
}
