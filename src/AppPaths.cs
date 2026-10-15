using System;
using System.IO;
using System.Text.RegularExpressions;

static class AppPaths
{
    internal static readonly string Folder=AppDomain.CurrentDomain.BaseDirectory;
    internal static readonly string CodexHome=ResolveHome(Environment.GetEnvironmentVariable("CODEX_HOME"));
    internal static readonly string DataFolder=ResolveData();

    internal static string ResolveHome(string value)
    {
        return String.IsNullOrWhiteSpace(value)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(value));
    }
    static string ResolveData()
    {
        string custom=Environment.GetEnvironmentVariable("CODEX_LITE_HUD_DATA_HOME");
        if(!String.IsNullOrWhiteSpace(custom))return Path.GetFullPath(custom);
        return File.Exists(Path.Combine(Folder,"portable.flag")) ? Path.Combine(Folder,"data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexLiteHud");
    }
    internal static void EnsureData() {Directory.CreateDirectory(DataFolder);}
    internal static string FindStateDatabase(string home)
    {
        if(!Directory.Exists(home))return null;
        string selected=null;int newest=-1;
        foreach(var path in Directory.GetFiles(home,"state_*.sqlite"))
        {
            var match=Regex.Match(Path.GetFileName(path),"^state_([0-9]+)\\.sqlite$");int version;
            if(match.Success && Int32.TryParse(match.Groups[1].Value,out version) && version>newest)
            {selected=path;newest=version;}
        }
        return selected;
    }
}
