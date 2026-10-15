using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

// Compile SessionReader.cs with /define:READER_CHECKS and this file only.
// Fixtures stay in a fresh temporary directory; no user configuration is read.
static class ReaderChecks
{
    static void Expect(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Write(string path, string text, int revision)
    {
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, new DateTime(2030, 1, 1, 0, 0, revision, DateTimeKind.Utc));
    }

    static void Main()
    {
        string folder = Path.Combine(Path.GetTempPath(), "CodexHud-reader-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "selection.json");
            int parses = 0;
            var cache = new MetadataFileCache<string>(path, text => { Interlocked.Increment(ref parses); return text; });
            string value;
            Expect(!cache.TryGet(out value), "Missing file must not return a value");
            Write(path, "one", 1);
            for (int i = 0; i < 2000; i++) Expect(cache.TryGet(out value) && value == "one", "Cached value changed");
            Expect(parses == 1, "Unchanged metadata must parse only once for 2000 reads");

            Write(path, "two", 2);
            Expect(cache.TryGet(out value) && value == "two" && parses == 2, "Same-length overwrite was not detected");
            File.AppendAllText(path, "-more");
            Expect(cache.TryGet(out value) && value == "two-more" && parses == 3, "Append was not detected");
            using (var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            {
                writer.SetLength(0);
                var bytes = System.Text.Encoding.UTF8.GetBytes("held");
                writer.Write(bytes, 0, bytes.Length); writer.Flush();
                File.SetLastWriteTimeUtc(path, new DateTime(2030, 1, 1, 0, 0, 3, DateTimeKind.Utc));
                Expect(cache.TryGet(out value) && value == "held", "Shared writer prevented a read");
            }
            int previous = parses;
            cache.Write("self");
            Expect(cache.TryGet(out value) && value == "self" && parses == previous + 1, "Own write did not invalidate");

            File.Delete(path);
            Expect(!cache.TryGet(out value), "Deleted file must not return stale data");
            Write(path, "back", 4);
            Expect(cache.TryGet(out value) && value == "back", "Recreated file was negatively cached");

            previous = parses;
            Write(path, "many", 5);
            var workers = new Thread[8];
            Exception failure = null;
            for (int i = 0; i < workers.Length; i++)
            {
                workers[i] = new Thread(() =>
                {
                    try
                    {
                        string actual;
                        for (int j = 0; j < 100; j++) Expect(cache.TryGet(out actual) && actual == "many", "Concurrent value mismatch");
                    }
                    catch (Exception ex) { Interlocked.CompareExchange(ref failure, ex, null); }
                });
                workers[i].Start();
            }
            foreach (var worker in workers) worker.Join();
            if (failure != null) throw failure;
            Expect(parses == previous + 1, "Concurrent readers must share one parse");

            string changingPath = Path.Combine(folder, "changing.json");
            Write(changingPath, "before", 6);
            int changingParses = 0;
            var changing = new MetadataFileCache<string>(changingPath, text =>
            {
                if (++changingParses == 1) Write(changingPath, "after!", 7);
                return text;
            });
            Expect(changing.TryGet(out value) && value == "before", "First changing snapshot mismatch");
            Expect(changing.TryGet(out value) && value == "after!" && changingParses == 2, "Changing snapshot must not be cached");
            Expect(changing.TryGet(out value) && changingParses == 2, "Stable snapshot should be cached");

            string a = Guid.NewGuid().ToString(), b = Guid.NewGuid().ToString();
            var catalog = SessionCatalog.Parse(
                "{\"id\":\"" + a + "\",\"thread_name\":\"old\"}\n" +
                "malformed\n{\"id\":\"invalid\",\"thread_name\":\"skip\"}\n" +
                "{\"id\":\"" + b + "\",\"thread_name\":\"other\"}\n" +
                "{\"id\":\"" + a + "\",\"thread_name\":\"latest\"}\n");
            var rows = catalog.Recent(Int32.MaxValue);
            Expect(rows.Count == 2 && rows[0].id == a && rows[0].title == "latest" && rows[1].id == b, "Catalog order or latest duplicate changed");
            rows[0].title = "mutated"; rows.Clear();
            Expect(catalog.Title(a) == "latest" && catalog.Recent(1).Count == 1 && catalog.Recent(-1).Count == 0, "Returned catalog must be bounded and defensive");
            Console.WriteLine("Reader checks passed: 2000 unchanged reads / one parse; overwrite, append, shared writer, own write, deletion/recreation, concurrent readers, changing snapshot, catalog ordering and defensive bounds.");
        }
        finally
        {
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(folder);
            if (target.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(target).StartsWith("CodexHud-reader-checks-", StringComparison.Ordinal)) Directory.Delete(target, true);
        }
    }
}

#if READER_CHECKS
static class Program
{
    internal static readonly string Folder = Path.GetTempPath();
    internal static readonly string DataFolder = Path.GetTempPath();
    internal static readonly string CodexHome = Path.GetTempPath();
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
}
sealed class QuotaSnapshot { }
static class QuotaReader
{
    internal static QuotaSnapshot FromSession(Dictionary<string, object> limits, DateTimeOffset at) { return null; }
}
sealed class Data
{
    public long? turns;
    public long? runningMs,spanMs;
    public long completedTurns,abortedTurns,activeTurns,compactions;
    public string sessionId, sessionTitle, model, usageAt, readAt;
    public long input, output, cache, lastInput, lastOutput, contextUsed, contextLimit, requestMs;
    public double? contextPercent, requestToks;
    public QuotaSnapshot accountQuota;
}
#endif
