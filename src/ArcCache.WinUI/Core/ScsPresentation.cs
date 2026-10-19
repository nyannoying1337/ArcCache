using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArcCache.Core;

/// <summary>One game's detail from `scskiller status steam:N`, read from its "  label      value" lines. Null = not reported.
/// Pipelines = what a compile creates (recorded + generated); ShadersInGame = shaders indexed in the game's files.</summary>
public sealed record ScsKillerGameStatus(string Status,string Reason,long? ShadersInGame,long? Recorded,long? Generated,long? EstimateBytes,
    TimeSpan? EstimateTime,DateTime? WarmedAt,TimeSpan? WarmTime,string? WarmCounts,TimeSpan? LastPlayTime,long? LastPlayCompiles,string? LastPlayWorstMs,DateTime CheckedUtc) {
    public long? Pipelines=>Recorded is null&&Generated is null?null:(Recorded??0)+(Generated??0);
}

/// <summary>The SCSKiller card's progress, from one model: Percent null = indeterminate bar.</summary>
public sealed record ScsProgressView(double? Percent,string Headline,string Detail);

/// <summary>The SCSKiller card's shader details: Title in bold, Reason under it, then label/value rows.</summary>
public sealed record ScsDetailView(string Title,string? Reason,IReadOnlyList<(string Label,string Value)> Rows,string? Warning,string Footer);

/// <summary>Every text the SCSKiller card shows about shaders and progress.
/// State table (shader details, per game):
///   never checked  -> what Check available shaders tells you, cache file now / cache size
///   checked        -> status, pipelines to compile and time, cache file now / once prepared / cache size (+ warning),
///                     last preparation, last play, when it was checked
/// State table (progress, per run):
///   starting       -> indeterminate, "Starting SCSKiller…"
///   check running  -> indeterminate, "Checking available shaders…"
///   steps 1-3      -> indeterminate, "Step n of 4: stage", running time
///   step 4         -> bar = done/total, "Step 4 of 4: Compiling · n%", counts, rate, time left, failed, cache file
///   done           -> bar full, "Done", pipelines compiled, time taken, failed, cache file (a check hides the panel:
///                     its result is the shader details)
///   stopped        -> bar kept, "Stopped at n%", what stays in the cache</summary>
public static class ScsPresentation {
    static readonly Regex Plan=new(@"^(?<rec>\d+) recorded \+ (?<gen>\d+) generated",RegexOptions.CultureInvariant);
    static readonly Regex Estimate=new(@"^(?<size>\d+(?:[.,]\d+)? (?:GB|MB)) of cache, (?<time>[\d:]+)",RegexOptions.CultureInvariant);
    static readonly Regex Warmed=new(@"^(?<at>\d{4}-\d{2}-\d{2} \d{2}:\d{2}) for driver \S+ in (?<time>[\d:]+)(?:; (?<counts>.+))?$",RegexOptions.CultureInvariant);
    static readonly Regex LastPlay=new(@"^(?<time>[\d:]+), .*?(?<compiles>\d+) compiles \(worst (?<worst>[\d.,]+) ms\)",RegexOptions.CultureInvariant);

    /// <summary>The status detail in the CLI's output (lines may carry the bridge's "SCSKiller: " prefix); null without a status line.</summary>
    public static ScsKillerGameStatus? ParseStatus(IEnumerable<string> lines,DateTime checkedUtc) {
        var fields=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(string raw in lines){
            string line=raw.StartsWith("SCSKiller: ",StringComparison.Ordinal)?raw["SCSKiller: ".Length..]:raw;
            if(line.Length<14||!line.StartsWith("  ",StringComparison.Ordinal)||line[2]==' '||line[12]!=' ')continue;
            fields.TryAdd(line[2..12].Trim(),line[13..].Trim());
        }
        if(!fields.TryGetValue("status",out string? status))return null;
        int colon=status.IndexOf(": ",StringComparison.Ordinal);
        string name=colon<0?status:status[..colon],reason=colon<0?"":status[(colon+2)..];
        long? shaders=fields.TryGetValue("shaders",out string? s)&&long.TryParse(s,NumberStyles.None,CultureInfo.InvariantCulture,out long n)?n:null;
        var plan=fields.TryGetValue("plan",out string? p)?Plan.Match(p):Match.Empty;
        var estimate=fields.TryGetValue("estimate",out string? e)?Estimate.Match(e):Match.Empty;
        var warmed=fields.TryGetValue("warmed",out string? w)?Warmed.Match(w):Match.Empty;
        var play=fields.TryGetValue("last play",out string? l)?LastPlay.Match(l):Match.Empty;
        return new(name,reason,shaders,
            plan.Success?long.Parse(plan.Groups["rec"].Value,CultureInfo.InvariantCulture):null,plan.Success?long.Parse(plan.Groups["gen"].Value,CultureInfo.InvariantCulture):null,
            estimate.Success?Bytes(estimate.Groups["size"].Value):null,estimate.Success?Hms(estimate.Groups["time"].Value):null,
            warmed.Success&&DateTime.TryParseExact(warmed.Groups["at"].Value,"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.AssumeLocal,out var at)?at:null,
            warmed.Success?Hms(warmed.Groups["time"].Value):null,warmed.Success&&warmed.Groups["counts"].Success?warmed.Groups["counts"].Value:null,
            play.Success?Hms(play.Groups["time"].Value):null,play.Success?long.Parse(play.Groups["compiles"].Value,CultureInfo.InvariantCulture):null,play.Success?play.Groups["worst"].Value:null,
            checkedUtc);
    }
    /// <summary>"1.2 GB", "1,2 GB" (the CLI writes in the user's culture) or "512 MB".</summary>
    static long? Bytes(string text) {
        string[] parts=text.Split(' ');
        if(!double.TryParse(parts[0].Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out double value))return null;
        return (long)(value*(parts[1]=="GB"?1L<<30:1L<<20));
    }
    /// <summary>The CLI's h:mm:ss; null for anything else.</summary>
    static TimeSpan? Hms(string text) {
        string[] parts=text.Split(':');
        if(parts.Length!=3||!int.TryParse(parts[0],NumberStyles.None,CultureInfo.InvariantCulture,out int h)||!int.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out int m)||!int.TryParse(parts[2],NumberStyles.None,CultureInfo.InvariantCulture,out int sec))return null;
        return new TimeSpan(h,m,sec);
    }

    public static string Size(long bytes)=>bytes>=1L<<30?(bytes/(double)(1L<<30)).ToString("0.0",CultureInfo.InvariantCulture)+" GB":(bytes/(double)(1L<<20)).ToString("0",CultureInfo.InvariantCulture)+" MB";
    public static string Duration(TimeSpan t)=>t.TotalMinutes<1?"under 1 min":t.TotalHours<1?$"{(int)Math.Round(t.TotalMinutes)} min":$"{(int)t.TotalHours} h"+(t.Minutes>0?$" {t.Minutes} min":"");
    static string Clock(TimeSpan t)=>t.TotalHours>=1?t.ToString(@"h\:mm\:ss",CultureInfo.InvariantCulture):t.ToString(@"m\:ss",CultureInfo.InvariantCulture);

    public static string StatusName(string status)=>status switch{
        "Ready"=>"Ready to prepare","Warmed"=>"Prepared","Stale"=>"Needs preparing again","NeedsRecording"=>"Needs a recording",
        "NeedsOfflineSession"=>"Needs one offline play session","Unsupported"=>"Not supported by SCSKiller",_=>status};

    public const string NotChecked="Not checked yet. Check available shaders shows how many shaders this game has, how much cache they need and how long preparing takes.";

    /// <summary>The shader details for one game as the card shows them: a status title, SCSKiller's reason, label/value rows,
    /// an optional warning and when it was checked. cacheMiB: the game's cache size (512 = Default). cacheFileBytes: its linked
    /// Intel cache file's size now (null: not linked, or linked but gone when linked=true).</summary>
    public static ScsDetailView DetailView(ScsKillerGameStatus? s,int cacheMiB,long? cacheFileBytes=null,bool cacheFileLinked=false) {
        string limit=SetupPresentation.Limit(cacheMiB);
        string cacheNow=cacheFileBytes is long have?$"{Size(have)} of {limit}":cacheFileLinked?$"no longer on disk · limit {limit}":$"not linked yet · limit {limit}";
        if(s==null)return new("Not checked yet",NotChecked,[("Cache file",cacheNow)],null,"");
        var rows=new List<(string,string)>();
        string? time=s.EstimateTime is {} t?$"about {Duration(t)}":null;
        if(s.Pipelines is long total)rows.Add(("Pipelines",$"{N(total)} ({N(s.Recorded)} recorded, {N(s.Generated)} generated)"+(s.ShadersInGame is long found?$" from {N(found)} shaders":"")));
        else if(s.ShadersInGame is long found)rows.Add(("Shaders",N(found)+" in the game's files"));
        if(time!=null&&s.Status!="Warmed")rows.Add(("Time to prepare",time));
        rows.Add(("Cache file",cacheNow));
        // The estimate is what the plan needs; once prepared, the real file size above is the better answer.
        if(s.EstimateBytes is long need&&s.Status!="Warmed")rows.Add(("Estimated need","about "+Size(need)));
        if(s.WarmedAt is {} at){
            rows.Add(("Last prepared",at.ToString("dd.MM.yyyy HH:mm",CultureInfo.InvariantCulture)+(s.WarmTime is {} wt?$" · took {Duration(wt)}":"")));
            if(s.WarmCounts is {} c)rows.Add(("Not compiled",c));
        }else rows.Add(("Last prepared","never"));
        if(s.LastPlayTime is {} play)rows.Add(("Last play",$"{Duration(play)} · {N(s.LastPlayCompiles)} pipelines compiled while playing (slowest {s.LastPlayWorstMs} ms)"));
        string? warning=s.EstimateBytes>(long)cacheMiB<<20?"The prepared shaders need more than the cache size, so the driver can't keep all of them"+(cacheMiB<16384?". A larger cache size keeps more.":"."):null;
        return new(StatusName(s.Status),s.Reason.Length>0?Sentence(s.Reason):null,rows,warning,"Checked "+s.CheckedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm",CultureInfo.InvariantCulture));
    }
    /// <summary>The same details as plain lines (Activity log).</summary>
    public static IReadOnlyList<string> Details(ScsKillerGameStatus? s,int cacheMiB,long? cacheFileBytes=null,bool cacheFileLinked=false)=>Lines(DetailView(s,cacheMiB,cacheFileBytes,cacheFileLinked));
    public static IReadOnlyList<string> Lines(ScsDetailView v) {
        var lines=new List<string>{v.Title+(v.Reason is null?"":": "+v.Reason)};
        lines.AddRange(v.Rows.Select(r=>r.Label+": "+r.Value));
        if(v.Warning is not null)lines.Add(v.Warning);
        if(v.Footer.Length>0)lines.Add(v.Footer);
        return lines;
    }
    static string N(long? n)=>(n??0).ToString("N0",CultureInfo.InvariantCulture);
    static string Sentence(string text)=>text.Length==0?text:char.ToUpperInvariant(text[0])+text[1..]+(text.EndsWith('.')?"":".");

    static int Step(string stage)=>stage switch{"Indexing"=>1,"Planning"=>2,"Materializing"=>3,"Warming"=>4,_=>0};
    static string StageName(string stage)=>stage switch{"Waiting"=>"Waiting for SCSKiller's queue","Indexing"=>"Reading the game's shaders","Planning"=>"Planning","Materializing"=>"Preparing the compile","Warming"=>"Compiling","Paused"=>"Paused",_=>stage};

    /// <summary>A running, finished or stopped compile. cache: "cache file 528 MB of 16 GB" or null; extra: e.g. the driver copy confirmation.</summary>
    public static ScsProgressView Progress(bool check,ScsKillerProgress? p,TimeSpan elapsed,string? cache,string? extra,bool done=false,bool stopped=false) {
        string Join(params string?[] parts)=>string.Join(" · ",parts.Where(x=>!string.IsNullOrEmpty(x)));
        string Lines(params string?[] lines)=>string.Join("\n",lines.Where(x=>!string.IsNullOrEmpty(x)));
        bool counted=p is {Total:>0};double? percent=counted?Math.Min(100,p!.Done*100.0/p.Total):null;
        string? failed=p is null?null:Join(p.Failed>0?$"{N(p.Failed)} failed (the driver rejected them)":null,p.Skipped>0?$"{N(p.Skipped)} skipped (not in this install)":null);
        // SCSKiller's note repeats the failed/skipped counts at the end; show it only when it says something else.
        string? note=p?.Note is {} n&&!n.Contains("failed",StringComparison.OrdinalIgnoreCase)&&!n.Contains("skipped",StringComparison.OrdinalIgnoreCase)?n:null;
        if(check)return new(done?100:null,done?"Done":"Checking available shaders…",done?"":$"Running {Clock(elapsed)}");
        if(done)return new(100,counted?$"Done · {N(p!.Done)} compile jobs in {Duration(elapsed)}":$"Done in {Duration(elapsed)}",Lines(Join(cache,extra),failed));
        if(stopped)return new(percent,counted?$"Stopped at {percent:0}%":"Stopped",Lines(counted?$"{N(p!.Done)} of {N(p.Total)} compile jobs done; what was compiled stays in the cache":"Nothing was compiled yet",cache));
        if(p==null)return new(null,"Starting SCSKiller…",Join($"Running {Clock(elapsed)}",extra));
        int step=Step(p.Stage);
        string headline=(step>0?$"Step {step} of 4: ":"")+StageName(p.Stage)+(counted?$" · {percent:0}%":"");
        // SCSKiller counts compile jobs, not pipelines: on Intel each pipeline's stages and the game's D3D11 shaders are
        // jobs of their own, so this total is much larger than the pipeline count in the details above.
        string? counts=counted?$"{N(p.Done)} of {N(p.Total)} compile jobs":null;
        string? rate=p.PerSecond>0?$"{N((long)p.PerSecond)} per second":null;
        string? left=p.PerSecond>0&&counted&&p.Done<p.Total?$"about {Duration(TimeSpan.FromSeconds((p.Total-p.Done)/p.PerSecond))} left":null;
        return new(percent,headline,Lines(Join(counts,rate,left,$"running {Clock(elapsed)}"),Join(cache,extra),Join(failed,note)));
    }

    /// <summary>Last check result per game (by Arc Cache's game key), kept across restarts.</summary>
    public static Dictionary<string,ScsKillerGameStatus> LoadStatuses(string path) {
        try{if(File.Exists(path)&&JsonSerializer.Deserialize<Dictionary<string,ScsKillerGameStatus>>(File.ReadAllText(path)) is {} all)return new(all,StringComparer.OrdinalIgnoreCase);}
        catch(JsonException){}catch(IOException){}catch(UnauthorizedAccessException){}
        return new(StringComparer.OrdinalIgnoreCase);
    }
    public static void SaveStatuses(string path,Dictionary<string,ScsKillerGameStatus> all) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(all));File.Move(temp,path,true);
    }
}
