namespace AniT.Player;
public sealed class PlaybackPreparationCancelledException:OperationCanceledException
{
    public PlaybackPreparationCancelledException():base("A abertura do player foi encerrada pelo usuário."){}
}
