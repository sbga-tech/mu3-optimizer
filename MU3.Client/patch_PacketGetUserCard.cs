using System.Collections.Generic;
using MonoMod;

namespace MU3.Client;

[MonoModIfFlag("BoostLoginRequests")]
public class patch_PacketGetUserCard : PacketGetUserCard
{
    [PatchDictAlloc]
    public extern void orig_ctor();
    
    [MonoModConstructor]
    public void ctor()
    {
        orig_ctor();
    }
}