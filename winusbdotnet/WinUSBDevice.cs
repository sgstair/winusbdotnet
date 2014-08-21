/*
Copyright (c) 2014 Stephen Stair (sgstair@akkit.org)

Permission is hereby granted, free of charge, to any person obtaining a copy
 of this software and associated documentation files (the "Software"), to deal
 in the Software without restriction, including without limitation the rights
 to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 copies of the Software, and to permit persons to whom the Software is
 furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
 all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 THE SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;

namespace winusbdotnet
{

    public class WinUSBEnumeratedDevice
    {
        internal string DevicePath;
        internal EnumeratedDevice EnumeratedData;
        internal WinUSBEnumeratedDevice(EnumeratedDevice enumDev)
        {
            DevicePath = enumDev.DevicePath;
            EnumeratedData = enumDev;
            Match m = Regex.Match(DevicePath, @"vid_([\da-f]{4})");
            if (m.Success) { VendorID = Convert.ToUInt16(m.Groups[1].Value, 16); }
            m = Regex.Match(DevicePath, @"pid_([\da-f]{4})");
            if (m.Success) { ProductID = Convert.ToUInt16(m.Groups[1].Value, 16); }
            m = Regex.Match(DevicePath, @"mi_([\da-f]{2})");
            if (m.Success) { UsbInterface = Convert.ToByte(m.Groups[1].Value, 16); }
        }

        public string Path { get { return DevicePath; } }
        public UInt16 VendorID { get; private set; }
        public UInt16 ProductID { get; private set; }
        public Byte UsbInterface { get; private set; }
        public Guid InterfaceGuid { get { return EnumeratedData.InterfaceGuid; } }


        public override string ToString()
        {
            return string.Format("WinUSBEnumeratedDevice({0},{1})", DevicePath, InterfaceGuid);
        }
    }

    public class WinUSBDevice : IDisposable
    {
        public static IEnumerable<WinUSBEnumeratedDevice> EnumerateDevices(Guid deviceInterfaceGuid)
        {
            foreach (EnumeratedDevice devicePath in NativeMethods.EnumerateDevicesByInterface(deviceInterfaceGuid))
            {
                yield return new WinUSBEnumeratedDevice(devicePath);
            }
        }

        public static IEnumerable<WinUSBEnumeratedDevice> EnumerateAllDevices()
        {
            foreach (EnumeratedDevice devicePath in NativeMethods.EnumerateAllWinUsbDevices())
            {
                yield return new WinUSBEnumeratedDevice(devicePath);
            }
        }
        public delegate void NewDataCallback();

        string myDevicePath;
        SafeFileHandle deviceHandle;
        IntPtr WinusbHandle;

        internal bool Stopping = false;

        public WinUSBDevice(WinUSBEnumeratedDevice deviceInfo)
        {
            myDevicePath = deviceInfo.DevicePath;

            deviceHandle = NativeMethods.CreateFile(myDevicePath, NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
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


        public byte AlternateSetting
        {
            get
            {
                byte alt;
                if (!NativeMethods.WinUsb_GetCurrentAlternateSetting(WinusbHandle, out alt))
                {
                    throw new Exception("GetCurrentAlternateSetting failed. " + (new Win32Exception()).ToString());
                }
                return alt;
            }
            set
            {
                if (!NativeMethods.WinUsb_SetCurrentAlternateSetting(WinusbHandle, value))
                {
                    throw new Exception("SetCurrentAlternateSetting failed. " + (new Win32Exception()).ToString());
                }
            }
        }


        public void Dispose()
        {
            Stopping = true;
            // Wait for pipe threads to quit
            foreach(BufferedPipeThread th in bufferedPipes.Values)
            {
                while (!th.Stopped) Thread.Sleep(5);
            }

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

        public void FlushPipe(byte pipeId)
        {
            if (!NativeMethods.WinUsb_FlushPipe(WinusbHandle, pipeId))
            {
                throw new Exception("FlushPipe failed. " + (new Win32Exception()).ToString());
            }
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

        Dictionary<byte, BufferedPipeThread> bufferedPipes = new Dictionary<byte, BufferedPipeThread>();
        public void EnableBufferedRead(byte pipeId)
        {
            if(!bufferedPipes.ContainsKey(pipeId))
            {
                bufferedPipes.Add(pipeId, new BufferedPipeThread(this, pipeId));
            }
        }

        public void BufferedReadNotifyPipe(byte pipeId, NewDataCallback callback)
        {
            if (!bufferedPipes.ContainsKey(pipeId))
            {
                throw new Exception("Pipe not enabled for buffered reads!");
            }
            bufferedPipes[pipeId].NewDataEvent += callback;
        }


        public byte[] BufferedReadPipe(byte pipeId, int byteCount)
        {
            if (!bufferedPipes.ContainsKey(pipeId))
            {
                throw new Exception("Pipe not enabled for buffered reads!");
            }
            return bufferedPipes[pipeId].ReceiveBytes(byteCount);
        }
        public byte[] BufferedPeekPipe(byte pipeId, int byteCount)
        {
            if (!bufferedPipes.ContainsKey(pipeId))
            {
                throw new Exception("Pipe not enabled for buffered reads!");
            }
            return bufferedPipes[pipeId].PeekBytes(byteCount);
        }

        public void BufferedSkipBytesPipe(byte pipeId, int byteCount)
        {
            if (!bufferedPipes.ContainsKey(pipeId))
            {
                throw new Exception("Pipe not enabled for buffered reads!");
            }
            bufferedPipes[pipeId].SkipBytes(byteCount);
        }

        public byte[] BufferedReadExactPipe(byte pipeId, int byteCount)
        {
            if (!bufferedPipes.ContainsKey(pipeId))
            {
                throw new Exception("Pipe not enabled for buffered reads!");
            }
            return bufferedPipes[pipeId].ReceiveExactBytes(byteCount);
        }
        public int BufferedByteCountPipe(byte pipeId)
        {
            if (!bufferedPipes.ContainsKey(pipeId))
            {
                throw new Exception("Pipe not enabled for buffered reads!");
            }
            return bufferedPipes[pipeId].QueuedDataLength;
        }


        public byte[] ReadExactPipe(byte pipeId, int byteCount)
        {
            int read = 0;
            byte[] accumulate = null;
            while (read < byteCount)
            {
                byte[] data = ReadPipe(pipeId, byteCount - read);
                if(data.Length  == 0)
                {
                    // Timeout happened in ReadPipe.
                    throw new Exception("Timed out while trying to read data.");
                }
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

            //using (Overlapped ov = new Overlapped())
            {
                /*
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
                    if(Marshal.GetLastWin32Error() == NativeMethods.ERROR_SEM_TIMEOUT)
                    { 
                        // This was a pipe timeout. Return an empty byte array to indicate this case.
                        System.Diagnostics.Debug.WriteLine("Timed out");
                        return new byte[0];
                    }
                    throw new Exception("ReadPipe's overlapped result failed. " + (new Win32Exception()).ToString());
                }
                 * 
                 * */

                UInt32 transferSize = 0;
                if (!NativeMethods.WinUsb_ReadPipe(WinusbHandle, pipeId, data, (uint)byteCount, ref transferSize, IntPtr.Zero))
                {
                    if (Marshal.GetLastWin32Error() == NativeMethods.ERROR_SEM_TIMEOUT)
                    {
                        // This was a pipe timeout. Return an empty byte array to indicate this case.
                        return new byte[0];
                    }
                    throw new Exception("ReadPipe failed. " + (new Win32Exception()).ToString());
                }

                byte[] newdata = new byte[transferSize];
                Array.Copy(data, newdata, transferSize);
                return newdata;
            }
        }

        // hacky synchronous send.
        public void WritePipe(byte pipeId, byte[] pipeData)
        {
            //using (Overlapped ov = new Overlapped())
            {
                int remainingbytes = pipeData.Length;
                while (remainingbytes > 0)
                {
                    /*
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
                     */

                    UInt32 transferSize = 0;
                    if (!NativeMethods.WinUsb_WritePipe(WinusbHandle, pipeId, pipeData, (uint)pipeData.Length, ref transferSize, IntPtr.Zero))
                    {
                        throw new Exception("WritePipe failed. " + (new Win32Exception()).ToString());
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

    // Naive and non-performant version to get something running quickly.
    internal class BufferedPipeThread
    {
        Thread PipeThread;
        WinUSBDevice Device;
        byte DevicePipeId;

        private int QueuedLength;
        private Queue<byte[]> ReceivedData;
        private int SkipFirstBytes;
        public bool Stopped = false;

        ManualResetEvent ReceiveTick;

        public BufferedPipeThread(WinUSBDevice dev, byte pipeId)
        {
            Device = dev;
            DevicePipeId = pipeId;
            QueuedLength = 0;
            ReceivedData = new Queue<byte[]>();
            ReceiveTick = new ManualResetEvent(false);
            PipeThread = new Thread(ThreadFunc);
            PipeThread.IsBackground = true;
            PipeThread.Start();
        }

        public int QueuedDataLength { get { lock (this) { return QueuedLength;  } } }

        // Only returns as many as it can.
        public byte[] ReceiveBytes(int count)
        {
            int queue = QueuedDataLength;
            if (queue < count) 
                count = queue;

            byte[] output = new byte[count];
            lock (this)
            {
                CopyReceiveBytes(output, 0, count);
            }
            return output;
        }

        // Only returns as many as it can.
        public byte[] PeekBytes(int count)
        {
            int queue = QueuedDataLength;
            if (queue < count)
                count = queue;

            byte[] output = new byte[count];
            lock (this)
            {
                CopyPeekBytes(output, 0, count);
            }
            return output;
        }

        public byte[] ReceiveExactBytes(int count)
        {
            byte[] output = new byte[count];
            if (QueuedDataLength >= count)
            {
                lock (this)
                {
                    CopyReceiveBytes(output, 0, count);
                }
                return output;
            }
            int failedcount = 0;
            int haveBytes = 0;
            while (haveBytes < count)
            {
                ReceiveTick.Reset();
                lock (this)
                {
                    int thisBytes = QueuedLength;

                    if(thisBytes == 0)
                    {
                        failedcount++;
                        if(failedcount > 3)
                        {
                            throw new Exception("Timed out waiting to receive bytes");
                        }
                    }
                    else
                    {
                        failedcount = 0;
                        if (thisBytes + haveBytes > count) thisBytes = count - haveBytes;
                        CopyReceiveBytes(output, haveBytes, thisBytes);
                    }
                    haveBytes += (int)thisBytes;
                }
                if(haveBytes < count)
                {
                    if (Stopped) throw new Exception("Not going to have enough bytes to complete request.");
                    ReceiveTick.WaitOne();
                }
            }
            return output;
        }

        // Must be called under lock with enough bytes in the buffer.
        void CopyReceiveBytes(byte[] target, int start, int count)
        {
            int copied = 0;
            while(copied < count)
            {
                byte[] firstData = ReceivedData.Peek();
                int available = firstData.Length - SkipFirstBytes;
                int toCopy = count - copied;
                if (toCopy > available) toCopy = available;

                Array.Copy(firstData, SkipFirstBytes, target, start, toCopy);

                if(toCopy == available)
                {
                    ReceivedData.Dequeue();
                    SkipFirstBytes = 0;
                }
                else
                {
                    SkipFirstBytes += toCopy;
                }

                copied += toCopy;
                start += toCopy;
                QueuedLength -= toCopy;
            }
        }

        // Must be called under lock with enough bytes in the buffer.
        void CopyPeekBytes(byte[] target, int start, int count)
        {
            int copied = 0;
            int skipBytes = SkipFirstBytes;

            foreach(byte[] firstData in ReceivedData)
            {
                int available = firstData.Length - skipBytes;
                int toCopy = count - copied;
                if (toCopy > available) toCopy = available;

                Array.Copy(firstData, skipBytes, target, start, toCopy);

                skipBytes = 0;

                copied += toCopy;
                start += toCopy;

                if (copied >= count)
                {
                    break;
                }
            }
        }

        public void SkipBytes(int count)
        {
            lock (this)
            {
                int queue = QueuedLength;
                if (queue < count)
                    throw new ArgumentException("count must be less than the data length");

                int copied = 0;
                while (copied < count)
                {
                    byte[] firstData = ReceivedData.Peek();
                    int available = firstData.Length - SkipFirstBytes;
                    int toCopy = count - copied;
                    if (toCopy > available) toCopy = available;

                    if (toCopy == available)
                    {
                        ReceivedData.Dequeue();
                        SkipFirstBytes = 0;
                    }
                    else
                    {
                        SkipFirstBytes += toCopy;
                    }

                    copied += toCopy;
                    QueuedLength -= toCopy;
                }
            }
        }


        void ThreadFunc(object context)
        {
            
            while(true)
            {
                if (Device.Stopping)
                    break;

                try
                {
                    byte[] data = Device.ReadPipe(DevicePipeId, 512);

                    if(data.Length > 0)
                    {
                        lock(this)
                        {
                            ReceivedData.Enqueue(data);
                            QueuedLength += data.Length;
                        }
                        ThreadPool.QueueUserWorkItem(RaiseNewData);
                    }
                }
                catch
                {
                    Thread.Sleep(15);
                }

                ReceiveTick.Set();

            }
            Stopped = true;
        }

        public event WinUSBDevice.NewDataCallback NewDataEvent;

        void RaiseNewData(object context)
        {
            WinUSBDevice.NewDataCallback cb = NewDataEvent;
            if (cb != null)
            {
                cb();
            }
        }

    }

}
