using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// A cached surface reconstructed from the native rail's four visible edges.
// One small screen copy provides the four edges; central pixels are ignored.
sealed class SidebarBackdrop : IDisposable
{
    Color[] top,bottom,left,right;
    Bitmap image,capture;
    internal bool Ready {get{return image!=null;}}
    static bool Same(Color[] a,Color[] b)
    {
        if(a==null || b==null || a.Length!=b.Length)return false;
        for(int i=0;i<a.Length;i++)if(a[i].ToArgb()!=b[i].ToArgb())return false;
        return true;
    }
    internal bool Update(Color[] t,Color[] b,Color[] l,Color[] r,Size size)
    {
        if(Same(top,t) && Same(bottom,b) && Same(left,l) && Same(right,r) && image!=null && image.Size==size)return false;
        top=t;bottom=b;left=l;right=r;Render(size);return true;
    }
    internal void Resize(Size size){if(top!=null && (image==null || image.Size!=size))Render(size);}
    internal void Capture(Point location,Size size,out Color[] t,out Color[] b,out Color[] l,out Color[] r)
    {
        var captureSize=new Size(size.Width,size.Height+2);
        if(capture==null || capture.Size!=captureSize){if(capture!=null)capture.Dispose();capture=new Bitmap(captureSize.Width,captureSize.Height);}
        using(var graphics=Graphics.FromImage(capture))graphics.CopyFromScreen(location.X,location.Y-1,0,0,captureSize,CopyPixelOperation.SourceCopy);
        t=new Color[size.Width];b=new Color[size.Width];l=new Color[size.Height];r=new Color[size.Height];
        for(int x=0;x<size.Width;x++){t[x]=capture.GetPixel(x,0);b[x]=capture.GetPixel(x,size.Height+1);}
        for(int y=0;y<size.Height;y++){l[y]=capture.GetPixel(0,y+1);r[y]=capture.GetPixel(size.Width-1,y+1);}
    }
    static int Channel(double value){return Math.Max(0,Math.Min(255,(int)Math.Round(value)));}
    static double Blend(double a,double b,double t){return a+(b-a)*t;}
    void Render(Size size)
    {
        if(size.Width<2 || size.Height<2)return;
        if(image==null || image.Size!=size){if(image!=null)image.Dispose();image=new Bitmap(size.Width,size.Height,PixelFormat.Format32bppPArgb);}
        var locked=image.LockBits(new Rectangle(Point.Empty,size),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
        // One short row avoids full-image managed allocations on the LOH.
        var row=new int[size.Width];
        try
        {
            for(int y=0;y<size.Height;y++)
            {
                double v=y/(double)(size.Height-1);var a=left[(int)Math.Round(v*(left.Length-1))];var b=right[(int)Math.Round(v*(right.Length-1))];
                double cr0=Blend(top[0].R,bottom[0].R,v),cr1=Blend(top[top.Length-1].R,bottom[bottom.Length-1].R,v);
                double cg0=Blend(top[0].G,bottom[0].G,v),cg1=Blend(top[top.Length-1].G,bottom[bottom.Length-1].G,v);
                double cb0=Blend(top[0].B,bottom[0].B,v),cb1=Blend(top[top.Length-1].B,bottom[bottom.Length-1].B,v);
                for(int x=0;x<size.Width;x++)
                {
                    double u=x/(double)(size.Width-1);var t=top[(int)Math.Round(u*(top.Length-1))];var d=bottom[(int)Math.Round(u*(bottom.Length-1))];
                    int red=Channel(Blend(t.R,d.R,v)+Blend(a.R,b.R,u)-Blend(cr0,cr1,u));
                    int green=Channel(Blend(t.G,d.G,v)+Blend(a.G,b.G,u)-Blend(cg0,cg1,u));
                    int blue=Channel(Blend(t.B,d.B,v)+Blend(a.B,b.B,u)-Blend(cb0,cb1,u));
                    row[x]=unchecked((int)0xff000000)|(red<<16)|(green<<8)|blue;
                }
                Marshal.Copy(row,0,IntPtr.Add(locked.Scan0,y*locked.Stride),row.Length);
            }
        }
        finally{image.UnlockBits(locked);}
    }
    internal void Paint(Graphics graphics){if(image!=null)graphics.DrawImageUnscaled(image,0,0);}
    public void Dispose(){if(image!=null){image.Dispose();image=null;}if(capture!=null){capture.Dispose();capture=null;}}
}
