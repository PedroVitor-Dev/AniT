using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using AniT.Infrastructure;

namespace AniT.App;

public partial class OnboardingWindow : Window
{
    private readonly FirstRunSetupStore setupStore;
    private readonly ProfileSettings profile;
    private string? avatarPath;
    private string? folderPath;
    private int step;
    private bool saving;

    public OnboardingWindow(FirstRunSetupStore setupStore, ProfileSettings profile, string? configuredFolder)
    {
        this.setupStore = setupStore;
        this.profile = profile;
        InitializeComponent();
        ResponsiveWindow.FitToWorkArea(this, 1040, 740);
        NameBox.Text = profile.DisplayName is "Pet-S" or "Meu perfil" ? string.Empty : profile.DisplayName;
        avatarPath = profile.AvatarPath;
        RefreshAvatar();
        folderPath = configuredFolder;
        FolderText.Text = folderPath ?? "Nenhuma pasta selecionada";
        AppearanceManager.ApplyToWindow(this);
        ShowStep();
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (StoryPanel is null) return;
        // Root size is measured after interface scaling, unlike the outer window size.
        var showStory = e.NewSize.Width >= 900 && e.NewSize.Height >= 580;
        StoryPanel.Visibility = showStory ? Visibility.Visible : Visibility.Collapsed;
        StoryColumn.Width = new GridLength(showStory ? 252 : 0);
    }

    private void ShowStep()
    {
        WelcomeStep.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        ProfileStep.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        FolderStep.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        ReadyStep.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Heading.Text = new[] { "Configurar o AniT", "Seu perfil", "Pasta de animes", "Revisar configuração" }[step];
        StepText.Text = $"PASSO {step + 1} DE 4 · {new[] { "BOAS-VINDAS", "SEU PERFIL", "PASTA DE ANIMES", "REVISÃO" }[step]}";
        StepProgress.Value = step + 1;
        var stepLabels = new[] { StepLabel0, StepLabel1, StepLabel2, StepLabel3 };
        for (var index = 0; index < stepLabels.Length; index++)
        {
            stepLabels[index].SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
                index == step ? "AniT.Brush.AccentCyan" : "AniT.Brush.TextSecondary");
            stepLabels[index].FontWeight = index == step ? FontWeights.SemiBold : FontWeights.Normal;
        }
        BackButton.Visibility = step == 0 ? Visibility.Hidden : Visibility.Visible;
        NextButton.Content = step == 0 ? "_Começar" : step == 3 ? "_Entrar no AniT" : "_Continuar";
        NextButton.IsEnabled = step != 2 || folderPath is not null;
        SummaryName.Text = DisplayName;
        SummaryFolder.Text = folderPath ?? "Configurar depois, em Configurações → Estante.";
        StatusText.Text = string.Empty;
        StepScroller.ScrollToTop();
        NextButton.Focus();
    }

    private string DisplayName => string.IsNullOrWhiteSpace(NameBox.Text) ? "Meu perfil" : NameBox.Text.Trim();

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (saving || step == 0) return;
        step--;
        ShowStep();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (saving) return;
        if (step < 3)
        {
            if (step == 2 && !ValidateFolder()) return;
            step++;
            ShowStep();
            return;
        }

        if (folderPath is not null && !ValidateFolder()) return;
        saving = true;
        BackButton.IsEnabled = NextButton.IsEnabled = false;
        StatusText.Text = "Salvando configuração…";
        try
        {
            var savedAvatar = ProfileSettingsStore.PersistAvatar(avatarPath);
            await using var context = await Task.Run(App.OpenFreshDatabase);
            var roots = await context.LibraryRoots.ToListAsync();
            if (folderPath is not null)
            {
                var root = roots.FirstOrDefault(item => string.Equals(item.Path, folderPath, StringComparison.OrdinalIgnoreCase));
                if (root is null)
                {
                    root = new global::AniT.Core.LibraryRoot { Path = folderPath };
                    context.LibraryRoots.Add(root);
                }
                root.DisplayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(folderPath));
                if (string.IsNullOrWhiteSpace(root.DisplayName)) root.DisplayName = "Minha estante";
                root.IsEnabled = true;
                root.IncludeSubfolders = true;
                root.LastUnavailableAt = null;
                foreach (var previous in roots.Where(item => item.Id != root.Id)) previous.IsEnabled = false;
            }
            else
            {
                foreach (var root in roots) root.IsEnabled = false;
            }
            await context.SaveChangesAsync();
            ProfileSettingsStore.Save(profile with { DisplayName = DisplayName, AvatarPath = savedAvatar });
            avatarPath = savedAvatar;
            setupStore.Complete();
            DialogResult = true;
        }
        catch (Exception exception)
        {
            global::AniT.Infrastructure.AniTDiagnostics.Write("ONBOARDING", "Não foi possível concluir a configuração inicial.", exception);
            StatusText.Text = "Não foi possível salvar. Confira o acesso à pasta e à foto e tente novamente. Você também pode voltar e escolher outras opções.";
        }
        finally
        {
            saving = false;
            BackButton.IsEnabled = NextButton.IsEnabled = true;
        }
    }

    private bool ValidateFolder()
    {
        if (folderPath is not null && Directory.Exists(folderPath)) return true;
        StatusText.Text = "A pasta não está disponível. Volte à etapa da estante para escolher outra ou configurar depois.";
        return false;
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Escolha a pasta principal dos seus animes" };
        if (folderPath is not null && Directory.Exists(folderPath)) dialog.InitialDirectory = folderPath;
        if (dialog.ShowDialog(this) != true) return;
        folderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dialog.FolderName));
        FolderText.Text = folderPath;
        NextButton.IsEnabled = true;
        StatusText.Text = string.Empty;
    }

    private void SkipFolder_Click(object sender, RoutedEventArgs e)
    {
        folderPath = null;
        FolderText.Text = "Nenhuma pasta selecionada";
        step = 3;
        ShowStep();
    }

    private void ChooseAvatar_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher foto de perfil",
            Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            // Decode now so invalid files cannot prevent completing first use later.
            var preview = ComfortableImageSource.Load(dialog.FileName, 320);
            avatarPath = dialog.FileName;
            AvatarBrush.ImageSource = preview;
            StatusText.Text = string.Empty;
        }
        catch
        {
            StatusText.Text = "Não foi possível abrir essa imagem. Escolha outra foto PNG, JPG ou BMP.";
        }
    }

    private void ResetAvatar_Click(object sender, RoutedEventArgs e)
    {
        avatarPath = null;
        RefreshAvatar();
        StatusText.Text = string.Empty;
    }

    private void RefreshAvatar()
    {
        const string fallback = ProfileSettingsStore.DefaultAvatarPath;
        try { AvatarBrush.ImageSource = ComfortableImageSource.Load(avatarPath ?? fallback, 320); }
        catch { avatarPath = null; AvatarBrush.ImageSource = ComfortableImageSource.Load(fallback, 320); }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Let database preparation and the explicit save finish before closing.
        if (saving && DialogResult != true) e.Cancel = true;
        base.OnClosing(e);
    }
}
