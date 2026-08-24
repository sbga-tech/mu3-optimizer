using MonoMod;

namespace MU3.Client;

[MonoModIfFlag("LoginRequestsBatching")]
public class patch_PacketGetUserCharacter : PacketGetUserCharacter
{
    [PatchDictAlloc]
    public extern void orig_ctor();
    
    [MonoModConstructor]
    public void ctor()
    {
        orig_ctor();
    }
}