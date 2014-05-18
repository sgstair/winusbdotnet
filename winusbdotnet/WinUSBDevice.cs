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



    }
}
