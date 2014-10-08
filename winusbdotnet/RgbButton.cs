using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace winusbdotnet
{
    public class RgbButton
    {
        public static IEnumerable<WinUSBEnumeratedDevice> Enumerate()
        {
            return WinUSBDevice.EnumerateDevices(new Guid("60d2a26c-1d22-4538-b7d5-337b2f07fff1"));
        }

        const byte OUT_PIPE = 0x03;
        const byte IN_PIPE = 0x83;
        const int ButtonThreshold = 0x70;

        WinUSBDevice BaseDevice;
        public RGBColor[] ButtonColors;
        public int[] ButtonValues;
        public bool[] ButtonPressed;
        public long DataCount;

        public RgbButton(WinUSBEnumeratedDevice dev)
        {
            BaseDevice = new WinUSBDevice(dev);

            ButtonColors = new RGBColor[4];
            ButtonValues = new int[4];
            ButtonPressed = new bool[4];

            BaseDevice.EnableBufferedRead(IN_PIPE);
            BaseDevice.BufferedReadNotifyPipe(IN_PIPE, NewDataCallback);
            EnableButtonData();
        }

        public void Close()
        {
            BaseDevice.Close();
            BaseDevice = null;
        }

        void NewDataCallback()
        {
            lock (this) // Prevent concurrent execution
            {
                bool newData = false;
                bool badData;

                while (BaseDevice.BufferedByteCountPipe(IN_PIPE) >= 5)
                {
                    badData = false;
                    byte[] data = BaseDevice.BufferedPeekPipe(IN_PIPE, 5);
                    if(data[0] != 0xFF)
                    {
                        badData = true;
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        if(data[i + 1] >= 0x80)
                        {
                            // This is also a bad data (truncated) message, this can happen if the host falls behind.
                            badData = true;
                            break;
                        }
                    }
                    if(badData)
                    {
                        // Bad data. Try again next byte.
                        BaseDevice.BufferedSkipBytesPipe(IN_PIPE, 1);
                        continue;
                    }

                    // This looks like a button message
                    for(int i=0;i<4;i++)
                    {
                        ButtonValues[i] = data[i + 1];
                        ButtonPressed[i] = ButtonValues[i] < ButtonThreshold;
                    }
                    newData = true;
                    DataCount++;
                    BaseDevice.BufferedSkipBytesPipe(IN_PIPE, 5);
                }

                if(newData)
                {
                    // Provide notification.
                }
            }
        }


        public void SendButtonColors()
        {
            byte[] command = new byte[13];
            command[0] = (byte)'L';

            int n = 1;
            foreach(RGBColor c in ButtonColors)
            {
                command[n++] = c.RByte;
                command[n++] = c.GByte;
                command[n++] = c.BByte;
            }
            BaseDevice.WritePipe(OUT_PIPE, command);
        }

        void SendByteCommand(byte b)
        {
            byte[] command = new byte[1];
            command[0] = b;
            BaseDevice.WritePipe(OUT_PIPE, command);
        }
        void EnableButtonData()
        {
            SendByteCommand((byte)'B');
        }
        void DisableButtonData()
        {
            SendByteCommand((byte)'X');
        }
        public void EnterProgrammingMode()
        {
            SendByteCommand((byte)'P');
        }

    }
}
