using System;
using System.Linq;
using System.Windows;
using AniT.Player;
namespace AniT.App;
internal partial class OnlinePlayerWindow
{
    OnlineSubtitleWindow? subtitleWindow;
    void CloseSubtitleCenter(){subtitleWindow?.Close();subtitleWindow=null;}
    internal void OpenSubtitleCenter()
    {
        CloseMenus();
        if(subtitleWindow is{IsVisible:true}){subtitleWindow.Activate();return;}
        subtitleWindow=new OnlineSubtitleWindow(player,preferences.SubtitleAppearance,async(style,delay,token)=>{
            await player.ApplySubtitleAppearanceAsync(style,token);await player.SetSubtitleDelayAsync(delay,token);
            preferences=preferences with{SubtitleAppearance=style.Normalize()};QueueSave();SavePreferences();
        }){Owner=this};subtitleWindow.Closed+=(_,_)=>subtitleWindow=null;subtitleWindow.Show();
    }
    void SubtitlesCenterClick(object sender,RoutedEventArgs args)=>OpenSubtitleCenter();
    void SubtitleDrop(object sender,DragEventArgs args)
    {
        if(!args.Data.GetDataPresent(DataFormats.FileDrop))return;
        var files=((string[]?)args.Data.GetData(DataFormats.FileDrop))?.Where(MpvOnlinePlayer.IsSubtitleFile).Take(32).ToArray()??[];
        if(files.Length==0)return;args.Handled=true;
        OpenSubtitleCenter();subtitleWindow?.ImportFiles(files);
    }
    void SubtitleDragOver(object sender,DragEventArgs args){args.Effects=args.Data.GetDataPresent(DataFormats.FileDrop)&&((string[]?)args.Data.GetData(DataFormats.FileDrop))?.Any(MpvOnlinePlayer.IsSubtitleFile)==true?DragDropEffects.Copy:DragDropEffects.None;args.Handled=true;}
}
