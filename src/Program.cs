using System;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class Program
{
    internal static readonly string Folder = AppPaths.Folder;
    internal static readonly string DataFolder = AppPaths.DataFolder;
    internal static readonly string CodexHome = AppPaths.CodexHome;
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 2097152 };

    [STAThread]
    static void Main(string[] args)
    {
        if(args.Length==2 && args[0]=="--check-info")
        {
            File.WriteAllText(args[1],Json.Serialize(new {name=Product.Name,version=Product.Version,codexHome=CodexHome,dataFolder=DataFolder}));return;
        }
        if ((args.Length == 2 && args[0] == "--check") || (args.Length == 3 && (args[0] == "--check-session" || args[0] == "--check-window")))
        {
            string path = args[args.Length - 1];
            try
            {
                var data = (args[0] == "--check-window" ? Reader.ReadCurrent(new IntPtr(long.Parse(args[1])),true,true) : Reader.Read(args[0] == "--check-session" ? args[1] : null)).GetAwaiter().GetResult();
                File.WriteAllText(path, args[0] == "--check-window" ? Json.Serialize(new { data = data, detection = WindowSession.LastDebug }) : Json.Serialize(data));
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(path, Json.Serialize(new { error = ex.Message, detection = WindowSession.LastDebug, sidebar = WindowSession.LatestSidebar }));
                Environment.ExitCode = 1;
            }
            return;
        }
        bool created;
        using (var single = new Mutex(true, "Local\\CodexLiteHud", out created))
        {
            if (!created) return;
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            AppPaths.EnsureData();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool preview = args.Length > 0 && (args[0] == "--preview" || args[0] == "--preview-window");
            IntPtr target = args.Length == 2 && args[0] == "--preview-window" ? new IntPtr(long.Parse(args[1])) : IntPtr.Zero;
            Application.Run(new Hud(preview,target));
        }
    }

}
