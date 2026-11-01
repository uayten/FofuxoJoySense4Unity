using Fofuxo.JoySense.Controls;

namespace Fofuxo.JoySense.Switch2
{
    // Where a Switch 2 controller keeps its stick calibration in flash, and how to read it. The same blocks are
    // read over either transport; only the way of asking for them differs.
    internal static class Switch2Calibration
    {
        // The player's own calibration, written by the console's stick-correction screen, wins over the
        // factory's: a controller drifted enough for its owner to have corrected it is exactly the one whose
        // factory centres no longer describe it. Both sticks' user copies live in one block (left at +0x02,
        // right at +0x22); the factory copies are at +0x28 of their own blocks.
        public const uint UserBlock = 0x1FC040;
        public const int UserLeftOffset = 0x02;
        public const int UserRightOffset = 0x22;
        public const uint FactoryLeftBlock = 0x013080;
        public const uint FactoryRightBlock = 0x0130C0;
        public const int FactoryOffset = 0x28;

        // Nine bytes per stick: three packed 12-bit pairs.
        public const int StickBytes = 9;

        // An erased region reads back as all-ones, which is how "never calibrated" is told from a real reading.
        public static bool IsWritten(byte[] block, int offset)
        {
            return !(block[offset] == 0xFF && block[offset + 1] == 0xFF && block[offset + 2] == 0xFF);
        }

        // [centre X, centre Y][range X+, range Y+][range X-, range Y-].
        public static StickCalibration Parse(byte[] block, int offset)
        {
            Unpack12(block, offset, out var centerX, out var centerY);
            Unpack12(block, offset + 3, out var rangeXPositive, out var rangeYPositive);
            Unpack12(block, offset + 6, out var rangeXNegative, out var rangeYNegative);
            return new StickCalibration(
                new AxisCalibration(centerX - rangeXNegative, centerX, centerX + rangeXPositive),
                new AxisCalibration(centerY - rangeYNegative, centerY, centerY + rangeYPositive));
        }

        // A block that is blank or erased parses into numbers perfectly happily -- an erased one into a centre of
        // 4095, a stick jammed to one side, and a blank one into a zero range -- so what comes back is checked
        // as a description of a stick: the centre near the middle of the 12-bit scale, and each direction some
        // real distance without spanning the whole of it.
        public static bool IsPlausible(StickCalibration calibration)
        {
            return IsPlausible(calibration.x) && IsPlausible(calibration.y);
        }

        static bool IsPlausible(AxisCalibration axis)
        {
            return axis.center >= 1000 && axis.center <= 3000
                && axis.max > axis.center + 200 && axis.max - axis.center <= 2048
                && axis.min + 200 < axis.center && axis.center - axis.min <= 2048;
        }

        // Two 12-bit values packed into 3 bytes, the same packing as the stick axes in input reports.
        static void Unpack12(byte[] bytes, int offset, out int first, out int second)
        {
            first = bytes[offset] | ((bytes[offset + 1] & 0x0F) << 8);
            second = (bytes[offset + 1] >> 4) | (bytes[offset + 2] << 4);
        }
    }
}
