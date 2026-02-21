using System.Collections;
using MonoMod;
using MU3.AM;
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
        var rootScript = RootScript.instance as patch_RootScript;
        if (rootScript == null || !rootScript.isPlayingMusic())
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