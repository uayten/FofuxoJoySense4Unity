#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Runtime.InteropServices;

namespace Fofuxo.JoySense.Windows
{
    // The slice of SetupAPI, Kernel32 and WinUSB the package needs. All of it lives in system DLLs, so the
    // USB side ships no native binary of its own.
    internal static class Win32
    {
        public const uint DIGCF_PRESENT = 0x02;
        public const uint DIGCF_DEVICEINTERFACE = 0x10;
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x1;
        public const uint FILE_SHARE_WRITE = 0x2;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        public const int ERROR_ACCESS_DENIED = 5;
        public const uint PIPE_TRANSFER_TIMEOUT = 0x03;
        public const int UsbdPipeTypeBulk = 2;

        public static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct USB_INTERFACE_DESCRIPTOR
        {
            public byte bLength;
            public byte bDescriptorType;
            public byte bInterfaceNumber;
            public byte bAlternateSetting;
            public byte bNumEndpoints;
            public byte bInterfaceClass;
            public byte bInterfaceSubClass;
            public byte bInterfaceProtocol;
            public byte iInterface;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINUSB_PIPE_INFORMATION
        {
            public int PipeType;
            public byte PipeId;
            public ushort MaximumPacketSize;
            public byte Interval;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
            uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("winusb.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WinUsb_Initialize(IntPtr deviceHandle, out IntPtr interfaceHandle);

        [DllImport("winusb.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WinUsb_Free(IntPtr interfaceHandle);

        [DllImport("winusb.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WinUsb_QueryInterfaceSettings(IntPtr interfaceHandle, byte alternateInterfaceNumber,
            out USB_INTERFACE_DESCRIPTOR usbAltInterfaceDescriptor);

        [DllImport("winusb.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WinUsb_QueryPipe(IntPtr interfaceHandle, byte alternateInterfaceNumber, byte pipeIndex,
            out WINUSB_PIPE_INFORMATION pipeInformation);

        [DllImport("winusb.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WinUsb_SetPipePolicy(IntPtr interfaceHandle, byte pipeId, uint policyType, uint valueLength, ref uint value);

        [DllImport("winusb.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WinUsb_WritePipe(IntPtr interfaceHandle, byte pipeId, byte[] buffer, uint bufferLength,
            out uint lengthTransferred, IntPtr overlapped);

        [DllImport("winusb.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WinUsb_ReadPipe(IntPtr interfaceHandle, byte pipeId, byte[] buffer, uint bufferLength,
            out uint lengthTransferred, IntPtr overlapped);
    }
}
#endif
