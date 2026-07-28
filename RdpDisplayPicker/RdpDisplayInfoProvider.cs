using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;

namespace RdpDisplayPicker
{
    internal sealed record RdpDisplayInfo(int RdpId, Int32Rect Bounds);

    internal static class RdpDisplayInfoProvider
    {
        private const int EnumCurrentSettings = -1;
        private const int DisplayDeviceAttachedToDesktop = 0x00000001;

        public static IReadOnlyDictionary<string, RdpDisplayInfo> GetActiveDisplays()
        {
            var displays = new Dictionary<string, RdpDisplayInfo>(StringComparer.OrdinalIgnoreCase);

            for (uint index = 0; ; index++)
            {
                var displayDevice = new DisplayDevice
                {
                    Size = Marshal.SizeOf<DisplayDevice>(),
                };

                if (!EnumDisplayDevices(null, index, ref displayDevice, 0))
                {
                    break;
                }

                if ((displayDevice.StateFlags & DisplayDeviceAttachedToDesktop) == 0 ||
                    string.IsNullOrWhiteSpace(displayDevice.DeviceName))
                {
                    continue;
                }

                var deviceMode = new DeviceMode
                {
                    Size = (short)Marshal.SizeOf<DeviceMode>(),
                };

                if (!EnumDisplaySettings(displayDevice.DeviceName, EnumCurrentSettings, ref deviceMode))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        $"ディスプレイ設定を取得できませんでした: {displayDevice.DeviceName}");
                }

                displays[displayDevice.DeviceName] = new RdpDisplayInfo(
                    checked((int)index),
                    new Int32Rect(
                        deviceMode.PositionX,
                        deviceMode.PositionY,
                        deviceMode.PelsWidth,
                        deviceMode.PelsHeight));
            }

            return displays;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayDevices(
            string? device,
            uint deviceIndex,
            ref DisplayDevice displayDevice,
            uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplaySettings(
            string deviceName,
            int modeNumber,
            ref DeviceMode deviceMode);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayDevice
        {
            public int Size;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;

            public int StateFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceId;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DeviceMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;

            public short SpecVersion;
            public short DriverVersion;
            public short Size;
            public short DriverExtra;
            public int Fields;
            public int PositionX;
            public int PositionY;
            public int DisplayOrientation;
            public int DisplayFixedOutput;
            public short Color;
            public short Duplex;
            public short YResolution;
            public short TTOption;
            public short Collate;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string FormName;

            public short LogPixels;
            public int BitsPerPel;
            public int PelsWidth;
            public int PelsHeight;
            public int DisplayFlags;
            public int DisplayFrequency;
            public int ICMMethod;
            public int ICMIntent;
            public int MediaType;
            public int DitherType;
            public int Reserved1;
            public int Reserved2;
            public int PanningWidth;
            public int PanningHeight;
        }
    }
}
