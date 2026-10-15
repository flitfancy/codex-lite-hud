using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// Count persisted top-level events, never messages embedded in a compaction's
// replacement history. Tool calls and in-turn steering do not start new turns.
sealed class SessionCounters
{
    readonly HashSet<string> started=new HashSet<string>(),completed=new HashSet<string>(),aborted=new HashSet<string>();
    readonly HashSet<string> compactionIds=new HashSet<string>();
    long anonymousCompactions;
    static readonly Regex CompactionId=new Regex("\\\"compaction_response_id\\\"\\s*:\\s*\\\"([^\\\"\\\\]+)\\\"",RegexOptions.Compiled);
    static readonly Regex WindowId=new Regex("\\\"window_id\\\"\\s*:\\s*\\\"([^\\\"\\\\]+)\\\"",RegexOptions.Compiled);
    internal long? Turns {get{return started.Count==0 ? (long?)null : started.Count;}}
    internal long Completed {get{return completed.Count;}}
    internal long Aborted {get{return aborted.Count;}}
    internal long Compactions {get{return compactionIds.Count+anonymousCompactions;}}
    internal long Active
    {
        get{long count=0;foreach(var id in started)if(!completed.Contains(id) && !aborted.Contains(id))count++;return count;}
    }
    internal void Event(Dictionary<string,object> payload)
    {
        string kind=SessionReader.Text(SessionReader.At(payload,"type"));
        string id=SessionReader.Text(SessionReader.At(payload,"turn_id"));
        if(String.IsNullOrEmpty(id))return;
        if(kind=="task_started")started.Add(id);
        else if(kind=="task_complete")completed.Add(id);
        else if(kind=="turn_aborted")aborted.Add(id);
    }
    internal void Compacted(string line)
    {
        // IDs live after the potentially large replacement history. Prefer the
        // suffix so normal compaction counting does not deserialize that history.
        string suffix=line.Length>8192 ? line.Substring(line.Length-8192) : line;
        var match=CompactionId.Match(suffix);
        if(!match.Success && suffix.Length!=line.Length)match=CompactionId.Match(line);
        if(!match.Success)match=WindowId.Match(suffix);
        if(!match.Success && suffix.Length!=line.Length)match=WindowId.Match(line);
        if(match.Success)compactionIds.Add(match.Groups[1].Value);
        else anonymousCompactions++;
    }
    internal void Reset(){started.Clear();completed.Clear();aborted.Clear();compactionIds.Clear();anonymousCompactions=0;}
}
