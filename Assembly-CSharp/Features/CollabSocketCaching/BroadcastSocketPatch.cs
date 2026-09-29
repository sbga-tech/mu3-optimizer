using System;
using System.Net;
using System.Net.Sockets;
using MonoMod;
using MU3.Collab;

namespace MU3.Mod.CollabSocketCaching;

[MonoModIfFlag(nameof(PatchConfig.CollabSocketCaching))]
[MonoModPatch("global::MU3.Collab.BroadcastSocket")]
public class BroadcastSocketPatch : BroadcastSocket
{
    [MonoModIgnore] private IPEndPoint _broadcastAddress;
    [MonoModIgnore] private string _addressString;
    [MonoModIgnore] private ushort _port;

    private IPEndPoint _unicastAddress;
    private bool _ready;

    public BroadcastSocketPatch(string name, int mockID)
        : base(name, mockID)
    {
    }

    [MonoModReplace]
    public new bool open(IpAddress addrTo, ushort port)
    {
        if (_socket != null && _ready && _port == port)
            return true;

        if (_socket != null && _port != port)
        {
            close();
            _ready = false;
        }

        _addressString = addrTo.convIpString();
        _port = port;
        if (_broadcastAddress == null)
            _broadcastAddress = new IPEndPoint(IPAddress.Any, 0);
        _broadcastAddress.Port = port;
        _broadcastAddress.Address = CollabAlloc.ToIPAddress(addrTo);

        if (_socket == null)
            initialize(new NFSocket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, -1));

        try
        {
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
            _ready = true;
            return true;
        }
        catch (Exception)
        {
            _ready = false;
            return false;
        }
    }

    [MonoModReplace]
    public new void sendTo(IpAddress addrTo, PacketType data)
    {
        if (_unicastAddress == null)
            _unicastAddress = new IPEndPoint(IPAddress.Any, 0);
        _unicastAddress.Port = _port;
        _unicastAddress.Address = CollabAlloc.ToIPAddress(addrTo);
        send(_unicastAddress, data);
    }

    [MonoModReplace]
    public new bool isValid()
    {
        return _socket != null && _ready;
    }
}
