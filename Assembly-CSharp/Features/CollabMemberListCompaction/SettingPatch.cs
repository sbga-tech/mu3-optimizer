using System;
using MonoMod;
using MU3.Collab;
using MU3.DB;

namespace MU3.Mod.CollabMemberListCompaction;

[MonoModIfFlag(nameof(PatchConfig.CollabMemberListCompaction))]
[MonoModPatch("global::MU3.Collab.Setting")]
public static class SettingPatch
{
    public class Host
    {
        [MonoModIgnore] private UdpRecvSocket _udpSocket;
        [MonoModIgnore] private Setting.MemberList _members;
        [MonoModIgnore] private int _eraseCount;

        private static readonly Predicate<Setting.Member> IsNull = m => m == null;

        [MonoModIgnore] private extern void executeBroadcast();
        [MonoModIgnore] private extern void executeAccept();


        [MonoModReplace]
        public void update()
        {
            ((RD1.SSS.StateMachine<Setting.Host, CollabSettingHostStateID>)(object)this)
                .updateState(-1f);
            executeBroadcast();
            executeAccept();
            _udpSocket.recv();
            var members = _members;
            var n = members.Count;
            for (var i = 0; i < n; i++)
            {
                var member = members[i];
                if (member == null)
                    continue;
                member.update();
                if (!member.isAlive())
                {
                    member.Dispose();
                    members[i] = null;
                }
            }
            var count = members.Count;
            members.RemoveAll(IsNull);
            _eraseCount += count - members.Count;
        }

        [MonoModReplace]
        public int getMemberNumber()
        {
            var n = 1;
            var members = _members;
            for (var i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m != null && m.isJoin())
                    n++;
            }
            return n;
        }
    }
}
