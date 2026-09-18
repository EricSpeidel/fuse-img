using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Fuse.Core;
using Microsoft.Win32;

namespace Fuse.App;

public sealed class MainWindow : Window
{
    private readonly EditorSurface editor = new();
    private readonly List<Document> undo = [];
    private readonly Stack<Document> redo = new();
    private readonly TextBlock status = new();
    private readonly TextBlock first = new(), second = new(), angleLabel = new(), strengthLabel = new();
    private readonly ComboBox mode = new(), background = new(), selection = new();
    private readonly Slider angle = new(), strength = new();
    private readonly TextBox canvasWidth = new(), canvasHeight = new();
    private readonly Button export = new(), undoButton = new(), redoButton = new();
    private bool syncing;
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(156, 166, 177));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(183, 241, 116));

    public MainWindow()
    {
        Title = "Fuse — two images, one canvas";
        Width = 1180; Height = 800; MinWidth = 920; MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(29, 33, 39));
        Foreground = Brushes.WhiteSmoke;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        var buttonStyle = new Style(typeof(Button));
        buttonStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(13, 8, 13, 8)));
        buttonStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(53, 61, 70))));
        buttonStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        buttonStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        buttonStyle.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content); template.VisualTree = border;
        buttonStyle.Setters.Add(new Setter(Control.TemplateProperty, template));
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(OpacityProperty, .8)); buttonStyle.Triggers.Add(hover);
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(OpacityProperty, .35)); buttonStyle.Triggers.Add(disabled);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.SteelBlue)); buttonStyle.Triggers.Add(focus);
        Resources.Add(typeof(Button), buttonStyle);

        var root = new DockPanel(); Content = root;
        var top = new Grid { Margin = new Thickness(24, 20, 24, 18) };
        top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel();
        brand.Children.Add(new TextBlock { Text = "fuse", FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Accent });
        brand.Children.Add(new TextBlock { Text = "Two images. One canvas.", Foreground = Muted, Margin = new Thickness(0, 1, 0, 0) });
        top.Children.Add(brand);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        ConfigureButton(undoButton, "↶", Undo, "Undo · Ctrl+Z"); actions.Children.Add(undoButton);
        ConfigureButton(redoButton, "↷", Redo, "Redo · Ctrl+Y"); actions.Children.Add(redoButton);
        actions.Children.Add(Button("Swap A / B", Swap, "Swap images while keeping the two placements"));
        ConfigureButton(export, "Export image…", Export, "Save PNG or JPEG · Ctrl+E");
        export.Background = Accent; export.Foreground = Brushes.Black; actions.Children.Add(export);
        Grid.SetColumn(actions, 1); top.Children.Add(actions); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);

        var footer = new Border { Padding = new Thickness(24, 12, 24, 12), Background = new SolidColorBrush(Color.FromRgb(24, 27, 32)) };
        status.Foreground = Muted; status.Text = "Add two images to begin. Everything stays on your computer.";
        footer.Child = status; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);

        var side = new StackPanel { Margin = new Thickness(20), Width = 232 };
        var sidebar = new ScrollViewer { Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        DockPanel.SetDock(sidebar, Dock.Right); root.Children.Add(sidebar);
        Section(side, "IMAGES");
        side.Children.Add(Button("＋  First image  ·  A", () => Open(0), "Open image A · Ctrl+O"));
        FileLabel(first, side);
        side.Children.Add(Button("＋  Second image  ·  B", () => Open(1), "Open image B · Ctrl+Shift+O"));
        FileLabel(second, side);
        selection.ItemsSource = new[] { "Adjust image A", "Adjust image B" }; selection.SelectedIndex = 0;
        Combo(selection, side); selection.SelectionChanged += (_, _) => { if (!syncing) editor.Selected = selection.SelectedIndex; };
        var photoTools = new UniformGridShim();
        photoTools.Children.Add(Button("Fit half", () => Change(editor.ResetPhoto), "Center the selected image and cover its half of the canvas"));
        photoTools.Children.Add(Button("Fill canvas", () => Change(editor.FillPhoto), "Cover the entire canvas with the selected image; useful for crossfades"));
        side.Children.Add(photoTools);

        Section(side, "BLEND");
        mode.ItemsSource = new[] { "Clean split", "Soft seam", "Crossfade" }; Combo(mode, side);
        mode.SelectionChanged += (_, _) => { if (!syncing && mode.SelectedIndex >= 0) Change(() => editor.Set(editor.Document with { Settings = editor.Document.Settings with { Mode = (BlendMode)mode.SelectedIndex } })); };
        side.Children.Add(angleLabel); SetupSlider(angle, -180, 180, side);
        angle.ValueChanged += (_, _) => { if (!syncing) editor.Set(editor.Document with { Settings = editor.Document.Settings with { Angle = angle.Value } }); };
        side.Children.Add(strengthLabel); SetupSlider(strength, 0, 100, side);
        strength.ValueChanged += (_, _) =>
        {
            if (syncing) return;
            var s = editor.Document.Settings;
            editor.Set(editor.Document with { Settings = s.Mode == BlendMode.Crossfade ? s with { Mix = strength.Value / 100 } : s with { Feather = strength.Value / 100 } });
        };
        side.Children.Add(Button("Center divider", () => Change(() => editor.Set(editor.Document with { Settings = editor.Document.Settings with { PivotX = .5, PivotY = .5, Angle = 0 } })), "Restore a centered vertical divider"));

        Section(side, "CANVAS");
        var sizes = new UniformGridShim();
        foreach (var (label, w, h) in new[] { ("1:1", 1600, 1600), ("4:5", 1600, 2000), ("16:9", 1920, 1080) })
            sizes.Children.Add(Button(label, () => Resize(w, h), $"{w} × {h} pixels"));
        side.Children.Add(sizes);
        var dimensions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        foreach (var box in new[] { canvasWidth, canvasHeight })
        {
            box.Width = 74; box.Padding = new Thickness(7); box.Background = Brushes.WhiteSmoke; box.Foreground = Brushes.Black;
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplySize(); e.Handled = true; } };
        }
        canvasWidth.ToolTip = "Canvas width in pixels"; canvasHeight.ToolTip = "Canvas height in pixels";
        System.Windows.Automation.AutomationProperties.SetName(canvasWidth, "Canvas width in pixels");
        System.Windows.Automation.AutomationProperties.SetName(canvasHeight, "Canvas height in pixels");
        dimensions.Children.Add(canvasWidth);
        dimensions.Children.Add(new TextBlock { Text = " × ", VerticalAlignment = VerticalAlignment.Center }); dimensions.Children.Add(canvasHeight);
        dimensions.Children.Add(Button("Set", ApplySize)); side.Children.Add(dimensions);
        background.ItemsSource = new[] { "White background", "Dark background", "Transparent background" }; Combo(background, side);
        background.SelectionChanged += (_, _) => { if (!syncing && background.SelectedIndex >= 0) Change(() => editor.Set(editor.Document with { Settings = editor.Document.Settings with { Background = (Backdrop)background.SelectedIndex } })); };
        var guides = new CheckBox { Content = "Show editing handles", IsChecked = true, Foreground = Brushes.WhiteSmoke, Margin = new Thickness(0, 10, 0, 0) };
        guides.Click += (_, _) => { editor.Guides = guides.IsChecked == true; editor.InvalidateVisual(); }; side.Children.Add(guides);
        Section(side, "MAKE IT YOURS");
        side.Children.Add(new TextBlock { Text = "Drag photos to move them.\nScroll over a photo to zoom.\nDrag the green dot to slide.\nDrag the white dot to rotate.\nDrag the corner to crop.\n\nAlt + drag adjusts the selected image anywhere on the canvas.", TextWrapping = TextWrapping.Wrap, Foreground = Muted, LineHeight = 21 });
        root.Children.Add(editor);
        editor.GestureStarting += Remember;
        editor.Changed += Sync;
        editor.OpenRequested += Open;
        editor.FilesDropped += Import;
        PreviewKeyDown += Shortcuts;
        Sync();
    }

    private static void FileLabel(TextBlock label, Panel panel)
    {
        label.Foreground = Muted; label.TextTrimming = TextTrimming.CharacterEllipsis; label.Margin = new Thickness(0, 5, 0, 10); panel.Children.Add(label);
    }
    private static void Section(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Muted, Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 24, 0, 10) });
    private static void Combo(ComboBox combo, Panel panel)
    {
        combo.Padding = new Thickness(8); combo.Margin = new Thickness(0, 0, 0, 8); combo.Foreground = Brushes.Black; combo.Background = Brushes.WhiteSmoke; panel.Children.Add(combo);
    }
    private void SetupSlider(Slider slider, double min, double max, Panel parent)
    {
        slider.Minimum = min; slider.Maximum = max; slider.SmallChange = 1; slider.LargeChange = 10;
        slider.Margin = new Thickness(0, 8, 0, 14); slider.Foreground = Accent;
        slider.PreviewMouseLeftButtonDown += (_, _) => { if (!syncing) Remember(); };
        slider.PreviewKeyDown += (_, e) => { if (!syncing && !e.IsRepeat && e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown) Remember(); };
        parent.Children.Add(slider);
    }
    private Button Button(string text, Action action, string? tooltip = null)
    {
        var button = new Button(); ConfigureButton(button, text, action, tooltip); return button;
    }
    private static void ConfigureButton(Button button, string text, Action action, string? tooltip)
    {
        button.Content = text; button.Margin = new Thickness(0, 0, 6, 0); button.ToolTip = tooltip ?? text;
        button.Click += (_, _) => action();
    }
    private void Remember()
    {
        if (undo.Count == 0 || undo[^1] != editor.Document) undo.Add(editor.Document);
        if (undo.Count > 40) undo.RemoveAt(0);
        redo.Clear(); UpdateHistory();
    }
    private void Change(Action action) { Remember(); action(); }
    private void UpdateHistory() { undoButton.IsEnabled = undo.Count > 0; redoButton.IsEnabled = redo.Count > 0; }
    private void Undo()
    {
        if (undo.Count == 0) return;
        redo.Push(editor.Document); var last = undo[^1]; undo.RemoveAt(undo.Count - 1); editor.Set(last); UpdateHistory();
    }
    private void Redo()
    {
        if (redo.Count == 0) return;
        undo.Add(editor.Document); editor.Set(redo.Pop()); UpdateHistory();
    }
    private void Sync()
    {
        syncing = true;
        var s = editor.Document.Settings;
        first.Text = editor.Document.A.Name; first.ToolTip = first.Text;
        second.Text = editor.Document.B.Name; second.ToolTip = second.Text;
        mode.SelectedIndex = (int)s.Mode; background.SelectedIndex = (int)s.Background; selection.SelectedIndex = editor.Selected;
        angle.Value = s.Angle; angle.IsEnabled = s.Mode != BlendMode.Crossfade;
        angleLabel.Text = $"Divider angle  {s.Angle:0}°";
        strength.IsEnabled = s.Mode != BlendMode.Split;
        strength.Value = (s.Mode == BlendMode.Crossfade ? s.Mix : s.Feather) * 100;
        strengthLabel.Text = s.Mode == BlendMode.Crossfade ? $"Image B opacity  {s.Mix:P0}" : $"Seam softness  {s.Feather:P0}";
        System.Windows.Automation.AutomationProperties.SetName(angle, "Divider angle");
        System.Windows.Automation.AutomationProperties.SetName(strength, s.Mode == BlendMode.Crossfade ? "Crossfade amount" : "Seam softness");
        canvasWidth.Text = s.Width.ToString(); canvasHeight.Text = s.Height.ToString();
        export.IsEnabled = editor.Document.A.Bitmap is not null && editor.Document.B.Bitmap is not null;
        syncing = false; UpdateHistory();
    }
    private void Open(int target)
    {
        var dialog = new OpenFileDialog { Title = target == 0 ? "Choose first image" : "Choose second image", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif|All files|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) == true) Import(dialog.FileNames, target);
    }
    private void Import(string[] paths, int target)
    {
        if (paths.Length == 0) return;
        try
        {
            // Decode first; failed imports leave the current document and history untouched.
            var bitmap = Imaging.Load(paths[0]);
            var other = paths.Length > 1 ? Imaging.Load(paths[1]) : null;
            Remember();
            var d = editor.Document;
            if (other is not null)
                d = d with { A = new Photo(bitmap, Path.GetFileName(paths[0])), B = new Photo(other, Path.GetFileName(paths[1])) };
            else if (target == 0) d = d with { A = new Photo(bitmap, Path.GetFileName(paths[0])) };
            else d = d with { B = new Photo(bitmap, Path.GetFileName(paths[0])) };
            editor.Selected = target; editor.Set(d);
            status.Text = paths.Length > 2 ? "Loaded the first two images. Drag each photo to compose." : "Images loaded. Drag to position, scroll to zoom.";
        }
        catch (Exception ex) { Error("Could not open that image", ex); }
    }
    private void Swap() => Change(() =>
    {
        var d = editor.Document;
        editor.Set(d with { A = d.A with { Bitmap = d.B.Bitmap, Name = d.B.Name }, B = d.B with { Bitmap = d.A.Bitmap, Name = d.A.Name } });
    });
    private void Resize(int w, int h)
    {
        try { var s = editor.Document.Settings with { Width = w, Height = h }; s.Validate(); Change(() => editor.Set(editor.Document with { Settings = s })); }
        catch (ArgumentException ex) { Error("Invalid canvas size", ex); }
    }
    private void ApplySize()
    {
        if (int.TryParse(canvasWidth.Text, out int w) && int.TryParse(canvasHeight.Text, out int h)) Resize(w, h);
        else MessageBox.Show(this, "Enter a whole-number width and height in pixels.", "Canvas size", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private async void Export()
    {
        if (!export.IsEnabled) return;
        var dialog = new SaveFileDialog { Title = "Export your composition", Filter = "PNG image|*.png|JPEG image|*.jpg", FileName = "fused.png", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        bool jpeg = dialog.FilterIndex == 2 || Path.GetExtension(dialog.FileName).Equals(".jpg", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(dialog.FileName).Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
        var snapshot = editor.Document;
        IsEnabled = false; Cursor = Cursors.Wait; status.Text = "Exporting at full canvas resolution…";
        try
        {
            // Render on a dedicated STA thread. The UI stays responsive; frozen source bitmaps can cross threads.
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { Imaging.Save(dialog.FileName, snapshot.A, snapshot.B, snapshot.Settings, jpeg); completion.SetResult(true); }
                catch (Exception ex) { completion.SetException(ex); }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); await completion.Task;
            status.Text = $"Saved {Path.GetFileName(dialog.FileName)} · {snapshot.Settings.Width} × {snapshot.Settings.Height} px" +
                (jpeg && snapshot.Settings.Background == Backdrop.Transparent ? " · Transparency filled with white for JPEG" : "");
        }
        catch (Exception ex) { Error("Could not export the image", ex); }
        finally { IsEnabled = true; Cursor = Cursors.Arrow; }
    }
    private void Error(string title, Exception ex)
    {
        status.Text = title + ".";
        MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private void Shortcuts(object sender, KeyEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        // Preserve normal text editing undo in the dimension fields.
        if (Keyboard.FocusedElement is TextBox && e.Key is Key.Z or Key.Y) return;
        if (e.Key == Key.Z) { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) Redo(); else Undo(); }
        else if (e.Key == Key.Y) Redo();
        else if (e.Key == Key.O) Open(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : 0);
        else if (e.Key is Key.E or Key.S) Export();
        else return;
        e.Handled = true;
    }
    private sealed class UniformGridShim : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGridShim() { Rows = 1; }
    }
}

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var application = new Application();
        application.Run(new MainWindow());
    }
}
