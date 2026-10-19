using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

// Layout, text and theme consume the same registered metric slots.

sealed partial class Hud
{
    int Px(int n) { return (int)Math.Round(n * uiScale); }
    int Py(int n) { return (int)Math.Round(n*uiScale*heightScale); }
    string NameText {get{return data!=null && lastError=="" ? data.sessionTitle : titleRaw;}}
    void ApplyLayout()
    {
        UpdateQuotaWindowCount(QuotaReader.Current(data==null?null:data.accountQuota));
        numberFit.Clear();bodyFit.Clear();SuspendLayout();
        int logicalWidth=railPixels>0 ? (int)Math.Round(railPixels/uiScale) : 73;
        int width=railPixels>0?railPixels:Px(logicalWidth);
        nameLayout.Measure(NameText,width,uiScale,fontFamily);
        int natural=display.NaturalHeight+nameLayout.LogicalHeight-28;
        int height=layoutPixels>0 ? layoutPixels : Px(natural);
        heightScale=Math.Min(1,height/(natural*uiScale));
        laidOutTitleHeight=nameLayout.LogicalHeight;
        textDensity=Typography.WidthDensity(logicalWidth);
        ClientSize = new Size(railPixels>0 ? railPixels : Px(logicalWidth),height);
        // Leave the two native edge columns visible for unobscured sampling.
        var oldRegion=Region;Region=new Region(new Rectangle(1,0,Math.Max(1,Width-2),Height));if(oldRegion!=null)oldRegion.Dispose();
        backdrop.Resize(ClientSize);lastTheme=DateTime.MinValue;
        compactLayout=heightScale<.65f || height<Px((int)Math.Max(164,Math.Min(370,display.NaturalHeight*.76)));
        SetLabel(brand,"CLH",6,5,logicalWidth-12,18,accent,9,FontStyle.Bold);
        SetLabel(tag, String.IsNullOrEmpty(tag.Text) ? "自动" : tag.Text, 4, 26, logicalWidth-8, 18, muted, Typography.BodyPoints, Typography.BodyStyle);
        SetLabel(close, "×", logicalWidth-20, 6, 18, 18, muted, 9, FontStyle.Regular);
        close.AutoEllipsis=false; close.UseCompatibleTextRendering=true;
        SetLabel(sessionTitle,nameLayout.Wrapped,3,49,logicalWidth-6,nameLayout.LogicalHeight,accent,Typography.BodyPoints,Typography.BodyStyle);
        sessionTitle.UseMnemonic=false;
        rules.Clear();int y=61+laidOutTitleHeight;
        foreach(string id in display.Order)
        {
            var row=rows[id];bool visible=!display.Hidden.Contains(id);
            MetricSlot previous=null;
            foreach(var slot in row.Slots)
            {
                var label=slot.Label;
                string text=slot.Reading && !String.IsNullOrEmpty(label.Text)?label.Text:slot.EmptyText(compactLayout);
                int inset=slot.Reading?1:2;
                float points=slot.Reading?Typography.ReadingPoints:Typography.BodyPoints;
                FontStyle style=slot.Reading?Typography.ReadingStyle:Typography.BodyStyle;
                SetLabel(label,text,inset,y+slot.Top,logicalWidth-2*inset,slot.Height,MetricColor(slot.Tone),points,style);
                label.Visible=visible && slot.Shown(compactLayout,display.QuotaWindowCount);
                if(slot.Shown(false,display.QuotaWindowCount))
                {
                    if(previous!=null)KeepAbove(previous.Label,label);
                    previous=slot;
                }
            }
            if(visible){rules.Add(y-8);y+=display.Height(id);}
        }
        menuTheme.Style(menu,uiScale);
        close.Visible=false;
        sessionTitle.Visible=!compactLayout;
        if(compactLayout)
        {
            brand.SetBounds(Px(6),Px(2),Width-Px(12),Px(14));ReplaceFont(brand,10.5f*uiScale,FontStyle.Bold);
            tag.SetBounds(Px(6),Px(17),Width-Px(12),Px(12));
            ReplaceFont(tag,Typography.Pixels(Typography.BodyPoints,uiScale,textDensity),Typography.BodyStyle);
            close.SetBounds(Width-Px(20),Px(1),Px(18),Px(16));
            var values=new List<Label>();
            foreach(string id in display.Visible)
                foreach(var slot in rows[id].Slots)
                    if(slot.Shown(true,display.QuotaWindowCount))values.Add(slot.Label);
            int start=Px(34),step=(Height-Px(39))/values.Count;
            for(int i=0;i<values.Count;i++)
            {
                values[i].SetBounds(Px(2),start+i*step,Width-Px(4),step);
                ReplaceFont(values[i],Math.Min(Typography.Pixels(Typography.ReadingPoints,uiScale,textDensity),step*.65f),Typography.ReadingStyle);
            }
        }
        UpdateTypographyGroups();
        if(data!=null)RenderReadings();
        else ClearReadings();
        ResumeLayout(false);
        Invalidate(true);
    }
    void ReplaceFont(Label label,float pixels,FontStyle style)
    {
        if(label.Font.Unit==GraphicsUnit.Pixel && Math.Abs(label.Font.Size-pixels)<.01 && label.Font.Style==style && Typography.Matches(label.Font,fontFamily)){label.Tag=pixels;return;}
        var previous=label.Font;label.Font=new Font(fontFamily,pixels,style,GraphicsUnit.Pixel);label.Tag=pixels;previous.Dispose();
    }
    static void KeepAbove(Label first,Label next)
    {
        // Use the same physical boundary for stacked labels at fractional DPI.
        first.Height=Math.Max(1,Math.Min(first.Height,next.Top-first.Top));
    }
    void UpdateTypographyGroups()
    {
        numericLabels.Clear();bodyLabels.Clear();bodyLabels.Add(tag);
        if(!compactLayout)bodyLabels.Add(sessionTitle);
        foreach(string id in display.Visible)
            foreach(var slot in rows[id].Slots)
                if(slot.Shown(compactLayout,display.QuotaWindowCount))
                    (slot.Reading?numericLabels:bodyLabels).Add(slot.Label);
    }
    void RenderReadings()
    {
        if(data==null)return;
        usage.Text=(compactLayout?"对话入":"对话")+Short(data.input);
        output.Text=(compactLayout?"对话出":"对话")+Short(data.output);
        requestInput.Text=(compactLayout?"本次入":"本次")+(data.lastInput>0 || data.lastOutput>0?Short(data.lastInput):"—");
        requestOutput.Text=(compactLayout?"本次出":"本次")+(data.lastInput>0 || data.lastOutput>0?Short(data.lastOutput):"—");
        string rate=data.requestToks.HasValue ? (data.requestToks.Value>=1000 ? (data.requestToks.Value/1000d).ToString("0.#",CultureInfo.InvariantCulture)+"K" : data.requestToks.Value.ToString("0.#",CultureInfo.InvariantCulture)) : "—";
        speed.Text=(compactLayout ? "速 " : "")+rate;
        context.Text=(compactLayout ? "占 " : "")+(data.contextPercent.HasValue ? data.contextPercent.Value.ToString("0.#",CultureInfo.InvariantCulture)+"%" : "—");
        capacity.Text=data.contextLimit>0 ? (data.contextUsed/1000d).ToString("0",CultureInfo.InvariantCulture)+"/"+(data.contextLimit/1000d).ToString("0",CultureInfo.InvariantCulture)+"K" : "未记录";
        RenderRemaining();
        RenderTime();
        counts.Text=(compactLayout?"轮":"")+(data.turns.HasValue?Short(data.turns.Value):"—")+(compactLayout?"/压":" / ")+Short(data.compactions);
        rows["model"].value.Text=String.IsNullOrEmpty(data.model)?"—":data.model.StartsWith("gpt-",StringComparison.Ordinal)?data.model.Substring(4):data.model;
        rows["cache"].value.Text=(compactLayout?"缓 ":"")+(data.input>0?(100d*data.cache/data.input).ToString("0.#",CultureInfo.InvariantCulture)+"%":"—");
        rows["duration"].value.Text=(compactLayout?"耗 ":"")+(data.requestMs>0?TimeShort(data.requestMs):"—");
        FitNumbers();FitBody();
    }
    static string TimeShort(long? milliseconds)
    {
        if(!milliseconds.HasValue)return "—";
        double seconds=Math.Max(0,milliseconds.Value)/1000d;
        if(seconds>=86400)return (seconds/86400).ToString("0.#",CultureInfo.InvariantCulture)+"d";
        if(seconds>=3600)return (seconds/3600).ToString("0.#",CultureInfo.InvariantCulture)+"h";
        if(seconds>=60)return (seconds/60).ToString("0.#",CultureInfo.InvariantCulture)+"m";
        return seconds.ToString("0",CultureInfo.InvariantCulture)+"s";
    }
    void RenderTime()
    {
        if(data==null)return;
        string running=(compactLayout?"用 ":"")+TimeShort(data.runningMs),span=(compactLayout?"跨 ":"")+TimeShort(data.spanMs);
        bool changed=false;
        if(time.Text!=running){time.Text=running;changed=true;}
        if(spanTime.Text!=span){spanTime.Text=span;changed=true;}
        if(changed)FitNumbers();
    }
    void RenderRemaining()
    {
        var snapshot=QuotaReader.Current(data==null ? null : data.accountQuota);
        if(UpdateQuotaWindowCount(snapshot))
        {
            if(codex!=IntPtr.Zero){layoutPixels=0;UpdateDock();if(!dragging)MoveToDock();}
            else ApplyLayout();
            return;
        }
        var windows=snapshot==null ? new QuotaWindow[0] : snapshot.windows.OrderBy(x=>x.windowMinutes>0?x.windowMinutes:Int64.MaxValue).ToArray();
        QuotaWindow first=windows.Length>0?windows[0]:null,second=windows.Length>1?windows[1]:null;
        RenderQuotaWindow(remaining,first,false);RenderQuotaWindow(remainingSecondary,second,true);
        FitNumbers();
    }
    bool UpdateQuotaWindowCount(QuotaSnapshot snapshot)
    {
        int count=snapshot!=null && snapshot.windows.Length>1?2:1;
        bool changed=display.QuotaWindowCount!=count;display.QuotaWindowCount=count;return changed;
    }
    void RenderQuotaWindow(Label label,QuotaWindow window,bool second)
    {
        if(window==null){label.Text=second?"":compactLayout?"余 —":"额度 —";label.ForeColor=accent;return;}
        string name=window.name;
        if(String.IsNullOrEmpty(name) || name=="额度窗口")name=second?"窗口2":"窗口1";
        if(compactLayout)name=name.Replace(" 小时","h").Replace(" 天","d").Replace(" 分钟","m");
        else name=name.Replace(" ","");
        label.Text=name+" "+window.remaining.ToString("0.#",CultureInfo.InvariantCulture)+"%";
        label.ForeColor=window.remaining<=10 ? Color.FromArgb(185,94,90) : accent;
    }
    void ClearReadings()
    {
        tag.Text = SessionReader.IsAutomatic() ? "自动" : "固定";
        foreach(var row in rows.Values)foreach(var slot in row.Slots)
            if(slot.Reading)slot.Label.Text=slot.EmptyText(compactLayout);
        RenderRemaining();
        FitBody();
        Invalidate(true);
    }
    void SetLabel(Label label, string text, int x, int y, int w, int h, Color color, float size, FontStyle style)
    {
        label.Text = text; label.SetBounds(Px(x), Py(y), Px(w), Py(h));
        label.ForeColor = color; label.BackColor = Color.Transparent;
        var previous = label.Parent == this ? label.Font : null;
        // Pixel fonts prevent the primary monitor's DPI from enlarging text
        // inside a narrower rail on another monitor.
        float pixels=Typography.Pixels(size,uiScale,textDensity);
        if(previous==null || previous.Unit!=GraphicsUnit.Pixel || Math.Abs(previous.Size-pixels)>.01 || previous.Style!=style || !Typography.Matches(previous,fontFamily))
        {
            label.Font=new Font(fontFamily,pixels,style,GraphicsUnit.Pixel);
            if(previous!=null)previous.Dispose();
        }
        label.Tag=pixels;
        label.AutoEllipsis = true; label.TextAlign = ContentAlignment.MiddleCenter;if(label.Parent!=this)Controls.Add(label);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if(ClientRectangle.Width<=0 || ClientRectangle.Height<=0)return;
        if(backdrop.Ready){backdrop.Paint(e.Graphics);return;}
        using(var brush=new LinearGradientBrush(ClientRectangle,backgroundLeft,backgroundRight,0f))e.Graphics.FillRectangle(brush,ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var rule = new Pen(separator))
            if(!compactLayout)foreach(int y in rules) e.Graphics.DrawLine(rule,Px(12),Py(y),Width-Px(12),Py(y));
        if (!display.Hidden.Contains("context") && data != null && data.contextPercent.HasValue)
        {
            var color = data.contextPercent.Value >= 90 ? Color.FromArgb(185,94,90) : Blend(backgroundRight,accent,.55);
            int filled = (int)((Height-Px(16))*Math.Min(100,data.contextPercent.Value)/100);
            using (var brush = new SolidBrush(color)) e.Graphics.FillRectangle(brush,Width-Px(3),Height-Px(8)-filled,Px(2),filled);
        }
    }

    static Color Blend(Color a,Color b,double amount)
    {
        return Color.FromArgb((int)Math.Round(a.R+(b.R-a.R)*amount),(int)Math.Round(a.G+(b.G-a.G)*amount),(int)Math.Round(a.B+(b.B-a.B)*amount));
    }

    void UpdateDock()
    {
        float targetScale=Native.GetDpiForWindow(codex)/96f;
        if(targetScale<=0)targetScale=1;
        var frame=WindowSession.LatestSidebar;
        int border=Native.VisibleBorder(codex);
        int width=(int)Math.Round(73*targetScale)-border;
        int reserved=(int)Math.Round(150*targetScale);
        footerGap=(int)Math.Round(64*targetScale);
        dockLeft=bounds.Left+border;dockTop=bounds.Top+(int)Math.Round(60*targetScale);dockBottom=bounds.Bottom-1;
        if(frame!=null && frame.window==codex.ToInt64())
        {
            double ratio=targetScale/(frame.dpi/96d);
            // DWM returns current physical pixels. Do not scale a stale inset
            // retained from another DPI or from the restored window state.
            int inset=codex!=IntPtr.Zero && Native.IsWindow(codex)?border:frame.leftOffset;
            dockLeft=bounds.Left+inset;
            dockTop=bounds.Top+(int)Math.Round(frame.topOffset*ratio);
            dockBottom=bounds.Bottom-(int)Math.Round(frame.bottomOffset*ratio);
            width=(int)Math.Round(frame.width*ratio)-inset;
            reserved=bounds.Top+(int)Math.Round(frame.navigationBottomOffset*ratio)+(int)Math.Round(12*targetScale)-dockTop;
            footerGap=Math.Max(footerGap,(int)Math.Round(frame.footerReserved*ratio));
        }
        effectiveGap=Math.Max(footerGap,(int)Math.Round(offsetBottom*targetScale));
        int available=dockBottom-dockTop-effectiveGap-(int)Math.Round(8*targetScale)-reserved;
        minimumTop=dockTop+Math.Max(0,reserved);
        dockFits=available>=(int)Math.Round(164*targetScale);
        nameLayout.Measure(NameText,width,targetScale,fontFamily);
        int desired=(int)Math.Round((display.NaturalHeight+nameLayout.LogicalHeight-28)*targetScale);
        if(!positionLocked){available=Screen.FromHandle(codex).WorkingArea.Height-(int)Math.Round(16*targetScale);dockFits=true;}
        int height=Math.Max((int)Math.Round(164*targetScale),Math.Min(desired,available));
        UpdateLayoutViewport(width,height,targetScale);
    }
    void UpdateLayoutViewport(int width,int height,float targetScale)
    {
        nameLayout.Measure(NameText,width,targetScale,fontFamily);
        // A constrained viewport keeps the same outer size when the title grows.
        // Its logical height still changes every row's position and scale.
        if(Math.Abs(uiScale-targetScale)>.01 || railPixels!=width || layoutPixels!=height || laidOutTitleHeight!=nameLayout.LogicalHeight)
        {
            uiScale=targetScale;railPixels=width;layoutPixels=height;ApplyLayout();
        }
        else if(sessionTitle.Text!=nameLayout.Wrapped){sessionTitle.Text=nameLayout.Wrapped;FitBody();}
    }

    void SampleTheme()
    {
        if(menu.Visible || InMotion || !positionLocked || Width<3 || Height<2)return;
        if(DateTime.UtcNow-lastTheme < TimeSpan.FromSeconds(2))return;
        lastTheme=DateTime.UtcNow;
        if(codex==IntPtr.Zero)return;
        // The region leaves the edge columns to Codex. Verify ownership before
        // sampling so other applications and pet windows do not become colors.
        foreach(int y in new[]{Top,Top+Height/3,Top+Height*2/3,Bottom-1})
            foreach(int x in new[]{Left,Right-1})if(Native.GetAncestor(Native.WindowFromPoint(new Point(x,y)),2)!=codex)return;
        foreach(int y in new[]{Top-1,Bottom})if(Native.GetAncestor(Native.WindowFromPoint(new Point(Left+Width/2,y)),2)!=codex)return;
        try
        {
            Color[] top,bottom,left,right;
            backdrop.Capture(Location,ClientSize,out top,out bottom,out left,out right);
            ApplyBackdrop(top,bottom,left,right);
        }
        catch(System.ComponentModel.Win32Exception){ }
    }
    void ApplyBackdrop(Color[] top,Color[] bottom,Color[] left,Color[] right)
    {
        if(!backdrop.Update(top,bottom,left,right,ClientSize))return;
        Color a=left[left.Length/2],b=right[right.Length/2];
        backgroundLeft=a;backgroundRight=b;BackColor=a;
        bool light=(a.R*299+a.G*587+a.B*114)/1000>150;
        ink=light ? Color.FromArgb(58,57,68) : Color.FromArgb(227,231,237);
        muted=light ? Color.FromArgb(131,128,144) : Color.FromArgb(151,160,176);
        accent=light ? Color.FromArgb(103,95,139) : Color.FromArgb(163,187,209);
        separator=Blend(a,ink,.10);
        menuTheme.Update(a,ink,muted,accent);
        tag.ForeColor=close.ForeColor=muted;
        foreach(var row in rows.Values)foreach(var slot in row.Slots)slot.Label.ForeColor=MetricColor(slot.Tone);
        brand.ForeColor=accent;
        sessionTitle.ForeColor=accent;
        RenderRemaining();
        Invalidate(true);
    }
    Color MetricColor(MetricTone tone)
    {
        if(tone==MetricTone.Muted)return muted;
        if(tone==MetricTone.Context)return data!=null && data.contextPercent.GetValueOrDefault()>=90?Color.FromArgb(185,94,90):accent;
        return tone==MetricTone.Accent?accent:ink;
    }

    void FitNumbers(){numberFit.Apply(numericLabels,uiScale,null,fontFamily);}
    void FitBody(){bodyFit.Apply(bodyLabels,uiScale,sessionTitle,fontFamily);}

    static string Short(long n)
    {
        if (n >= 1000000000) return (n / 1000000000d).ToString(n>=100000000000 ? "0" : "0.#", CultureInfo.InvariantCulture) + "B";
        if (n >= 1000000) return (n / 1000000d).ToString(n>=100000000 ? "0" : "0.#", CultureInfo.InvariantCulture) + "M";
        if (n >= 1000) return (n / 1000d).ToString(n>=100000 ? "0" : "0.#", CultureInfo.InvariantCulture) + "K";
        return n.ToString();
    }
}
