using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

static class LayoutChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
            Application.EnableVisualStyles();
            var data=SessionReader.Read(args[0]);
            data.runningMs=(long)TimeSpan.FromHours(12.3).TotalMilliseconds;data.spanMs=(long)TimeSpan.FromDays(23.4).TotalMilliseconds;
            data.accountQuota=new QuotaSnapshot { readAt=DateTimeOffset.Now.ToString("o"),source="layout check",windows=new[]{new QuotaWindow { name="7 天",remaining=100 }} };
            var cases=new[]{new[]{73f,730f,1f},new[]{73f,620f,1f},new[]{73f,474f,1f},new[]{73f,164f,1f},new[]{73f,369f,1f},new[]{73f,370f,1f},new[]{110f,930f,1.5f},new[]{95f,620f,1f}};
            using(var log=new StreamWriter(args[1]))
            foreach(var sample in cases)
            using(var form=new Hud(true,IntPtr.Zero))
            {
                typeof(Hud).GetField("data",Private).SetValue(form,data);
                typeof(Hud).GetField("uiScale",Private).SetValue(form,sample[2]);
                typeof(Hud).GetField("railPixels",Private).SetValue(form,(int)sample[0]);
                typeof(Hud).GetField("layoutPixels",Private).SetValue(form,(int)sample[1]);
                typeof(Hud).GetMethod("ApplyLayout",Private).Invoke(form,null);
                if(sample[1]/sample[2]>=620)
                {
                    var title=(Label)typeof(Hud).GetField("quota",Private).GetValue(form);
                    title.Text="查看 flitrealize 架构与测试文件";
                    var required=TextRenderer.MeasureText(title.Text,title.Font,new Size(title.Width,32767),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
                    if(required.Width>title.Width || required.Height>title.Height)throw new Exception("Title does not fit multiline area: "+required+" in "+title.Size);
                }
                if(form.ClientSize.Width!=(int)sample[0] || form.ClientSize.Height!=(int)sample[1])throw new Exception("Viewport size mismatch");
                foreach(string name in new[]{"requestInput","usage","requestOutput","output","context","speed","remaining","counts","time","spanTime"})
                {
                    var label=(Label)typeof(Hud).GetField(name,Private).GetValue(form);
                    var measured=TextRenderer.MeasureText(label.Text,label.Font,new Size(32767,32767),TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
                    if(measured.Width>label.Width || measured.Height>label.Height)throw new Exception(name+" clipped in "+sample[0]+"x"+sample[1]+": "+measured+" in "+label.Size);
                    if(label.Left<0 || label.Top<0 || label.Right>form.ClientSize.Width || label.Bottom>form.ClientSize.Height)throw new Exception(name+" outside viewport");
                }
                var rows=(IDictionary)typeof(Hud).GetField("rows",Private).GetValue(form);
                var settings=(DisplaySettings)typeof(Hud).GetField("display",Private).GetValue(form);
                var running=(Label)typeof(Hud).GetField("time",Private).GetValue(form);var span=(Label)typeof(Hud).GetField("spanTime",Private).GetValue(form);
                bool compactTime=(bool)typeof(Hud).GetField("compactLayout",Private).GetValue(form);
                if(running.Text.Contains("/") || span.Text.Contains("/") || running.Bottom>span.Top)throw new Exception("Time readings still share one crowded line");
                if(!compactTime && sample[1]/sample[2]>=620 && (running.Font.Size<13*sample[2] || span.Font.Size<13*sample[2]))throw new Exception("Time digits are too small at normal height");
                int bottom=0;
                foreach(string id in settings.Visible)
                {
                    object row=rows[id];var label=(Label)row.GetType().GetField("value",Private).GetValue(row);
                    var measured=TextRenderer.MeasureText(label.Text,label.Font,new Size(32767,32767),TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
                    if(measured.Width>label.Width || measured.Height>label.Height || label.Top<bottom || label.Bottom>form.Height)throw new Exception(id+" clipped, overlaps, or leaves viewport at "+form.Size);
                    bottom=label.Bottom;
                }
                using(var image=new Bitmap(form.Width,form.Height))
                {
                    form.DrawToBitmap(image,form.ClientRectangle);
                    // The fixture remains hidden. Paint each intended child
                    // explicitly because WinForms skips children of hidden forms.
                    bool compact=(bool)typeof(Hud).GetField("compactLayout",Private).GetValue(form);
                    using(var graphics=Graphics.FromImage(image))
                    foreach(Control child in form.Controls)
                    {
                        bool shown=child==typeof(Hud).GetField("brand",Private).GetValue(form) || child==typeof(Hud).GetField("tag",Private).GetValue(form);
                        if(!compact && (child==typeof(Hud).GetField("quota",Private).GetValue(form) || child==typeof(Hud).GetField("capacity",Private).GetValue(form)))shown=true;
                        foreach(string id in settings.Visible)
                        {
                            var row=rows[id];if(child==row.GetType().GetField("value",Private).GetValue(row) || (!compact && child==row.GetType().GetField("caption",Private).GetValue(row)))shown=true;
                        }
                        if(child==typeof(Hud).GetField("requestInput",Private).GetValue(form) || child==typeof(Hud).GetField("requestOutput",Private).GetValue(form))shown=true;
                        if(child==span || (!compact && child==typeof(Hud).GetField("spanCaption",Private).GetValue(form)))shown=true;
                        if(!shown)continue;
                        using(var layer=new Bitmap(child.Width,child.Height)){child.DrawToBitmap(layer,child.ClientRectangle);graphics.DrawImageUnscaled(layer,child.Location);}
                    }
                    image.Save(Path.Combine(Path.GetDirectoryName(args[1]),"layout-"+sample[0]+"x"+sample[1]+".png"));
                }
                log.WriteLine("PASS "+sample[0]+"x"+sample[1]+" @ "+sample[2]+" scale; numeric text fits without clipping.");
            }
            using(var log=new StreamWriter(args[1],true))
            using(var form=new Hud(true,IntPtr.Zero))
            {
                WindowSession.LatestSidebar=new SidebarFrame {window=0,width=73,dpi=96,topOffset=61,bottomOffset=5,navigationBottomOffset=505,footerReserved=65};
                var samples=new[]{new Native.RECT {Left=-1445,Top=1246,Right=-5,Bottom=2502},new Native.RECT {Left=40,Top=80,Right=1480,Bottom=1336},new Native.RECT {Left=-600,Top=0,Right=840,Bottom=900}};
                foreach(var rect in samples)
                {
                    typeof(Hud).GetField("bounds",Private).SetValue(form,rect);
                    typeof(Hud).GetMethod("UpdateDock",Private).Invoke(form,null);
                    if((int)typeof(Hud).GetField("dockLeft",Private).GetValue(form)!=rect.Left)throw new Exception("Dock drifted from native window edge");
                    if(form.Width!=73)throw new Exception("Dock width changed during window movement");
                }
                log.WriteLine("PASS docking at negative and positive screen positions, with resized window height.");
            }
        }
        catch(Exception ex){File.WriteAllText(args[1],ex.ToString());Environment.ExitCode=1;}
    }
}
