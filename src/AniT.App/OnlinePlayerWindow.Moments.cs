using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using AniT.Infrastructure;
namespace AniT.App;
internal partial class OnlinePlayerWindow
{
    PlaybackMomentStore? momentStore;
    Guid? momentEpisode;
    OnlineMomentsWindow? momentsWindow;
    internal void SetEpisodeContext(Guid? episode)
    {
        if(momentEpisode!=episode)CloseMoments();momentEpisode=episode;
        MomentsAction.IsEnabled=momentStore is not null&&episode is not null;
    }
    void CloseMoments(){momentsWindow?.Close();momentsWindow=null;}
    void OpenMoments()
    {
        if(momentStore is null||momentEpisode is null)return;
        if(momentsWindow is {IsVisible:true}){momentsWindow.Activate();return;}
        momentsWindow=new OnlineMomentsWindow(momentStore,momentEpisode.Value,()=>player.Snapshot,SaveMomentAsync,SeekTo,
            ()=>preferences.ProtectMomentSpoilers,value=>{preferences=preferences with{ProtectMomentSpoilers=value};QueueSave();SavePreferences();}){Owner=this};
        momentsWindow.Closed+=(_,_)=>momentsWindow=null;momentsWindow.Show();
    }
    internal async Task SaveMomentAsync(string note,CancellationToken token)
    {
        if(momentStore is null||momentEpisode is not {} episode)return;
        var store=momentStore;var id=Guid.NewGuid();var final=store.ThumbnailPath(id);var temporary=final+".capture.png";
        Directory.CreateDirectory(Path.GetDirectoryName(final)!);bool saved=false;
        try {
            var position=await player.CaptureFrameAsync(temporary,token);
            token.ThrowIfCancellationRequested();if(momentEpisode!=episode)throw new OperationCanceledException(token);
            var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=320;image.UriSource=new Uri(temporary);image.EndInit();image.Freeze();
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(final))encoder.Save(output);
            store.Add(new(id,episode,position.TotalSeconds,note));saved=true;
        } finally {
            try{File.Delete(temporary);if(!saved)File.Delete(final);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    bool registeringMoment;
    void RegisterMomentClick(object sender,RoutedEventArgs args)
    {
        if(registeringMoment)return;
        Run(async()=>{
            registeringMoment=true;RegisterMomentAction.IsEnabled=false;
            try{await SaveMomentAsync("",lifetime.Token);OpenMoments();momentsWindow?.ConfirmRegistered();}
            finally{registeringMoment=false;UpdateView(player.Snapshot);}
        });
    }
    void MomentsClick(object sender,RoutedEventArgs args)=>OpenMoments();
}
