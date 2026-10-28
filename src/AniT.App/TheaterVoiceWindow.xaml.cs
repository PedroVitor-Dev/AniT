using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AniT.Theater;

namespace AniT.App;

internal sealed class TheaterVoiceMember(TheaterParticipant person, bool canModerate) : INotifyPropertyChanged
{
    public TheaterParticipant Person { get; private set; } = person;
    public bool CanModerate { get; } = canModerate;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(TheaterParticipant next)
    {
        if (Person == next) return;
        Person = next; PropertyChanged?.Invoke(this, new(nameof(Person)));
    }
}
internal partial class TheaterVoiceWindow : Window
{
    private readonly Func<Task> join, mute, deafen;
    private readonly Func<Guid, Task> moderate;
    private bool executing;
    private IReadOnlyList<TheaterVoiceMember> members = Array.Empty<TheaterVoiceMember>();
    public TheaterVoiceWindow(Func<Task> join, Func<Task> mute, Func<Task> deafen, Func<Guid, Task> moderate)
    {
        this.join = join; this.mute = mute; this.deafen = deafen; this.moderate = moderate;
        InitializeComponent(); ResponsiveWindow.FitToWorkArea(this, 400, 510); AppearanceManager.ApplyToWindow(this);
    }
    public void Refresh(TheaterRoom room, bool busy)
    {
        StateLabel.Text = !room.VoiceEnabled ? "Microfone desligado" : room.MicrophoneBlocked ? "Microfone bloqueado pelo host" : room.VoiceDeafened ? "Áudio desativado" : room.VoiceMuted ? "Conectado · microfone silenciado" : "Conectado · microfone ativo";
        JoinLabel.Text = room.VoiceEnabled ? "Sair da voz" : "Entrar na voz";
        MuteLabel.Text = room.VoiceMuted ? "Ativar microfone" : "Silenciar microfone";
        MuteButton.Visibility = DeafenButton.Visibility = room.VoiceEnabled ? Visibility.Visible : Visibility.Collapsed;
        DeafenLabel.Text = room.VoiceDeafened ? "Ativar áudio" : "Desativar áudio";
        JoinButton.IsEnabled = !busy && !executing;
        MuteButton.IsEnabled = !busy && !executing && (!room.VoiceMuted || !room.MicrophoneBlocked && !room.VoiceDeafened);
        DeafenButton.IsEnabled = !busy && !executing;
        Members.IsEnabled = !busy && !executing;
        var next = room.Participants;
        if (!members.Select(p => p.Person.Id).SequenceEqual(next.Select(p => p.Id)))
        {
            members = next.Select(p => new TheaterVoiceMember(p, room.IsHost && !p.IsHost)).ToArray(); Members.ItemsSource = members;
        }
        else for (var i = 0; i < next.Count; i++) members[i].Update(next[i]);
    }
    internal void ShowError(string message) => Error.Text = message;

    private async Task ExecuteAsync(Func<Task> action)
    {
        if (executing) return;
        executing = true; JoinButton.IsEnabled = MuteButton.IsEnabled = DeafenButton.IsEnabled = Members.IsEnabled = false;
        try { Error.Text = ""; await action(); }
        catch (Exception error) { Error.Text = error.Message; }
        finally { executing = false; }
    }
    private async void Join_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(join);
    private async void Mute_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(mute);
    private async void Deafen_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(deafen);
    private async void Moderate_Click(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { Tag: Guid id }) await ExecuteAsync(() => moderate(id)); }
}
