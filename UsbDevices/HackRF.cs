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
using System.Text;
using System.Threading.Tasks;


using winusbdotnet;

namespace winusbdotnet.UsbDevices
{
    public class HackRF
    {
        public static IEnumerable<WinUSBEnumeratedDevice> Enumerate()
        {
            foreach (WinUSBEnumeratedDevice dev in WinUSBDevice.EnumerateAllDevices())
            {
                // HackRF One
                if (dev.VendorID == 0x1d50 && dev.ProductID == 0x6089 && dev.UsbInterface == 0)
                {
                    yield return dev;
                }
                // HackRF Jawbreaker
                if (dev.VendorID == 0x1d50 && dev.ProductID == 0x604b && dev.UsbInterface == 0)
                {
                    yield return dev;
                }
            }
        }

        const byte EP_RX = 0x81; // IN 1
        const byte EP_TX = 0x02; // OUT 2

        WinUSBDevice Device;
        IPipePacketReader RxPipeReader;

        public HackRF(WinUSBEnumeratedDevice dev)
        {
            Device = new WinUSBDevice(dev);

            // Set a bunch of sane defaults.
            SetSampleRate(10000000); // 10MHz
            SetFilterBandwidth(10000000);
            SetLnaGain(8);
            SetVgaGain(20);
            SetTxVgaGain(0);
        }

        public void Close()
        {
            Device.Close();
            Device = null;
        }


        void RxDataCallback()
        {
            lock (this)
            {
                while (RxPipeReader.QueuedPackets > 0)
                {
                    int len = RxPipeReader.DequeuePacket().Length;
                    BytesEaten += len;
                    if (!EatenHistogram.ContainsKey(len)) { EatenHistogram.Add(len, 0); }
                    EatenHistogram[len]++;
                    PacketsEaten++;
                }
            }
        }

        public int PacketsEaten; // debug
        public long BytesEaten;
        public Dictionary<int, int> EatenHistogram = new Dictionary<int, int>();

        enum DeviceRequest
        {
            SetTransceiverMode = 1,
            SetSampleRate = 6,
            SetFilterBandwidth = 7,
            VersionStringRead = 15,
            SetFrequency = 16,
            AmpEnable = 17,
            SetLnaGain = 19,
            SetVgaGain = 20,
            SetTxVgaGain = 21
        }

        enum TransceiverMode
        {
            Off = 0,
            Receive = 1,
            Transmit = 2,
        }


        byte[] VendorRequestIn(DeviceRequest request, ushort value, ushort index, ushort length)
        {
            byte requestType = WinUSBDevice.ControlRecipientDevice | WinUSBDevice.ControlTypeVendor;

            return Device.ControlTransferIn(requestType, (byte)request, value, index, length);
        }
        void VendorRequestOut(DeviceRequest request, ushort value, ushort index, byte[] data)
        {
            byte requestType = WinUSBDevice.ControlRecipientDevice | WinUSBDevice.ControlTypeVendor;
            Device.ControlTransferOut(requestType, (byte)request, value, index, data);
        }


        void SetTransceiverMode(TransceiverMode mode)
        {
            VendorRequestOut(DeviceRequest.SetTransceiverMode, (ushort)mode, 0, null);
        }


        public string ReadVersion()
        {
            byte[] data = VendorRequestIn(DeviceRequest.VersionStringRead, 0, 0, 255);
            return new string(data.Select(b => (char)b).ToArray());
        }

        public void ModeOff()
        {
            SetTransceiverMode(TransceiverMode.Off);
        }

        public void ModeReceive()
        {
            SetTransceiverMode(TransceiverMode.Receive);

            Device.EnableBufferedRead(EP_RX, 4,64);
            RxPipeReader = Device.BufferedGetPacketInterface(EP_RX);
            Device.BufferedReadNotifyPipe(EP_RX, RxDataCallback);
        }

        public void ModeTransmit()
        {
            SetTransceiverMode(TransceiverMode.Transmit);
        }

        public void SetSampleRate(uint integerFrequency, uint divider = 1)
        {
            byte[] parameters = new byte[8];

            byte[] value = BitConverter.GetBytes(integerFrequency);
            Array.Copy(value, parameters, 4);

            value = BitConverter.GetBytes(divider);
            Array.Copy(value, 0, parameters, 4, 4);

            VendorRequestOut(DeviceRequest.SetSampleRate, 0, 0, parameters);
        }

        public uint SetFilterBandwidth(uint requestedFilterBandwidth)
        {
            uint[] filterValues = { 1750000, 2250000, 3500000, 5000000,
                                    5500000, 6000000, 7000000, 8000000,
                                    9000000,10000000,12000000,14000000,
                                   15000000,20000000,24000000,28000000 };

            uint actualBandwidth = filterValues.TakeWhile(value => value < requestedFilterBandwidth).LastOrDefault();
            if (actualBandwidth == 0) 
                actualBandwidth = filterValues[0];

            VendorRequestOut(DeviceRequest.SetFilterBandwidth,(ushort)(actualBandwidth&0xFFFF), (ushort)(actualBandwidth>>16), null);

            return actualBandwidth;
        }

        public void SetFrequency(ulong frequency)
        {
            uint mhz = (uint)(frequency / 1000000);
            uint hz = (uint)(frequency % 1000000);

            byte[] parameters = new byte[8];

            byte[] value = BitConverter.GetBytes(mhz);
            Array.Copy(value, parameters, 4);

            value = BitConverter.GetBytes(hz);
            Array.Copy(value, 0, parameters, 4, 4);

            VendorRequestOut(DeviceRequest.SetFrequency, 0, 0, parameters);
        }

        public void SetLnaGain(uint gaindB)
        {
            if (gaindB > 40) throw new ArgumentOutOfRangeException("gaindB", "Lna Gain should be in the range 0-40");
            byte[] check = VendorRequestIn(DeviceRequest.SetLnaGain, 0, (ushort)gaindB, 1);
        }
        public void SetVgaGain(uint gaindB)
        {
            if (gaindB > 62) throw new ArgumentOutOfRangeException("gaindB","Vga Gain should be in the range 0-62");
            byte[] check = VendorRequestIn(DeviceRequest.SetVgaGain, 0, (ushort)gaindB, 1);            
        }
        public void SetTxVgaGain(uint gaindB)
        {
            if (gaindB > 47) throw new ArgumentOutOfRangeException("gaindB", "TxVga Gain should be in the range 0-47");
            byte[] check = VendorRequestIn(DeviceRequest.SetTxVgaGain, 0, (ushort)gaindB, 1);    
        }


    }
}
