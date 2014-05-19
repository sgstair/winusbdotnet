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

        public UInt32 GetPipePolicy(byte pipeId, WinUsbPipePolicy policyType)
        {
            UInt32[] data = new UInt32[1];
            UInt32 length = 4;

            if(!NativeMethods.WinUsb_GetPipePolicy(WinusbHandle, pipeId, (uint)policyType, ref length, data))
            {
                throw new Exception("GetPipePolicy failed. " + (new Win32Exception()).ToString());
            }

            return data[0];
        }

        public void SetPipePolicy(byte pipeId, WinUsbPipePolicy policyType, UInt32 newValue)
        {
            UInt32[] data = new UInt32[1];
            UInt32 length = 4;
            data[0] = newValue;

            if (!NativeMethods.WinUsb_SetPipePolicy(WinusbHandle, pipeId, (uint)policyType, length, data))
            {
                throw new Exception("SetPipePolicy failed. " + (new Win32Exception()).ToString());
            }
        }


        public byte[] ReadExactPipe(byte pipeId, int byteCount)
        {
            int read = 0;
            byte[] accumulate = null;
            while (read < byteCount)
            {
                byte[] data = ReadPipe(pipeId, byteCount - read);
                if (data.Length == byteCount) return data;
                if (accumulate == null)
                {
                    accumulate = new byte[byteCount];
                }
                Array.Copy(data, 0, accumulate, read, data.Length);
                read += data.Length;
            }
            return accumulate;
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
                    //ov.WaitEvent.WaitOne();
                }
                UInt32 transferSize;

                if (!NativeMethods.WinUsb_GetOverlappedResult(WinusbHandle, ref ov.OverlappedStruct, out transferSize, true))
                {
                    throw new Exception("ReadPipe's overlapped result failed. " + (new Win32Exception()).ToString());
                }

                byte[] newdata = new byte[transferSize];
                Array.Copy(data, newdata, transferSize);
                return newdata;
            }
        }

        // hacky synchronous send.
        public void WritePipe(byte pipeId, byte[] pipeData)
        {
            using (Overlapped ov = new Overlapped())
            {
                int remainingbytes = pipeData.Length;
                while (remainingbytes > 0)
                {
                    if (!NativeMethods.WinUsb_WritePipe(WinusbHandle, pipeId, pipeData, (uint)pipeData.Length, IntPtr.Zero, ref ov.OverlappedStruct))
                    {
                        if (Marshal.GetLastWin32Error() != NativeMethods.ERROR_IO_PENDING)
                        {
                            throw new Exception("WritePipe failed. " + (new Win32Exception()).ToString());
                        }
                        // Wait for IO to complete.
                        //ov.WaitEvent.WaitOne();
                    }
                    UInt32 transferSize;

                    if (!NativeMethods.WinUsb_GetOverlappedResult(WinusbHandle, ref ov.OverlappedStruct, out transferSize, true))
                    {
                        throw new Exception("WritePipe's overlapped result failed. " + (new Win32Exception()).ToString());
                    }

                    if (transferSize == pipeData.Length) return;

                    remainingbytes -= (int)transferSize;

                    // Need to retry. Copy the remaining data to a new buffer.
                    byte[] data = new byte[remainingbytes];
                    Array.Copy(pipeData, transferSize, data, 0, remainingbytes);

                    pipeData = data;
                }
            }

        }

    }
}
