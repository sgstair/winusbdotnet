using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace winusbdotnet
{
    public class WinUSBDevice
    {
        public static string[] EnumerateDevices(Guid deviceInterfaceGuid)
        {
            return NativeMethods.EnumerateDevicesByInterface(deviceInterfaceGuid);
        }

    }
}
