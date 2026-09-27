using System.Globalization;
using System.Windows;
using Microsoft.Win32;

namespace AniT.App;

internal static class BackupController
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");

    public static async Task ExportAsync(Window owner, Action<string>? reportStatus = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Exportar backup portátil do AniT",
            Filter = "Backup portátil do AniT (*.anitbackup)|*.anitbackup",
            FileName = $"AniT-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.anitbackup",
            AddExtension = true,
            DefaultExt = ".anitbackup",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(owner) != true) return;

        try
        {
            owner.IsEnabled = false;
            reportStatus?.Invoke("Preparando um snapshot seguro de todos os seus dados…");
            var manifest = await App.ExportBackupAsync(dialog.FileName);
            await App.Achievements.RecordAsync(new global::AniT.Core.Achievements.AchievementEvent(
                global::AniT.Core.Achievements.AchievementEventType.BackupCreated));
            reportStatus?.Invoke($"✓ Backup salvo com {manifest.Summary.AnimeCount} animes e {manifest.Summary.EpisodeCount} episódios.");
            MessageBox.Show(
                $"Backup exportado com sucesso.\n\n" +
                $"{manifest.Summary.AnimeCount} animes • {manifest.Summary.EpisodeCount} episódios • {manifest.Summary.UnlockedAchievementCount} conquistas\n\n" +
                "O arquivo é único e pode ser guardado no OneDrive, Google Drive, Dropbox ou em qualquer pasta.",
                "Backup do AniT",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            reportStatus?.Invoke("Não foi possível criar o backup.");
            MessageBox.Show($"Não foi possível exportar o backup.\n\n{exception.Message}", "AniT", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            owner.IsEnabled = true;
        }
    }

    public static async Task ImportAsync(Window owner, Action<string>? reportStatus = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importar backup do AniT",
            Filter = "Backup portátil do AniT (*.anitbackup)|*.anitbackup",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(owner) != true) return;

        try
        {
            owner.IsEnabled = false;
            reportStatus?.Invoke("Verificando integridade, versão e conteúdo do backup…");
            var inspection = await App.InspectBackupAsync(dialog.FileName);
            var summary = inspection.Manifest.Summary;
            owner.IsEnabled = true;
            var confirmation = MessageBox.Show(
                $"Backup de {inspection.Manifest.CreatedAtUtc.ToLocalTime().ToString("g", Portuguese)}\n\n" +
                $"• {summary.AnimeCount} animes\n" +
                $"• {summary.EpisodeCount} episódios\n" +
                $"• {summary.WatchedEpisodeCount} episódios com atividade\n" +
                $"• {summary.UnlockedAchievementCount} conquistas desbloqueadas\n\n" +
                "O perfil, histórico, progresso, avaliações, favoritos, conquistas, configurações e artes serão substituídos. " +
                "Antes disso, o AniT criará automaticamente um backup de recuperação do estado atual.\n\n" +
                "Importe somente arquivos criados por você ou recebidos de uma fonte confiável. Os hashes detectam corrupção, mas não comprovam quem criou o arquivo.\n\n" +
                "Deseja continuar?",
                "Importar backup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                reportStatus?.Invoke("Importação cancelada; nenhum dado foi alterado.");
                return;
            }

            owner.IsEnabled = false;
            reportStatus?.Invoke("Criando ponto de recuperação e restaurando seus dados…");
            var result = await App.RestoreBackupAsync(dialog.FileName);
            MessageBox.Show(
                "Backup importado e validado com sucesso. O AniT será reiniciado para carregar todos os dados restaurados.\n\n" +
                $"Cópia de segurança anterior:\n{result.RecoveryBackupPath}\n\n" +
                "Se os vídeos estiverem em outra pasta no novo PC, use Estante nas Configurações para reconectar a biblioteca.",
                "Restauração concluída",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            App.RestartAfterBackupRestore();
        }
        catch (Exception exception)
        {
            reportStatus?.Invoke("O backup não foi importado; seus dados atuais foram preservados.");
            MessageBox.Show(
                $"Não foi possível importar este backup. Nenhum arquivo não validado foi aplicado.\n\n{exception.Message}",
                "Backup inválido ou indisponível",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            if (!Application.Current.Dispatcher.HasShutdownStarted) owner.IsEnabled = true;
        }
    }
}
