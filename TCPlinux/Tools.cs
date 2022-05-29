using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Threading;
using System.Security.Cryptography;
namespace TCPlinux;

public class Tools
{
    public static byte[] GenerateKey(string key)
    {
        return (string.IsNullOrEmpty(key) ? null : SHA256.Create().ComputeHash(Encoding.Default.GetBytes(key)));
    }
    public static string GetSafeRemoteEndPoint(Socket s)
    {
        if (s == null)
        {
            return "<null>";
        }
        try
        {
            return s.RemoteEndPoint.ToString();
        }
        catch
        {
            return "<unknown>";
        }
    }
    public static int WaitAny(List<WaitHandle> Handles, int TimeOut)
    {
        for (int i = 0; i < Handles.Count; i += 0x40)
        {
            int count = Math.Min(0x40, Handles.Count - i);
            int millisecondsTimeout = TimeOut;
            if (Handles.Count > (i + count))
            {
                millisecondsTimeout = 0;
            }
            WaitHandle[] array = new WaitHandle[count];
            Handles.CopyTo(i, array, 0, count);
            int num4 = WaitHandle.WaitAny(array, millisecondsTimeout, false);
            if (num4 != 0x102)
            {
                return (num4 + i);
            }
        }
        return 0x102;
    }
}