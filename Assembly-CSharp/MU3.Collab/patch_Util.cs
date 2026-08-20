using System.Net;
using MonoMod;

namespace MU3.Collab;

[MonoModIfFlag("NoAlloc")]
public static class patch_Util
{
    [MonoModReplace]
    public static uint ToNetworkByteOrderU32(this IPAddress ip)
    {
        if (ip == null || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return 0u;
        var a = (uint) ip.Address;
        return ((a & 0xFFu) << 24) | ((a & 0xFF00u) << 8) | ((a >> 8) & 0xFF00u) | (a >> 24);
    }
}
