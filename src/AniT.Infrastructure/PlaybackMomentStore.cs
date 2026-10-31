using System.Text.Json;
namespace AniT.Infrastructure;
public sealed record PlaybackMoment(Guid Id,Guid EpisodeId,double Seconds,string Note);
public sealed class PlaybackMomentStore(string path)
{
    readonly object sync=new();
    static readonly JsonSerializerOptions Options=new(){WriteIndented=true};
    public string ThumbnailPath(Guid id)=>Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,"moments-thumbnails",id.ToString("N")+".png");
    List<PlaybackMoment>? Read(string file)
    {
        try {
            if(!File.Exists(file)||new FileInfo(file).Length>8*1024*1024)return null;
            var items=JsonSerializer.Deserialize<List<PlaybackMoment>>(File.ReadAllText(file));
            return items?.Where(m=>m is not null&&m.Id!=Guid.Empty&&m.EpisodeId!=Guid.Empty&&double.IsFinite(m.Seconds)&&m.Seconds>=0&&m.Seconds<=86400)
                .DistinctBy(m=>m.Id).Take(1000).Select(m=>m with{Note=Clean(m.Note)}).ToList();
        }catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){return null;}
    }
    List<PlaybackMoment> Load()=>Read(path)??Read(path+".bak")??[];
    public IReadOnlyList<PlaybackMoment> ForEpisode(Guid episode){lock(sync)return Load().Where(m=>m.EpisodeId==episode).OrderBy(m=>m.Seconds).ToArray();}
    public void Add(PlaybackMoment moment)
    {
        if(moment.Id==Guid.Empty||moment.EpisodeId==Guid.Empty||!double.IsFinite(moment.Seconds)||moment.Seconds<0||moment.Seconds>86400)throw new ArgumentException("O momento não tem uma posição válida.");
        lock(sync){var items=Load();if(items.Count>=1000||items.Count(m=>m.EpisodeId==moment.EpisodeId)>=200)throw new InvalidOperationException("O limite de momentos foi atingido. Remova alguns antes de adicionar novos.");
            if(items.Any(m=>m.Id==moment.Id))throw new ArgumentException("Esse momento já existe.");items.Add(moment with{Note=Clean(moment.Note)});Save(items);}
    }
    public void Edit(Guid episode,Guid id,string note){lock(sync){var items=Load();var at=items.FindIndex(m=>m.EpisodeId==episode&&m.Id==id);if(at<0)return;items[at]=items[at] with{Note=Clean(note)};Save(items);}}
    public void Remove(Guid episode,Guid id)
    {
        lock(sync){var items=Load();if(items.RemoveAll(m=>m.EpisodeId==episode&&m.Id==id)==0)return;Save(items);
            try{File.Delete(ThumbnailPath(id));}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    static string Clean(string? note)=>new((note??"").Where(c=>!char.IsControl(c)||c==Convert.ToChar(10)).Take(500).ToArray());
    void Save(List<PlaybackMoment> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(items,Options));
            if(File.Exists(path)){if(Read(path) is not null)File.Replace(temporary,path,path+".bak");else File.Move(temporary,path,true);}else File.Move(temporary,path);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
