#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
using System.Text;

namespace Fofuxo.JoySense.Windows
{
    // The package's one native binary: the Bluetooth LE transport, which needs WinRT's GATT API and therefore
    // cannot be reached from Mono directly (D-002). Source and build script in Native~/JoySenseBle. Every call
    // that talks to the radio blocks, so only worker threads call those; the queue and handle lookups are cheap.
    internal static unsafe class JoySenseBle
    {
        const string Dll = "JoySenseBle";

        public const int LogInfo = 0;
        public const int LogWarning = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct Advertisement
        {
            public ulong address;
            public ulong reconnectAddress;
            public ushort vendorId;
            public ushort productId;
            public uint reserved;
        }

        [DllImport(Dll)] public static extern int JsBle_IsSupported();
        [DllImport(Dll)] public static extern ulong JsBle_GetHostAddress();
        [DllImport(Dll)] public static extern int JsBle_StartScan();
        [DllImport(Dll)] public static extern void JsBle_StopScan();
        [DllImport(Dll)] public static extern void JsBle_SuppressFor(ulong address, int milliseconds);
        [DllImport(Dll)] public static extern int JsBle_DrainDiscovered([Out] Advertisement[] advertisements, int maxCount);
        [DllImport(Dll)] public static extern int JsBle_Connect(ulong address, ushort productId);
        [DllImport(Dll)] public static extern void JsBle_Disconnect(int handle);
        [DllImport(Dll)] public static extern int JsBle_IsConnected(int handle);
        [DllImport(Dll)] public static extern int JsBle_GetConnections([Out] int[] handles, int maxCount);
        [DllImport(Dll)] public static extern int JsBle_GetConnectionInfo(int handle, out ulong address, out ushort productId);

        [DllImport(Dll)]
        public static extern int JsBle_SendCommand(int handle, byte commandId, byte subcommandId, byte[] data, int dataLength,
            [Out] byte[] response, int responseCapacity, out int responseLength, int minResponseBytes, int timeoutMs);

        [DllImport(Dll)] public static extern int JsBle_ReadInputReport(int handle, byte* buffer, int maxLength, int timeoutMs);
        [DllImport(Dll, CharSet = CharSet.Unicode)] public static extern int JsBle_PopLog(StringBuilder buffer, int capacity);
        [DllImport(Dll)] public static extern void JsBle_Shutdown();
    }
}
#endif
