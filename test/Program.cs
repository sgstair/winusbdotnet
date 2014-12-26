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
using winusbdotnet.UsbDevices;

namespace test
{
    class Program
    {
        // Temporary test application to validate functionality as it's being built.
        static void Main(string[] args)
        {
            Guid testGuid = new Guid("d2938a49-3191-4b25-ba33-e45f0828ced4");
            Random r = new Random();

            WinUSBEnumeratedDevice[] allDevices = WinUSBDevice.EnumerateAllDevices().ToArray();
            foreach (WinUSBEnumeratedDevice devicePath in allDevices)
            {
                Console.Out.WriteLine(devicePath.ToString());
            }


            WinUSBEnumeratedDevice[] devices = WinUSBDevice.EnumerateDevices(testGuid).ToArray();
            foreach (WinUSBEnumeratedDevice devicePath in devices)
            {
                Console.Out.WriteLine(devicePath);

                WinUSBDevice test = new WinUSBDevice(devicePath);

                // Try a data test. Test board just has OUT 3 looped back into IN 3
                // Set pipe timeouts to avoid hanging forever.
                test.SetPipePolicy(0x03, WinUsbPipePolicy.PIPE_TRANSFER_TIMEOUT, 100);
                test.SetPipePolicy(0x83, WinUsbPipePolicy.PIPE_TRANSFER_TIMEOUT, 100);

                // Send some junk via OUT 3
                byte[] data = new byte[128];
                r.NextBytes(data);

                // Flush out any data that might have been here from a previous run...
                // Will take about as long as the transfer timeout.
                while (test.ReadPipe(0x83, 64).Length != 0) ;


                test.WritePipe(0x03, data);

                // read it back.
                byte[] returnData = test.ReadExactPipe(0x83, data.Length);

                for (int i = 0; i < data.Length;i++)
                {
                    if(data[i] != returnData[i])
                    {
                        throw new Exception("Error validating data returned from the device!");
                    }
                }
                Console.Out.WriteLine("Passed basic transfer test");

                // Timeout test
                returnData = test.ReadPipe(0x83, 32);
                if (returnData.Length != 0) { throw new Exception("Pipe didn't timeout, where did it get that data?"); }
                Console.Out.WriteLine("Passed timeout test");

                test.Close();
                test.Close(); // checking that double close doesn't cause issues.
            }

            Console.Out.WriteLine("{0} device{1}", devices.Length, devices.Length==1?"":"s");

            WinUSBEnumeratedDevice[] hackrfs = HackRF.Enumerate().ToArray();
            if (hackrfs.Length > 0)
            {
                Console.WriteLine("Connecting to hackrf device {0}", hackrfs[0].ToString());

                HackRF rf = new HackRF(hackrfs[0]);

                Console.WriteLine("Version String: {0}", rf.ReadVersion());
                
                // Do some benchmarking with the receive modes.
                rf.SetSampleRate(2000000);
                rf.ModeReceive();
                rf.SetFrequency(100000000); // 100 MHz
                rf.SetSampleRate(10000000);

                int lastEaten = 0;
                int eaten = rf.PacketsEaten;
                long lastEatenBytes = 0;
                long eatenBytes = rf.BytesEaten;
                DateTime lastTime = DateTime.Now;
                while (true)
                {
                    System.Threading.Thread.Sleep(2000);
                    DateTime newTime = DateTime.Now;
                    lastEaten = eaten;
                    eaten = rf.PacketsEaten;
                    lastEatenBytes = eatenBytes;
                    eatenBytes = rf.BytesEaten;
                    double seconds = newTime.Subtract(lastTime).TotalSeconds;
                    double pps = (eaten - lastEaten) / seconds;
                    double mbps = ((eatenBytes - lastEatenBytes) / seconds)/1000000;
                    lastTime = newTime;


                    Console.WriteLine("Receiving... {0}  {1:n2}pps {2:n4}MB/s", eaten, pps, mbps);
                    string[] histogramData;
                    lock (rf)
                    {
                        histogramData = rf.EatenHistogram.OrderByDescending(kv => kv.Value).Take(8).Select(kv => string.Format("{0}:{1}", kv.Key, kv.Value)).ToArray();
                    }
                    Console.WriteLine(string.Join(" ", histogramData));
                }

            }


            WinUSBEnumeratedDevice[] fadecandies = Fadecandy.Enumerate().ToArray();
            if(fadecandies.Length > 0)
            {
                Fadecandy fc = new Fadecandy(fadecandies[0]);

                double t = 0;
                while(true)
                {
                    for (int i = 0; i < 24; i++)
                    {
                        fc.Pixels[i].R = Math.Sin(t ) * 0.2 + 0.2;
                    }
                    fc.Pixels[64].G = Math.Sin(t) * 0.2 + 0.2;
                    fc.FlushRange(0, 65);

                    t += 0.01;
                    System.Threading.Thread.Sleep(10);
                }
                

            }


            WinUSBEnumeratedDevice[] rgbbuttons = RgbButton.Enumerate().ToArray();
            if (rgbbuttons.Length > 0)
            {
                RgbButton rb = new RgbButton(rgbbuttons[0]);

#if false
                { // Put device into programming mode.
                    rb.EnterProgrammingMode();
                    return;
                }
#endif

                double t = 0;
                while (true)
                {
                    rb.ButtonColors[0].G = Math.Sin(t) * 0.2 + 0.2;
                    rb.ButtonColors[1].R = Math.Sin(t) * 0.2 + 0.2;
                    rb.ButtonColors[2].G = 0.5 - rb.ButtonValues[0] / 256.0;
                    rb.ButtonColors[2].R = 0.5 - rb.ButtonValues[1] / 256.0;
                    rb.ButtonColors[2].B = 0.5 - rb.ButtonValues[3] / 256.0;
                    rb.ButtonColors[3].B = (rb.DataCount & 1023) / 2048.0;
                    rb.SendButtonColors();

                    t += 0.02;
                    System.Threading.Thread.Sleep(20);

                    Console.WriteLine("{0} {1} {2} {3}", rb.ButtonValues[0],  rb.ButtonValues[1],  rb.ButtonValues[2],  rb.ButtonValues[3]);
                }


            }

        }
    }
}
