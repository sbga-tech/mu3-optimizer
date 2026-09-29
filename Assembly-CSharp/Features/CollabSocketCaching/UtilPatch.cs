using System.Net;
using MonoMod;
using MU3.Collab;

namespace MU3.Mod.CollabSocketCaching;

[MonoModIfFlag(nameof(PatchConfig.CollabSocketCaching))]
[MonoModPatch("global::MU3.Collab.Util")]
public static class UtilPatch
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
