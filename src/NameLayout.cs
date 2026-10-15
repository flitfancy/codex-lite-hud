using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

// Only remeasure when the name, rail width or DPI changes, never on movement.
sealed class NameLayout
{
    string raw;int width;float scale;
    internal string Wrapped;
    internal int LogicalHeight;
    internal void Measure(string name,int railWidth,float dpiScale)
    {
        if(raw==name && width==railWidth && scale==dpiScale)return;
        raw=name;width=railWidth;scale=dpiScale;
        int logicalWidth=(int)Math.Round(railWidth/dpiScale);
        int pixels=Math.Max(20,(int)Math.Round((logicalWidth-10)*dpiScale));
        float density=Math.Max(.65f,Math.Min(1.3f,logicalWidth/73f));
        using(var font=new Font("Microsoft YaHei UI",8.5f*96f/72f*dpiScale*density,FontStyle.Regular,GraphicsUnit.Pixel))
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
