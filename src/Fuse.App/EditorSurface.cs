using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fuse.Core;

namespace Fuse.App;

public sealed record Document(Composition Settings, Photo A, Photo B);

public sealed class EditorSurface : FrameworkElement
{
    public Document Document { get; private set; } = new(new Composition(), new Photo(), new Photo());
    public int Selected { get; set; }
    public bool Guides { get; set; } = true;
    public event Action? Changed;
    public event Action? GestureStarting;
    public event Action<string[], int>? FilesDropped;
    public event Action<int>? OpenRequested;
    private Rect viewport;
    private Point previous, start;
    private Document? dragStart;
    private double dragScale;
    private string? drag;
    private byte[]? cachedA, cachedB;
    private (Photo, int, int, int, int)? keyA, keyB;
    private readonly System.Windows.Threading.DispatcherTimer wheelTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(183, 241, 116));

    public EditorSurface()
    {
        Focusable = true;
        AllowDrop = true;
        ClipToBounds = true;
        wheelTimer.Tick += (_, _) => wheelTimer.Stop();
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
                FilesDropped?.Invoke(paths, HitPhoto(e.GetPosition(this)));
            e.Handled = true;
        };
    }

    public void Set(Document value)
    {
        value.Settings.Validate();
        Document = value;
        InvalidateVisual();
        Changed?.Invoke();
    }

    private Point CanvasPoint(Point p) => new((p.X - viewport.X) / viewport.Width * Document.Settings.Width,
                                               (p.Y - viewport.Y) / viewport.Height * Document.Settings.Height);
    private Point Pivot => new(viewport.X + Document.Settings.PivotX * viewport.Width, viewport.Y + Document.Settings.PivotY * viewport.Height);
    private Point RotationHandle
    {
        get
        {
            double a = Document.Settings.Angle * Math.PI / 180;
            double radius = Math.Min(76, Math.Min(viewport.Width, viewport.Height) * .24);
            return Pivot + new Vector(Math.Sin(a) * radius, -Math.Cos(a) * radius);
        }
    }
    private int HitPhoto(Point point)
    {
        if (Document.Settings.Mode == BlendMode.Crossfade || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return Selected;
        var p = CanvasPoint(point);
        return Document.Settings.Distance(p.X, p.Y) < 0 ? 0 : 1;
    }
    private Photo Active => Selected == 0 ? Document.A : Document.B;
    private void SetActive(Photo value) => Set(Selected == 0 ? Document with { A = value } : Document with { B = value });

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(21, 24, 28)), null, new Rect(RenderSize));
        if (ActualWidth < 90 || ActualHeight < 90) return;
        var state = Document.Settings;
        double scale = Math.Min((ActualWidth - 80) / state.Width, (ActualHeight - 80) / state.Height);
        viewport = new Rect((ActualWidth - state.Width * scale) / 2, (ActualHeight - state.Height * scale) / 2, state.Width * scale, state.Height * scale);
        dc.DrawRectangle(Brushes.Black, null, new Rect(viewport.X + 4, viewport.Y + 8, viewport.Width, viewport.Height));
        dc.PushClip(new RectangleGeometry(viewport));
        dc.DrawRectangle(Brushes.WhiteSmoke, null, viewport);
        for (double y = viewport.Top; y < viewport.Bottom; y += 16)
            for (double x = viewport.Left; x < viewport.Right; x += 16)
                if (((int)((x - viewport.Left) / 16) + (int)((y - viewport.Top) / 16)) % 2 == 0)
                    dc.DrawRectangle(Brushes.LightGray, null, new Rect(x, y, 16, 16));
        int pw = Math.Max(1, (int)Math.Min(1200, viewport.Width));
        int ph = Math.Max(1, (int)Math.Round(pw * (double)state.Height / state.Width));
        var ka = (Document.A, pw, ph, state.Width, state.Height);
        var kb = (Document.B, pw, ph, state.Width, state.Height);
        if (keyA != ka) { cachedA = Imaging.Layer(Document.A, state, pw, ph); keyA = ka; }
        if (keyB != kb) { cachedB = Imaging.Layer(Document.B, state, pw, ph); keyB = kb; }
        var output = new byte[pw * ph * 4];
        Compositor.Blend(cachedA!, cachedB!, output, pw, ph, state);
        var bitmap = BitmapSource.Create(pw, ph, 96, 96, PixelFormats.Pbgra32, null, output, pw * 4);
        dc.DrawImage(bitmap, viewport);
        if (Document.A.Bitmap is null) Placeholder(dc, "A", "Drop first image", viewport.Left + viewport.Width * .25);
        if (Document.B.Bitmap is null) Placeholder(dc, "B", "Drop second image", viewport.Left + viewport.Width * .75);
        if (Guides && state.Mode != BlendMode.Crossfade)
        {
            double angle = state.Angle * Math.PI / 180;
            Vector tangent = new(-Math.Sin(angle), Math.Cos(angle));
            double length = viewport.Width + viewport.Height;
            dc.DrawLine(new Pen(Brushes.Black, 4), Pivot - tangent * length, Pivot + tangent * length);
            dc.DrawLine(new Pen(Brushes.White, 1.5), Pivot - tangent * length, Pivot + tangent * length);
            dc.DrawEllipse(Accent, new Pen(Brushes.Black, 2), Pivot, 13, 13);
            Text(dc, "↔", Pivot.X - 8, Pivot.Y - 12, 18, Brushes.Black);
            dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, 2), RotationHandle, 10, 10);
            Text(dc, "↻", RotationHandle.X - 7, RotationHandle.Y - 10, 14, Brushes.Black);
        }
        dc.Pop();
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(83, 90, 101)), 1), viewport);
        if (Guides)
        {
            dc.DrawRoundedRectangle(Accent, new Pen(Brushes.Black, 1), new Rect(viewport.BottomRight - new Vector(7, 7), new Size(14, 14)), 3, 3);
            Text(dc, $"{state.Width} × {state.Height} px", viewport.Left, viewport.Bottom + 12, 11, Brushes.LightGray);
        }
    }

    private void Placeholder(DrawingContext dc, string letter, string caption, double x)
    {
        var center = new Point(x, viewport.Top + viewport.Height * .5);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(231, 235, 229)), null, center - new Vector(0, 18), 24, 24);
        Text(dc, letter, center.X - 7, center.Y - 32, 21, Brushes.DarkSlateGray);
        Text(dc, caption, center.X - 54, center.Y + 22, 13, Brushes.DimGray);
    }
    private void Text(DrawingContext dc, string text, double x, double y, double size, Brush color) =>
        dc.DrawText(new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        Point p = e.GetPosition(this);
        if (viewport.IsEmpty || viewport.Width <= 0) return;
        bool seam = Guides && Document.Settings.Mode != BlendMode.Crossfade;
        if (Guides && (p - viewport.BottomRight).Length < 15) drag = "crop";
        else if (!viewport.Contains(p)) return;
        else if (seam && (p - RotationHandle).Length < 15) drag = "rotate";
        else if (seam && ((p - Pivot).Length < 18 || Math.Abs(Document.Settings.Distance(CanvasPoint(p).X, CanvasPoint(p).Y)) * viewport.Width / Document.Settings.Width < 7)) drag = "seam";
        else
        {
            Selected = HitPhoto(p);
            Changed?.Invoke();
            if (Active.Bitmap is null) { OpenRequested?.Invoke(Selected); return; }
            if (e.ClickCount == 2) { GestureStarting?.Invoke(); ResetPhoto(); return; }
            drag = "photo";
        }
        GestureStarting?.Invoke();
        dragStart = Document;
        previous = start = p;
        dragScale = viewport.Width / Document.Settings.Width;
        CaptureMouse();
        Cursor = Cursors.SizeAll;
        e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point p = e.GetPosition(this);
        if (drag is null)
        {
            ToolTip = "Drag a photo to move it · Wheel to zoom · Double-click to fit";
            if (Guides && (p - viewport.BottomRight).Length < 15) { Cursor = Cursors.SizeNWSE; ToolTip = "Drag to change canvas size"; }
            else if (Guides && Document.Settings.Mode != BlendMode.Crossfade && (p - RotationHandle).Length < 15) { Cursor = Cursors.Hand; ToolTip = "Drag to rotate · Hold Shift to snap to 15°"; }
            else if (Guides && Document.Settings.Mode != BlendMode.Crossfade && (p - Pivot).Length < 18) { Cursor = Cursors.SizeAll; ToolTip = "Drag the divider"; }
            else Cursor = Cursors.Arrow;
            return;
        }
        var state = Document.Settings;
        Vector delta = p - previous;
        if (drag == "seam")
            Set(Document with { Settings = state with { PivotX = Math.Clamp(state.PivotX + delta.X / viewport.Width, 0, 1), PivotY = Math.Clamp(state.PivotY + delta.Y / viewport.Height, 0, 1) } });
        else if (drag == "rotate")
        {
            Vector vector = p - Pivot;
            double angle = Math.Atan2(vector.X, -vector.Y) * 180 / Math.PI;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) angle = Math.Round(angle / 15) * 15;
            Set(Document with { Settings = state with { Angle = angle } });
        }
        else if (drag == "photo")
            SetActive(Active with { X = Active.X + delta.X / viewport.Width, Y = Active.Y + delta.Y / viewport.Height });
        else if (drag == "crop" && dragStart is not null)
        {
            int w = Math.Clamp((int)Math.Round(dragStart.Settings.Width + (p.X - start.X) / dragScale), 64, 8192);
            int h = Math.Clamp((int)Math.Round(dragStart.Settings.Height + (p.Y - start.Y) / dragScale), 64, Math.Min(8192, 24_000_000 / w));
            Set(Document with { Settings = state with { Width = w, Height = h } });
        }
        previous = p;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        drag = null;
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); drag = null; }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Point p = e.GetPosition(this);
        if (!viewport.Contains(p)) return;
        Selected = HitPhoto(p);
        if (Active.Bitmap is null) return;
        if (!wheelTimer.IsEnabled) GestureStarting?.Invoke();
        wheelTimer.Stop(); wheelTimer.Start();
        double zoom = Math.Clamp(Active.Zoom * Math.Pow(1.12, e.Delta / 120.0), .05, 20);
        double ratio = zoom / Active.Zoom;
        Point cp = CanvasPoint(p);
        SetActive(Active with { Zoom = zoom, X = cp.X / Document.Settings.Width + (Active.X - cp.X / Document.Settings.Width) * ratio,
            Y = cp.Y / Document.Settings.Height + (Active.Y - cp.Y / Document.Settings.Height) * ratio });
        e.Handled = true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && dragStart is not null && drag is not null) { Set(dragStart); drag = null; ReleaseMouseCapture(); e.Handled = true; return; }
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
        if (!e.IsRepeat) GestureStarting?.Invoke();
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        double dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
        double dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var s = Document.Settings;
            Set(Document with { Settings = s with { PivotX = Math.Clamp(s.PivotX + dx / s.Width, 0, 1), PivotY = Math.Clamp(s.PivotY + dy / s.Height, 0, 1) } });
        }
        else SetActive(Active with { X = Active.X + dx / Document.Settings.Width, Y = Active.Y + dy / Document.Settings.Height });
        e.Handled = true;
    }
    public void ResetPhoto()
    {
        if (Active.Bitmap is null) return;
        var s = Document.Settings;
        double fullScale = Math.Max((double)s.Width / Active.Bitmap.PixelWidth, (double)s.Height / Active.Bitmap.PixelHeight);
        double halfScale = Math.Max(s.Width / 2.0 / Active.Bitmap.PixelWidth, (double)s.Height / Active.Bitmap.PixelHeight);
        SetActive(Active with { X = Selected == 0 ? .25 : .75, Y = .5, Zoom = halfScale / fullScale });
    }
    public void FillPhoto() => SetActive(Active with { X = .5, Y = .5, Zoom = 1 });
}
