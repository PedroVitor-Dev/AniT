using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AniT.Theater;

namespace AniT.App;

internal partial class TheaterPreparationWindow : Window
{
    private readonly Func<Task> confirm;
    private IReadOnlyList<TheaterParticipant> members = Array.Empty<TheaterParticipant>();
    private bool executing;
    public TheaterPreparationWindow(Func<Task> confirm)
    {
        this.confirm = confirm; InitializeComponent();
        ResponsiveWindow.FitToWorkArea(this, 460, 510); AppearanceManager.ApplyToWindow(this);
    }
    public void Refresh(TheaterRoom room, bool busy, long deadline)
    {
        var participants = room.Participants;
        if (!members.Select(p => (p.Id, p.DisplayName, p.SessionLabel)).SequenceEqual(participants.Select(p => (p.Id, p.DisplayName, p.SessionLabel)))) { members = participants; Members.ItemsSource = participants; }
        var now = ClockEstimator.NowMs + (room.IsHost ? 0 : room.Clock.OffsetMs);
        var remaining = room.PlaybackAuthorized ? Math.Max(0, (int)Math.Ceiling((deadline - now) / 1000d)) : 0;
        Countdown.Text = remaining > 0 ? $"Começa em {remaining}…" : "";
        Heading.Text = remaining > 0 ? "Início autorizado" : room.IsHost ? room.CanApprovePlayback ? "Todos estão prontos" : "Preparando o grupo" : room.GuestConfirmed ? "Presença confirmada" : "Seu vídeo está pronto";
        Message.Text = remaining > 0 ? "Todos seguem a mesma contagem. Aguarde o início do vídeo." : room.IsHost ? room.CanApprovePlayback ? "Dê o aval para iniciar a contagem de cinco segundos." : !room.IsConnected ? "Aguarde os convidados entrarem na sala. O vídeo continua pausado." : $"{participants.Count(p => !p.IsHost && p.Confirmed)} de {participants.Count(p => !p.IsHost)} convidados confirmaram. Aguarde o grupo ficar pronto." : room.GuestConfirmed ? "Aguarde o host autorizar. Você pode continuar usando a janela de voz." : "Confirme aqui, sem sair do vídeo. Só o host pode iniciar a reprodução.";
        Progress.Value = room.PlaybackAuthorized ? 100 : 100d * participants.Count(p => p.IsHost ? p.Ready : p.Confirmed) / Math.Max(1, participants.Count);
        ConfirmButton.Content = room.IsHost ? "Autorizar e iniciar" : "Confirmar presença";
        ConfirmButton.Visibility = room.IsHost ? room.CanApprovePlayback && !room.PlaybackAuthorized ? Visibility.Visible : Visibility.Collapsed : !room.GuestConfirmed ? Visibility.Visible : Visibility.Collapsed;
        ConfirmButton.IsEnabled = !busy && !executing && (room.IsHost ? room.CanApprovePlayback : room.LocalReady && room.PreparationStarted && room.IsConnected);
    }
    internal void ShowError(string message) => Error.Text = message;

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (executing || !ConfirmButton.IsEnabled) return;
        executing = true; ConfirmButton.IsEnabled = false; Error.Text = "";
        try { await confirm(); }
        catch (Exception error) { Error.Text = error.Message; }
        finally { executing = false; }
    }
}
