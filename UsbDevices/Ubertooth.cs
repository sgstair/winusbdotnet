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
    public class Ubertooth
    {
        public static IEnumerable<WinUSBEnumeratedDevice> Enumerate()
        {
            return WinUSBDevice.EnumerateDevices(new Guid("8ac47a88-cc26-4aa9-887b-42ca8cf07a63"));
        }

        WinUSBDevice Device;

        public Ubertooth(WinUSBEnumeratedDevice dev)
        {
            Device = new WinUSBDevice(dev);

        }

        public enum DeviceRequest
        {
            Ping = 0,
            RxSymbols = 1,
            TxSymbols = 2,
            GetUsrLed = 3,
            SetUsrLed = 4,
            GetRxLed = 5,
            SetRxLed = 6,
            GetTxLed = 7,
            SetTxLed = 8,
            GetChannel = 11,
            SetChannel = 12,
            Reset = 13,
            GetSerial = 14,
            GetPartnum = 15,
            GetPaEn = 16,
            SetPaEn = 17,
            GetHgm = 18,
            SetHgm = 19,
            TxTest = 20,
            Stop = 21,
            GetMod = 22,
            SetMod = 23,
            SetIsp = 24,
            Flash = 25,
            BootloaderFlash = 26,
            SpecAn = 27,
            GetPaLevel = 28,
            SetPaLevel = 29,
            Repeater = 30,
            RangeTest = 31,
            RangeCheck=32,
            GetRevNum = 33,
            LedSpecAn = 34,
            GetBoardId = 35,

        }

        public enum ModulationType
        {
            BtBasicRate = 0,
            BtLowEnergy = 1,
            FHSS80211 = 2
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

        byte GetByte(DeviceRequest request)
        {
            byte[] data = VendorRequestIn(request, 0, 0, 1);
            return data[0];
        }
        UInt16 GetU16(DeviceRequest request)
        {
            byte[] data = VendorRequestIn(request, 0, 0, 2);
            return BitConverter.ToUInt16(data, 0);
        }

        bool GetLed(DeviceRequest request)
        {
            return GetByte(request) != 0;
        }
        void SetLed(DeviceRequest request, bool value)
        {
            VendorRequestOut(request, (ushort)(value ? 1 : 0), 0, null);
        }



        public void Ping()
        {
            VendorRequestOut(DeviceRequest.Ping, 0, 0, null);
        }

        public bool UsrLed
        {
            get { return GetLed(DeviceRequest.GetUsrLed); }
            set { SetLed(DeviceRequest.SetUsrLed, value); }
        }
        public bool RxLed
        {
            get { return GetLed(DeviceRequest.GetRxLed); }
            set { SetLed(DeviceRequest.SetRxLed, value); }
        }
        public bool TxLed
        {
            get { return GetLed(DeviceRequest.GetTxLed); }
            set { SetLed(DeviceRequest.SetTxLed, value); }
        }

        public UInt16 Channel
        {
            get { return GetU16(DeviceRequest.GetChannel); }
            set { VendorRequestOut(DeviceRequest.SetChannel, value, 0, null); }
        }

        public UInt32 PartNumber
        {
            get
            {
                byte[] data = VendorRequestIn(DeviceRequest.GetPartnum, 0, 0, 5);
                if (data[0] != 0) throw new Exception("Operation failed");
                return BitConverter.ToUInt32(data, 1);
            }
        }

        public byte[] RawSerialNumber
        {
            get
            {
                byte[] data = VendorRequestIn(DeviceRequest.GetSerial, 0, 0, 17);
                if (data[0] != 0) throw new Exception("Operation failed");
                byte[] output = new byte[16];
                Array.Copy(data, 1, output, 0, 16);
                return output;
            }
        }

        public string SerialNumber
        {
            get
            {
                byte[] serialNumber = RawSerialNumber;
                return string.Format("{0:x8}{1:x8}{2:x8}{3:x8}",
                                     BitConverter.ToUInt32(serialNumber, 0),
                                     BitConverter.ToUInt32(serialNumber, 4),
                                     BitConverter.ToUInt32(serialNumber, 8),
                                     BitConverter.ToUInt32(serialNumber, 12));
            }
        }

        public ModulationType Modulation
        {
            set
            {
                VendorRequestOut(DeviceRequest.SetMod, (ushort)value, 0, null);
            }
        }


    }
}
