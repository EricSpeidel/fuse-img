using Fuse.Core;

static void Check(bool value, string name)
{
    if (!value) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
}
var s = new Composition(100, 100);
Check(s.WeightB(s.Distance(10, 50)) == 0 && s.WeightB(s.Distance(90, 50)) == 1, "Vertical seam chooses the correct image");
var horizontal = s with { Angle = 90 };
Check(horizontal.WeightB(horizontal.Distance(50, 10)) == 0 && horizontal.WeightB(horizontal.Distance(50, 90)) == 1, "Rotated seam chooses top and bottom");
var moved = s with { PivotX = .2 };
Check(moved.WeightB(moved.Distance(19, 50)) == 0 && moved.WeightB(moved.Distance(21, 50)) == 1, "Moved seam uses new pivot");
var feather = s with { Mode = BlendMode.Feather, Feather = .2 };
Check(feather.WeightB(-10) == 0 && feather.WeightB(0) == .5 && feather.WeightB(10) == 1, "Feather width and midpoint");
Check(Math.Abs(feather.WeightB(-3) + feather.WeightB(3) - 1) < 1e-12, "Complementary masks do not darken the seam");
Check((feather with { Feather = 0 }).WeightB(1) == 1, "Zero softness becomes a clean split");
byte[] red = [0, 0, 255, 255], blue = [255, 0, 0, 255], result = new byte[4];
var crossfade = s with { Mode = BlendMode.Crossfade, Mix = .5, Background = Backdrop.Transparent };
Compositor.Blend(red, blue, result, 1, 1, crossfade);
Check(result.SequenceEqual(new byte[] { 128, 0, 128, 255 }), "Opaque red and blue mix to purple without alpha loss");
Compositor.Blend(red, blue, result, 1, 1, crossfade with { Mix = 0 });
Check(result.SequenceEqual(red), "Crossfade endpoint A");
Compositor.Blend(red, blue, result, 1, 1, crossfade with { Mix = 1 });
Check(result.SequenceEqual(blue), "Crossfade endpoint B");
byte[] transparent = [0, 0, 0, 0];
Compositor.Blend(red, transparent, result, 1, 1, crossfade);
Check(result.SequenceEqual(new byte[] { 0, 0, 128, 128 }), "Transparency stays premultiplied");
Compositor.Blend(red, transparent, result, 1, 1, crossfade with { Background = Backdrop.White });
Check(result.SequenceEqual(new byte[] { 128, 128, 255, 255 }), "White backdrop composites correctly");
Compositor.Blend(transparent, transparent, result, 1, 1, crossfade with { Background = Backdrop.Dark });
Check(result.SequenceEqual(new byte[] { 24, 24, 24, 255 }), "Dark backdrop appears through transparent inputs");
bool rejected = false;
try { (s with { Width = 8192, Height = 8192 }).Validate(); } catch (ArgumentException) { rejected = true; }
Check(rejected, "Oversized export is rejected");
rejected = false;
try { Compositor.Blend(red, blue, [], 1, 1, s); } catch (ArgumentException) { rejected = true; }
Check(rejected, "Mismatched buffer is rejected");
// Verify that a proportional preview makes the same image choices as a full render.
byte[] a = Enumerable.Range(0, 8 * 4).Select(i => (byte)(i % 4 == 2 || i % 4 == 3 ? 255 : 0)).ToArray();
byte[] b = Enumerable.Range(0, 8 * 4).Select(i => (byte)(i % 4 == 0 || i % 4 == 3 ? 255 : 0)).ToArray();
byte[] pixels = new byte[a.Length];
Compositor.Blend(a, b, pixels, 4, 2, new Composition(400, 200));
Check(pixels[2] == 255 && pixels[6] == 255 && pixels[8] == 255 && pixels[12] == 255, "Preview coordinates map to canvas pixels");
Console.WriteLine("All core checks passed.");
