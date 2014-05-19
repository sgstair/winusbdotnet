using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using winusbdotnet;

namespace test
{
    class Program
    {
        // Temporary test application to validate functionality as it's being built.
        static void Main(string[] args)
        {
            Guid testGuid = new Guid("d2938a49-3191-4b25-ba33-e45f0828ced4");
            Random r = new Random();

            string[] devices = WinUSBDevice.EnumerateDevices(testGuid);
            foreach(string devicePath in devices)
            {
                Console.Out.WriteLine(devicePath);

                WinUSBDevice test = new WinUSBDevice(devicePath);

                // Try a data test. Test board just has OUT 3 looped back into IN 3
                // Set pipe timeouts to avoid hanging forever.
                test.SetPipePolicy(0x03, WinUsbPipePolicy.PIPE_TRANSFER_TIEMOUT, 100);
                test.SetPipePolicy(0x83, WinUsbPipePolicy.PIPE_TRANSFER_TIEMOUT, 100);

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
        }
    }
}
