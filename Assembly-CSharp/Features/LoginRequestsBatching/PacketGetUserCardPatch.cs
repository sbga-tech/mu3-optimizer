using System.Collections.Generic;
using MonoMod;
using MU3.Client;

namespace MU3.Mod.LoginRequestsBatching;

[MonoModIfFlag(nameof(PatchConfig.LoginRequestsBatching))]
[MonoModPatch("global::MU3.Client.PacketGetUserCard")]
public class PacketGetUserCardPatch : PacketGetUserCard
{
    [PatchDictAlloc]
    public extern void orig_ctor();
    
    [MonoModConstructor]
    public void ctor()
    {
        orig_ctor();
    }
}