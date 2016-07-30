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


        public WinUSBDevice BaseDevice;
        ushort DfuInterface;
        DfuAttributes Attributes;
        ushort DetachTimeout;
        ushort MaxTransferSize;
        ushort DfuVersion;
        bool DfuMode;

        /// <summary>
        /// If the device is not in DFU mode, it must be detached in order to activate DFU mode and allow transfer.
        /// </summary>
        public bool InDfuMode {  get { return DfuMode; } }
        public bool CanDownload {  get { return (Attributes & DfuAttributes.CanDnload) != 0; } }

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
                            MaxTransferSize = n.ReadShort(5);
                            DfuVersion = n.ReadShort(7);

                            DfuMode = i.Protocol == 2;

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

        void DfuOut(DfuRequest req, UInt16 value, byte[] data)
        {
            BaseDevice.ControlTransferOut(WinUSBDevice.ControlTypeClass | WinUSBDevice.ControlRecipientInterface, (byte)req, value, DfuInterface, data);
        }

        /// <summary>
        /// Enter DFU mode. Device will re-enumerate as a different device and it will be necessary to re-identify and create a new DFU device class to speak with it.
        /// </summary>
        public void EnterDfuMode()
        {
            if((Attributes & DfuAttributes.WillDetach) == 0)
            {
                throw new NotImplementedException("Device requires a bus reset, code for this has not yet been written.");
            }
            try
            {
                DfuOut(DfuRequest.Detach, DetachTimeout, new byte[0]); // Device is going to re-enumerate itself.
            }
            catch(Exception ex)
            {
                // If there was an exception, try again but with the wrong direction on the packet
                // (My NXP device does not respond correctly to the above request)
                DfuIn(DfuRequest.Detach, 0, 0);
            }
        }

        public DfuStatus GetStatus()
        {
            byte[] status = DfuIn(DfuRequest.GetStatus, 0, 6);
            DfuStatus ds = new DfuStatus(status);
            // Todo: return custom string if status ErrVendor.
            return ds;
        }


        void DfuDnload(int blockNum, byte[] data)
        {
            DfuOut(DfuRequest.Dnload, (ushort)(blockNum & 0xFFFF), data);
        }


        public void ProgramFirmware(byte[] FirmwareData)
        {
            if ((Attributes & DfuAttributes.CanDnload) == 0)
            {
                // Device is unable to download firmware.
                throw new Exception("DFU Device is not capable of downloading firmware.");
            }

            DfuStatus status = GetStatus();
            if (status.State != DfuState.AppIdle && status.State != DfuState.DfuIdle)
            {
                // Currently don't have any logic to apply corrective actions to this state.
                throw new Exception("Expect DFU to be in idle state before programming. Status: " + status.ToString());
            }

            int blockSize = 4096;
            if (blockSize > MaxTransferSize)
            {
                blockSize = MaxTransferSize;
            }
            byte[] block = new byte[blockSize];

            // Send the firmware to the device in blocks.
            int cursor = 0;
            int blockIndex = 0;
            while (cursor < FirmwareData.Length)
            {
                int copyLength = FirmwareData.Length - cursor;
                if (copyLength > blockSize) { copyLength = blockSize; }

                Array.Copy(FirmwareData, cursor, block, 0, copyLength);
                if (copyLength < blockSize)
                {
                    Array.Clear(block, copyLength, blockSize - copyLength);
                }

                DfuDnload(blockIndex, block);

                int timeout = 30000;
                while (true)
                {
                    status = GetStatus();
                    if (status.Status != DfuStatusValue.Ok)
                    {
                        throw new Exception("DFU Error status while downloading firmware: " + status.ToString());
                    }
                    if (status.State == DfuState.DfuDownloadIdle)
                    {
                        // Continue sending data
                        break;
                    }
                    timeout -= status.Polltimeout + 15;
                    if (timeout < 0) { throw new Exception("Timeout while waiting for DFU status. Current: " + status.ToString()); }
                    System.Threading.Thread.Sleep(status.Polltimeout);
                }

                cursor += copyLength;
            }

            // Transfer is completed, now send a zero-length DFU Dnload request followed by getting status to complete the transfer.

            DfuDnload(0, new byte[0] { });

            status = GetStatus();
            if (status.Status != DfuStatusValue.Ok)
            {
                throw new Exception("DFU Error status while manifesting firmware: " + status.ToString());
            }
            if (status.State != DfuState.DfuManifest)
            {
                throw new Exception("DFU unexpected state while manifesting firmware: " + status.ToString());
            }
        }
    }

    public enum DfuStatusValue
    {
        Ok = 0,
        ErrTarget = 1,
        ErrFile = 2,
        ErrWrite = 3,
        ErrErase=4,
        ErrCheckErased = 5,
        ErrProg = 6,
        ErrVerify = 7,
        ErrAddress = 8,
        ErrNotDone = 9,
        ErrFirmware = 10,
        ErrVendor = 11,
        ErrUsbReset = 12,
        ErrPowerOnReset = 13,
        ErrUnknown = 14,
        ErrStalledPacket = 15
    };

    public enum DfuState
    {
        AppIdle = 0,
        AppDetach = 1,
        DfuIdle = 2,
        DfuDownloadSync = 3,
        DfuDownloadBusy = 4,
        DfuDownloadIdle = 5,
        DfuManifestSync = 6,
        DfuManifest = 7,
        DfuManifestWaitReset = 8,
        DfuUploadIdle = 9,
        DfuError = 10
    };

    public class DfuStatus
    {
        static string[] DfuStatusStrings = new string[] {
            "No error condition is present.",
            "File is not targeted for use by this device.",
            "File is for this device but fails some vendor-specific verification test.",
            "Device is unable to write memory.",
            "Memory erase function failed.",
            "Memory erase check failed.",
            "Program memory function failed.",
            "Programmed memory failed verification.",
            "Cannot program memory due to received address that is out of range.",
            "Received DFU_DNLOAD with wLength = 0, but device does not think it has all of the data yet.",
            "Device’s firmware is corrupt. It cannot return to run-time (non-DFU) operations.",
            "iString indicates a vendor-specific error.",
            "Device detected unexpected USB reset signaling.",
            "Device detected unexpected power on reset.",
            "Something went wrong, but the device does not know what it was.",
            "Device stalled an unexpected request."
        };
        static string[] DfuStateStrings = new string[] {
            "Device is running its normal application.",
            "Device is running its normal application, has received the DFU_DETACH request, and is waiting for a USB reset.",
            "Device is operating in the DFU mode and is waiting for requests.",
            "Device has received a block and is waiting for the host to solicit the status via DFU_GETSTATUS.",
            "Device is programming a control-write block into its nonvolatile memories.",
            "Device is processing a download operation. Expecting DFU_DNLOAD requests.",
            "Device has received the final block of firmware from the host and is waiting for receipt of DFU_GETSTATUS to begin the Manifestation phase; or device has completed the Manifestation phase and is waiting for receipt of DFU_GETSTATUS. (Devices that can enter this state after the Manifestation phase set bmAttributes bit bitManifestationTolerant to 1.)",
            "Device is in the Manifestation phase. (Not all devices will be able to respond to DFU_GETSTATUS when in this state.)",
            "Device has programmed its memories and is waiting for a USB reset or a power on reset. (Devices that must enter this state clear bitManifestationTolerant to 0.)",
            "The device is processing an upload operation. Expecting DFU_UPLOAD requests.",
            "An error has occurred. Awaiting the DFU_CLRSTATUS request."
        };


        public DfuStatus(byte[] data)
        {
            Status = (DfuStatusValue)data[0];
            State = (DfuState)data[4];
            StringId = data[5];
            Polltimeout = data[1] | (data[2] << 8) | (data[3] << 16);

            StatusText = "Status String Unavailable";
            StateText = "State String Unavailable";
            if(data[0] < DfuStatusStrings.Length)
            {
                StatusText = DfuStatusStrings[data[0]];
            }
            if(data[5] < DfuStateStrings.Length)
            {
                StateText = DfuStateStrings[data[5]];
            }
        }

        public readonly DfuStatusValue Status;
        public readonly int Polltimeout;
        public readonly DfuState State;
        public readonly byte StringId;

        public string StatusText;
        public string StateText;

        public override string ToString()
        {
            return string.Format("DfuStatus({0:d} {0} '{1}', PollTimeout {2}ms, String ID {3}, State {4:d} {4} '{5}')",
                Status, StatusText, Polltimeout, StringId, State, StateText);
        }
    }
}
