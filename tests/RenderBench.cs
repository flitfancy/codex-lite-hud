using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

// Usage: RenderBench.exe HUD-assembly-path report-path
// Runs a hidden fixture form; no Application.Run, Show, data reader or tracking.
static class RenderBench
{
    const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static FieldInfo Field(Type type, string name)
    {
        var field = type.GetField(name, Fields);
        if (field == null) throw new MissingFieldException(type.FullName, name);
        return field;
    }
    static void Set(object target, string name, object value) { Field(target.GetType(), name).SetValue(target, value); }
    static void CountEvents(Control control, Action font, Action text, Action invalidate)
    {
        control.FontChanged += delegate { font(); };
        control.TextChanged += delegate { text(); };
        control.Invalidated += delegate { invalidate(); };
        foreach (Control child in control.Controls) CountEvents(child, font, text, invalidate);
    }
    static string Number(double number) { return number.ToString("0.###", CultureInfo.InvariantCulture); }

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Expected HUD assembly and report path");
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var hudType = assembly.GetType("Hud", true);
        var dataType = assembly.GetType("Data", true);
        var fixture = Activator.CreateInstance(dataType, true);
        const string title = "查看 flitrealize 架构与测试文件";
        Set(fixture, "sessionId", "00000000-0000-0000-0000-000000000001");
        Set(fixture, "sessionTitle", title);
        Set(fixture, "model", "fixture");
        Set(fixture, "input", 53000000L); Set(fixture, "output", 200000L); Set(fixture, "cache", 50000000L);
        Set(fixture, "lastInput", 190000L); Set(fixture, "lastOutput", 1200L);
        Set(fixture, "contextUsed", 200000L); Set(fixture, "contextLimit", 828400L);
        Set(fixture, "contextPercent", (double?)24.1d); Set(fixture, "requestToks", (double?)37.5d);
        Set(fixture, "usageAt", "2026-10-15T14:00:00+08:00");
        Set(fixture, "readAt", "2026-10-15T14:00:01+08:00");
        Set(fixture, "selectionSource", "fixture");
        var windowType = assembly.GetType("QuotaWindow", true);
        var quotaType = assembly.GetType("QuotaSnapshot", true);
        var quotaWindow = Activator.CreateInstance(windowType, true);
        Set(quotaWindow, "name", "7 天"); Set(quotaWindow, "remaining", 26d); Set(quotaWindow, "resetAt", "2030-10-20T14:50:08+08:00");
        var quota = Activator.CreateInstance(quotaType, true);
        var windows = Array.CreateInstance(windowType, 1); windows.SetValue(quotaWindow, 0);
        Set(quota, "windows", windows); Set(quota, "source", "测试夹具"); Set(quota, "readAt", "2026-10-15T14:00:00+08:00");
        Set(fixture, "accountQuota", quota);

        var constructor = hudType.GetConstructor(Fields, null, new[] { typeof(bool), typeof(IntPtr) }, null);
        using (var form = (Form)constructor.Invoke(new object[] { true, IntPtr.Zero }))
        {
            Set(form, "uiScale", 1f); Set(form, "railPixels", 73); Set(form, "layoutPixels", 450); Set(form, "data", fixture);
            ((Label)Field(hudType, "quota").GetValue(form)).Text = title;
            var layout = hudType.GetMethod("ApplyLayout", Fields);
            var render = hudType.GetMethod("RenderReadings", Fields);
            layout.Invoke(form, null);
            // Hidden child handles make invalidation counters comparable while
            // retaining a hidden form and never starting its Shown handlers.
            foreach (Control child in form.Controls) { var handle = child.Handle; }
            for (int i = 0; i < 10; i++) render.Invoke(form, null);
            int fonts = 0, texts = 0, invalidations = 0;
            CountEvents(form, () => fonts++, () => texts++, () => invalidations++);
            var process = Process.GetCurrentProcess(); process.Refresh();
            long privateBefore = process.PrivateMemorySize64, workingBefore = process.WorkingSet64;
            int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
            var cpu = process.TotalProcessorTime;
            const int iterations = 600;
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++) render.Invoke(form, null);
            watch.Stop(); process.Refresh();
            var report = new StringBuilder();
            report.AppendLine("Assembly: " + Path.GetFullPath(args[0]));
            report.AppendLine("Fixture: hidden 73 x 450, unchanged data, 10 warm-up calls, " + iterations + " measured RenderReadings calls");
            report.AppendLine("Elapsed ms: " + Number(watch.Elapsed.TotalMilliseconds));
            report.AppendLine("Elapsed microseconds per call: " + Number(watch.Elapsed.TotalMilliseconds * 1000 / iterations));
            report.AppendLine("Process CPU ms: " + Number((process.TotalProcessorTime - cpu).TotalMilliseconds));
            report.AppendLine("GC collections: gen0=" + (GC.CollectionCount(0) - gc0) + ", gen1=" + (GC.CollectionCount(1) - gc1) + ", gen2=" + (GC.CollectionCount(2) - gc2));
            report.AppendLine("FontChanged events: " + fonts + "; TextChanged events: " + texts + "; Invalidated events: " + invalidations);
            report.AppendLine("Private bytes before/after: " + privateBefore + " / " + process.PrivateMemorySize64);
            report.AppendLine("Working-set bytes before/after: " + workingBefore + " / " + process.WorkingSet64);
            report.AppendLine("No forced collection or working-set trim; figures describe this isolated rendering workload, not the live HUD process.");
            foreach (string name in new[] { "quota", "usage", "output", "context", "capacity", "speed", "remaining" })
            {
                var label = (Label)Field(hudType, name).GetValue(form);
                report.AppendLine(name + ": " + label.Bounds + "; font=" + Number(label.Font.Size) + "px; text=" + label.Text);
            }
            File.WriteAllText(Path.GetFullPath(args[1]), report.ToString(), Encoding.UTF8);
            Console.Write(report.ToString());
        }
    }
}
