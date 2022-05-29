using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
namespace Config;

public class SourcePoint
{
    public string? IPFrom { get; set; }

    public string? IPTo { get; set; }

    public static bool IsInside(IPAddress IPAddress, List<SourcePoint> list)
    {
        if (list.Count == 0)
            return true;
        foreach (SourcePoint sourcePoint in list)
        {
            if (sourcePoint.IsInside(IPAddress))
                return true;
        }
        return false;
    }

    private bool IsInside(IPAddress IPAddress)
    {
        byte[] addressBytes1 = IPAddress.GetAddressBytes();
        IPAddress ipAddress1 = IPAddress.Parse(this.IPFrom);
        IPAddress ipAddress2 = IPAddress.Parse(this.IPTo);
        byte[] addressBytes2 = ipAddress1.GetAddressBytes();
        byte[] addressBytes3 = ipAddress2.GetAddressBytes();
        if (addressBytes1.Length != addressBytes2.Length || addressBytes1.Length != addressBytes3.Length)
            return false;
        for (int index = 0; index < addressBytes1.Length; ++index)
        {
            if ((int)addressBytes1[index] < (int)addressBytes2[index])
                return false;
            if ((int)addressBytes1[index] > (int)addressBytes2[index])
                break;
        }
        for (int index = 0; index < addressBytes1.Length; ++index)
        {
            if ((int)addressBytes1[index] > (int)addressBytes3[index])
                return false;
            if ((int)addressBytes1[index] < (int)addressBytes3[index])
                break;
        }
        return true;
    }
}