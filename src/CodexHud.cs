using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

sealed partial class Hud : Form
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
    readonly Label sessionTitle = new Label();
    readonly Label usage = new Label(), tag = new Label(), brand=new Label(),context = new Label(), speed = new Label();
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
    readonly Dictionary<string,MetricRow> rows=new Dictionary<string,MetricRow>();
    readonly List<Label> numericLabels=new List<Label>(),bodyLabels=new List<Label>();
    readonly List<int> rules=new List<int>();
    bool positionLocked=true;
    int freeX,freeY;
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly HudMenuRenderer menuTheme=new HudMenuRenderer();
    readonly TypographyFit numberFit=new TypographyFit(),bodyFit=new TypographyFit();
    Data data;
    bool reading,quotaReading, dragging, placed;
    string lastError = "";
    DateTime lastRead = DateTime.MinValue, lastStatus = DateTime.MinValue;
    IntPtr codex;
    Native.RECT bounds;
    Point mouseStart, windowStart;
    int offsetBottom = 64;
    int dockLeft,dockTop,dockBottom,minimumTop,railPixels,layoutPixels,footerGap,effectiveGap,laidOutTitleHeight;
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
        RegisterMetric("input",inputCaption,usage,requestInput);RegisterMetric("output",outputCaption,output,requestOutput);
        RegisterMetric("context",contextCaption,context,capacity);RegisterMetric("time",timeCaption,time,spanTime,spanCaption);
        RegisterMetric("counts",countsCaption,counts);RegisterMetric("speed",speedCaption,speed);
        RegisterMetric("remaining",remainingCaption,remaining,remainingSecondary);
        foreach(var definition in Metrics.Definitions)
            if(!rows.ContainsKey(definition.Id))RegisterMetric(definition.Id,new Label(),new Label());
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
            data = null; ResetSessionDisplay();
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
                    ResetSessionDisplay(chosen.title);
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
        yield return this;yield return brand;yield return tag;yield return sessionTitle;
        foreach(var row in rows.Values)foreach(var slot in row.Slots)yield return slot.Label;
    }
    void RegisterMetric(string id,Label caption,Label value,Label secondary=null,Label secondaryCaption=null)
    {rows.Add(id,new MetricRow(Metrics.Find(id),caption,value,secondary,secondaryCaption));}
    void ResetSessionDisplay(string title="识别会话")
    {
        titleRaw=String.IsNullOrEmpty(title)?"识别会话":title;
        sessionTitle.Text=titleRaw;
        ClearReadings();
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
        else offsetBottom=64;
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
        try { File.WriteAllText(Path.Combine(Program.DataFolder, "position.json"), Program.Json.Serialize(new { bottom = offsetBottom,locked=positionLocked,freeX=freeX,freeY=freeY })); } catch { }
    }
}
