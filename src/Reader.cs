using System;
using System.Threading.Tasks;

static class Reader
{
    internal static Task<Data> Read(string id = null)
    {
        return Task.Run(() => { QuotaLogs.Refresh();var data=SessionReader.Read(id); QuotaReader.Apply(data); return data; });
    }
    internal static Task<Data> ReadCurrent(IntPtr window, bool automatic,bool diagnostic=false)
    {
        return Task.Run(() =>
        {
            QuotaLogs.Refresh();
            string id = automatic ? WindowSession.Resolve(window,SessionReader.Recent(Int32.MaxValue),diagnostic) : SessionReader.SelectedId();
            var result = SessionReader.Read(id);
            result.selectionSource = automatic ? "current-window" : "manual";
            result.sidebar = WindowSession.LatestSidebar;
            QuotaReader.Apply(result);
            return result;
        });
    }
}
