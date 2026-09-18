using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fuse.Core;

namespace Fuse.App;

public sealed record Photo(BitmapSource? Bitmap = null, string Name = "Drop an image", double X = .5, double Y = .5, double Zoom = 1)
{
    public Rect Bounds(Composition state)
    {
        if (Bitmap is null) return Rect.Empty;
        double scale = Math.Max((double)state.Width / Bitmap.PixelWidth, (double)state.Height / Bitmap.PixelHeight) * Zoom;
        double w = Bitmap.PixelWidth * scale, h = Bitmap.PixelHeight * scale;
        return new Rect(X * state.Width - w / 2, Y * state.Height - h / 2, w, h);
    }
}

public static class Imaging
{
    public static BitmapSource Load(string path)
    {
        // OnLoad releases the source file immediately; exports may overwrite it safely.
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource source = decoder.Frames[0];
        if ((long)source.PixelWidth * source.PixelHeight > 60_000_000)
            throw new ArgumentException("This image is over 60 megapixels. Please resize it before importing.");
        ushort orientation = 1;
        if (source is BitmapFrame frame && frame.Metadata is BitmapMetadata metadata)
        {
            foreach (string query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
            {
                try { if (metadata.GetQuery(query) is ushort value) { orientation = value; break; } }
                catch (NotSupportedException) { }
            }
        }
        // EXIF orientation, including mirrored phone-camera variants.
        Matrix matrix = orientation switch
        {
            2 => new Matrix(-1, 0, 0, 1, 0, 0),
            3 => new Matrix(-1, 0, 0, -1, 0, 0),
            4 => new Matrix(1, 0, 0, -1, 0, 0),
            5 => new Matrix(0, 1, 1, 0, 0, 0),
            6 => new Matrix(0, 1, -1, 0, 0, 0),
            7 => new Matrix(0, -1, -1, 0, 0, 0),
            8 => new Matrix(0, -1, 1, 0, 0, 0),
            _ => Matrix.Identity
        };
        if (!matrix.IsIdentity) source = new TransformedBitmap(source, new MatrixTransform(matrix));
        source.Freeze();
        return source;
    }

    public static byte[] Layer(Photo photo, Composition state, int width, int height)
    {
        var pixels = new byte[checked(width * height * 4)];
        if (photo.Bitmap is null) return pixels;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform((double)width / state.Width, (double)height / state.Height));
            dc.DrawImage(photo.Bitmap, photo.Bounds(state));
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    public static BitmapSource Render(Photo a, Photo b, Composition state, int width, int height)
    {
        var left = Layer(a, state, width, height);
        var right = Layer(b, state, width, height);
        var result = new byte[left.Length];
        Compositor.Blend(left, right, result, width, height, state);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, result, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public static void Save(string path, Photo a, Photo b, Composition state, bool jpeg)
    {
        if (jpeg && state.Background == Backdrop.Transparent) state = state with { Background = Backdrop.White };
        var bitmap = Render(a, b, state, state.Width, state.Height);
        BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder { QualityLevel = 95 } : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        // Encode to a temporary sibling first, so failures do not destroy an existing file.
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temp)) encoder.Save(stream);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
