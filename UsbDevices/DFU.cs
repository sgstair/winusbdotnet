/*
Copyright (c) 2016 Stephen Stair (sgstair@akkit.org)

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
    public class DFU
    {


        WinUSBDevice BaseDevice;
        ushort DfuInterface;
        DfuAttributes Attributes;
        ushort DetachTimeout;
        ushort TransferSize;
        ushort DfuVersion;

        public DFU(WinUSBEnumeratedDevice dev)
        {
            BaseDevice = new WinUSBDevice(dev);

            // Read DFU descriptors to fill in important details.
            FindDfuInterface();
        }

        void FindDfuInterface()
        {
            ConfigurationDescriptor cfg = BaseDevice.GetConfigurationDescriptor();

            DfuInterface = 0xFFFF;

            bool done = false;
            foreach(InterfaceDescriptor i in cfg.Interfaces)
            {
                if(i.Class == 0xFE && i.SubClass == 1) // DFU Interface
                {
                    // Look for DFU descriptor
                    foreach(DescriptorNode n in i.RawSubDescriptors) // Todo: change to use interface subdescriptors rather than raw list (once implemented)
                    {
                        if(n.RawType == 0x21 && n.Data.Length == 7)
                        {
                            // Extract values from it.
                            Attributes = (DfuAttributes)n.ReadByte(2);

                            DetachTimeout = n.ReadShort(3);
                            TransferSize = n.ReadShort(5);
                            DfuVersion = n.ReadShort(7);


                            // Confirm this is the interface we need.
                            DfuInterface = i.Number;

                            done = true;
                            break;
                        }
                    }
                    if (done)
                    {
                        break;
                    }
                }
            }

            if(DfuInterface == 0xFFFF)
            {
                throw new Exception("Unable to find DFU descriptor on device.");
            }
        }

        [Flags]
        enum DfuAttributes
        {
            CanDnload = 1,
            CanUpload = 2,
            ManifestationTolerant = 4,
            WillDetach = 8
        }

        enum DfuRequest
        {
            Detach = 0,
            Dnload = 1,
            Upload = 2,
            GetStatus = 3,
            ClrStatus = 4,
            GetState = 5,
            Abort = 6
        }

        byte[] DfuIn(DfuRequest req, UInt16 value, UInt16 length)
        {
            return BaseDevice.ControlTransferIn(WinUSBDevice.ControlTypeClass | WinUSBDevice.ControlRecipientInterface, (byte)req, value, DfuInterface, length);
        }

    }
}
