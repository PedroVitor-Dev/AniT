using System.Text.Json;
namespace AniT.Player;

public sealed partial class MpvOnlinePlayer
{
    readonly SemaphoreSlim pauseCommands=new(1,1);
    readonly object pauseSync=new();
    bool indexReloading,reloadPauseIntent;
    int pauseReloadGeneration;
    bool UserPaused { get { lock(pauseSync)return indexReloading?reloadPauseIntent:paused; } }
    async Task ChangeUserPauseAsync(bool? target,CancellationToken token)
    {
        await pauseCommands.WaitAsync(token);
        try {
            lock(pauseSync) if(indexReloading){reloadPauseIntent=target??!reloadPauseIntent;PublishSnapshot();return;}
            await CommandAsync(target is { } value?["set_property","pause",value]:["cycle","pause"],token);
        } finally {pauseCommands.Release();}
    }
    async Task BeginPauseReloadAsync(int expected,CancellationToken token)
    {
        await pauseCommands.WaitAsync(token);
        try {
            var state=await CommandAsync(["get_property","pause"],token);token.ThrowIfCancellationRequested();
            lock(pauseSync){
                if(generation!=expected)throw new OperationCanceledException(token);
                indexReloading=true;pauseReloadGeneration=expected;
                reloadPauseIntent=state.ValueKind is JsonValueKind.True or JsonValueKind.False?state.GetBoolean():paused;
            }
        } finally {pauseCommands.Release();}
    }
    async Task FinishPauseReloadAsync(int expected,CancellationToken token)
    {
        try {
            await pauseCommands.WaitAsync(token);
            try {
                bool target;
                lock(pauseSync){
                    if(!indexReloading||pauseReloadGeneration!=expected||generation!=expected)return;
                    target=reloadPauseIntent;indexReloading=false;
                }
                await CommandAsync(["set_property","pause",target],token);
                if(generation==expected){paused=target;PublishSnapshot();}
            } finally {pauseCommands.Release();}
        } finally {lock(pauseSync)if(pauseReloadGeneration==expected)indexReloading=false;}
    }
    async void ResumeRequested(){try{await ChangeUserPauseAsync(false,CancellationToken.None);}catch(Exception error){Trace(error.Message);}}
}
