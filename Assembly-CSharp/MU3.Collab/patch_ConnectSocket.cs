using System.Net;
using System.Net.Sockets;
using MonoMod;
using MU3.DB;
using UnityEngine;

namespace MU3.Collab;

[MonoModIfFlag("NoAlloc")]
public class patch_ConnectSocket : ConnectSocket
{
    [MonoModIgnore] private IPEndPoint _socketAddr;
    [MonoModIgnore] private bool _isRecvEnd;
    [MonoModIgnore] private SocketAsyncEventArgs _socketConnectAsyncArg;
    [MonoModIgnore] private bool _connectDone;
    [MonoModIgnore] private RD1.SSS.StateMachine<ConnectSocket, CollabConnectStateID> _stateMachine;



    public patch_ConnectSocket(string name, int mockID)
        : base(name, mockID)
    {
    }

    [MonoModReplace]
    public new void connect(IpAddress address, ushort portNumber)
    {
        _isRecvEnd = false;
        if (_socketAddr == null)
            _socketAddr = new IPEndPoint(IPAddress.Any, 0);
        _socketAddr.Port = portNumber;
        _socketAddr.Address = CollabAlloc.ToIPAddress(address);
        if (getCurrentState() != CollabConnectStateID.Connect)
            setCurrentStateID(CollabConnectStateID.Connect);
        else
            updateState();
    }

    [MonoModReplace]
    public new void Enter_Connect()
    {
        if (_socket != null)
            closeSocket(ref _socket, getSocketName());
        initialize(new NFSocket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp, -1));
        _socketConnectAsyncArg.RemoteEndPoint = _socketAddr;
        _connectDone = false;
        try
        {
            if (!_socket.ConnectAsync(_socketConnectAsyncArg, -1))
                ConnectCompletedEvent(this, _socketConnectAsyncArg);
        }
        catch (System.Exception ex)
        {
            error("ConnectSocket.ConnectAsync: " + ex.Message, 0);
        }
    }


    [MonoModReplace]
    public new bool updateState(float deltaTime = -1f)
    {
        if (deltaTime < 0f)
            deltaTime = Time.deltaTime;
        var before = getCurrentState();
        var result = _stateMachine.updateState(deltaTime);
        if (before != getCurrentState())
            updateStateString();
        return result;
    }
    [MonoModIgnore] private extern void updateStateString();
    [MonoModIgnore] private extern void ConnectCompletedEvent(object sender, SocketAsyncEventArgs e);
    [MonoModIgnore] private extern void setCurrentStateID(CollabConnectStateID nextState);
}

