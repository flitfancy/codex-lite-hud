using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

static class NameLayoutChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            Native.SetProcessDpiAwarenessContext(new IntPtr(-4));Application.EnableVisualStyles();
            var measure=new NameLayout();measure.Measure("HUD",73,1);
            Require(measure.LogicalHeight<=24,"Short name retained a tall blank block");
            string cached=measure.Wrapped;measure.Measure("HUD",73,1);Require(Object.ReferenceEquals(cached,measure.Wrapped),"Unchanged name did not reuse layout cache");
            measure.Measure("VeryLongProjectNameWithoutSpaces",73,1);Require(measure.Wrapped.Contains("\n") && measure.LogicalHeight>24,"Long unbroken name did not wrap");
            using(var form=new Hud(true,IntPtr.Zero))
            {
                var data=new Data{sessionTitle="HUD"};typeof(Hud).GetField("data",Private).SetValue(form,data);
                WindowSession.LatestSidebar=new SidebarFrame{window=0,width=73,dpi=96,topOffset=61,bottomOffset=5,navigationBottomOffset=505,footerReserved=65};
                typeof(Hud).GetField("bounds",Private).SetValue(form,new Native.RECT{Left=0,Top=0,Right=1440,Bottom=1600});
                var update=typeof(Hud).GetMethod("UpdateDock",Private);update.Invoke(form,null);
                int shortHeight=form.Height;var title=(Label)typeof(Hud).GetField("quota",Private).GetValue(form);var brand=(Label)typeof(Hud).GetField("brand",Private).GetValue(form);var mode=(Label)typeof(Hud).GetField("tag",Private).GetValue(form);var input=(Label)typeof(Hud).GetField("inputCaption",Private).GetValue(form);
                Require(brand.Text=="CLH" && brand.Top<mode.Top && mode.Bottom<title.Top,"CLH, mode and name order differs");
                Require(title.Height<=24 && input.Top-title.Bottom<=16,"Short title blank space remains");
                data.sessionTitle="查看 flitrealize 架构与测试文件";update.Invoke(form,null);
                Require(form.Height>shortHeight && title.Height>24,"Long name did not expand HUD");
                var required=TextRenderer.MeasureText(title.Text,title.Font,new Size(title.Width,32767),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
                Require(required.Height<=title.Height && required.Width<=title.Width,"Wrapped name clipped at natural height");
                data.sessionTitle="HUD";update.Invoke(form,null);Require(form.Height==shortHeight && title.Height<=24,"Returning to short name did not shrink HUD");
                var counts=(Label)typeof(Hud).GetField("counts",Private).GetValue(form);var speed=(Label)typeof(Hud).GetField("speed",Private).GetValue(form);
                Require(counts.Bottom<=speed.Top,"Turns/compactions must stay above tok/s");
            }
            File.WriteAllText(args[0],"PASS cached wrapping, unbroken names, CLH header, short/long/short natural height, complete title and row order.");
        }
        catch(Exception ex){File.WriteAllText(args[0],ex.ToString());Environment.ExitCode=1;}
    }
}
