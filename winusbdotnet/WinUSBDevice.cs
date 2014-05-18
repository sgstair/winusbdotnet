using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;

namespace winusbdotnet
{

    public class WinUSBDevice : IDisposable
    {
        public static string[] EnumerateDevices(Guid deviceInterfaceGuid)
        {
            return NativeMethods.EnumerateDevicesByInterface(deviceInterfaceGuid);
        }


        string myDevicePath;
        SafeFileHandle deviceHandle;
        IntPtr WinusbHandle;

        public WinUSBDevice(string devicePath)
        {
            myDevicePath = devicePath;

            deviceHandle = NativeMethods.CreateFile(devicePath, NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING,
                NativeMethods.FILE_ATTRIBUTE_NORMAL | NativeMethods.FILE_FLAG_OVERLAPPED, IntPtr.Zero);

            if(deviceHandle.IsInvalid)
            {
                throw new Exception("Could not create file. " + (new Win32Exception()).ToString());
            }

            if(!NativeMethods.WinUsb_Initialize(deviceHandle, out WinusbHandle))
            {
                WinusbHandle = IntPtr.Zero;
                throw new Exception("Could not Initialize WinUSB. " + (new Win32Exception()).ToString());
            }
            

        }

        public void Dispose()
        {
            if(WinusbHandle != IntPtr.Zero)
            {
                NativeMethods.WinUsb_Free(WinusbHandle);
                WinusbHandle = IntPtr.Zero;
            }
            deviceHandle.Close();
            GC.SuppressFinalize(this);
        }

        public void Close()
        {
            Dispose();
        }

        // Hacky synchronous read
        public byte[] ReadPipe(byte pipeId, int byteCount)
        {
            byte[] data = new byte[byteCount];

            using (Overlapped ov = new Overlapped())
            {
                if (!NativeMethods.WinUsb_ReadPipe(WinusbHandle, pipeId, data, (uint)byteCount, IntPtr.Zero, ref ov.OverlappedStruct))
                {
                    if (Marshal.GetLastWin32Error() != NativeMethods.ERROR_IO_PENDING)
                    {
                        throw new Exception("ReadPipe failed. " + (new Win32Exception()).ToString());
                    }
                    // Wait for IO to complete.
                    ov.WaitEvent.WaitOne();
                }
                UInt32 transferSize;

                if (!NativeMethods.WinUsb_GetOverlappedResult(WinusbHandle, ref ov.OverlappedStruct, out transferSize, false))
                {
                    throw new Exception("ReadPipe's overlapped result failed. " + (new Win32Exception()).ToString());
                }

                byte[] newdata = new byte[transferSize];
                Array.Copy(data, newdata, transferSize);
                return newdata;
            }
        }

    }
}
