using System.Windows;
using AniT.Core;
using AniT.Player;
namespace AniT.App;
internal partial class OnlinePlayerWindow
{
    readonly PlaybackAssistant assistant=new();
    PlaybackAdvice? advice;
    bool buildingReserve;
    internal void UpdateAssistant(OnlinePlayerSnapshot state)
    {
        advice=preferences.BakiAssistantEnabled&&!showError?assistant.Update(new(state.VideoReady,state.Paused,state.Buffering,state.Seeking,state.CacheSeconds,player.SupplyRatio,buildingReserve)):null;
        var visible=advice is not null?Visibility.Visible:Visibility.Collapsed;
        AssistantFace.Visibility=AssistantDismiss.Visibility=visible;
        AssistantAction.Visibility=advice?.Action is not null?Visibility.Visible:Visibility.Collapsed;
        if(advice is null)return;
        Status.Text="Baki-Pi · "+advice.Message;
        AssistantAction.Content=advice.Action switch{"pause"=>"Acumular","play"=>"Reproduzir",_=>"Fechar"};
    }
    void DismissAssistant(object sender,RoutedEventArgs args){assistant.Dismiss();UpdateAssistant(player.Snapshot);}
    void AssistantClick(object sender,RoutedEventArgs args)
    {
        var action=advice?.Action;
        if(action=="close"){Close();return;}
        Run(async()=>{
            if(action=="pause"){await player.PauseAsync(lifetime.Token);buildingReserve=true;}
            else if(action=="play"){buildingReserve=false;await player.ResumeAsync(lifetime.Token);}
        });
    }
}
