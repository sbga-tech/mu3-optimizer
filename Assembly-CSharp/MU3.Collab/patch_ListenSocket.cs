using System;
using System.Net;
using System.Net.Sockets;
using MonoMod;


namespace MU3.Collab;

[MonoModIfFlag("CollabSocketCaching")]
public class patch_ListenSocket : ListenSocket
{
    [MonoModIgnore] private NFSocket _socket;
    [MonoModIgnore] private ushort _portNumber;
    [MonoModIgnore] private string _socketName;
    [MonoModIgnore] private int _acceptCount;

    private bool _bound;

    public patch_ListenSocket(string name, int mockID)
        : base(name, mockID)
    {
    }

    [MonoModReplace]
    public new bool open(ushort portNumber)
    {
        if (_socket != null && _bound && _portNumber == portNumber)
            return true;

        if (_socket != null && _portNumber != portNumber)
        {
            close();
            _bound = false;
        }

        _portNumber = portNumber;
        _acceptCount = 0;
        if (_socket == null)
            _socket = new NFSocket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp, -1);

        var ep = new IPEndPoint(IPAddress.Any, portNumber);
        try
        {
            _socket.Bind(ep);
            _socket.Listen(16);
            _bound = true;
            return true;
        }
        catch (Exception)
        {
            close();
            _bound = false;
            return false;
        }
    }

    [MonoModReplace]
    public new bool isValid()
    {
        return _socket != null && _bound;
    }


}

