#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fofuxo.JoySense.Controls;
using Fofuxo.JoySense.Windows;

namespace Fofuxo.JoySense.Switch2
{
    // The Switch 2 controllers' command channel over USB. Their HID interface (MI_00) is input-only -- a HID
    // write to it fails -- so initialisation and every factory read go over a second, WinUSB interface (MI_01)
    // through its bulk pipes. Ported from JoyShockLibrary4Unreal's sw2_open_winusb / init_switch2, which
    // replicate what Steam does (confirmed there with a USBPcap capture).
    //
    // Everything here blocks for up to a few hundred milliseconds, so it only ever runs on a worker thread.
    internal static class Switch2CommandInterface
    {
        public enum Outcome
        {
            Initialized,
            AccessDenied,
            Failed,
        }

        public struct Result
        {
            public Outcome outcome;
            public string devicePath;
            public string detail;
            public bool hasLeftStick;
            public bool hasRightStick;
            public StickCalibration leftStick;
            public StickCalibration rightStick;
        }

        // Device interface GUID from the controller's own MS OS descriptor for its WinUSB interface
        // (Enum\USB\VID_057E&PID_2069&MI_01\...\Device Parameters\DeviceInterfaceGUID). Firmware-defined, so
        // it is the same on every machine.
        static Guid s_WinUsbInterfaceGuid = new Guid(0x6F13725E, 0xEF0E, 0x4FD3, 0xAE, 0x5F, 0xB2, 0xDE, 0x98, 0x9E, 0xC8, 0x25);

        static readonly object s_Lock = new object();

        // The init sequence, as [command, subcommand, data...]. Sent as
        // [command] 0x91 0x00 [subcommand] 0x00 [data length] 0x00 0x00 [data]. Over Bluetooth LE byte 2 is
        // 0x01 instead; sending that over USB makes the controller search for a Bluetooth host.
        static readonly byte[][] s_InitCommands =
        {
            new byte[] { 0x07, 0x01 },
            new byte[] { 0x0c, 0x02, 0x27, 0x00, 0x00, 0x00 }, // feature select
            new byte[] { 0x11, 0x01 },
            new byte[] { 0x0a, 0x08, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x35, 0x00, 0x46, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            new byte[] { 0x0c, 0x04, 0x27, 0x00, 0x00, 0x00 },
            new byte[] { 0x01, 0x0c },
            new byte[] { 0x01, 0x01 },
            new byte[] { 0x08, 0x02, 0x01, 0x00, 0x00, 0x00 },
            new byte[] { 0x03, 0x0a, 0x05, 0x00, 0x00, 0x00 }, // input report mode 0x05
            new byte[] { 0x03, 0x0d, 0x01, 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff },
        };

        /// <summary>
        /// Initialises every Switch 2 controller present on USB and reads its stick calibration.
        /// </summary>
        public static List<Result> InitializeAll()
        {
            lock (s_Lock)
            {
                var results = new List<Result>();
                foreach (var path in EnumerateInterfacePaths())
                {
                    results.Add(Initialize(path));
                }
                return results;
            }
        }

        static List<string> EnumerateInterfacePaths()
        {
            var paths = new List<string>();
            var deviceInfo = Win32.SetupDiGetClassDevsW(ref s_WinUsbInterfaceGuid, IntPtr.Zero, IntPtr.Zero,
                Win32.DIGCF_PRESENT | Win32.DIGCF_DEVICEINTERFACE);
            if (deviceInfo == Win32.InvalidHandleValue)
            {
                return paths;
            }

            try
            {
                var interfaceData = new Win32.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<Win32.SP_DEVICE_INTERFACE_DATA>() };
                for (uint index = 0; Win32.SetupDiEnumDeviceInterfaces(deviceInfo, IntPtr.Zero, ref s_WinUsbInterfaceGuid, index, ref interfaceData); index++)
                {
                    Win32.SetupDiGetDeviceInterfaceDetailW(deviceInfo, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
                    if (requiredSize == 0)
                    {
                        continue;
                    }

                    var detail = Marshal.AllocHGlobal(requiredSize);
                    try
                    {
                        // SP_DEVICE_INTERFACE_DETAIL_DATA_W: a DWORD cbSize followed by the path. cbSize is the
                        // size of the fixed part as the platform pads it, not of this buffer.
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (Win32.SetupDiGetDeviceInterfaceDetailW(deviceInfo, ref interfaceData, detail, requiredSize, out _, IntPtr.Zero))
                        {
                            var path = Marshal.PtrToStringUni(detail + 4);
                            if (path != null
                                && path.IndexOf("vid_057e", StringComparison.OrdinalIgnoreCase) >= 0
                                && path.IndexOf("pid_2069", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                paths.Add(path);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                Win32.SetupDiDestroyDeviceInfoList(deviceInfo);
            }

            return paths;
        }

        static Result Initialize(string path)
        {
            var result = new Result { devicePath = path };

            var file = Win32.CreateFileW(path, Win32.GENERIC_READ | Win32.GENERIC_WRITE,
                Win32.FILE_SHARE_READ | Win32.FILE_SHARE_WRITE, IntPtr.Zero, Win32.OPEN_EXISTING,
                Win32.FILE_ATTRIBUTE_NORMAL | Win32.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (file == Win32.InvalidHandleValue)
            {
                // The interface is exclusive: Steam, or any other tool that drives the controller, holds it.
                var error = Marshal.GetLastWin32Error();
                result.outcome = error == Win32.ERROR_ACCESS_DENIED ? Outcome.AccessDenied : Outcome.Failed;
                result.detail = $"CreateFile failed ({error})";
                return result;
            }

            if (!Win32.WinUsb_Initialize(file, out var usb))
            {
                result.outcome = Outcome.Failed;
                result.detail = $"WinUsb_Initialize failed ({Marshal.GetLastWin32Error()})";
                Win32.CloseHandle(file);
                return result;
            }

            try
            {
                var pipes = OpenPipes(usb);
                ReadStickCalibration(pipes, ref result);

                var failedCommands = 0;
                foreach (var command in s_InitCommands)
                {
                    if (!SendCommand(pipes, command))
                    {
                        failedCommands++;
                    }
                }

                result.outcome = failedCommands == 0 ? Outcome.Initialized : Outcome.Failed;
                if (failedCommands > 0)
                {
                    result.detail = $"{failedCommands} of {s_InitCommands.Length} init commands failed";
                }
                return result;
            }
            finally
            {
                // Released straight away: input arrives on the HID interface, which the controller keeps
                // streaming once initialised, and a held lease would lock out every other process.
                Win32.WinUsb_Free(usb);
                Win32.CloseHandle(file);
            }
        }

        struct Pipes
        {
            public IntPtr usb;
            public byte output;
            public byte input;
        }

        static Pipes OpenPipes(IntPtr usb)
        {
            // A capture shows OUT 0x02 and IN 0x82; queried anyway in case a firmware moves them.
            var pipes = new Pipes { usb = usb, output = 0x02, input = 0x82 };
            if (Win32.WinUsb_QueryInterfaceSettings(usb, 0, out var descriptor))
            {
                for (byte index = 0; index < descriptor.bNumEndpoints; index++)
                {
                    if (Win32.WinUsb_QueryPipe(usb, 0, index, out var pipe) && pipe.PipeType == Win32.UsbdPipeTypeBulk)
                    {
                        if ((pipe.PipeId & 0x80) != 0)
                        {
                            pipes.input = pipe.PipeId;
                        }
                        else
                        {
                            pipes.output = pipe.PipeId;
                        }
                    }
                }
            }

            // Bounded, so a controller that stops answering cannot hang the worker.
            uint timeoutMs = 200;
            Win32.WinUsb_SetPipePolicy(usb, pipes.input, Win32.PIPE_TRANSFER_TIMEOUT, 4, ref timeoutMs);
            Win32.WinUsb_SetPipePolicy(usb, pipes.output, Win32.PIPE_TRANSFER_TIMEOUT, 4, ref timeoutMs);
            return pipes;
        }

        static bool SendCommand(Pipes pipes, byte[] command)
        {
            var dataLength = command.Length - 2;
            var buffer = new byte[8 + dataLength];
            buffer[0] = command[0];
            buffer[1] = 0x91;
            buffer[3] = command[1];
            buffer[5] = (byte)dataLength;
            Array.Copy(command, 2, buffer, 8, dataLength);

            if (!Win32.WinUsb_WritePipe(pipes.usb, pipes.output, buffer, (uint)buffer.Length, out _, IntPtr.Zero))
            {
                return false;
            }

            // Drains the acknowledgement. Best effort: a missing ack is not taken as failure, as in JSL4U.
            var response = new byte[64];
            Win32.WinUsb_ReadPipe(pipes.usb, pipes.input, response, (uint)response.Length, out _, IntPtr.Zero);
            return true;
        }

        // SPI read, command 0x02:0x01 with data [flags u32 = 0][address u32 LE]. The response is a 16-byte header
        // (command echo, status, length 0x40, address echo) followed by 64 bytes of flash, possibly split across
        // bulk packets.
        static byte[] ReadSpi(Pipes pipes, uint address)
        {
            var command = new byte[16];
            command[0] = 0x02;
            command[1] = 0x91;
            command[3] = 0x01;
            command[5] = 0x08;
            command[12] = (byte)address;
            command[13] = (byte)(address >> 8);
            command[14] = (byte)(address >> 16);
            command[15] = (byte)(address >> 24);
            if (!Win32.WinUsb_WritePipe(pipes.usb, pipes.output, command, (uint)command.Length, out _, IntPtr.Zero))
            {
                return null;
            }

            var response = new byte[96];
            var chunk = new byte[96];
            var total = 0;
            while (total < 80)
            {
                if (!Win32.WinUsb_ReadPipe(pipes.usb, pipes.input, chunk, (uint)(response.Length - total), out var got, IntPtr.Zero) || got == 0)
                {
                    break;
                }
                Array.Copy(chunk, 0, response, total, (int)got);
                total += (int)got;
            }

            if (total < 80 || response[0] != 0x02)
            {
                return null;
            }

            var data = new byte[64];
            Array.Copy(response, 16, data, 0, 64);
            return data;
        }

        // The player's own calibration wins over the factory's (see Switch2Calibration).
        static void ReadStickCalibration(Pipes pipes, ref Result result)
        {
            var user = ReadSpi(pipes, Switch2Calibration.UserBlock);

            if (user != null && Switch2Calibration.IsWritten(user, Switch2Calibration.UserLeftOffset))
            {
                result.leftStick = Switch2Calibration.Parse(user, Switch2Calibration.UserLeftOffset);
                result.hasLeftStick = true;
            }
            else
            {
                var factory = ReadSpi(pipes, Switch2Calibration.FactoryLeftBlock);
                if (factory != null)
                {
                    result.leftStick = Switch2Calibration.Parse(factory, Switch2Calibration.FactoryOffset);
                    result.hasLeftStick = true;
                }
            }

            if (user != null && Switch2Calibration.IsWritten(user, Switch2Calibration.UserRightOffset))
            {
                result.rightStick = Switch2Calibration.Parse(user, Switch2Calibration.UserRightOffset);
                result.hasRightStick = true;
            }
            else
            {
                var factory = ReadSpi(pipes, Switch2Calibration.FactoryRightBlock);
                if (factory != null)
                {
                    result.rightStick = Switch2Calibration.Parse(factory, Switch2Calibration.FactoryOffset);
                    result.hasRightStick = true;
                }
            }
        }
    }
}
#endif
