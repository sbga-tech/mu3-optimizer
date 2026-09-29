using System;
using System.Net;
using System.Net.Sockets;
using MonoMod;
using MU3.Collab;


namespace MU3.Mod.CollabSocketCaching;

[MonoModIfFlag(nameof(PatchConfig.CollabSocketCaching))]
[MonoModPatch("global::MU3.Collab.ListenSocket")]
public class ListenSocketPatch : ListenSocket
{
    [MonoModIgnore] private NFSocket _socket;
    [MonoModIgnore] private ushort _portNumber;
    [MonoModIgnore] private string _socketName;
    [MonoModIgnore] private int _acceptCount;

    private bool _bound;

    public ListenSocketPatch(string name, int mockID)
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

