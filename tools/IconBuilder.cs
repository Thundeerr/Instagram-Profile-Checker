using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class IconBuilder
{
    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        float diameter = radius * 2F;
        GraphicsPath path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180F, 90F);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270F, 90F);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0F, 90F);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90F, 90F);
        path.CloseFigure();
        return path;
    }

    private static Bitmap Render(int size)
    {
        Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.ScaleTransform(size / 256F, size / 256F);

            using (GraphicsPath background = RoundedRectangle(new RectangleF(7F, 7F, 242F, 242F), 48F))
            using (LinearGradientBrush gradient = new LinearGradientBrush(
                new PointF(20F, 20F), new PointF(235F, 235F),
                Color.FromArgb(174, 104, 255), Color.FromArgb(92, 55, 201)))
            {
                graphics.FillPath(gradient, background);
            }

            using (Pen lens = new Pen(Color.White, 18F))
            {
                lens.StartCap = LineCap.Round;
                lens.EndCap = LineCap.Round;
                graphics.DrawEllipse(lens, 42F, 38F, 126F, 126F);
                graphics.DrawLine(lens, 151F, 151F, 207F, 207F);
            }

            using (Pen check = new Pen(Color.FromArgb(52, 211, 153), 17F))
            {
                check.StartCap = LineCap.Round;
                check.EndCap = LineCap.Round;
                check.LineJoin = LineJoin.Round;
                graphics.DrawLines(check, new PointF[]
                {
                    new PointF(74F, 105F),
                    new PointF(101F, 132F),
                    new PointF(143F, 82F)
                });
            }
        }
        return bitmap;
    }

    private static byte[] ToPng(Bitmap bitmap)
    {
        using (MemoryStream stream = new MemoryStream())
        {
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }

    private static void WriteIcon(string path, int[] sizes, List<byte[]> images)
    {
        using (FileStream stream = File.Create(path))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)sizes.Length);

            int offset = 6 + (16 * sizes.Length);
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(images[i].Length);
                writer.Write(offset);
                offset += images[i].Length;
            }

            for (int i = 0; i < images.Count; i++) writer.Write(images[i]);
        }
    }

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: IconBuilder <output.ico> <preview.png>");
            return 2;
        }

        int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
        List<byte[]> images = new List<byte[]>();
        for (int i = 0; i < sizes.Length; i++)
        {
            using (Bitmap bitmap = Render(sizes[i])) images.Add(ToPng(bitmap));
        }
        WriteIcon(args[0], sizes, images);

        using (Bitmap preview = Render(256)) preview.Save(args[1], ImageFormat.Png);
        return 0;
    }
}
