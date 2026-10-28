using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AniT.Theater;

namespace AniT.App;

public partial class MovieTheaterWindow
{
    private IReadOnlyList<TheaterParticipant> shownParticipants = Array.Empty<TheaterParticipant>();
    private bool returningAfterRemoval;
    private TheaterVoiceDevice? voiceDevice;

    private void UpdateHostManager()
    {
        var active = room;
        VoicePanel.Visibility = active is not null ? Visibility.Visible : Visibility.Collapsed;
        OpenPreparationPopupButton.Visibility = active is { Mode: TheaterMode.PeerSync } && prepared ? Visibility.Visible : Visibility.Collapsed;
        VoiceStateLabel.Text = active?.VoiceEnabled == true ? active.MicrophoneBlocked ? "Microfone bloqueado pelo host" : active.VoiceDeafened ? "Áudio e microfone desativados" : active.VoiceMuted ? "Conectado · microfone silenciado" : "Conectado · microfone ativo" : "Seu microfone está desligado. Entre na voz para conversar.";
        if (voiceDevice is not null && active?.VoiceEnabled != true) { voiceDevice.Dispose(); voiceDevice = null; }
        try { voiceDevice?.Refresh(); } catch (Exception) { VoiceDeviceFailed("Não foi possível ativar o dispositivo de áudio."); }
        HostManagerPanel.Visibility = active?.IsHost == true ? Visibility.Visible : Visibility.Collapsed;
        GuestVoicePolicyPanel.Visibility = active is { IsHost: false, IsConnected: true } ? Visibility.Visible : Visibility.Collapsed;
        UpdateFloatingWindows();
        if (active is null) return;
        if (!active.IsHost)
        {
            var members = active.Participants;
            if (!shownParticipants.SequenceEqual(members)) { shownParticipants = members; GuestVoiceMembers.ItemsSource = members; }
            GuestVoicePolicyLabel.Text = active.MicrophoneBlocked ? "O host bloqueou sua permissão de microfone." : "Sua permissão de microfone está liberada pelo host.";
            return;
        }
        HostManagerPanel.IsEnabled = !busy;
        var participants = active.Participants;
        HostManagerCount.Text = $"{participants.Count} de 5 participantes · você é o host";
        HostManagerEmpty.Visibility = participants.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (!shownParticipants.SequenceEqual(participants)) { shownParticipants = participants; HostParticipantList.ItemsSource = participants; }
        var invite = active.Invite!.Encode();
        if (RoomInvite.Text != invite) RoomInvite.Text = invite;
    }

    private Task ToggleVoiceAsync() => RunAsync(async token =>
    {
        if (room is not { } active) return;
        if (active.VoiceEnabled) { voiceDevice?.Dispose(); voiceDevice = null; await active.StopVoiceAsync(); return; }
        try
        {
            await active.StartVoiceAsync(token);
            voiceDevice = new TheaterVoiceDevice(active);
            voiceDevice.Failed += VoiceDeviceFailed;
            voiceDevice.Refresh();
        }
        catch { voiceDevice?.Dispose(); voiceDevice = null; await active.StopVoiceAsync(); throw; }
    });
    private Task ToggleMicrophoneAsync() => RunAsync(async token =>
    {
        if (room is not { VoiceEnabled: true } active) return;
        await active.SetVoiceStateAsync(!active.VoiceMuted, active.VoiceDeafened, token); voiceDevice?.Refresh();
    });
    private Task ToggleVoiceAudioAsync() => RunAsync(async token =>
    {
        if (room is not { VoiceEnabled: true } active) return;
        await active.SetVoiceStateAsync(true, !active.VoiceDeafened, token); voiceDevice?.Refresh();
    });
    private void VoiceDeviceFailed(string message) => Dispatcher.BeginInvoke(new Action(async () =>
    {
        voiceDevice?.Dispose(); voiceDevice = null;
        if (room is { } active) await active.StopVoiceAsync();
        TheaterStatus.Text = message; voicePopup?.ShowError(message); UpdateControls();
    }));

    private async void MuteParticipant_Click(object sender, RoutedEventArgs e) => await RunAsync(async token =>
    {
        if (sender is not FrameworkElement { Tag: Guid id } || room is not { IsHost: true } active) return;
        var target = active.Participants.SingleOrDefault(p => p.Id == id && !p.IsHost) ?? throw new InvalidOperationException("O convidado já saiu da sala.");
        await active.SetMicrophoneBlockedAsync(id, !target.MicrophoneBlocked, token);
        TheaterStatus.Text = target.MicrophoneBlocked ? "Microfone liberado pelo host." : "Microfone bloqueado pelo host. O áudio do episódio continua normal.";
    });

    private Task ToggleParticipantMicrophoneAsync(Guid id) => RunAsync(async token =>
    {
        if (room is not { IsHost: true } active) return;
        var member = active.Participants.SingleOrDefault(p => p.Id == id && !p.IsHost);
        if (member is not null) await active.SetMicrophoneBlockedAsync(id, !member.MicrophoneBlocked, token);
    });

    private async void RemoveParticipant_Click(object sender, RoutedEventArgs e) => await RunAsync(async token =>
    {
        if (sender is not FrameworkElement { Tag: Guid id } || room is not { IsHost: true } active) return;
        TheaterStatus.Text = "Removendo convidado e encerrando sua participação…";
        await active.RemoveParticipantAsync(id, token);
        TheaterStatus.Text = "Convidado removido. O convite anterior foi invalidado; compartilhe o novo convite para permitir uma nova entrada.";
        HostInvitePanel.IsExpanded = true;
    });

    private async Task ReturnAfterRemovalAsync()
    {
        if (returningAfterRemoval || closed) return;
        returningAfterRemoval = true;
        UpdateControls();
        try
        {
            var hadEpisode = room is { Mode: TheaterMode.PeerSync, HasMedia: true };
            TheaterStatus.Text = "O host removeu você. Encerrando a participação…";
            operation?.Cancel(); LockPlayerForBuffer();
            await LeaveRoomAsync();
            JoinInvite.Clear();
            if (!closed) { TheaterStatus.Text = "Você foi removido da sala pelo host. Peça um novo convite para entrar novamente." + (hadEpisode ? " Seu episódio completo permanece salvo." : ""); UpdateControls(); }
        }
        finally { returningAfterRemoval = false; if (!closed) UpdateControls(); }
    }
}
