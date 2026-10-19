using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class FontPreferenceChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    [DllImport("user32.dll")]static extern int GetGuiResources(IntPtr process,int kind);
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Preference(string family){File.WriteAllText(Path.Combine(Program.DataFolder,"appearance.json"),Program.Json.Serialize(new{fontFamily=family}));}
    static string Family(Font font){return font.FontFamily.GetName(1033);}
    static ToolStripMenuItem FontChoice(Hud hud,string text)
    {
        foreach(ToolStripItem item in hud.ContextMenuStrip.Items)if(item.Text=="字体")
            foreach(ToolStripItem choice in ((ToolStripMenuItem)item).DropDownItems)if(choice.Text==text)return choice as ToolStripMenuItem;
        return null;
    }
    static void CheckFamily(Hud hud,string family)
    {
        foreach(Control control in hud.Controls)if(control is Label)Require(Family(control.Font)==family,"Label still uses "+Family(control.Font)+" instead of "+family+": "+control.Text);
        Require(Family(hud.ContextMenuStrip.Font)==family,"Context menu uses another family");
    }
    static void Run(List<string> log,string name,Action action,ref int passed,ref int failed)
    {
        try{action();log.Add("PASS "+name);passed++;}catch(Exception ex){log.Add("FAIL "+name+": "+ex.GetBaseException().Message);failed++;}
    }
    [STAThread]
    static void Main(string[] args)
    {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));Application.EnableVisualStyles();AppPaths.EnsureData();
        var log=new List<string>();int passed=0,failed=0;const string alternate="Segoe UI";
        string displayPath=Path.Combine(Program.DataFolder,"display.json");string originalDisplay=File.Exists(displayPath)?File.ReadAllText(displayPath):null;
        Run(log,"saved family reaches HUD labels and menus",delegate{
            Preference(alternate);using(var hud=new Hud(true,IntPtr.Zero))CheckFamily(hud,alternate);
        },ref passed,ref failed);
        Run(log,"missing font falls back without changing data visibility",delegate{
            Preference("Codex HUD fixture font that is not installed");using(var hud=new Hud(true,IntPtr.Zero))CheckFamily(hud,Typography.Family);
        },ref passed,ref failed);
        Run(log,"font menu selection survives reopen",delegate{
            Preference(Typography.Family);
            using(var hud=new Hud(true,IntPtr.Zero))
            {
                ToolStripMenuItem fonts=null;
                foreach(ToolStripItem item in hud.ContextMenuStrip.Items)if(item.Text=="字体")fonts=item as ToolStripMenuItem;
                Require(fonts!=null,"Font selection menu is missing");
                ToolStripMenuItem choice=null;
                foreach(ToolStripItem item in fonts.DropDownItems)if(item.Text==alternate)choice=item as ToolStripMenuItem;
                Require(choice!=null && choice.Enabled,"Installed font choice is unavailable");choice.PerformClick();
                CheckFamily(hud,alternate);
                var settings=SessionReader.Map(Program.Json.DeserializeObject(File.ReadAllText(Path.Combine(Program.DataFolder,"appearance.json"))));
                Require(SessionReader.Text(SessionReader.At(settings,"fontFamily"))==alternate,"Selected font was not saved");
            }
            using(var hud=new Hud(true,IntPtr.Zero))CheckFamily(hud,alternate);
            Require((File.Exists(displayPath)?File.ReadAllText(displayPath):null)==originalDisplay,"Choosing a font rewrote metric visibility/order");
        },ref passed,ref failed);
        Run(log,"selected font fits normal and fractional-DPI viewports",delegate{
            Preference(alternate);
            foreach(var sample in new[]{new[]{73f,661f,1f},new[]{119f,910f,1.5f},new[]{146f,1322f,2f}})
            using(var hud=new Hud(true,IntPtr.Zero))
            {
                typeof(Hud).GetField("uiScale",Private).SetValue(hud,sample[2]);
                typeof(Hud).GetField("railPixels",Private).SetValue(hud,(int)sample[0]);typeof(Hud).GetField("layoutPixels",Private).SetValue(hud,(int)sample[1]);
                typeof(Hud).GetMethod("ApplyLayout",Private).Invoke(hud,null);
                var labels=new List<Label>((List<Label>)typeof(Hud).GetField("numericLabels",Private).GetValue(hud));
                labels.AddRange((List<Label>)typeof(Hud).GetField("bodyLabels",Private).GetValue(hud));
                var title=typeof(Hud).GetField("sessionTitle",Private).GetValue(hud);
                foreach(var label in labels)
                {
                    var measured=TextRenderer.MeasureText(label.Text,label.Font,Object.ReferenceEquals(label,title)?new Size(label.Width,32767):new Size(32767,32767),Object.ReferenceEquals(label,title)?TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix:TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
                    Require(measured.Width<=label.Width && measured.Height<=label.Height,"Selected font clips at "+sample[2]+" scale: "+label.Text);
                }
            }
        },ref passed,ref failed);
        Run(log,"repeated font switching releases native font resources",delegate{
            Preference(Typography.Family);using(var hud=new Hud(true,IntPtr.Zero))
            {
                var first=FontChoice(hud,"微软雅黑（默认）");var second=FontChoice(hud,alternate);
                Require(first!=null && second!=null,"Font choices are missing");
                for(int i=0;i<10;i++){second.PerformClick();first.PerformClick();}
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                var process=Process.GetCurrentProcess();int beforeGdi=GetGuiResources(process.Handle,0),beforeUser=GetGuiResources(process.Handle,1);
                for(int i=0;i<40;i++){second.PerformClick();first.PerformClick();}
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                int afterGdi=GetGuiResources(process.Handle,0),afterUser=GetGuiResources(process.Handle,1);
                Require(afterGdi<=beforeGdi+4 && afterUser<=beforeUser+4,"Font switching leaked native resources: GDI "+beforeGdi+" -> "+afterGdi+", USER "+beforeUser+" -> "+afterUser);
            }
        },ref passed,ref failed);
        Run(log,"unchanged docking reuses selected title layout and fonts",delegate{
            Preference(alternate);using(var hud=new Hud(true,IntPtr.Zero))
            {
                typeof(Hud).GetField("data",Private).SetValue(hud,new Data{sessionTitle="HUD font preview"});
                var update=typeof(Hud).GetMethod("UpdateLayoutViewport",Private);
                var viewport=new object[]{119,910,1.5f};update.Invoke(hud,viewport);
                var layout=(NameLayout)typeof(Hud).GetField("nameLayout",Private).GetValue(hud);
                string wrapped=layout.Wrapped;
                int changed=0;foreach(Control label in hud.Controls)label.FontChanged+=delegate{changed++;};
                for(int i=0;i<200;i++)update.Invoke(hud,viewport);
                Require(changed==0,"Unchanged docking rebuilt "+changed+" fonts");
                Require(Object.ReferenceEquals(wrapped,layout.Wrapped),"Unchanged docking discarded the title layout cache");
                CheckFamily(hud,alternate);
            }
        },ref passed,ref failed);
        Run(log,"unchanged data reuse selected fonts",delegate{
            Preference(alternate);using(var hud=new Hud(true,IntPtr.Zero))
            {
                var data=new Data{sessionTitle="调整电脑全局字体",input=25000000,output=127000,lastInput=448000,lastOutput=337,contextUsed=448000,contextLimit=828400,contextPercent=54.1,turns=12,requestMs=13000,runningMs=2946000,spanMs=5400000,requestToks=25.9};
                typeof(Hud).GetField("data",Private).SetValue(hud,data);
                typeof(Hud).GetMethod("RenderReadings",Private).Invoke(hud,null);
                int changed=0;foreach(Control label in hud.Controls)label.FontChanged+=delegate{changed++;};
                for(int i=0;i<200;i++)typeof(Hud).GetMethod("RenderReadings",Private).Invoke(hud,null);
                Require(changed==0,"Unchanged readings rebuilt "+changed+" fonts");CheckFamily(hud,alternate);
            }
        },ref passed,ref failed);
        File.Delete(Path.Combine(Program.DataFolder,"appearance.json"));
        log.Add("Font preferences: "+passed+" passed, "+failed+" failed, 0 skipped. Isolated HUD configuration.");
        File.WriteAllLines(args[0],log);Environment.ExitCode=failed==0?0:1;
    }
}
