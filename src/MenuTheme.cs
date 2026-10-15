using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// Shared by the root menu and its drop-downs. Painting creates no fonts;
// their small DPI-specific cache is released with the HUD.
sealed class HudMenuRenderer : ToolStripRenderer, IDisposable
{
    readonly Dictionary<int,Font> fonts=new Dictionary<int,Font>();
    readonly List<WeakReference> menus=new List<WeakReference>();
    Color background=Color.FromArgb(243,242,249),ink=Color.FromArgb(58,57,68);
    Color muted=Color.FromArgb(131,128,144),accent=Color.FromArgb(103,95,139);
    Color border=Color.FromArgb(222,220,232),selection=Color.FromArgb(230,227,240);
    bool disposed;

    internal void Update(Color backgroundColor,Color inkColor,Color mutedColor,Color accentColor)
    {
        if(disposed)return;
        background=backgroundColor;ink=inkColor;muted=mutedColor;accent=accentColor;
        border=Blend(background,ink,.13);selection=Blend(background,accent,.14);
        for(int i=menus.Count-1;i>=0;i--)
        {
            var menu=menus[i].Target as ToolStripDropDown;
            if(menu==null || menu.IsDisposed){menus.RemoveAt(i);continue;}
            menu.BackColor=background;menu.ForeColor=ink;menu.Invalidate(true);
        }
    }

    internal void Style(ToolStripDropDown menu,float scale)
    {
        if(disposed || menu==null || menu.IsDisposed)return;
        scale=Math.Max(.75f,Math.Min(4f,scale));
        int key=(int)Math.Round(scale*100);
        Font font;
        if(!fonts.TryGetValue(key,out font))
        {
            font=new Font("Microsoft YaHei UI",12f*scale,FontStyle.Regular,GraphicsUnit.Pixel);
            fonts.Add(key,font);
        }
        menu.Renderer=this;menu.Font=font;
        menu.ShowItemToolTips=false;
        menu.BackColor=background;menu.ForeColor=ink;
        menu.DropShadowEnabled=false;
        menu.Padding=new Padding(Px(4,scale),Px(5,scale),Px(4,scale),Px(5,scale));
        var list=menu as ToolStripDropDownMenu;
        if(list!=null){list.ShowImageMargin=false;list.ShowCheckMargin=true;}
        foreach(ToolStripItem item in menu.Items)
            if(!(item is ToolStripSeparator))item.Padding=new Padding(Px(7,scale),Px(4,scale),Px(10,scale),Px(4,scale));
        bool tracked=false;
        for(int i=menus.Count-1;i>=0;i--)
        {
            var existing=menus[i].Target as ToolStripDropDown;
            if(existing==null || existing.IsDisposed)menus.RemoveAt(i);
            else if(Object.ReferenceEquals(existing,menu))tracked=true;
        }
        if(!tracked)menus.Add(new WeakReference(menu));
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using(var brush=new SolidBrush(background))e.Graphics.FillRectangle(brush,e.ToolStrip.ClientRectangle);
    }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var r=e.ToolStrip.ClientRectangle;
        if(r.Width<2 || r.Height<2)return;
        using(var pen=new Pen(border))e.Graphics.DrawRectangle(pen,0,0,r.Width-1,r.Height-1);
    }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if(!e.Item.Enabled || (!e.Item.Selected && !e.Item.Pressed))return;
        float scale=e.Item.Font.Size/12f;
        var r=new RectangleF(2,1,Math.Max(0,e.Item.Width-4),Math.Max(0,e.Item.Height-2));
        if(r.Width<1 || r.Height<1)return;
        var smoothing=e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var shape=Rounded(r,Math.Min(4f*scale,Math.Min(r.Width,r.Height)/2)))
        using(var brush=new SolidBrush(selection))e.Graphics.FillPath(brush,shape);
        e.Graphics.SmoothingMode=smoothing;
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        TextRenderer.DrawText(e.Graphics,e.Text,e.TextFont,e.TextRectangle,e.Item.Enabled ? ink : muted,e.TextFormat);
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor=e.Item.Enabled && (e.Item.Selected || e.Item.Pressed) ? accent : muted;
        base.OnRenderArrow(e);
    }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r=e.ImageRectangle;
        if(r.Width<3 || r.Height<3)return;
        var smoothing=e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        float side=Math.Min(r.Width,r.Height),x=r.Left+(r.Width-side)/2f,y=r.Top+(r.Height-side)/2f;
        using(var pen=new Pen(e.Item.Enabled ? accent : muted,Math.Max(1.4f,side/8f)))
        {
            pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
            var item=e.Item as ToolStripMenuItem;
            if(item!=null && item.CheckState==CheckState.Indeterminate)e.Graphics.DrawLine(pen,x+side*.25f,y+side*.5f,x+side*.75f,y+side*.5f);
            else e.Graphics.DrawLines(pen,new[]{new PointF(x+side*.20f,y+side*.51f),new PointF(x+side*.42f,y+side*.72f),new PointF(x+side*.81f,y+side*.28f)});
        }
        e.Graphics.SmoothingMode=smoothing;
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        float scale=e.Item.Font.Size/12f;
        int inset=Px(10,scale),y=e.Item.Height/2;
        if(e.Item.Width<=inset*2)return;
        using(var pen=new Pen(border))e.Graphics.DrawLine(pen,inset,y,e.Item.Width-inset-1,y);
    }

    static int Px(int value,float scale){return Math.Max(1,(int)Math.Round(value*scale));}
    static Color Blend(Color a,Color b,double amount)
    {
        return Color.FromArgb((int)Math.Round(a.R+(b.R-a.R)*amount),(int)Math.Round(a.G+(b.G-a.G)*amount),(int)Math.Round(a.B+(b.B-a.B)*amount));
    }
    static GraphicsPath Rounded(RectangleF r,float radius)
    {
        var path=new GraphicsPath();
        if(radius<.5f){path.AddRectangle(r);return path;}
        float diameter=radius*2;
        path.AddArc(r.Left,r.Top,diameter,diameter,180,90);
        path.AddArc(r.Right-diameter,r.Top,diameter,diameter,270,90);
        path.AddArc(r.Right-diameter,r.Bottom-diameter,diameter,diameter,0,90);
        path.AddArc(r.Left,r.Bottom-diameter,diameter,diameter,90,90);
        path.CloseFigure();return path;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        foreach(var font in fonts.Values)font.Dispose();
        fonts.Clear();menus.Clear();
    }
}
