using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using MonoMod;

namespace MU3.Collab;

[MonoModIfFlag("CollabSocketCaching")]
public class patch_UdpRecvSocket : UdpRecvSocket
{
    [MonoModIgnore] private uint _port;
    [MonoModIgnore] private IpAddress _lastRecvAddress;
    [MonoModIgnore] private IpAddress _address;
    [MonoModIgnore] private byte[] _recvTempBuffer;

    private EndPoint _remoteEP;
    private bool _bound;

    public patch_UdpRecvSocket(string name, int mockID)
        : base(name, mockID)
    {
    }

    [MonoModReplace]
    public new void open(IpAddress addrTo, ushort port)
    {
        if (_socket != null && _bound && _port == port && _address == addrTo)
            return;

        if (_socket != null && (_port != port || _address != addrTo))
        {
            close();
            _bound = false;
        }

        _port = port;
        _address = addrTo;
        var ep = _remoteEP as IPEndPoint;
        if (ep == null)
        {
            ep = new IPEndPoint(IPAddress.Any, 0);
            _remoteEP = ep;
        }
        ep.Port = port;
        ep.Address = CollabAlloc.ToIPAddress(addrTo);

        if (_socket == null)
            initialize(new NFSocket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, -1));

        try
        {
            _socket.Bind(ep);
            _bound = true;
        }
        catch (Exception)
        {
            _bound = false;
        }
    }

    [MonoModReplace]
    public override void recv()
    {
        if (_socket == null || !_bound || !checkRecvEnable(_socket))
            return;
        if (_remoteEP == null)
            _remoteEP = new IPEndPoint(IPAddress.Any, 0);
        var remoteEP = _remoteEP;
        int num;
        try
        {
            num = _socket.ReceiveFrom(_recvTempBuffer, SocketFlags.None, ref remoteEP);
        }
        catch (Exception)
        {
            error("recv failed unknown error", Marshal.GetLastWin32Error());
            _bound = false;
            return;
        }
        _remoteEP = remoteEP;
        var iPEndPoint = remoteEP as IPEndPoint;
        if (iPEndPoint == null)
            return;
        _lastRecvAddress = new IpAddress(iPEndPoint.Address);
        if (num < 0)
        {
            close();
            _bound = false;
            return;
        }
        _recvBuffer.CopyRange(_recvTempBuffer, 0, num);
        _recvByte += (uint) num;
        _analyzer.analyze(_lastRecvAddress, getRecvBuffer(), _isCheckVersion);
        _recvBuffer.Clear();
    }

    [MonoModReplace]
    public new bool isValid()
    {
        return _socket != null && _bound;
    }
}
