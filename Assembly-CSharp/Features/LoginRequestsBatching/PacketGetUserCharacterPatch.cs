using MU3.Client;

﻿using MonoMod;

namespace MU3.Mod.LoginRequestsBatching;

[MonoModIfFlag(nameof(PatchConfig.LoginRequestsBatching))]
[MonoModPatch("global::MU3.Client.PacketGetUserCharacter")]
public class PacketGetUserCharacterPatch : PacketGetUserCharacter
{
    [PatchDictAlloc]
    public extern void orig_ctor();
    
    [MonoModConstructor]
    public void ctor()
    {
        orig_ctor();
    }
}