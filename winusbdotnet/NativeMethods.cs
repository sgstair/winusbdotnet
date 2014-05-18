using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace winusbdotnet
{
    internal class NativeMethods
    {

        private struct SP_DEVICE_INTERFACE_DATA
        {
            public UInt32 cbSize;
            public Guid interfaceClassGuid;
            public UInt32 flags;
            public IntPtr reserved;
        }

        private const UInt32 DIGCF_PRESENT = 2;
        private const UInt32 DIGCF_DEVICEINTERFACE = 0x10;

        private static IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        private const int ERROR_NO_MORE_ITEMS = 259;
        private const int ERROR_INSUFFICIENT_BUFFER = 122; 
        public const int ERROR_IO_PENDING = 997;

        /// <summary>
        /// Retrieve device paths that can be opened from a specific device interface guid.
        /// todo: Make this friendlier & query some more data about the devices being returned.
        /// </summary>
        /// <param name="deviceInterface">Guid uniquely identifying the interface to search for</param>
        /// <returns>List of device paths that can be opened with CreateFile</returns>
        public static string[] EnumerateDevicesByInterface(Guid deviceInterface)
        {
            // Horribe horrible things have to be done with SetupDI here. These travesties must never leave this class.
            List<string> outputPaths = new List<string>();

            IntPtr devInfo = SetupDiGetClassDevs(ref deviceInterface, null, IntPtr.Zero, DIGCF_DEVICEINTERFACE | DIGCF_PRESENT);
            if(devInfo == INVALID_HANDLE_VALUE)
            {
                throw new Exception("SetupDiGetClassDevs failed. " + (new Win32Exception()).ToString());
            }

            try
            {
                uint deviceIndex = 0;
                SP_DEVICE_INTERFACE_DATA interfaceData = new SP_DEVICE_INTERFACE_DATA();

                bool success = true;
                for (deviceIndex = 0; ; deviceIndex++)
                {
                    interfaceData.cbSize = (uint)Marshal.SizeOf(interfaceData);
                    success = SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref deviceInterface, deviceIndex, ref interfaceData);
                    if (!success)
                    {
                        if (Marshal.GetLastWin32Error() != ERROR_NO_MORE_ITEMS)
                        {
                            throw new Exception("SetupDiEnumDeviceInterfaces failed " + (new Win32Exception()).ToString());
                        }
                        // We have reached the end of the list of devices.
                        break;
                    }


                    // This is a valid interface, retrieve its path
                    UInt32 requiredLength = 0;

                    if (!SetupDiGetDeviceInterfaceDetail(devInfo, ref interfaceData, IntPtr.Zero, 0, ref requiredLength, IntPtr.Zero))
                    {
                        if (Marshal.GetLastWin32Error() != ERROR_INSUFFICIENT_BUFFER)
                        {
                            throw new Exception("SetupDiGetDeviceInterfaceDetail failed (determining length) " + (new Win32Exception()).ToString());
                        }
                    }
                    
                    UInt32 actualLength = requiredLength;

                    if (requiredLength < 6)
                    {
                        throw new Exception("Consistency issue: Required memory size should be larger");
                    }

                    IntPtr mem = Marshal.AllocHGlobal((int)requiredLength);
                    try
                    {
                        Marshal.WriteInt32(mem, 6); // set fake size in fake structure

                        if (!SetupDiGetDeviceInterfaceDetail(devInfo, ref interfaceData, mem, requiredLength, ref actualLength, IntPtr.Zero))
                        {
                            throw new Exception("SetupDiGetDeviceInterfaceDetail failed (retrieving data) " + (new Win32Exception()).ToString());
                        }

                        // Convert TCHAR string into chars.
                        if(actualLength > requiredLength)
                        {
                            throw new Exception("Consistency issue: Actual length should not be larger than buffer size.");
                        }

                        int numChars = (int)((actualLength - 4) / 2);
                        char[] stringChars = new char[numChars];
                        for (int i = 0; i < numChars; i++)
                        {
                            stringChars[i] = (char)Marshal.ReadInt16(mem, 4 + i * 2);
                        }

                        string devicePath = new string(stringChars);

                        outputPaths.Add(devicePath);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(mem);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfo);
            }

            return outputPaths.ToArray();
        }


        /*
        HDEVINFO SetupDiGetClassDevs(
          _In_opt_  const GUID *ClassGuid,
          _In_opt_  PCTSTR Enumerator,
          _In_opt_  HWND hwndParent,
          _In_      DWORD Flags
        );
         */
        [DllImport("setupapi.dll", SetLastError = true)]
        private extern static IntPtr SetupDiGetClassDevs(ref Guid classGuid, string enumerator, IntPtr hwndParent, UInt32 flags);

        /*
         BOOL SetupDiEnumDeviceInterfaces(
          _In_      HDEVINFO DeviceInfoSet,
          _In_opt_  PSP_DEVINFO_DATA DeviceInfoData,
          _In_      const GUID *InterfaceClassGuid,
          _In_      DWORD MemberIndex,
          _Out_     PSP_DEVICE_INTERFACE_DATA DeviceInterfaceData
        );
         */
        [DllImport("setupapi.dll", SetLastError = true)]
        private extern static bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr optDeviceInfoData, ref Guid interfaceClassGuid, UInt32 memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        
        /*                 
        BOOL SetupDiDestroyDeviceInfoList(
          _In_  HDEVINFO DeviceInfoSet
        );
          */
        [DllImport("setupapi.dll", SetLastError = true)]
        private extern static bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);


        /* 
        BOOL SetupDiGetDeviceInterfaceDetail(
          _In_       HDEVINFO DeviceInfoSet,
          _In_       PSP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
          _Out_opt_  PSP_DEVICE_INTERFACE_DETAIL_DATA DeviceInterfaceDetailData,
          _In_       DWORD DeviceInterfaceDetailDataSize,
          _Out_opt_  PDWORD RequiredSize,
          _Out_opt_  PSP_DEVINFO_DATA DeviceInfoData
        );
          */
        [DllImport("setupapi.dll", SetLastError = true, CharSet=CharSet.Unicode)]
        private extern static bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, 
            IntPtr deviceInterfaceDetailData, UInt32 deviceInterfaceDetailSize, ref UInt32 requiredSize, IntPtr deviceInfoData );


        /* 
        HANDLE WINAPI CreateFile(
          _In_      LPCTSTR lpFileName,
          _In_      DWORD dwDesiredAccess,
          _In_      DWORD dwShareMode,
          _In_opt_  LPSECURITY_ATTRIBUTES lpSecurityAttributes,
          _In_      DWORD dwCreationDisposition,
          _In_      DWORD dwFlagsAndAttributes,
          _In_opt_  HANDLE hTemplateFile
        );
          */
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public extern static SafeFileHandle CreateFile(string lpFileName, UInt32 dwDesiredAccess, 
            UInt32 dwShareMode, IntPtr lpSecurityAttributes, UInt32 dwCreationDisposition, UInt32 dwFlagsAndAttributes, IntPtr hTemplateFile);

        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint CREATE_NEW = 1;
        public const uint CREATE_ALWAYS = 2;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_SHARE_READ = 1;
        public const uint FILE_SHARE_WRITE = 2;



        /* 
        BOOL __stdcall WinUsb_Initialize(
          _In_   HANDLE DeviceHandle,
          _Out_  PWINUSB_INTERFACE_HANDLE InterfaceHandle
        );
          */
        [DllImport("winusb.dll", SetLastError = true)]
        public extern static bool WinUsb_Initialize(SafeFileHandle deviceHandle, out IntPtr interfaceHandle);

        /* 
        BOOL __stdcall WinUsb_Free(
          _In_  WINUSB_INTERFACE_HANDLE InterfaceHandle
        );
        */

        [DllImport("winusb.dll", SetLastError = true)]
        public extern static bool WinUsb_Free(IntPtr interfaceHandle);


        /* 
        BOOL __stdcall WinUsb_ReadPipe(
          _In_       WINUSB_INTERFACE_HANDLE InterfaceHandle,
          _In_       UCHAR PipeID,
          _Out_      PUCHAR Buffer,
          _In_       ULONG BufferLength,
          _Out_opt_  PULONG LengthTransferred,
          _In_opt_   LPOVERLAPPED Overlapped
        );
        */

        [DllImport("winusb.dll", SetLastError = true)]
        public extern static bool WinUsb_ReadPipe(IntPtr interfaceHandle, byte pipeId, byte[] buffer, uint bufferLength, IntPtr lengthTransferred, ref NativeOverlapped overlapped);

        /* 
        BOOL __stdcall WinUsb_GetOverlappedResult(
          _In_   WINUSB_INTERFACE_HANDLE InterfaceHandle,
          _In_   LPOVERLAPPED lpOverlapped,
          _Out_  LPDWORD lpNumberOfBytesTransferred,
          _In_   BOOL bWait
        );
        */

        [DllImport("winusb.dll", SetLastError = true)]
        public extern static bool WinUsb_GetOverlappedResult(IntPtr interfaceHandle, ref NativeOverlapped overlapped, out UInt32 numberOfBytesTransferred, bool wait);



    }

    public struct NativeOverlapped
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public IntPtr Pointer;
        public SafeWaitHandle Event;
    }

    public class Overlapped : IDisposable
    { 
        public Overlapped()
        {
            WaitEvent = new ManualResetEvent(false);
            OverlappedStruct = new NativeOverlapped();
            OverlappedStruct.Event = WaitEvent.SafeWaitHandle;
        }
        public void Dispose()
        {
            WaitEvent.Dispose();
            GC.SuppressFinalize(this);
        }

        public ManualResetEvent WaitEvent;
        public NativeOverlapped OverlappedStruct;
    }

}
