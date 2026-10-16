using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

static class Typography
{
    internal const string Family="Microsoft YaHei UI";
    internal const float BodyPoints=8.5f;
    internal const float ReadingPoints=11;
    internal const float MinimumPixels=7.2f;
    internal const FontStyle BodyStyle=FontStyle.Bold,ReadingStyle=FontStyle.Bold;
    internal static float WidthDensity(float width){return Math.Max(.65f,Math.Min(1.3f,width/73f));}
    internal static float Pixels(float points,float scale,float density){return Math.Max(MinimumPixels*scale,points*96f/72f*scale*density);}
}

// One fitter per visible text role. Layout supplies the actual rectangles;
// height affects fitting here rather than pre-shrinking every nominal font.
sealed class TypographyFit
{
    sealed class State {internal string text;internal Size size;internal float nominal,scale,applied;internal FontStyle style;}
    readonly Dictionary<Label,State> cache=new Dictionary<Label,State>();
    internal void Clear(){cache.Clear();}
    internal void Apply(List<Label> labels,float scale,Label multiline=null)
    {
        if(labels.Count==0)return;
        bool changed=cache.Count!=labels.Count;float desired=Single.MaxValue;
        foreach(var label in labels)
        {
            float nominal=label.Tag is float?(float)label.Tag:label.Font.Size;desired=Math.Min(desired,nominal);
            State state;
            if(!cache.TryGetValue(label,out state) || state.text!=label.Text || state.size!=label.Size || state.nominal!=nominal || state.style!=label.Font.Style || state.scale!=scale || Math.Abs(state.applied-label.Font.Size)>.01)changed=true;
        }
        if(!changed)return;
        float minimum=Math.Min(desired,Typography.MinimumPixels*scale);
        while(true)
        {
            bool fits=true;
            using(var font=new Font(Typography.Family,desired,labels[0].Font.Style,GraphicsUnit.Pixel))
            foreach(var label in labels)
            {
                var size=TextRenderer.MeasureText(label.Text,font,label==multiline?new Size(label.Width,32767):new Size(32767,32767),label==multiline?TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix:TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
                if(size.Width>label.Width || size.Height>label.Height){fits=false;break;}
            }
            if(fits || desired<=minimum)break;
            desired=Math.Max(minimum,desired-.2f*scale);
        }
        cache.Clear();
        foreach(var label in labels)
        {
            if(Math.Abs(label.Font.Size-desired)>.01)
            {
                var old=label.Font;label.Font=new Font(Typography.Family,desired,old.Style,GraphicsUnit.Pixel);old.Dispose();
            }
            cache[label]=new State{text=label.Text,size=label.Size,nominal=label.Tag is float?(float)label.Tag:label.Font.Size,scale=scale,style=label.Font.Style,applied=desired};
        }
    }
}
