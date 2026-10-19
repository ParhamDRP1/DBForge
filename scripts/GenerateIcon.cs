using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
public static class GenerateIcon {
    public static void Main(string[] args) {
        string output = args[0]; Directory.CreateDirectory(output);
        var sizes = new[] {16,24,32,48,64,128,256}; var images = new List<byte[]>();
        foreach(int size in sizes) using(var bitmap = new Bitmap(size,size)) {
            using(var g=Graphics.FromImage(bitmap)) {
                g.SmoothingMode=SmoothingMode.AntiAlias; g.ScaleTransform(size/256f,size/256f);
                g.Clear(Color.FromArgb(18,40,65));
                using(var teal=new Pen(Color.FromArgb(62,214,189),10))
                using(var gold=new Pen(Color.FromArgb(255,188,102),12))
                using(var body=new SolidBrush(Color.FromArgb(18,63,82)))
                using(var top=new SolidBrush(Color.FromArgb(28,82,97))) {
                    gold.StartCap=gold.EndCap=LineCap.Round;gold.LineJoin=LineJoin.Round;
                    g.FillRectangle(body,60,79,136,96);g.FillEllipse(body,60,145,136,60);
                    g.DrawLine(teal,60,79,60,175);g.DrawLine(teal,196,79,196,175);
                    g.DrawArc(teal,60,145,136,60,0,180);g.DrawArc(teal,60,95,136,60,0,180);
                    g.FillEllipse(top,60,49,136,60);g.DrawEllipse(teal,60,49,136,60);
                    g.DrawLine(gold,26,111,88,111);g.DrawLines(gold,new[]{new Point(71,94),new Point(88,111),new Point(71,128)});
                    g.DrawLine(gold,230,172,168,172);g.DrawLines(gold,new[]{new Point(185,155),new Point(168,172),new Point(185,189)});
                }
            }
            using(var stream=new MemoryStream()){bitmap.Save(stream,ImageFormat.Png);images.Add(stream.ToArray());}
            if(size==256)bitmap.Save(Path.Combine(output,"icon.png"),ImageFormat.Png);
        }
        using(var writer=new BinaryWriter(File.Create(Path.Combine(output,"app.ico")))) {
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);
            int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++){writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(images[i].Length);writer.Write(offset);offset+=images[i].Length;}
            foreach(var bytes in images)writer.Write(bytes);
        }
    }
}
