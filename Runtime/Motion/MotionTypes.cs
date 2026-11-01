namespace Fofuxo.JoySense.Motion
{
    /// <summary>
    /// What a controller's rotation is measured against. Explained in depth at
    /// http://gyrowiki.jibbsmart.com/blog:player-space-gyro-and-alternatives-explained
    /// </summary>
    public enum GyroSpace
    {
        /// <summary>The controller's own axes.</summary>
        Controller,

        /// <summary>Yaw around the real vertical, wherever the controller is pointing.</summary>
        World,

        /// <summary>Yaw that feels like turning the controller however the player happens to hold it.</summary>
        Player,
    }

    /// <summary>How a controller's gyro drift is worked out.</summary>
    public enum GyroCalibrationMode
    {
        /// <summary>By itself, whenever the controller is held still or moved steadily.</summary>
        Automatic,

        /// <summary>Only between starting and stopping a manual calibration.</summary>
        Manual,
    }
}
