namespace Fuse.Core;

public enum BlendMode { Split, Feather, Crossfade }
public enum Backdrop { White, Dark, Transparent }

public sealed record Composition(
    int Width = 1600, int Height = 1000,
    double PivotX = .5, double PivotY = .5, double Angle = 0,
    BlendMode Mode = BlendMode.Split, double Feather = .15,
    double Mix = .5, Backdrop Background = Backdrop.White)
{
    public void Validate()
    {
        if (Width < 64 || Height < 64 || Width > 8192 || Height > 8192 || (long)Width * Height > 24_000_000)
            throw new ArgumentException("Choose 64–8192 pixels per side, up to 24 megapixels in total.");
        if (!double.IsFinite(PivotX) || !double.IsFinite(PivotY) || !double.IsFinite(Angle) ||
            PivotX < 0 || PivotX > 1 || PivotY < 0 || PivotY > 1 ||
            !double.IsFinite(Feather) || Feather < 0 || Feather > 1 ||
            !double.IsFinite(Mix) || Mix < 0 || Mix > 1)
            throw new ArgumentException("Invalid composition settings.");
    }

    // Angle is clockwise from a vertical seam. Negative signed distances belong to A.
    public double Distance(double x, double y) =>
        (x - PivotX * Width) * Math.Cos(Angle * Math.PI / 180) +
        (y - PivotY * Height) * Math.Sin(Angle * Math.PI / 180);

    public double WeightB(double distance)
    {
        if (Mode == BlendMode.Crossfade) return Mix;
        if (Mode == BlendMode.Split || Feather <= 0) return distance >= 0 ? 1 : 0;
        double t = Math.Clamp(.5 + distance / (Feather * Math.Min(Width, Height)), 0, 1);
        return t * t * (3 - 2 * t);
    }
}

public static class Compositor
{
    // Inputs and output are premultiplied BGRA. Blending premultiplied values avoids
    // black halos around transparent PNG edges and preserves output transparency.
    public static void Blend(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, Span<byte> output,
        int width, int height, Composition settings)
    {
        settings.Validate();
        int count = checked(width * height * 4);
        if (width <= 0 || height <= 0 || a.Length != count || b.Length != count || output.Length != count)
            throw new ArgumentException("Pixel buffers must match the output dimensions.");
        double nx = Math.Cos(settings.Angle * Math.PI / 180);
        double ny = Math.Sin(settings.Angle * Math.PI / 180);
        double dx = (double)settings.Width / width;
        double dy = (double)settings.Height / height;
        byte background = settings.Background == Backdrop.Dark ? (byte)24 : (byte)255;
        for (int y = 0; y < height; y++)
        {
            double distance = (.5 * dx - settings.PivotX * settings.Width) * nx +
                              ((y + .5) * dy - settings.PivotY * settings.Height) * ny;
            for (int x = 0; x < width; x++, distance += dx * nx)
            {
                int i = (y * width + x) * 4;
                double wb = settings.WeightB(distance), wa = 1 - wb;
                double alpha = a[i + 3] * wa + b[i + 3] * wb;
                double fill = settings.Background == Backdrop.Transparent ? 0 : background * (1 - alpha / 255);
                for (int c = 0; c < 3; c++) output[i + c] = (byte)Math.Clamp(Math.Round(a[i + c] * wa + b[i + c] * wb + fill), 0, 255);
                output[i + 3] = settings.Background == Backdrop.Transparent ? (byte)Math.Round(alpha) : (byte)255;
            }
        }
    }
}
