using System.Net;

namespace MU3.Collab;

internal static class CollabAlloc
{
    public static IPAddress ToIPAddress(IpAddress addr)
    {
        var v = addr.ToNetworkByteOrderU32();
        var swapped = ((v & 0xFFu) << 24) | ((v & 0xFF00u) << 8) | ((v >> 8) & 0xFF00u) | (v >> 24);
        return new IPAddress((long) swapped);
    }
}
