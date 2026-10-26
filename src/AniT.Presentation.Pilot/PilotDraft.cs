using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AniT.Presentation.Pilot;

/// <summary>Ephemeral demonstration data. Nothing is written to profiles or disk.</summary>
public sealed class PilotDraft : INotifyPropertyChanged, IDataErrorInfo
{
    private string name = "Minha estante";
    private bool validate;
    public string Name
    {
        get => name;
        set { name = value; Changed(); Changed(nameof(ValidationMessage)); }
    }
    public string ValidationMessage => !validate ? string.Empty
        : string.IsNullOrWhiteSpace(Name) ? "Digite um nome para a coleção."
        : Name.Trim().Length > 32 ? "Use até 32 caracteres." : string.Empty;
    public string Error => ValidationMessage;
    public string this[string column] => column == nameof(Name) ? ValidationMessage : string.Empty;
    public bool Validate()
    {
        validate = true;
        Changed(nameof(Name)); Changed(nameof(ValidationMessage));
        return ValidationMessage.Length == 0;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
}
