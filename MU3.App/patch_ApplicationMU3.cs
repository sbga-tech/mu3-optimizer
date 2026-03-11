using System.Collections;
using MonoMod;
using MU3.AM;
using MU3.Sequence;
using MU3.Util;

namespace MU3.App;

[MonoModIfFlag("NoAMDuringPlay")]
public class patch_ApplicationMU3 : SingletonMonoBehaviourStateMachine<ApplicationMU3, ApplicationMU3.EState>
{
    
    [MonoModIgnore]
    private enum AbortState
    {
        None,
        Req,
        Aborting
    }

    private AbortState _abortState; 
    
    [MonoModIgnore]
    private extern IEnumerator abortProc();
    
    [MonoModReplace]
    public new void Update()
    {
        if (!patch_PlayMusic.IsPlayingMusic)
        {
            SingletonStateMachine<AMManager, AMManager.EState>.instance.execute();
        }
        
        base.Update();
        
        if (_abortState == AbortState.Req)
        {
            _abortState = AbortState.Aborting;
            StartCoroutine(abortProc());
        }
    }
}