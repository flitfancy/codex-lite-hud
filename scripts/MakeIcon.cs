using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class MakeIcon
{
    static GraphicsPath Round(float x,float y,float w,float h,float r)
    {
        var p=new GraphicsPath();float d=r*2;
        p.AddArc(x,y,d,d,180,90);p.AddArc(x+w-d,y,d,d,270,90);p.AddArc(x+w-d,y+h-d,d,d,0,90);p.AddArc(x,y+h-d,d,d,90,90);p.CloseFigure();return p;
    }
    static void Main(string[] args)
    {
        var frames=new List<byte[]>();var sizes=new[]{16,32,48,64,256};
        foreach(int size in sizes)
        using(var image=new Bitmap(size,size))
        using(var g=Graphics.FromImage(image))
        {
            g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(size/64f,size/64f);
            using(var shape=Round(9,2,46,60,11))using(var fill=new SolidBrush(Color.FromArgb(237,234,246)))using(var pen=new Pen(Color.FromArgb(211,204,229),1.5f)){g.FillPath(fill,shape);g.DrawPath(pen,shape);}
            foreach(var r in new[]{new RectangleF(18,16,22,4),new RectangleF(18,28,28,4),new RectangleF(18,40,17,4)})
                using(var shape=Round(r.X,r.Y,r.Width,r.Height,2))using(var fill=new SolidBrush(Color.FromArgb(116,102,159)))g.FillPath(fill,shape);
            using(var fill=new SolidBrush(Color.FromArgb(118,168,151)))g.FillEllipse(fill,41,48,8,8);
            using(var stream=new MemoryStream()){image.Save(stream,ImageFormat.Png);frames.Add(stream.ToArray());}
        }
        using(var stream=File.Create(args[0]))using(var writer=new BinaryWriter(stream))
        {
            writer.Write((short)0);writer.Write((short)1);writer.Write((short)sizes.Length);int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++){writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((short)1);writer.Write((short)32);writer.Write(frames[i].Length);writer.Write(offset);offset+=frames[i].Length;}
            foreach(var bytes in frames)writer.Write(bytes);
        }
    }
}
