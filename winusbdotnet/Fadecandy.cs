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

//
// This is a control library for Fadecandy
// It probably doesn't belong in the winusbdotnet library and will be moved elsewhere eventually.
// In the meantime, it is a good place to develop this library.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace winusbdotnet
{

    public struct RGBColor
    {
        public double R, G, B;

        byte ConvertValue(double source)
        {
            if (source < 0) source = 0;
            if (source > 1) source = 1;
            return (byte)(source * 255);
        }
        public byte RByte { get { return ConvertValue(R); } }
        public byte GByte { get { return ConvertValue(G); } }
        public byte BByte { get { return ConvertValue(B); } }
    }

    public class Fadecandy : IDisposable
    {
        public static IEnumerable<WinUSBEnumeratedDevice> Enumerate()
        {
            foreach(WinUSBEnumeratedDevice dev in WinUSBDevice.EnumerateAllDevices())
            {
                if (dev.VendorID == 0x1d50 && dev.ProductID == 0x607A && dev.UsbInterface == 0)
                {
                    yield return dev;
                }
            }
        }

        public Fadecandy(WinUSBEnumeratedDevice dev)
        {
            BaseDevice = new WinUSBDevice(dev);
            Pixels = new RGBColor[512];
            Initialize();
        }

        public WinUSBDevice BaseDevice;

        const byte DataPipe = 0x01; // OUT 1

        public void Dispose()
        {
            if (BaseDevice != null)
            {
                BaseDevice.Dispose();
                BaseDevice = null;
            }
            GC.SuppressFinalize(this);
        }

        public void Close()
        {
            BaseDevice.Close();
            BaseDevice = null;
        }

        public RGBColor[] Pixels;
        

        public void FlushAll()
        {
            FlushRange(0, 512);

        }
        public void FlushRange(int start, int count)
        {
            if (start < 0 || start > 511) throw new ArgumentException("start");
            if (count < 0 || (start + count) > 512) throw new ArgumentException("count");
            const int pixelsPerChunk = 21;

            int firstChunk = (start / pixelsPerChunk);
            int lastChunk = ((start + count - 1) / pixelsPerChunk);

            byte[] data = new byte[64];
            for (int chunk = firstChunk; chunk <= lastChunk; chunk++)
            {
                int offset = chunk * pixelsPerChunk;
                data[0] = ControlByte(0, chunk == lastChunk, chunk);
                for (int i = 0; i < pixelsPerChunk; i++)
                {
                    if (i + offset > 511) continue;
                    data[1 + i * 3] = Pixels[i + offset].GByte; // not sure if just the LEDs I'm testing with, but R/G seem reversed from the spec.
                    data[2 + i * 3] = Pixels[i + offset].RByte; // Confirm with other LED strips later.
                    data[3 + i * 3] = Pixels[i + offset].BByte;
                }
                BaseDevice.WritePipe(DataPipe, data);
            }
        }


        public void Initialize()
        {
            double gammaCorrection = 1.6;
            // compute basic uniform gamma table for r/g/b

            const int lutEntries = 257;
            const int lutTotalEntries = lutEntries * 3;
            UInt16[] lutValues = new UInt16[lutTotalEntries];

            for(int i=0;i<lutEntries;i++)
            {
                double r, g, b;
                r = g = b = Math.Pow((double)i / (lutEntries-1), gammaCorrection) * 65535;
                lutValues[i] = (UInt16)r;
                lutValues[i+lutEntries] = (UInt16)g;
                lutValues[i+lutEntries*2] = (UInt16)b;
            }

            // Send LUT 31 entries at a time.
            byte[] data = new byte[64];

            int blockIndex = 0;
            int lutIndex = 0;
            while (lutIndex < lutTotalEntries)
            {
                bool lastChunk = false;
                int lutCount = (lutTotalEntries - lutIndex);
                if (lutCount > 31)
                    lutCount = 31;

                int nextIndex = lutIndex + lutCount;
                if (nextIndex == lutTotalEntries)
                    lastChunk = true;

                data[0] = ControlByte(1, lastChunk, blockIndex);

                for (int i = 0; i < lutCount; i++)
                {
                    data[i * 2 + 2] = (byte)(lutValues[lutIndex + i] & 0xFF);
                    data[i * 2 + 3] = (byte)((lutValues[lutIndex + i] >> 8) & 0xFF);
                }

                BaseDevice.WritePipe(DataPipe, data);

                blockIndex++;
                lutIndex = nextIndex;
            }

        }

        byte ControlByte(int type, bool final = false, int index = 0)
        {
            if (type < 0 || type > 3) throw new ArgumentException("type");
            if (index < 0 || index > 31) throw new ArgumentException("index");
            byte output = (byte)((type << 6) | index);
            if (final) output |= 0x20;
            return output;
        }

        public void SendConfiguration(bool enableDithering = true, bool enableKeyframeInterpolation = true, bool manualLedControl = false, bool ledValue = false, bool reservedMode = false)
        {
            byte[] data = new byte[64];

            data[0] = ControlByte(2);

            if (!enableDithering) 
                data[1] |= 0x01;

            if (enableKeyframeInterpolation)
                data[1] |= 0x02;

            if (manualLedControl)
                data[1] |= 0x04;

            if (ledValue)
                data[1] |= 0x08;

            if (reservedMode)
                data[1] |= 0x10;


            BaseDevice.WritePipe(DataPipe, data);
        }

    }
}
