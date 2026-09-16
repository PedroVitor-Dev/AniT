namespace AniT.Core;

public sealed record FileSidecarMove(string SourcePath, string DestinationPath);

public sealed record FileOrganizationOperation(
    Guid MediaFileId,
    string SourcePath,
    string DestinationPath,
    IReadOnlyList<FileSidecarMove> Sidecars,
    bool HasConflict,
    string? ValidationMessage);

public sealed record FileOrganizationPlan(IReadOnlyList<FileOrganizationOperation> Operations)
{
    public bool CanExecute => Operations.Count > 0 && Operations.All(operation => !operation.HasConflict);
}

public sealed record FileOrganizationResult(int Completed, IReadOnlyList<string> Errors);
