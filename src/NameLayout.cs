using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

// Only remeasure when the name, rail width, DPI or family changes.
sealed class NameLayout
{
    string raw,family;int width;float scale;
    internal string Wrapped;
    internal int LogicalHeight;
    internal void Measure(string name,int railWidth,float dpiScale,string fontFamily=Typography.Family)
    {
        if(raw==name && width==railWidth && scale==dpiScale && family==fontFamily)return;
        raw=name;width=railWidth;scale=dpiScale;family=fontFamily;
        int logicalWidth=(int)Math.Round(railWidth/dpiScale);
        int pixels=Math.Max(20,(int)Math.Round((logicalWidth-6)*dpiScale));
        float density=Typography.WidthDensity(logicalWidth);
        using(var font=new Font(fontFamily,Typography.Pixels(Typography.BodyPoints,dpiScale,density),Typography.BodyStyle,GraphicsUnit.Pixel))
        {
            var result=new StringBuilder();string line="";
            var elements=StringInfo.GetTextElementEnumerator((name??"").Replace("\r", ""));
            while(elements.MoveNext())
            {
                string next=elements.GetTextElement();
                if(next=="\n"){result.Append(line.TrimEnd()).Append('\n');line="";continue;}
                if(line.Length==0 && String.IsNullOrWhiteSpace(next))continue;
                string candidate=line+next;
                var size=TextRenderer.MeasureText(candidate,font,new Size(32767,32767),TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
                if(size.Width>pixels && line.Length>0)
                {
                    int split=line.LastIndexOf(' ');
                    if(split>0){result.Append(line.Substring(0,split).TrimEnd()).Append('\n');line=line.Substring(split+1)+next;}
                    else{result.Append(line.TrimEnd()).Append('\n');line=next.TrimStart();}
                }
                else line=candidate;
            }
            result.Append(line.TrimEnd());Wrapped=result.ToString();
            int lines=1;foreach(char c in Wrapped)if(c=='\n')lines++;
            int linePixels=TextRenderer.MeasureText("Ag",font,new Size(32767,32767),TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix).Height;
            LogicalHeight=Math.Max(20,Math.Min(144,(int)Math.Ceiling(lines*linePixels/dpiScale)+4));
        }
    }
}
