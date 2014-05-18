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

            string[] devices = WinUSBDevice.EnumerateDevices(testGuid);
            foreach(string devicePath in devices)
            {
                Console.Out.WriteLine(devicePath);
            }

            Console.Out.WriteLine("{0} device{1}", devices.Length, devices.Length==1?"":"s");
        }
    }
}
