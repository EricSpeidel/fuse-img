using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fuse.App;
using Fuse.Core;

internal static class Checks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAILED: " + message);
        Console.WriteLine("PASS: " + message);
    }
    private static BitmapSource Solid(byte b, byte g, byte r)
    {
        byte[] pixels = Enumerable.Range(0, 64 * 64).SelectMany(_ => new byte[] { b, g, r, 255 }).ToArray();
        var bitmap = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Pbgra32, null, pixels, 256); bitmap.Freeze(); return bitmap;
    }
    [STAThread]
    public static int Main()
    {
        string folder = Path.Combine(Path.GetTempPath(), "FuseChecks-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var a = new Photo(Solid(0, 0, 255)); var b = new Photo(Solid(255, 0, 0));
            var state = new Composition(128, 64);
            var bitmap = Imaging.Render(a, b, state, 128, 64);
            byte[] pixels = new byte[128 * 64 * 4]; bitmap.CopyPixels(pixels, 128 * 4, 0);
            Check(pixels[2] == 255 && pixels[127 * 4] == 255, "WPF rendering crops and splits at requested dimensions");
            string png = Path.Combine(folder, "image.png");
            Imaging.Save(png, a, b, state, false);
            var decoded = Imaging.Load(png);
            Check(decoded.PixelWidth == 128 && decoded.PixelHeight == 64, "PNG export retains exact dimensions");
            // OnLoad releases its file handle, allowing same-path export.
            Imaging.Save(png, new Photo(decoded), b, state, false);
            Check(File.Exists(png), "Export can replace an imported source without a file lock");
            var empty = new Photo();
            Imaging.Save(png, empty, empty, state with { Background = Backdrop.Transparent }, false);
            var alpha = new FormatConvertedBitmap(Imaging.Load(png), PixelFormats.Bgra32, null, 0);
            alpha.CopyPixels(pixels, 128 * 4, 0);
            Check(pixels[3] == 0, "PNG keeps transparent canvas pixels");
            string jpg = Path.Combine(folder, "image.jpg");
            Imaging.Save(jpg, empty, empty, state with { Background = Backdrop.Transparent }, true);
            var jpeg = new FormatConvertedBitmap(Imaging.Load(jpg), PixelFormats.Bgra32, null, 0);
            jpeg.CopyPixels(pixels, 128 * 4, 0);
            Check(pixels[0] > 250 && pixels[1] > 250 && pixels[2] > 250, "JPEG fills transparency with white");
            // Exercise window construction, event synchronization, and rendering without a user session.
            var app = new Application();
            var window = new MainWindow();
            window.Show();
            window.Measure(new Size(1180, 800)); window.Arrange(new Rect(0, 0, 1180, 800)); window.UpdateLayout();
            var preview = new RenderTargetBitmap(1180, 800, 96, 96, PixelFormats.Pbgra32); preview.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(preview));
            string artifact = Path.GetFullPath("artifacts/ui-smoke.png"); Directory.CreateDirectory(Path.GetDirectoryName(artifact)!);
            using (var stream = File.Create(artifact)) encoder.Save(stream);
            window.Close(); app.Shutdown();
            Console.WriteLine("All Windows checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(folder, true); }
    }
}
