// Generates GameOptimizer icon assets.
// Design: dark navy circle, cyan CPU-chip ring, green lightning bolt center.
// Run from repo root: dotnet run --project Tools/IconGen
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Walk up from bin/Debug/net.../ to find the project root (has Assets/ AND a .csproj that is NOT IconGen.csproj)
string? dir = AppContext.BaseDirectory;
while (dir != null)
{
    var csprojFiles = Directory.GetFiles(dir, "*.csproj");
    bool hasProjectCsproj = csprojFiles.Any(f => !f.Contains("IconGen", StringComparison.OrdinalIgnoreCase));
    if (hasProjectCsproj && Directory.Exists(Path.Combine(dir, "Assets")))
        break;
    dir = Directory.GetParent(dir)?.FullName;
}
string assetsDir = Path.Combine(dir ?? AppContext.BaseDirectory, "Assets");
Directory.CreateDirectory(assetsDir);

// Sizes needed for ICO
int[] icoSizes = [16, 24, 32, 48, 256];

// Generate all sizes as Bitmap
var bitmaps = icoSizes.Select(s => (size: s, bmp: Render(s))).ToList();

// Write individual PNGs for WinUI tile assets
Save(Render(32),  Path.Combine(assetsDir, "AppIcon.png"));
Save(Render(88),  Path.Combine(assetsDir, "Square44x44Logo.scale-200.png"));
Save(Render(300), Path.Combine(assetsDir, "Square150x150Logo.scale-200.png"));
Save(Render(50),  Path.Combine(assetsDir, "StoreLogo.png"));
Save(Render(96),  Path.Combine(assetsDir, "Square44x44Logo.targetsize-48_altform-lightunplated.png"));
Save(Render(48),  Path.Combine(assetsDir, "Square44x44Logo.targetsize-24_altform-unplated.png"));
Save(Render(320), Path.Combine(assetsDir, "Wide310x150Logo.scale-200.png"));
Save(Render(400), Path.Combine(assetsDir, "SplashScreen.scale-200.png"));

// Write ICO (multi-size)
WriteIco(bitmaps.Select(x => x.bmp).ToList(), Path.Combine(assetsDir, "AppIcon.ico"));

foreach (var (size, bmp) in bitmaps) bmp.Dispose();
Console.WriteLine($"Icons written to {assetsDir}");

static Bitmap Render(int size)
{
    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.Clear(Color.Transparent);

    float s = size;
    float cx = s / 2f, cy = s / 2f;
    float r = s * 0.46f;

    // Background circle - dark navy
    using var bgBrush = new SolidBrush(Color.FromArgb(255, 15, 22, 45));
    g.FillEllipse(bgBrush, cx - r, cy - r, r * 2, r * 2);

    if (size >= 24)
    {
        // Chip ring - cyan outline segments (8 tick marks)
        float ringR = r * 0.78f;
        float tickLen = r * 0.14f;
        float tickW = Math.Max(1f, s * 0.035f);
        using var tickPen = new Pen(Color.FromArgb(220, 40, 210, 210), tickW);
        tickPen.StartCap = LineCap.Round;
        tickPen.EndCap = LineCap.Round;
        for (int i = 0; i < 8; i++)
        {
            double angle = i * Math.PI / 4;
            float x1 = cx + (float)Math.Cos(angle) * (ringR - tickLen / 2);
            float y1 = cy + (float)Math.Sin(angle) * (ringR - tickLen / 2);
            float x2 = cx + (float)Math.Cos(angle) * (ringR + tickLen / 2);
            float y2 = cy + (float)Math.Sin(angle) * (ringR + tickLen / 2);
            g.DrawLine(tickPen, x1, y1, x2, y2);
        }

        // Chip ring circle
        float ringStroke = Math.Max(1f, s * 0.04f);
        using var ringPen = new Pen(Color.FromArgb(180, 40, 210, 210), ringStroke);
        g.DrawEllipse(ringPen, cx - ringR, cy - ringR, ringR * 2, ringR * 2);
    }

    // Lightning bolt - bright green, centered
    DrawBolt(g, cx, cy, r * 0.50f);

    return bmp;
}

static void DrawBolt(Graphics g, float cx, float cy, float h)
{
    // Classic lightning bolt: top-right to bottom-left with horizontal jag
    float w = h * 0.55f;
    var pts = new PointF[]
    {
        new(cx + w * 0.15f, cy - h),          // top right
        new(cx - w * 0.05f, cy - h * 0.05f),  // mid left
        new(cx + w * 0.40f, cy - h * 0.05f),  // mid right (jag)
        new(cx - w * 0.15f, cy + h),           // bottom left
        new(cx + w * 0.05f, cy + h * 0.05f),  // mid right low
        new(cx - w * 0.40f, cy + h * 0.05f),  // mid left low (jag)
    };

    using var boltBrush = new LinearGradientBrush(
        new PointF(cx, cy - h), new PointF(cx, cy + h),
        Color.FromArgb(255, 120, 255, 160),
        Color.FromArgb(255, 40, 200, 80));
    g.FillPolygon(boltBrush, pts);

    // Thin white glow edge
    using var edgePen = new Pen(Color.FromArgb(80, 255, 255, 255), Math.Max(0.5f, h * 0.04f));
    g.DrawPolygon(edgePen, pts);
}

static void Save(Bitmap bmp, string path)
{
    bmp.Save(path, ImageFormat.Png);
    bmp.Dispose();
    Console.WriteLine($"  wrote {Path.GetFileName(path)}");
}

static void WriteIco(List<Bitmap> bitmaps, string path)
{
    // ICO format: header + directory + image data (PNG-encoded for sizes >= 256, BMP for smaller)
    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);

    // ICO header
    bw.Write((short)0);           // reserved
    bw.Write((short)1);           // type: ICO
    bw.Write((short)bitmaps.Count);

    var imageData = new List<byte[]>();
    foreach (var bmp in bitmaps)
    {
        using var imgMs = new MemoryStream();
        bmp.Save(imgMs, ImageFormat.Png);
        imageData.Add(imgMs.ToArray());
    }

    // Directory entries (16 bytes each)
    int dataOffset = 6 + bitmaps.Count * 16;
    for (int i = 0; i < bitmaps.Count; i++)
    {
        var bmp = bitmaps[i];
        int sz = bmp.Width; // square
        bw.Write((byte)(sz >= 256 ? 0 : sz));  // width (0 = 256)
        bw.Write((byte)(sz >= 256 ? 0 : sz));  // height
        bw.Write((byte)0);    // color count
        bw.Write((byte)0);    // reserved
        bw.Write((short)1);   // color planes
        bw.Write((short)32);  // bits per pixel
        bw.Write((int)imageData[i].Length);
        bw.Write(dataOffset);
        dataOffset += imageData[i].Length;
    }

    foreach (var data in imageData)
        bw.Write(data);

    File.WriteAllBytes(path, ms.ToArray());
    Console.WriteLine($"  wrote AppIcon.ico ({bitmaps.Count} sizes)");
}
