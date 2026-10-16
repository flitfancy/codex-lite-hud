using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

static class NameLayoutChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static void CheckConstrainedDpi(float scale)
    {
        using(var form=new Hud(true,IntPtr.Zero))
        {
            var data=new Data{sessionTitle="HUD"};typeof(Hud).GetField("data",Private).SetValue(form,data);
            var update=typeof(Hud).GetMethod("UpdateLayoutViewport",Private);
            int width=(int)Math.Round(73*scale),height=Math.Min((int)Math.Round(505*scale),SystemInformation.MaxWindowTrackSize.Height);
            var viewport=new object[]{width,height,scale};update.Invoke(form,viewport);
            var title=(Label)typeof(Hud).GetField("sessionTitle",Private).GetValue(form);var input=(Label)typeof(Hud).GetField("inputCaption",Private).GetValue(form);
            int initialTitleHeight=title.Height,initialInputTop=input.Top;
            data.sessionTitle="查看 flitrealize 架构与测试文件";update.Invoke(form,viewport);
            Require(form.Height==height && title.Height>initialTitleHeight && input.Top>initialInputTop,"Constrained title did not reflow at DPI scale "+scale);
            Require(title.Bottom<=input.Top,"Constrained title overlaps the first row at DPI scale "+scale);
            // A small host may force the compact layout, where the title is
            // intentionally hidden. Geometry invalidation still applies there.
            if(!(bool)typeof(Hud).GetField("compactLayout",Private).GetValue(form))
            {
                var required=TextRenderer.MeasureText(title.Text,title.Font,new Size(title.Width,32767),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
                Require(required.Width<=title.Width && required.Height<=title.Height,"Constrained title clipped at DPI scale "+scale);
            }
            int fontChanges=0;title.FontChanged+=delegate{fontChanges++;};
            update.Invoke(form,viewport);Require(fontChanges==0,"Unchanged constrained title rebuilt its font at DPI scale "+scale);
            data.sessionTitle="HUD";update.Invoke(form,viewport);
            Require(form.Height==height && title.Height==initialTitleHeight && input.Top==initialInputTop,"Constrained short title did not restore row positions at DPI scale "+scale);
        }
    }
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
                int shortHeight=form.Height;var title=(Label)typeof(Hud).GetField("sessionTitle",Private).GetValue(form);var brand=(Label)typeof(Hud).GetField("brand",Private).GetValue(form);var mode=(Label)typeof(Hud).GetField("tag",Private).GetValue(form);var input=(Label)typeof(Hud).GetField("inputCaption",Private).GetValue(form);
                Require(brand.Text=="CLH" && brand.Top<mode.Top && mode.Bottom<title.Top,"CLH, mode and name order differs");
                Require(title.Height<=24 && input.Top-title.Bottom<=16,"Short title blank space remains");
                data.sessionTitle="查看 flitrealize 架构与测试文件";update.Invoke(form,null);
                Require(form.Height>shortHeight && title.Height>24,"Long name did not expand HUD");
                var required=TextRenderer.MeasureText(title.Text,title.Font,new Size(title.Width,32767),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
                Require(required.Height<=title.Height && required.Width<=title.Width,"Wrapped name clipped at natural height");
                data.sessionTitle="HUD";update.Invoke(form,null);Require(form.Height==shortHeight && title.Height<=24,"Returning to short name did not shrink HUD");
                var counts=(Label)typeof(Hud).GetField("counts",Private).GetValue(form);var speed=(Label)typeof(Hud).GetField("speed",Private).GetValue(form);
                Require(counts.Bottom<=speed.Top,"Turns/compactions must stay above tok/s");

                // When the sidebar limits the total height, changing the title
                // still has to redistribute the space inside the same viewport.
                typeof(Hud).GetField("bounds",Private).SetValue(form,new Native.RECT{Left=0,Top=0,Right=1440,Bottom=1100});
                update.Invoke(form,null);
                int constrainedHeight=form.Height,shortTitleHeight=title.Height,shortInputTop=input.Top;
                data.sessionTitle="查看 flitrealize 架构与测试文件";update.Invoke(form,null);
                required=TextRenderer.MeasureText(title.Text,title.Font,new Size(title.Width,32767),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
                Require(form.Height==constrainedHeight,"Constrained viewport changed with the title");
                Require(title.Height>shortTitleHeight && input.Top>shortInputTop,"Constrained title did not redistribute row space");
                Require(required.Height<=title.Height && required.Width<=title.Width,"Wrapped name clipped at constrained height");
                data.sessionTitle="HUD";update.Invoke(form,null);
                Require(form.Height==constrainedHeight && title.Height==shortTitleHeight && input.Top==shortInputTop,"Constrained short title did not recover its previous layout");
            }
            foreach(float scale in new[]{1f,1.25f,1.5f,2f})CheckConstrainedDpi(scale);
            File.WriteAllText(args[0],"PASS cached wrapping, unbroken names, CLH header, short/long/short natural and constrained height, complete title and row order, constrained DPI scales 1/1.25/1.5/2, unchanged layout cache.");
        }
        catch(Exception ex){File.WriteAllText(args[0],ex.ToString());Environment.ExitCode=1;}
    }
}
