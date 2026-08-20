using System;
using MonoMod;
using MU3.DB;

namespace MU3.Collab;

[MonoModIfFlag("NoAlloc")]
public class patch_HeartBeat : HeartBeat
{
    [MonoModIgnore] private int _recvNumber;
    [MonoModIgnore] private int _sendNumber;
    [MonoModIgnore] private int _timeout;
    [MonoModIgnore] private int _interval;
    [MonoModIgnore] private DateTime _recvTime;
    [MonoModIgnore] private DateTime _sendTime;
    [MonoModIgnore] private readonly ConnectSocket _socket;
    [MonoModIgnore] private CollabHeartBeatStateID _state;
    [MonoModIgnore] private CollabTick _tick;
    [MonoModIgnore] private long _sendUsec;
    [MonoModIgnore] private long _sendFrame;
    [MonoModIgnore] private long _currentFrame;

    private static readonly HeartBeatRequest Request = new HeartBeatRequest();
    private static readonly HeartBeatResponse Response = new HeartBeatResponse();

    public patch_HeartBeat(ConnectSocket socket, int interval, int timeout)
        : base(socket, interval, timeout)
    {
    }

    [MonoModReplace]
    public new void update()
    {
        _currentFrame++;
        var now = CustomDateTime.Now;
        if (!_socket.isActive())
        {
            _recvTime = now;
            _sendTime = now;
            if (_state == CollabHeartBeatStateID.Active)
                _state = CollabHeartBeatStateID.Closed;
            return;
        }
        if (_sendTime.AddSeconds(_interval) <= now)
        {
            _socket.sendClass(Request);
            _sendTime = now;
            _sendNumber++;
            _sendUsec = _tick.getUsec();
            _sendFrame = _currentFrame;
        }
        if (_recvTime.AddSeconds(_timeout) < now)
        {
            _state = CollabHeartBeatStateID.Timeout;
            _socket.close();
        }
    }

    [MonoModReplace]
    public new void recvRequest(HeartBeatRequest request)
    {
        _recvTime = CustomDateTime.Now;
        _recvNumber++;
        _socket.sendClass(Response);
    }
}
