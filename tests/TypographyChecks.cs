using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

static class TypographyChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static Label Field(Hud hud,string name){return (Label)typeof(Hud).GetField(name,Private).GetValue(hud);}
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static string MinimumBlockers(List<Label> labels,float pixels)
    {
        var blockers=new List<string>();
        using(var font=new Font(Typography.Family,pixels,FontStyle.Bold,GraphicsUnit.Pixel))
        foreach(var label in labels)
        {
            var size=TextRenderer.MeasureText(label.Text,font,new Size(32767,32767),TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
            if(size.Width>label.Width || size.Height>label.Height)blockers.Add(label.Text+" "+size+" in "+label.Size);
        }
        return String.Join("; ",blockers);
    }
    static void SameFont(List<Label> labels,string group)
    {
        var font=labels[0].Font;
        foreach(var label in labels)
        {
            Require(Math.Abs(label.Font.Size-font.Size)<.01 && label.Font.FontFamily.Name==font.FontFamily.Name && label.Font.Unit==font.Unit,group+" differs: "+labels[0].Text+"="+font.Size+", "+label.Text+"="+label.Font.Size);
            Require(label.Font.Bold,group+" is not bold: "+label.Text);
            var size=TextRenderer.MeasureText(label.Text,label.Font,label.Name=="title"?new Size(label.Width,32767):new Size(32767,32767),label.Name=="title"?TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix:TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
            Require(size.Width<=label.Width && size.Height<=label.Height,group+" clipped: "+label.Text+" "+size+" in "+label.Size);
        }
    }
    static void Save(Hud hud,List<Label> numbers,List<Label> body,string path)
    {
        using(var image=new Bitmap(hud.Width,hud.Height))
        using(var graphics=Graphics.FromImage(image))
        {
            hud.DrawToBitmap(image,hud.ClientRectangle);
            var labels=new List<Label>(numbers);labels.AddRange(body);labels.Add(Field(hud,"brand"));
            foreach(var label in labels)using(var layer=new Bitmap(label.Width,label.Height)){label.DrawToBitmap(layer,label.ClientRectangle);graphics.DrawImageUnscaled(layer,label.Location);}
            image.Save(path);
        }
    }
    [STAThread]
    static void Main(string[] args)
    {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));Application.EnableVisualStyles();
        var log=new List<string>();int passed=0,failed=0;
        var cases=new[]{new[]{73f,830f,1f},new[]{73f,661f,1f},new[]{72f,661f,1f},new[]{73f,474f,1f},new[]{73f,164f,1f},new[]{110f,992f,1.5f},new[]{146f,1322f,2f}};
        var limit=SystemInformation.MaxWindowTrackSize;
        foreach(var requested in cases)
        {
            var sample=(float[])requested.Clone();if(limit.Width>0)sample[0]=Math.Min(sample[0],limit.Width);if(limit.Height>0)sample[1]=Math.Min(sample[1],limit.Height);
            try
            {
                using(var hud=new Hud(true,IntPtr.Zero))
                {
                    var data=new Data{sessionTitle="加载本机最新的 skill 我们继续",input=48500000,output=268000,lastInput=345000,lastOutput=984,contextUsed=346000,contextLimit=828400,contextPercent=41.8,turns=20,compactions=1,cache=46705500,requestMs=35000,runningMs=181440000,spanMs=52920000,requestToks=28.1};
                    data.accountQuota=new QuotaSnapshot{readAt=DateTimeOffset.UtcNow.ToString("o"),windows=new[]{new QuotaWindow{name="5 小时",windowMinutes=300,remaining=60,resetAt=DateTimeOffset.Now.AddHours(2).AddMinutes(4).ToString("o")},new QuotaWindow{name="7 天",windowMinutes=10080,remaining=38,resetAt=DateTimeOffset.Now.AddDays(2).AddHours(5).ToString("o")}}};
                    typeof(Hud).GetField("data",Private).SetValue(hud,data);
                    typeof(Hud).GetField("uiScale",Private).SetValue(hud,sample[2]);
                    typeof(Hud).GetField("railPixels",Private).SetValue(hud,(int)sample[0]);typeof(Hud).GetField("layoutPixels",Private).SetValue(hud,(int)sample[1]);
                    typeof(Hud).GetMethod("ApplyLayout",Private).Invoke(hud,null);
                    bool compact=(bool)typeof(Hud).GetField("compactLayout",Private).GetValue(hud);
                    var numbers=new List<Label>();var body=new List<Label>{Field(hud,"tag")};
                    var rows=(IDictionary)typeof(Hud).GetField("rows",Private).GetValue(hud);var settings=(DisplaySettings)typeof(Hud).GetField("display",Private).GetValue(hud);
                    if(!compact){var title=Field(hud,"sessionTitle");title.Name="title";body.Add(title);}
                    foreach(string id in settings.Visible)
                    {
                        var row=rows[id];numbers.Add((Label)row.GetType().GetField("value",Private).GetValue(row));
                        if(!compact)body.Add((Label)row.GetType().GetField("caption",Private).GetValue(row));
                        if(id=="input")numbers.Add(Field(hud,"requestInput"));
                        if(id=="output")numbers.Add(Field(hud,"requestOutput"));
                        if(id=="context" && !compact)numbers.Add(Field(hud,"capacity"));
                        if(id=="time"){numbers.Add(Field(hud,"spanTime"));if(!compact)body.Add(Field(hud,"spanCaption"));}
                        if(id=="remaining" && settings.QuotaWindowCount>1)numbers.Add(Field(hud,"remainingSecondary"));
                    }
                    string prefix="typography-"+sample[0]+"x"+sample[1];Save(hud,numbers,body,Path.Combine(Path.GetDirectoryName(args[0]),prefix+".png"));
                    SameFont(numbers,"readings");SameFont(body,"body");
                    float initialReading=numbers[0].Font.Size,initialBody=body[0].Font.Size;
                    if(sample[0]/sample[2]<=74 && sample[1]/sample[2]>=600)Require(numbers[0].Font.Size>=11.5f*sample[2],"Normal-height readings were unnecessarily shrunk: "+numbers[0].Font.Size+"px; "+MinimumBlockers(numbers,11.5f*sample[2]));
                    data.input=999900000;data.lastInput=999900000;data.requestToks=88888.9;
                    typeof(Hud).GetMethod("RenderReadings",Private).Invoke(hud,null);
                    SameFont(numbers,"changed readings");SameFont(body,"changed body");
                    float fitted=numbers[0].Font.Size;
                    var hiddenRow=rows["model"];((Label)hiddenRow.GetType().GetField("value",Private).GetValue(hiddenRow)).Text=new string('x',300);
                    typeof(Hud).GetMethod("FitNumbers",Private).Invoke(hud,null);
                    Require(Math.Abs(numbers[0].Font.Size-fitted)<.01,"A hidden model label shrank visible readings");
                    int fonts=0;foreach(var label in numbers)label.FontChanged+=delegate{fonts++;};foreach(var label in body)label.FontChanged+=delegate{fonts++;};
                    for(int i=0;i<40;i++)typeof(Hud).GetMethod("RenderReadings",Private).Invoke(hud,null);
                    Require(fonts==0,"Unchanged readings repeatedly rebuild fonts");
                    log.Add("PASS "+prefix+" @ "+sample[2]+" scale: readings="+initialReading.ToString("0.##")+"px (long values "+numbers[0].Font.Size.ToString("0.##")+"px), body="+initialBody.ToString("0.##")+"px, bold, no clipping or unchanged font rebuilds");passed++;
                }
            }
            catch(Exception ex){log.Add("FAIL "+sample[0]+"x"+sample[1]+": "+ex.GetBaseException().Message);failed++;}
        }
        log.Add("Typography: "+passed+" passed, "+failed+" failed, 0 skipped. Hidden generated fixtures.");
        File.WriteAllLines(args[0],log);Environment.ExitCode=failed==0?0:1;
    }
}
