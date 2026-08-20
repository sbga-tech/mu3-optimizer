using System;
using MonoMod;

namespace MU3.Collab;

[MonoModIfFlag("NoAlloc")]
public static class patch_Party
{
    public class Recruit
    {
        [MonoModIgnore] private Party.RecruitList _list;


        [MonoModReplace]
        public void update(DateTime now)
        {
            var list = _list;
            var timeout = Party.c_recruitTimeout;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i]._recvTime.AddSeconds(timeout) <= now)
                    list.RemoveAt(i);
            }
        }
    }

    public class Host
    {
        [MonoModIgnore] private Party.MemberList _memberList;

        private static readonly Predicate<Party.Member> IsNull = m => m == null;


        [MonoModIgnore] private extern void sendPartyMemberInfo();
        [MonoModIgnore] private extern void sendPartyMemberState();
        [MonoModIgnore] private extern void sendRecruitInfo();

        [MonoModReplace]
        private void eraseDisconnectMember(bool isAutoSend)
        {
            var members = _memberList;
            var count = members.Count;
            for (var i = 0; i < count; i++)
            {
                var member = members[i];
                if (member == null || !member.isActive())
                {
                    member?.Dispose();
                    members[i] = null;
                }
            }
            members.RemoveAll(IsNull);
            if (count == members.Count)
                return;
            count = members.Count;
            for (var i = 0; i < count; i++)
            {
                var member = members[i];
                if (member != null && member.isJoin())
                    member.setStateChanged(stateChanged: true);
            }
            if (!isAutoSend)
                return;
            sendPartyMemberInfo();
            sendPartyMemberState();
            sendRecruitInfo();
        }
    }
}
