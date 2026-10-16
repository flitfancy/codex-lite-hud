using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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

sealed class Data
{
    public string sessionId, sessionTitle, model, usageAt, readAt, selectionSource;
    public long input, output, cache, lastInput, lastOutput, contextUsed, contextLimit, requestMs;
    public double? contextPercent, requestToks;
    public long? turns;
    public long? runningMs,spanMs;
    public long completedTurns,abortedTurns,activeTurns,compactions;
    public SidebarFrame sidebar;
    public QuotaSnapshot accountQuota;
    internal bool SameDisplay(Data other)
    {
        // Live elapsed values update their own label in RenderTime; they do not
        // invalidate the rest of an otherwise unchanged dashboard.
        if(other==null || sessionId!=other.sessionId || sessionTitle!=other.sessionTitle || model!=other.model || usageAt!=other.usageAt || selectionSource!=other.selectionSource || input!=other.input || output!=other.output || cache!=other.cache || lastInput!=other.lastInput || lastOutput!=other.lastOutput || contextUsed!=other.contextUsed || contextLimit!=other.contextLimit || requestMs!=other.requestMs || contextPercent!=other.contextPercent || requestToks!=other.requestToks)return false;
        if(turns!=other.turns || completedTurns!=other.completedTurns || abortedTurns!=other.abortedTurns || activeTurns!=other.activeTurns || compactions!=other.compactions)return false;
        if(accountQuota==other.accountQuota)return true;
        if(accountQuota==null || other.accountQuota==null || accountQuota.readAt!=other.accountQuota.readAt || accountQuota.source!=other.accountQuota.source || accountQuota.windows.Length!=other.accountQuota.windows.Length)return false;
        for(int i=0;i<accountQuota.windows.Length;i++)
        {
            var a=accountQuota.windows[i];var b=other.accountQuota.windows[i];
            if(a.name!=b.name || a.windowMinutes!=b.windowMinutes || a.remaining!=b.remaining || a.resetAt!=b.resetAt)return false;
        }
        return true;
    }
}

static class Reader
{
    internal static Task<Data> Read(string id = null)
    {
        return Task.Run(() => { QuotaLogs.Refresh();var data=SessionReader.Read(id); QuotaReader.Apply(data); return data; });
    }
    internal static Task<Data> ReadCurrent(IntPtr window, bool automatic,bool diagnostic=false)
    {
        return Task.Run(() =>
        {
            QuotaLogs.Refresh();
            string id = automatic ? WindowSession.Resolve(window,SessionReader.Recent(Int32.MaxValue),diagnostic) : SessionReader.SelectedId();
            var result = SessionReader.Read(id);
            result.selectionSource = automatic ? "current-window" : "manual";
            result.sidebar = WindowSession.LatestSidebar;
            QuotaReader.Apply(result);
            return result;
        });
    }
}
sealed class Hud : Form
{
    readonly bool preview;
    readonly int ownPid = Process.GetCurrentProcess().Id;
    readonly System.Windows.Forms.Timer follow = new System.Windows.Forms.Timer { Interval = 200 };
    WindowTracking tracking;
    bool followQueued,positionUpdating,positionPending,targetMoving;
    DateTime lastMotion=DateTime.MinValue;
    IntPtr geometryTarget;
    Native.RECT frameInsets;
    int geometryWidth,geometryHeight;
    uint geometryDpi;
    readonly Label quota = new Label(), usage = new Label(), tag = new Label(), brand=new Label(),context = new Label(), speed = new Label();
    readonly Label time=new Label(),timeCaption=new Label(),spanTime=new Label(),spanCaption=new Label();
    readonly NameLayout nameLayout=new NameLayout();
    string titleRaw="识别会话";
    readonly Label output = new Label(), capacity = new Label();
    readonly Label requestInput=new Label(),requestOutput=new Label();
    readonly Label remaining = new Label(), remainingCaption = new Label(), remainingSecondary=new Label();
    readonly Label counts=new Label(),countsCaption=new Label();
    readonly Label inputCaption = new Label(), outputCaption = new Label(), contextCaption = new Label(), speedCaption = new Label();
    readonly Label close = new Label();
    readonly DisplaySettings display=new DisplaySettings();
    sealed class MetricRow {internal Label caption,value;internal MetricRow(Label c,Label v){caption=c;value=v;}}
    readonly Dictionary<string,MetricRow> rows=new Dictionary<string,MetricRow>();
    readonly List<Label> numericLabels=new List<Label>();
    readonly List<int> rules=new List<int>();
    bool positionLocked=true;
    int freeX,freeY;
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly HudMenuRenderer menuTheme=new HudMenuRenderer();
    readonly Dictionary<Label,FitState> fitStates=new Dictionary<Label,FitState>();
    sealed class FitState {internal string text;internal Size size;internal float nominal,scale;internal FontStyle style;}
    Data data;
    bool reading,quotaReading, dragging, placed;
    string lastError = "";
    DateTime lastRead = DateTime.MinValue, lastStatus = DateTime.MinValue;
    IntPtr codex;
    Native.RECT bounds;
    Point mouseStart, windowStart;
    int offsetLeft = 0, offsetBottom = 64;
    int dockLeft,dockTop,dockBottom,minimumTop,railPixels,layoutPixels,footerGap,effectiveGap,titleExtension;
    DateTime lastDiscovery=DateTime.MinValue;
    bool dockFits=true,compactLayout;
    float uiScale = 1;
    float heightScale=1,textDensity=1;
    Color backgroundLeft=Color.FromArgb(243,242,249),backgroundRight=Color.FromArgb(239,240,246);
    readonly SidebarBackdrop backdrop=new SidebarBackdrop();
    Color ink=Color.FromArgb(58,57,68),muted=Color.FromArgb(131,128,144),accent=Color.FromArgb(103,95,139),separator=Color.FromArgb(223,221,232);
    DateTime lastTheme=DateTime.MinValue;

    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x08000000; if (!preview) p.ExStyle |= 0x00000080; p.Style &= ~(0x00080000|0x00020000|0x00010000); return p; }
    }

    internal Hud(bool previewMode,IntPtr previewTarget)
    {
        preview = previewMode;
        codex = previewTarget;
        Text = Product.Name + " " + Product.Version;
        FormBorderStyle = FormBorderStyle.None;
        ControlBox=false; MinimizeBox=false; MaximizeBox=false; ShowIcon=false;
        ShowInTaskbar = preview;
        TopMost = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        BackColor = backgroundLeft;
        ForeColor = ink;
        Opacity = 0;
        Font = new Font("Microsoft YaHei UI", 9);
        display.Load();
        rows.Add("input",new MetricRow(inputCaption,usage));rows.Add("output",new MetricRow(outputCaption,output));
        rows.Add("context",new MetricRow(contextCaption,context));rows.Add("time",new MetricRow(timeCaption,time));
        rows.Add("counts",new MetricRow(countsCaption,counts));rows.Add("speed",new MetricRow(speedCaption,speed));
        rows.Add("remaining",new MetricRow(remainingCaption,remaining));
        foreach(string id in new[]{"model","cache","duration"})rows.Add(id,new MetricRow(new Label(),new Label()));
        foreach(var row in rows.Values)numericLabels.Add(row.value);numericLabels.AddRange(new[]{capacity,requestInput,requestOutput,spanTime,remainingSecondary});
        using (var g = CreateGraphics()) uiScale = Math.Max(1, g.DpiX / 96f);
        ApplyLayout();
        close.Cursor = Cursors.Hand;
        close.Click += delegate { Close(); };
        var automaticMenu = new ToolStripMenuItem("自动跟随当前会话");
        automaticMenu.Click += delegate
        {
            bool enable = !SessionReader.IsAutomatic();
            if(!enable && data != null) SessionReader.Select(data.sessionId);
            else SessionReader.Automatic(enable);
            data = null; ClearReadings();
            lastRead = DateTime.MinValue; RefreshData();
        };
        menu.Items.Add(automaticMenu);
        var sessionMenu = new ToolStripMenuItem("选择会话");
        menu.Items.Add(sessionMenu);
        var displayMenu=new ToolStripMenuItem("显示数据");menu.Items.Add(displayMenu);
        var orderMenu=new ToolStripMenuItem("数据顺序");menu.Items.Add(orderMenu);
        var lockMenu=new ToolStripMenuItem("解锁，自由拖动");
        lockMenu.Click+=delegate{SetPositionLocked(!positionLocked);};
        menu.Opening += delegate
        {
            automaticMenu.Checked = SessionReader.IsAutomatic();
            lockMenu.Text=positionLocked?"解锁，自由拖动":"锁定到侧边栏";
            BuildDisplayMenus(displayMenu,orderMenu);
            ClearMenu(sessionMenu.DropDownItems);
            string selected = "";
            try { selected = SessionReader.SelectedId(); } catch { }
            foreach (var entry in SessionReader.Recent(15))
            {
                var chosen = entry;
                string shown = String.IsNullOrEmpty(chosen.title) ? chosen.id.Substring(0, 8) : chosen.title;
                if(shown.Length > 28) shown = shown.Substring(0,28) + "…";
                var item = new ToolStripMenuItem(shown);
                item.Checked = !SessionReader.IsAutomatic() && chosen.id == selected;
                item.Click += delegate
                {
                    SessionReader.Select(chosen.id); data = null;
                    ClearReadings(); titleRaw=chosen.title;quota.Text=chosen.title;
                    lastRead = DateTime.MinValue; RefreshData();
                };
                sessionMenu.DropDownItems.Add(item);
            }
            menuTheme.Style(sessionMenu.DropDown,uiScale);
        };
        menu.Items.Add("立即刷新", null, delegate { lastRead = DateTime.MinValue; RefreshData(); });
        menu.Items.Add(lockMenu);
        menu.Items.Add("关于 Codex Lite HUD",null,delegate {MessageBox.Show(this,Product.Name+" "+Product.Version+"\n\n只读本地会话统计，不调用模型。\n数据目录："+Program.DataFolder,Product.Name,MessageBoxButtons.OK,MessageBoxIcon.Information);});
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出悬浮条", null, delegate { Close(); });
        menu.Closed+=delegate {Invalidate(true);};
        menuTheme.Update(backgroundLeft,ink,muted,accent);menuTheme.Style(menu,uiScale);
        ContextMenuStrip = menu;
        foreach (Control c in ReadoutControls())
        {
            c.MouseDown += BeginDrag;
            c.MouseMove += MoveDrag;
            c.MouseUp += EndDrag;
            c.ContextMenuStrip = menu;
            c.MouseEnter += delegate { close.Visible=true; };
            c.MouseLeave += delegate { if(!ClientRectangle.Contains(PointToClient(Cursor.Position)))close.Visible=false; };
        }
        close.MouseLeave += delegate { if(!ClientRectangle.Contains(PointToClient(Cursor.Position)))close.Visible=false; };
        LoadPosition();
        UpdateDragCursor();
        follow.Tick += delegate { Follow(); };
        Shown += delegate { tracking=new WindowTracking(FollowPosition,TargetMoving,QueueFollow);follow.Start();Follow();RefreshData(); };
        FormClosed += delegate { StopTracking(); WriteStatus(false); };
    }

    IEnumerable<Control> ReadoutControls()
    {
        yield return this;yield return brand;yield return tag;yield return quota;yield return capacity;yield return requestInput;yield return requestOutput;yield return spanTime;yield return spanCaption;yield return remainingSecondary;
        foreach(var row in rows.Values){yield return row.caption;yield return row.value;}
    }
    void BuildDisplayMenus(ToolStripMenuItem visibility,ToolStripMenuItem order)
    {
        ClearMenu(visibility.DropDownItems);ClearMenu(order.DropDownItems);
        foreach(string id in display.Order)
        {
            string chosen=id;
            var show=new ToolStripMenuItem(Metrics.Name(id)){Checked=!display.Hidden.Contains(id)};
            show.Enabled=display.Hidden.Contains(id) || display.Count>1;
            show.Click+=delegate{if(!display.Hidden.Remove(chosen))display.Hidden.Add(chosen);DisplayChanged();};
            visibility.DropDownItems.Add(show);
            var metric=new ToolStripMenuItem(Metrics.Name(id));
            var up=new ToolStripMenuItem("上移"){Enabled=display.Order.IndexOf(id)>0};
            var down=new ToolStripMenuItem("下移"){Enabled=display.Order.IndexOf(id)<display.Order.Count-1};
            var first=new ToolStripMenuItem("移到最前"){Enabled=display.Order.IndexOf(id)>0};
            up.Click+=delegate{display.Move(chosen,-1);DisplayChanged();};down.Click+=delegate{display.Move(chosen,1);DisplayChanged();};
            first.Click+=delegate{display.Order.Remove(chosen);display.Order.Insert(0,chosen);DisplayChanged();};
            metric.DropDownItems.AddRange(new ToolStripItem[]{up,down,first});order.DropDownItems.Add(metric);menuTheme.Style(metric.DropDown,uiScale);
        }
        visibility.DropDownItems.Add(new ToolStripSeparator());
        visibility.DropDownItems.Add("恢复默认显示和顺序",null,delegate{display.Reset();DisplayChanged();});
        menuTheme.Style(visibility.DropDown,uiScale);menuTheme.Style(order.DropDown,uiScale);
    }
    static void ClearMenu(ToolStripItemCollection items){foreach(ToolStripItem item in items.Cast<ToolStripItem>().ToArray())item.Dispose();items.Clear();}
    void DisplayChanged(){display.Save();layoutPixels=0;ApplyLayout();if(codex!=IntPtr.Zero)Follow();}
    void SetPositionLocked(bool locked)
    {
        if(positionLocked==locked)return;
        dragging=false;foreach(Control control in ReadoutControls())control.Capture=false;
        if(!locked){freeX=(int)Math.Round((Left-bounds.Left)/uiScale);freeY=(int)Math.Round((Top-bounds.Top)/uiScale);}
        else {offsetLeft=0;offsetBottom=64;}
        positionLocked=locked;UpdateDragCursor();SavePosition();if(codex!=IntPtr.Zero)Follow();
    }
    void UpdateDragCursor()
    {
        foreach(Control control in ReadoutControls())control.Cursor=positionLocked?Cursors.Default:Cursors.SizeAll;
        close.Cursor=Cursors.Hand;
    }

    void StopTracking()
    {
        follow.Stop();
        if(tracking!=null){tracking.Dispose();tracking=null;}
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){StopTracking();follow.Dispose();backdrop.Dispose();menu.Dispose();menuTheme.Dispose();}
        base.Dispose(disposing);
    }
    bool InMotion {get{return targetMoving || DateTime.UtcNow-lastMotion<TimeSpan.FromMilliseconds(140);}}
    void QueueFollow()
    {
        if(followQueued || IsDisposed || !IsHandleCreated)return;
        followQueued=true;
        BeginInvoke(new Action(delegate {followQueued=false;if(!IsDisposed)Follow();}));
    }
    void TargetMoving(bool moving)
    {
        targetMoving=moving;
        if(!moving){lastMotion=DateTime.MinValue;lastRead=DateTime.MinValue;lastTheme=DateTime.MinValue;QueueFollow();}
    }
    void AttachTracking()
    {
        if(tracking==null)return;
        tracking.Attach(codex);
        follow.Interval=tracking.Active ? 200 : 40;
    }
    void RecoverMotion()
    {
        if(!targetMoving)return;
        var state=new Native.GUITHREADINFO {cbSize=(uint)Marshal.SizeOf(typeof(Native.GUITHREADINFO))};
        uint pid;uint thread=Native.GetWindowThreadProcessId(codex,out pid);
        if(thread==0 || !Native.GetGUIThreadInfo(thread,ref state) || (state.flags&2)==0)
        {targetMoving=false;lastMotion=DateTime.MinValue;lastRead=DateTime.MinValue;}
    }
    bool ReadBounds(bool immediate)
    {
        Native.RECT raw;
        if(!Native.GetWindowRect(codex,out raw))return false;
        int w=raw.Right-raw.Left,h=raw.Bottom-raw.Top;
        uint dpi=Native.GetDpiForWindow(codex);
        if(!immediate || geometryTarget!=codex || geometryWidth!=w || geometryHeight!=h || geometryDpi!=dpi)
        {
            Native.RECT visual;
            bool prior=geometryTarget==codex && geometryDpi==dpi;
            if(Native.DwmGetWindowAttribute(codex,9,out visual,Marshal.SizeOf(typeof(Native.RECT)))==0)
            {
                var candidate=new Native.RECT {Left=visual.Left-raw.Left,Top=visual.Top-raw.Top,Right=raw.Right-visual.Right,Bottom=raw.Bottom-visual.Bottom};
                int maximum=(int)Math.Ceiling(32*Math.Max(1,dpi/96d));
                bool valid=candidate.Left>=0 && candidate.Top>=0 && candidate.Right>=0 && candidate.Bottom>=0 && candidate.Left<=maximum && candidate.Top<=maximum && candidate.Right<=maximum && candidate.Bottom<=maximum;
                if(valid)frameInsets=candidate;
                else if(!prior)frameInsets=new Native.RECT();
            }
            else if(!prior)frameInsets=new Native.RECT();
            geometryTarget=codex;geometryWidth=w;geometryHeight=h;geometryDpi=dpi;
        }
        bounds=new Native.RECT {Left=raw.Left+frameInsets.Left,Top=raw.Top+frameInsets.Top,Right=raw.Right-frameInsets.Right,Bottom=raw.Bottom-frameInsets.Bottom};
        return true;
    }
    void MoveToDock()
    {
        int x=dockLeft,y=Math.Max(minimumTop,Math.Min(dockBottom-Height-footerGap,dockBottom-Height-effectiveGap));
        if(!positionLocked)
        {
            x=bounds.Left+Px(freeX);y=bounds.Top+Px(freeY);
            var area=Screen.FromRectangle(new Rectangle(x,y,Width,Height)).WorkingArea;
            x=Math.Max(area.Left,Math.Min(area.Right-Width,x));y=Math.Max(area.Top,Math.Min(area.Bottom-Height,y));
        }
        if(Left!=x || Top!=y)
            Native.SetWindowPos(Handle,IntPtr.Zero,x,y,0,0,0x0010|0x0004|0x0200|0x0001);
    }
    void FollowPosition()
    {
        if(IsDisposed || dragging || !Visible || codex==IntPtr.Zero)return;
        lastMotion=DateTime.UtcNow;
        if(positionUpdating){positionPending=true;return;}
        positionUpdating=true;
        try
        {
            do
            {
                positionPending=false;
                if(!Native.IsIconic(codex) && ReadBounds(true)){UpdateDock();if(dockFits)MoveToDock();}
            }while(positionPending && !IsDisposed);
        }
        finally{positionUpdating=false;}
    }

    int Px(int n) { return (int)Math.Round(n * uiScale); }
    int Py(int n) { return (int)Math.Round(n*uiScale*heightScale); }
    string NameText {get{return data!=null && lastError=="" ? data.sessionTitle : titleRaw;}}
    void ApplyLayout()
    {
        UpdateQuotaWindowCount(QuotaReader.Current(data==null?null:data.accountQuota));
        fitStates.Clear();SuspendLayout();
        int logicalWidth=railPixels>0 ? (int)Math.Round(railPixels/uiScale) : 73;
        int width=railPixels>0?railPixels:Px(logicalWidth);
        nameLayout.Measure(NameText,width,uiScale);
        int natural=display.NaturalHeight+nameLayout.LogicalHeight-28;
        int height=layoutPixels>0 ? layoutPixels : Px(natural);
        heightScale=Math.Min(1,height/(natural*uiScale));
        titleExtension=20+nameLayout.LogicalHeight-28;
        textDensity=Math.Max(.65f,Math.Min(1.3f,logicalWidth/73f)*heightScale);
        ClientSize = new Size(railPixels>0 ? railPixels : Px(logicalWidth),height);
        // Leave the two native edge columns visible for unobscured sampling.
        var oldRegion=Region;Region=new Region(new Rectangle(1,0,Math.Max(1,Width-2),Height));if(oldRegion!=null)oldRegion.Dispose();
        backdrop.Resize(ClientSize);lastTheme=DateTime.MinValue;
        compactLayout=height<Px((int)Math.Max(164,Math.Min(370,display.NaturalHeight*.76)));
        SetLabel(brand,"CLH",6,5,logicalWidth-12,18,accent,9,FontStyle.Bold);
        SetLabel(tag, String.IsNullOrEmpty(tag.Text) ? "自动" : tag.Text, 6, 26, logicalWidth-12, 16, muted, 7.5f, FontStyle.Regular);
        SetLabel(close, "×", logicalWidth-20, 6, 18, 18, muted, 9, FontStyle.Regular);
        close.AutoEllipsis=false; close.UseCompatibleTextRendering=true;
        SetLabel(quota,nameLayout.Wrapped,5,49,logicalWidth-10,nameLayout.LogicalHeight,accent,8.5f,FontStyle.Regular);
        quota.UseMnemonic=false;
        FitTitle();
        rules.Clear();int y=69+titleExtension;
        foreach(string id in display.Order)
        {
            var row=rows[id];bool visible=!display.Hidden.Contains(id);
            string caption=id=="time"?"用时":Metrics.Name(id)+(id=="input" || id=="output"?" token":"");
            SetLabel(row.caption,caption,4,y,logicalWidth-8,16,muted,8,FontStyle.Regular);
            SetLabel(row.value,String.IsNullOrEmpty(row.value.Text)?"—":row.value.Text,4,y+18,logicalWidth-8,id=="time"?26:24,id=="remaining" || id=="context"?accent:ink,id=="time"?12:10,FontStyle.Bold);
            row.caption.Visible=visible && !compactLayout;row.value.Visible=visible;
            if(id=="input" || id=="output")
            {
                var request=id=="input"?requestInput:requestOutput;
                SetLabel(request,String.IsNullOrEmpty(request.Text)?"本次 —":request.Text,4,y+18,logicalWidth-8,24,ink,9,FontStyle.Bold);
                row.value.SetBounds(Px(4),Py(y+42),Px(logicalWidth-8),Py(24));
                request.Visible=visible;
            }
            if(id=="context")SetLabel(capacity,String.IsNullOrEmpty(capacity.Text)?"— / —":capacity.Text,5,y+42,logicalWidth-10,16,muted,7,FontStyle.Regular);
            if(id=="remaining")
            {
                SetLabel(remainingSecondary,remainingSecondary.Text,4,y+42,logicalWidth-8,24,accent,10,FontStyle.Bold);
                // Shared pixel boundary avoids a one-pixel overlap from
                // independently rounded positions/heights at fractional DPI.
                row.value.Height=Math.Max(1,Math.Min(row.value.Height,remainingSecondary.Top-row.value.Top));
                remainingSecondary.Visible=visible && display.QuotaWindowCount>1;
            }
            if(id=="time")
            {
                SetLabel(spanCaption,"跨度",4,y+48,logicalWidth-8,16,muted,8,FontStyle.Regular);
                SetLabel(spanTime,String.IsNullOrEmpty(spanTime.Text)?"—":spanTime.Text,4,y+66,logicalWidth-8,26,ink,12,FontStyle.Bold);
                spanCaption.Visible=visible && !compactLayout;spanTime.Visible=visible;
            }
            if(visible){rules.Add(y-8);y+=display.Height(id);}
        }
        capacity.Visible=!display.Hidden.Contains("context") && !compactLayout;
        menuTheme.Style(menu,uiScale);
        close.Visible=false;
        quota.Visible=!compactLayout;
        if(compactLayout)
        {
            brand.SetBounds(Px(6),Px(2),Width-Px(12),Px(14));ReplaceFont(brand,10.5f*uiScale,FontStyle.Bold);
            tag.SetBounds(Px(6),Px(17),Width-Px(12),Px(12));
            ReplaceFont(tag,9f*uiScale,FontStyle.Regular);
            close.SetBounds(Width-Px(20),Px(1),Px(18),Px(16));
            var values=new List<Label>();
            foreach(string id in display.Visible)
            {
                if(id=="input")values.Add(requestInput);else if(id=="output")values.Add(requestOutput);
                values.Add(rows[id].value);
                if(id=="time")values.Add(spanTime);
                if(id=="remaining" && display.QuotaWindowCount>1)values.Add(remainingSecondary);
            }
            int start=Px(34),step=(Height-Px(39))/values.Count;
            for(int i=0;i<values.Count;i++)
            {
                values[i].SetBounds(Px(5),start+i*step,Width-Px(10),step);
                ReplaceFont(values[i],Math.Min(10.5f*uiScale,step*.65f),FontStyle.Regular);
            }
        }
        if(data!=null)RenderReadings();
        else ClearReadings();
        ResumeLayout(false);
        Invalidate(true);
    }
    void ReplaceFont(Label label,float pixels,FontStyle style)
    {
        if(label.Font.Unit==GraphicsUnit.Pixel && Math.Abs(label.Font.Size-pixels)<.01 && label.Font.Style==style){label.Tag=pixels;return;}
        var previous=label.Font;label.Font=new Font("Microsoft YaHei UI",pixels,style,GraphicsUnit.Pixel);label.Tag=pixels;previous.Dispose();
    }
    void FitTitle()
    {
        for(int i=0;i<20;i++)
        {
            var raw=TextRenderer.MeasureText(NameText,quota.Font,new Size(quota.Width,32767),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
            var wrapped=TextRenderer.MeasureText(quota.Text,quota.Font,new Size(quota.Width,32767),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
            if(Math.Max(raw.Height,wrapped.Height)<=quota.Height || quota.Font.Size<=7.2f*uiScale)break;
            ReplaceFont(quota,quota.Font.Size-.3f*uiScale,FontStyle.Regular);
        }
    }
    void RenderReadings()
    {
        if(data==null)return;
        usage.Text=(compactLayout?"对话入 ":"对话 ")+Short(data.input);
        output.Text=(compactLayout?"对话出 ":"对话 ")+Short(data.output);
        requestInput.Text=(compactLayout?"本次入 ":"本次 ")+(data.lastInput>0 || data.lastOutput>0?Short(data.lastInput):"—");
        requestOutput.Text=(compactLayout?"本次出 ":"本次 ")+(data.lastInput>0 || data.lastOutput>0?Short(data.lastOutput):"—");
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
        FitNumbers();
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
        label.Text=name+" "+(window==null?"—":window.remaining.ToString("0.#",CultureInfo.InvariantCulture)+"%");
        label.ForeColor=window!=null && window.remaining<=10 ? Color.FromArgb(185,94,90) : accent;
    }
    void ClearReadings()
    {
        tag.Text = SessionReader.IsAutomatic() ? "自动" : "固定";
        titleRaw="识别会话";quota.Text = titleRaw; usage.Text = output.Text = context.Text = speed.Text = "—";
        usage.Text=compactLayout?"对话入 —":"对话 —";output.Text=compactLayout?"对话出 —":"对话 —";
        requestInput.Text=compactLayout?"本次入 —":"本次 —";requestOutput.Text=compactLayout?"本次出 —":"本次 —";
        if(compactLayout){context.Text="占 —";speed.Text="速 —";}
        capacity.Text = "— / —";
        counts.Text=compactLayout?"轮—/压—":"— / —";
        time.Text=compactLayout?"用 —":"—";spanTime.Text=compactLayout?"跨 —":"—";
        foreach(string id in new[]{"model","cache","duration"})rows[id].value.Text="—";
        RenderRemaining();
        Invalidate(true);
    }
    void SetLabel(Label label, string text, int x, int y, int w, int h, Color color, float size, FontStyle style)
    {
        label.Text = text; label.SetBounds(Px(x), Py(y), Px(w), Py(h));
        label.ForeColor = color; label.BackColor = Color.Transparent;
        var previous = label.Parent == this ? label.Font : null;
        // Pixel fonts prevent the primary monitor's DPI from enlarging text
        // inside a narrower rail on another monitor.
        float pixels=Math.Max(7.2f*uiScale,size*96f/72f*uiScale*textDensity);
        if(previous==null || previous.Unit!=GraphicsUnit.Pixel || Math.Abs(previous.Size-pixels)>.01 || previous.Style!=style)
        {
            label.Font=new Font("Microsoft YaHei UI",pixels,style,GraphicsUnit.Pixel);
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
        nameLayout.Measure(NameText,width,targetScale);
        int desired=(int)Math.Round((display.NaturalHeight+nameLayout.LogicalHeight-28)*targetScale);
        if(!positionLocked){available=Screen.FromHandle(codex).WorkingArea.Height-(int)Math.Round(16*targetScale);dockFits=true;}
        int height=Math.Max((int)Math.Round(164*targetScale),Math.Min(desired,available));
        if(Math.Abs(uiScale-targetScale)>.01 || railPixels!=width || layoutPixels!=height)
        {
            uiScale=targetScale;railPixels=width;layoutPixels=height;ApplyLayout();
        }
        else if(quota.Text!=nameLayout.Wrapped)quota.Text=nameLayout.Wrapped;
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
        foreach(var item in new[]{tag,close,inputCaption,outputCaption,contextCaption,speedCaption,remainingCaption,countsCaption,timeCaption,spanCaption,capacity})item.ForeColor=muted;
        foreach(var item in new[]{usage,output,requestInput,requestOutput,speed,counts,time,spanTime})item.ForeColor=ink;
        foreach(string id in new[]{"model","cache","duration"}){rows[id].caption.ForeColor=muted;rows[id].value.ForeColor=ink;}
        brand.ForeColor=accent;
        quota.ForeColor=accent;context.ForeColor=data!=null && data.contextPercent.GetValueOrDefault()>=90 ? Color.FromArgb(185,94,90) : accent;
        RenderRemaining();
        Invalidate(true);
    }

    void FitNumbers()
    {
        foreach(var label in numericLabels)
        {
            float desired=label.Tag is float ? (float)label.Tag : label.Font.Size;
            FitState state;
            if(fitStates.TryGetValue(label,out state) && state.text==label.Text && state.size==label.Size && state.nominal==desired && state.style==label.Font.Style && state.scale==uiScale)continue;
            var next=new FitState {text=label.Text,size=label.Size,nominal=desired,style=label.Font.Style,scale=uiScale};
            for(int attempt=0;attempt<18;attempt++)
            {
                using(var font=new Font("Microsoft YaHei UI",desired,label.Font.Style,GraphicsUnit.Pixel))
                {
                    var size=TextRenderer.MeasureText(label.Text,font,new Size(32767,32767),TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
                    if((size.Width<=label.Width && size.Height<=label.Height) || desired<=7.2f*uiScale)break;
                }
                desired-=.3f*uiScale;
            }
            if(Math.Abs(label.Font.Size-desired)>.05)
            {
                var old=label.Font;label.Font=new Font("Microsoft YaHei UI",desired,old.Style,GraphicsUnit.Pixel);old.Dispose();
            }
            fitStates[label]=next;
        }
    }

    static readonly Dictionary<uint, bool> KnownProcesses = new Dictionary<uint, bool>();
    static bool IsMainWindow(IntPtr window)
    {
        if(window==IntPtr.Zero || !Native.IsWindow(window) || !Native.IsWindowVisible(window) || Native.IsIconic(window) || Native.GetWindow(window,4)!=IntPtr.Zero)return false;
        // Codex pets share the main process and can have a full-monitor
        // transparent surface. Process identity and size alone are insufficient.
        long extended=Native.GetWindowLongPtr(window,-20).ToInt64();
        if((extended&(0x80|0x20))!=0 || (Native.GetWindowLongPtr(window,-16).ToInt64()&0x40000000)!=0)return false;
        Native.RECT rect;
        if(!Native.GetWindowRect(window,out rect))return false;
        double scale=Math.Max(1,Native.GetDpiForWindow(window)/96d);
        return rect.Right-rect.Left>=300*scale && rect.Bottom-rect.Top>=250*scale;
    }
    void DiscoverWindow()
    {
        if(DateTime.UtcNow-lastDiscovery<TimeSpan.FromSeconds(2))return;
        lastDiscovery=DateTime.UtcNow;
        IntPtr found=IntPtr.Zero;
        Native.EnumWindows(delegate(IntPtr window,IntPtr unused)
        {
            uint pid;Native.GetWindowThreadProcessId(window,out pid);
            if(!IsCodex(pid) || !IsMainWindow(window))return true;
            found=window;return false;
        },IntPtr.Zero);
        if(found!=IntPtr.Zero)codex=found;
    }
    void SyncStacking()
    {
        if(preview || !Visible || codex==IntPtr.Zero)return;
        var above=Native.GetWindow(codex,3);
        if(above==Handle)return;
        if(above!=IntPtr.Zero && (Native.GetWindowLongPtr(above,-20).ToInt64()&8)!=0)above=IntPtr.Zero;
        Native.SetWindowPos(Handle,above,0,0,0,0,0x0010|0x0200|0x0001|0x0002);
    }
    static bool IsCodex(uint id)
    {
        bool known;
        if (KnownProcesses.TryGetValue(id, out known)) return known;
        try
        {
            using (var p = Process.GetProcessById((int)id))
            {
                string path = p.MainModule.FileName;
                known = p.ProcessName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) &&
                    (path.IndexOf("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("\\OpenAI\\Codex\\", StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }
        catch { known = false; }
        if (KnownProcesses.Count > 100) KnownProcesses.Clear();
        KnownProcesses[id] = known;
        return known;
    }
    void ConsiderForeground(IntPtr foreground,uint pid)
    {
        if(pid==ownPid || !IsCodex(pid))return;
        var target=Native.GetAncestor(foreground,2);
        if(!IsMainWindow(target))return;
        if(target!=codex){codex=target;targetMoving=false;lastMotion=DateTime.MinValue;}
    }

    void Follow()
    {
        if (preview)
        {
            if(codex == IntPtr.Zero)DiscoverWindow();
            AttachTracking();
            if(codex!=IntPtr.Zero && ReadBounds(InMotion))
            {
                UpdateDock();if(!dragging)MoveToDock();
            }
            else if(!placed)
            {
                var area=Screen.PrimaryScreen.WorkingArea;
                Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);
            }
            if(!placed || !Visible){Opacity=1;Show();placed=true;}
            RefreshIdle();
            return;
        }
        var foreground = Native.GetForegroundWindow();
        uint pid; Native.GetWindowThreadProcessId(foreground, out pid);
        ConsiderForeground(foreground,pid);
        if(codex==IntPtr.Zero || !Native.IsWindow(codex) || !Native.IsWindowVisible(codex))DiscoverWindow();
        AttachTracking();
        bool show = codex != IntPtr.Zero && Native.IsWindow(codex) && Native.IsWindowVisible(codex) && !Native.IsIconic(codex);
        if (!show)
        {
            targetMoving=false;lastMotion=DateTime.MinValue;
            if (Visible) { Hide(); WriteStatus(false); }
            return;
        }
        if (!ReadBounds(InMotion)) return;
        UpdateDock();
        if(!dockFits){Hide();return;}
        if (bounds.Right - bounds.Left < Width + 20 || bounds.Bottom - bounds.Top < Height + 20) { Hide(); return; }
        if (!dragging)MoveToDock();
        if (!Visible || !placed) { Opacity = 1; Show(); placed = true; WriteStatus(true); }
        SyncStacking();
        RefreshIdle();
    }
    void RefreshIdle()
    {
        RecoverMotion();
        if(InMotion)return;
        SampleTheme();
        if(QuotaLogs.Changed && !quotaReading)RefreshAccountQuota();
        if (!reading && !menu.Visible && DateTime.UtcNow - lastRead >= TimeSpan.FromSeconds(1)) RefreshData();
        if (DateTime.UtcNow - lastStatus > TimeSpan.FromSeconds(30)) WriteStatus(true);
    }

    async void RefreshAccountQuota()
    {
        if(quotaReading || IsDisposed)return;
        quotaReading=true;
        try
        {
            await Task.Run(()=>QuotaLogs.Refresh());
            if(IsDisposed)return;
            var snapshot=QuotaReader.Current();
            if(data==null){RenderRemaining();return;}
            if(data.accountQuota!=snapshot){data.accountQuota=snapshot;RenderRemaining();}
        }
        finally{quotaReading=false;}
    }

    async void RefreshData()
    {
        if (reading || IsDisposed) return;
        reading = true;
        bool changed=false,initial=data==null;
        try
        {
            bool automatic = SessionReader.IsAutomatic();
            IntPtr window = codex;
            var next = await Reader.ReadCurrent(window,automatic);
            if (IsDisposed) return;
            if(InMotion)return;
            if(automatic != SessionReader.IsAutomatic() || (automatic && window != codex) || (!automatic && next.sessionId != SessionReader.SelectedId())) return;
            changed=!next.SameDisplay(data) || lastError!="";
            data=next;lastError="";
            RenderTime();
            if(!changed)return;
            tag.Text = automatic ? "自动" : "固定";
            if(codex!=IntPtr.Zero){UpdateDock();if(!dragging)MoveToDock();}
            quota.Text = nameLayout.Wrapped;
            quota.ForeColor = accent;
            RenderReadings();
            context.ForeColor = data.contextPercent.GetValueOrDefault() >= 90 ? Color.FromArgb(185,94,90) : accent;
            Invalidate(true);
        }
        catch (Exception ex)
        {
            RenderRemaining();changed=lastError!=ex.Message;lastError=ex.Message;
            if(!changed)return;
            if(SessionReader.IsAutomatic()) { data = null; ClearReadings(); quota.Text = "待识别"; capacity.Text = "未识别"; }
            else if (data == null) { quota.Text = "等待读数"; capacity.Text = "未记录"; }
            else { quota.Text = data.sessionTitle + " · 旧读数"; }
            titleRaw=quota.Text;
            quota.ForeColor = Color.FromArgb(189, 166, 117);
        }
        finally
        {
            reading=false;lastRead=DateTime.UtcNow;
            if(!IsDisposed && !InMotion && ((initial && changed) || DateTime.UtcNow-lastStatus>TimeSpan.FromSeconds(changed?5:30)))WriteStatus(Visible);
        }
    }

    static string Short(long n)
    {
        if (n >= 1000000000) return (n / 1000000000d).ToString(n>=100000000000 ? "0" : "0.#", CultureInfo.InvariantCulture) + "B";
        if (n >= 1000000) return (n / 1000000d).ToString(n>=100000000 ? "0" : "0.#", CultureInfo.InvariantCulture) + "M";
        if (n >= 1000) return (n / 1000d).ToString(n>=100000 ? "0" : "0.#", CultureInfo.InvariantCulture) + "K";
        return n.ToString();
    }

    void BeginDrag(object sender, MouseEventArgs e)
    {
        if (positionLocked || e.Button != MouseButtons.Left) return;
        dragging = true; mouseStart = Cursor.Position; windowStart = Location; ((Control)sender).Capture = true;
    }
    void MoveDrag(object sender, MouseEventArgs e)
    {
        if (!dragging) return;
        var at = Cursor.Position;
        Location = DragLocation(at);
    }
    Point DragLocation(Point at){return new Point(windowStart.X+at.X-mouseStart.X,windowStart.Y+at.Y-mouseStart.Y);}
    void EndDrag(object sender, MouseEventArgs e)
    {
        if (!dragging) return;
        dragging = false; ((Control)sender).Capture = false;
        freeX=(int)Math.Round((Left-bounds.Left)/uiScale);freeY=(int)Math.Round((Top-bounds.Top)/uiScale);
        SavePosition(); Follow();
    }
    void LoadPosition()
    {
        try
        {
            var p=SessionReader.Map(Program.Json.DeserializeObject(File.ReadAllText(Path.Combine(Program.DataFolder,"position.json"))));
            if(SessionReader.At(p,"bottom")!=null)offsetBottom=Math.Max(64,(int)SessionReader.Count(SessionReader.At(p,"bottom")));
            positionLocked=!Object.Equals(SessionReader.At(p,"locked"),false);
            freeX=(int)SessionReader.Count(SessionReader.At(p,"freeX"));freeY=(int)SessionReader.Count(SessionReader.At(p,"freeY"));
        }catch { }
    }
    void SavePosition()
    {
        try { File.WriteAllText(Path.Combine(Program.DataFolder, "position.json"), Program.Json.Serialize(new { left = offsetLeft, bottom = offsetBottom,locked=positionLocked,freeX=freeX,freeY=freeY })); } catch { }
    }
    void WriteStatus(bool shown)
    {
        lastStatus = DateTime.UtcNow;
        try { File.WriteAllText(Path.Combine(Program.DataFolder, "status.json"), Program.Json.Serialize(new { visible = shown, nativeVisible = Native.IsWindowVisible(Handle), preview = preview, targetWindow = codex.ToInt64(), followMode=tracking!=null && tracking.Active ? "native-window-events" : "timer-fallback",positionEvents=tracking==null?0:tracking.PositionEvents,x = Left, y = Top, width = Width, height = Height,nativeBackdrop=backdrop.Ready, backgroundLeft=ColorTranslator.ToHtml(backgroundLeft),backgroundRight=ColorTranslator.ToHtml(backgroundRight),error = lastError, data = data })); } catch { }
    }
}

static class Native
{
    const int DwmVisibleFrameBorderThickness=37;
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct GUITHREADINFO
    {
        internal uint cbSize,flags;
        internal IntPtr active,focus,capture,menuOwner,moveSize,caret;
        internal RECT caretRect;
    }
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread,ref GUITHREADINFO state);
    [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
    internal delegate bool WindowEnum(IntPtr window,IntPtr unused);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(WindowEnum callback,IntPtr unused);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window,uint relation);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out RECT rect, int size);
    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] static extern int DwmGetBorder(IntPtr window,int attribute,out uint value,int size);
    internal static int VisibleBorder(IntPtr window)
    {
        if(window==IntPtr.Zero || IsZoomed(window))return 0;
        uint value;return DwmGetBorder(window,DwmVisibleFrameBorderThickness,out value,4)==0 && value<=8?(int)value:0;
    }
}
