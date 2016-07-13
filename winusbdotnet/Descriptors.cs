using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace winusbdotnet
{
    public class Descriptors
    {

    }

    public enum DescriptorType
    {
        Unknown = -1,
        Device = 1,
        Configuration = 2,
        String = 3,
        Interface = 4,
        Endpoint = 5,
        DeviceQualifier = 6,
        OtherSpeedConfiguration = 7,
        InterfacePower = 8
    }

    public class DeviceDescriptor
    {
        public static DeviceDescriptor Parse(byte[] srcdata)
        {
            if(srcdata.Length != 18 || srcdata[0] != 18 || srcdata[1] != (byte)DescriptorType.Device)
            {
                throw new Exception("Invalid device descriptor");
            }
            DeviceDescriptor d = new DeviceDescriptor();
            d.USBVersion = (UInt16)(srcdata[2] | (srcdata[3] << 8));
            d.DeviceClassCode = srcdata[4];
            d.DeviceSubClassCode = srcdata[5];
            d.DeviceProtocolCode = srcdata[6];
            d.MaxPacketSizeEP0 = srcdata[7];
            d.IDVendor = (UInt16)(srcdata[8] | (srcdata[9] << 8));
            d.IDProduct = (UInt16)(srcdata[10] | (srcdata[11] << 8));
            d.DeviceRelease = (UInt16)(srcdata[12] | (srcdata[13] << 8));
            d.ManufacturerString = srcdata[14];
            d.ProductNameString = srcdata[15];
            d.SerialNumberString = srcdata[16];
            d.NumConfigurations = srcdata[17];

            // Sanity checks
            if (d.MaxPacketSizeEP0 != 8 && d.MaxPacketSizeEP0 != 16 && d.MaxPacketSizeEP0 != 32 && d.MaxPacketSizeEP0 != 64)
            {
                throw new Exception("Device descriptor has invalid EP0 packet size");
            }
            return d;
        }
        public byte[] GenerateStructure()
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(ms);
            bw.Write((byte)18); // length
            bw.Write((byte)DescriptorType.Device);
            bw.Write((byte)(USBVersion & 255));
            bw.Write((byte)(USBVersion >> 8));
            bw.Write(DeviceClassCode);
            bw.Write(DeviceSubClassCode);
            bw.Write(DeviceProtocolCode);
            bw.Write(MaxPacketSizeEP0);
            bw.Write((byte)(IDVendor & 255));
            bw.Write((byte)(IDVendor >> 8));
            bw.Write((byte)(IDProduct & 255));
            bw.Write((byte)(IDProduct >> 8));
            bw.Write((byte)(DeviceRelease & 255));
            bw.Write((byte)(DeviceRelease >> 8));
            bw.Write(ManufacturerString);
            bw.Write(ProductNameString);
            bw.Write(SerialNumberString);
            bw.Write(NumConfigurations);
            return ms.ToArray();
        }

        public UInt16 USBVersion;
        public byte DeviceClassCode;
        public byte DeviceSubClassCode;
        public byte DeviceProtocolCode;
        public byte MaxPacketSizeEP0;
        public UInt16 IDVendor;
        public UInt16 IDProduct;
        public UInt16 DeviceRelease;
        public byte ManufacturerString;
        public byte ProductNameString;
        public byte SerialNumberString;
        public byte NumConfigurations;

        public override string ToString()
        {
            return string.Format("DeviceDescriptor( Ven {0:x4} Dev {1:x4} Release {2:x4} Configurations {3:x2} USBVer {4:x4} Class {5:x2} {6:x2} Protocol {7:x2} EP0Size {8:x2} Strings {9:x2} {10:x2} {11:x2})",
                IDVendor, IDProduct, DeviceRelease, NumConfigurations, USBVersion, DeviceClassCode, DeviceSubClassCode, DeviceProtocolCode,
                MaxPacketSizeEP0, ManufacturerString, ProductNameString, SerialNumberString);
        }
    }

    [Flags]
    public enum DeviceConfigurationFlags
    {
        Reserved1 = 0x80,
        SelfPowered = 0x40,
        RemoteWakeup = 0x20
    }

    public class DescriptorNode
    {
        public DescriptorNode(byte[] reference, ref int Cursor)
        {
            if(Cursor + 2 >= reference.Length)
            {
                throw new Exception("Not enough space for another descriptor");
            }
            int len = reference[Cursor];
            if(len<2)
            {
                throw new Exception("Invalid descriptor length");
            }
            RawType = reference[Cursor + 1];
            Type = DescriptorType.Unknown;
            if(Enum.IsDefined(typeof(DescriptorType), (int)RawType))
            {
                Type = (DescriptorType)RawType;
            }
            if(Cursor+len > reference.Length)
            {
                throw new Exception("Not enough space to contain the descriptor");
            }
            Data = new byte[len - 2];
            if(Data.Length > 0)
            {
                Array.Copy(reference, Cursor + 2, Data, 0, Data.Length);
            }
            Cursor += len;
        }
        public DescriptorNode(DescriptorType descriptorType)
        {
            Type = descriptorType;
            RawType = (byte)Type;
            Data = new byte[0];
        }

        public DescriptorType Type;
        public byte RawType;
        public byte[] Data;

        public override string ToString()
        {
            return string.Format("DescriptorNode( {0} ({1:x2}): {2})", Type, RawType, string.Join(" ", Data.Select(b => b.ToString("x2"))));
        }
    }

    public class DeviceConfiguration
    {
        public DeviceConfiguration()
        {
            RawDescriptors = new List<DescriptorNode>();
        }

        public static DeviceConfiguration Parse(byte[] srcData)
        {
            DeviceConfiguration c = new DeviceConfiguration();

            if(srcData.Length < 9 || srcData[0] != 9 || srcData[1] != (byte)DescriptorType.Configuration)
            {
                throw new Exception("Invalid configuration descriptor");
            }
            int fullLength = srcData[2] | (srcData[3] << 8);
            if(fullLength < 9 || fullLength > srcData.Length)
            {
                throw new Exception("Invalid configuration descriptor length");
            }

            c.NumInterfaces = srcData[4];
            c.Index = srcData[5];
            c.DescriptionString = srcData[6];
            c.Attributes = (DeviceConfigurationFlags)srcData[7];
            c.PowerDraw = srcData[8];

            int cursor = 9;
            while(cursor < fullLength)
            {
                c.RawDescriptors.Add(new DescriptorNode(srcData, ref cursor));
            }
            if(cursor != fullLength)
            {
                throw new Exception("Configuration descriptors overran specified length");
            }

            // Next step, parse them off into interfaces and endpoints.


            return c;
        }


        public override string ToString()
        {
            string nodes = string.Join("\n",RawDescriptors.Select(d => "  " + d.ToString()));
            return string.Format("ConfigurationDescriptor( Index {0} Attributes {1:x} ({1}) PowerDraw {2}mA NumInterfaces {3} String {4:x2})\n{5}",
                Index, Attributes, PowerDraw * 2, NumInterfaces, DescriptionString, nodes);
        }

        public byte NumInterfaces;
        public byte Index;
        public byte DescriptionString;
        public DeviceConfigurationFlags Attributes;
        public byte PowerDraw;

        public List<DescriptorNode> RawDescriptors;
    }
}
