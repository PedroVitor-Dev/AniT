namespace AniT.Core;

public sealed record PlaybackAssistantInput(bool Ready,bool Paused,bool Buffering,bool Seeking,double CacheSeconds,double? SupplyRatio=null,bool BuildingReserve=false);
public sealed record PlaybackAdvice(string Code,string Message,string? Action=null);
/// <summary>Local playback guidance; no cloud model, network request or inferred peer count.</summary>
public sealed class PlaybackAssistant(TimeProvider? timeProvider=null)
{
    readonly TimeProvider clock=timeProvider??TimeProvider.System;
    string state="";long since;bool dismissed;
    public void Reset(){state="";dismissed=false;}
    public void Dismiss()=>dismissed=true;
    public PlaybackAdvice? Update(PlaybackAssistantInput input)
    {
        if(dismissed)return null;
        var next=input.BuildingReserve&&input.Paused?"reserve":!input.Ready?"opening":input.Paused?"paused":input.Seeking?"seeking":input.Buffering?"buffering":"playing";
        if(next!=state){state=next;since=clock.GetTimestamp();}
        var seconds=clock.GetElapsedTime(since).TotalSeconds;
        return next switch {
            "reserve" when input.CacheSeconds>=8=>new("ready","A reserva já tem pelo menos 8 segundos. Você decide quando continuar.","play"),
            "reserve"=>new("reserve","Acumulando trechos com o vídeo pausado. A reprodução continua quando você pedir.","play"),
            "opening" when seconds>=8=>new("opening","O vídeo ainda está abrindo. Você pode fechar o player e tentar novamente.","close"),
            "seeking" when seconds>=4=>new("seeking","Esse trecho ainda está carregando. Avançar para fora do cache pode levar mais tempo.","pause"),
            "buffering" when seconds>=3&&input.SupplyRatio is >=0 and <.85=>new("slow_supply","A entrada de vídeo está abaixo do ritmo de consumo. Pause para acumular uma reserva.","pause"),
            "buffering" when seconds>=3=>new("buffering","Estou aguardando mais trechos de vídeo. Você pode pausar para acumular uma reserva.","pause"),
            _=>null
        };
    }
}
