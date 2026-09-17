using System.Windows;
using AniT.Core.Achievements;

namespace AniT.App;

public partial class AchievementDetailWindow : Window
{
    public AchievementDetailWindow(AchievementProgress progress)
    {
        InitializeComponent();
        var card = AchievementCardView.Create(progress);
        DataContext = new DetailModel(card, Line(progress));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string Line(AchievementProgress progress)
    {
        if (progress.Definition.IsSecret && !progress.IsUnlocked) return "O Baki-Pi ainda está escondendo este segredo de você.";
        return progress.Definition.Id switch
        {
            30 => "Nem o Baki-Pi sabe como você ainda está acordado.",
            65 => "Cem horas, cem mundos e ainda há muito para descobrir.",
            91 => "Ele estava mesmo de olho — exatamente às 03:33.",
            95 => "Dez notas perfeitas seguidas. O Baki-Pi aprova!",
            100 => "A jornada nunca termina; ela apenas encontra novas histórias.",
            _ => "Cada marco guarda uma lembrança da sua jornada."
        };
    }

    private sealed record DetailModel(AchievementCardView Card, string BakiPiLine)
    {
        public int Id => Card.Id;
        public string IdLabel => Card.IdLabel;
        public string DisplayName => Card.DisplayName;
        public string DisplayDescription => Card.DisplayDescription;
        public string IconPath => Card.IconPath;
        public Visibility IconVisibility => Card.IconVisibility;
        public Visibility LockedVisibility => Card.LockedVisibility;
        public string RarityLabel => Card.RarityLabel;
        public string ProgressLabel => Card.ProgressLabel;
        public double ProgressPercent => Card.ProgressPercent;
        public string StatusLabel => Card.StatusLabel;
        public System.Windows.Media.Brush AccentBrush => Card.AccentBrush;
        public System.Windows.Media.Brush RarityBackground => Card.RarityBackground;
        public System.Windows.Media.Color GlowColor => Card.GlowColor;
    }
}
