#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Elts.EltsLink;
using Microsoft.Win32.SafeHandles;

namespace Elts.Operator
{
    /// <summary>USB CDC-ACM transport for an explicitly selected Jetson simulator.</summary>
    internal sealed class WindowsTrackerSimulatorTransport : ITrackerByteTransport
    {
        private const uint PresentDeviceInterfaces = 0x12;
        private const int NoMoreItems = 259;
        private static readonly Guid SerialInterfaces = new Guid("86e0d1e0-8089-11d0-9ce4-08003e301f73");
        private static readonly PropertyKey PortName = new PropertyKey
        {
            Format = new Guid("4c6bf15c-4c03-4aac-91f5-64c0f852bcf4"),
            Id = 4
        };
        private readonly string selectedPort;
        private readonly Action<byte[], int>? observeRead;
        private readonly byte[] writeBuffer = new byte[513]; // Maximum JSON frame plus LF.
        private SafeFileHandle? handle;
        private string? boundInstance;
        public WindowsTrackerSimulatorTransport(string selectedPort, Action<byte[], int>? observeRead = null)
        {
            this.selectedPort = NormalizePort(selectedPort);
            this.observeRead = observeRead;
        }

        internal static string? ReadSelectedPort(string[] arguments)
        {
            const string option = "-eltsTrackerSimulatorCom";
            int index = Array.IndexOf(arguments, option);
            if (index < 0) return null;
            if (index + 1 >= arguments.Length || Array.LastIndexOf(arguments, option) != index)
                throw new ArgumentException("Supply exactly one COM port with " + option + ".");
            return NormalizePort(arguments[index + 1]);
        }

        private static string NormalizePort(string port)
        {
            string normalized = port.ToUpperInvariant();
            if (!Regex.IsMatch(normalized, @"\ACOM[1-9][0-9]{0,4}\z"))
                throw new ArgumentException("Select an explicit Windows COM port (for example COM4).");
            return normalized;
        }

        internal static bool IsSimulatorInstance(string instance) =>
            Regex.IsMatch(instance, @"\AUSB\\VID_0955&PID_7020(?:&MI_[0-9A-F]{2})?\\[^\\]+\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal bool SimulatorPresent() => FindDevicePath(out _) != null;

        public bool TryOpen()
        {
            var path = FindDevicePath(out var instance);
            if (path == null) return false;

            // Open the checked interface itself. A reassigned COM alias cannot redirect the open.
            var opened = CreateFileW(path, 0xc0000000, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (opened.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                opened.Dispose();
                throw IoError("Open selected simulator", error);
            }
            try
            {
                var settings = new DeviceControlBlock
                {
                    Length = Marshal.SizeOf<DeviceControlBlock>(), Baud = 115200,
                    Flags = 0x11, // Binary I/O and asserted DTR for CDC carrier. No flow control.
                    DataBits = 8, Xon = 17, Xoff = 19 // Distinct characters, even with XON/XOFF disabled.
                };
                var timeouts = new CommTimeouts
                {
                    ReadInterval = uint.MaxValue, // Immediate reads of currently available bytes.
                    WriteConstant = 5 // Bound each write on the sender's worker. Counts can be partial.
                };
                Check(SetCommState(opened, ref settings), "Set 115200 8N1");
                Check(SetCommTimeouts(opened, ref timeouts), "Set serial I/O timeouts");
                Check(PurgeComm(opened, 0x0c), "Clear old serial buffers");
                handle = opened;
                boundInstance = instance;
                return true;
            }
            catch
            {
                opened.Dispose();
                throw;
            }
        }

        private string? FindDevicePath(out string? matchingInstance)
        {
            matchingInstance = null;
            Guid interfaceClass = SerialInterfaces;
            IntPtr devices = SetupDiGetClassDevsW(ref interfaceClass, IntPtr.Zero, IntPtr.Zero, PresentDeviceInterfaces);
            if (devices == new IntPtr(-1)) throw IoError("Enumerate present serial interfaces", Marshal.GetLastWin32Error());
            try
            {
                string? matchingPath = null;
                for (uint index = 0; ; index++)
                {
                    var entry = new DeviceInterface { Size = Marshal.SizeOf<DeviceInterface>() };
                    if (!SetupDiEnumDeviceInterfaces(devices, IntPtr.Zero, ref interfaceClass, index, ref entry))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == NoMoreItems) return matchingPath;
                        throw IoError("Read serial interface", error);
                    }
                    var name = new byte[64];
                    var property = PortName;
                    if (!SetupDiGetDeviceInterfacePropertyW(devices, ref entry, ref property, out uint type, name,
                            name.Length, out int size, 0) || type != 0x12 || size < 2 || size > name.Length ||
                        !String.Equals(Encoding.Unicode.GetString(name, 0, size).TrimEnd('\0'), selectedPort, StringComparison.Ordinal))
                        continue;

                    var info = new DeviceInfo { Size = Marshal.SizeOf<DeviceInfo>() };
                    SetupDiGetDeviceInterfaceDetailW(devices, ref entry, IntPtr.Zero, 0, out int required, ref info);
                    if (required < 8 || required > 65536) continue;
                    IntPtr detail = Marshal.AllocHGlobal(required);
                    try
                    {
                        // SP_DEVICE_INTERFACE_DETAIL_DATA_W uses native packing. DevicePath starts after cbSize.
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetailW(devices, ref entry, detail, required, out _, ref info)) continue;
                        var instance = new StringBuilder(256);
                        if (!SetupDiGetDeviceInstanceIdW(devices, ref info, instance, instance.Capacity, out _)) continue;
                        string id = instance.ToString();
                        if (!IsSimulatorInstance(id) ||
                            (boundInstance != null && !String.Equals(boundInstance, id, StringComparison.OrdinalIgnoreCase)))
                            continue;
                        if (matchingPath != null) throw new IOException("The selected simulator port is ambiguous.");
                        matchingInstance = id;
                        matchingPath = Marshal.PtrToStringUni(IntPtr.Add(detail, sizeof(int)));
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(devices); }
        }

        public int Read(byte[] buffer)
        {
            Check(ReadFile(Opened(), buffer, buffer.Length, out int count, IntPtr.Zero), "Read simulator");
            if (count > 0) observeRead?.Invoke(buffer, count);
            return count;
        }

        public int Write(byte[] frame, int offset)
        {
            int count = frame.Length - offset;
            Buffer.BlockCopy(frame, offset, writeBuffer, 0, count);
            Check(WriteFile(Opened(), writeBuffer, count, out int sent, IntPtr.Zero), "Write simulator");
            return sent;
        }

        public void Close()
        {
            handle?.Dispose();
            handle = null;
        }

        private SafeFileHandle Opened() => handle ?? throw new IOException("Simulator port is closed.");
        private static void Check(bool success, string operation)
        {
            if (!success) throw IoError(operation, Marshal.GetLastWin32Error());
        }
        private static IOException IoError(string operation, int code) =>
            new IOException(operation + " failed.", new Win32Exception(code));

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceInterface { public int Size; public Guid Class; public uint Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceInfo { public int Size; public Guid Class; public uint Instance; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey { public Guid Format; public uint Id; }
        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceControlBlock
        {
            public int Length;
            public uint Baud, Flags;
            public ushort Reserved, XonLimit, XoffLimit;
            public byte DataBits, Parity, StopBits, Xon, Xoff, Error, Eof, Event;
            public ushort Reserved1;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct CommTimeouts
        {
            public uint ReadInterval, ReadMultiplier, ReadConstant, WriteMultiplier, WriteConstant;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(ref Guid @class, IntPtr enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr devices, IntPtr info, ref Guid @class, uint index, ref DeviceInterface entry);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfacePropertyW(IntPtr devices, ref DeviceInterface entry, ref PropertyKey key,
            out uint type, [Out] byte[] data, int capacity, out int size, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr devices, ref DeviceInterface entry, IntPtr data,
            int capacity, out int size, ref DeviceInfo info);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr devices, ref DeviceInfo info, StringBuilder id, int capacity, out int size);
        [DllImport("setupapi.dll")]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr devices);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string path, uint access, uint sharing, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetCommState(SafeFileHandle file, ref DeviceControlBlock settings);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetCommTimeouts(SafeFileHandle file, ref CommTimeouts timeouts);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PurgeComm(SafeFileHandle file, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(SafeFileHandle file, [Out] byte[] buffer, int size, out int read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(SafeFileHandle file, [In] byte[] buffer, int size, out int written, IntPtr overlapped);
    }
}
#endif
