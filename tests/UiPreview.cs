using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using DawnTrainer;

// Windows exporter: runs the shared UI checks and saves PNG previews through System.Drawing.
internal sealed class PreviewCanvas : CheckCanvas
{
    private readonly Bitmap bitmap;
    private readonly Graphics graphics;
    private readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
    private readonly Dictionary<string, System.Drawing.Image> images = new Dictionary<string, System.Drawing.Image>();
    private readonly Stack<GraphicsState> states = new Stack<GraphicsState>();
    internal PreviewCanvas(float width, float height)
    {
        bitmap = new Bitmap((int)width, (int)height);
        graphics = Graphics.FromImage(bitmap); graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.Clear(Color.FromArgb(26, 24, 22));
    }
    private static RectangleF Rect(UiRect r) { return new RectangleF(r.X, r.Y, r.W, r.H); }
    private static Color ColorOf(string value)
    {
        string s = value.TrimStart('#'); int r = Int32.Parse(s.Substring(0, 2), NumberStyles.HexNumber), g = Int32.Parse(s.Substring(2, 2), NumberStyles.HexNumber), b = Int32.Parse(s.Substring(4, 2), NumberStyles.HexNumber);
        return Color.FromArgb(s.Length == 8 ? Int32.Parse(s.Substring(6, 2), NumberStyles.HexNumber) : 255, r, g, b);
    }
    private Font FontOf(int size, bool bold)
    {
        string key = size + ":" + bold; Font font;
        if (!fonts.TryGetValue(key, out font)) { font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); fonts[key] = font; }
        return font;
    }
    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        GraphicsPath path = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d < .2f) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
    protected override void DrawFill(UiRect r, string color, float radius)
    { using (GraphicsPath path = Rounded(Rect(r), radius)) using (SolidBrush brush = new SolidBrush(ColorOf(color))) graphics.FillPath(brush, path); }
    protected override void DrawText(UiRect r, string text, int size, string color, bool bold, int align)
    {
        using (SolidBrush brush = new SolidBrush(ColorOf(color))) using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
        {
            format.Alignment = align == 1 ? StringAlignment.Center : align == 2 ? StringAlignment.Far : StringAlignment.Near;
            format.LineAlignment = StringAlignment.Center; format.FormatFlags = StringFormatFlags.NoWrap;
            format.Trimming = StringTrimming.EllipsisCharacter;
            graphics.DrawString(text, FontOf(size, bold), brush, Rect(r), format);
        }
    }
    protected override void DrawLine(float x1, float y1, float x2, float y2, string color, float width)
    { using (Pen pen = new Pen(ColorOf(color), width)) { pen.StartCap = pen.EndCap = LineCap.Round; graphics.DrawLine(pen, x1, y1, x2, y2); } }
    protected override void DrawImage(UiRect r, object image)
    {
        string path = image as string; if (String.IsNullOrEmpty(path) || !File.Exists(path)) return;
        System.Drawing.Image picture;
        if (!images.TryGetValue(path, out picture)) { picture = System.Drawing.Image.FromFile(path); images[path] = picture; }
        float scale = Math.Min(r.W / picture.Width, r.H / picture.Height), w = picture.Width * scale, h = picture.Height * scale;
        graphics.DrawImage(picture, r.X + (r.W - w) / 2, r.Y + (r.H - h) / 2, w, h);
    }
    protected override void PushClip(UiRect viewport, float position)
    { states.Push(graphics.Save()); graphics.SetClip(Rect(viewport), CombineMode.Intersect); graphics.TranslateTransform(viewport.X, viewport.Y - position); }
    protected override void PopClip() { graphics.Restore(states.Pop()); }
    internal override float Measure(string text, int size, bool bold)
    { using (StringFormat format = new StringFormat(StringFormat.GenericTypographic)) return graphics.MeasureString(text ?? "", FontOf(size, bold), Int32.MaxValue, format).Width; }
    internal void Save(string path) { bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
    // The drawer as it docks on a screen of the given resolution, at its automatic interface size.
    internal void SaveDocked(string path, int width, int height, bool collapsed)
    {
        using (Bitmap screen = new Bitmap(width, height)) using (Graphics g = Graphics.FromImage(screen))
        {
            g.Clear(Color.FromArgb(46, 58, 36)); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float scale = FeaturePolicy.DrawerScale(width, height, 100), x, y;
            FeaturePolicy.DockPosition(TrainerView.DockRight, width / scale, height / scale, TrainerView.PanelWidth(collapsed), TrainerView.PanelHeight(collapsed), out x, out y);
            g.DrawImage(bitmap, x * scale, y * scale, bitmap.Width * scale, bitmap.Height * scale); screen.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
    public override void Dispose() { foreach (Font f in fonts.Values) f.Dispose(); foreach (System.Drawing.Image image in images.Values) image.Dispose(); graphics.Dispose(); bitmap.Dispose(); }
}

internal static class UiPreview
{
    private static int Main(string[] args)
    {
        UiChecks.Create = (w, h) => new PreviewCanvas(w, h);
        try
        {
            string catalog = args.Length == 0 ? "--fixtures" : args[0];
            UiChecks.Run(catalog);
            if (args.Length < 2) return UiChecks.Result("System.Drawing");
            string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            foreach (KeyValuePair<string, UiModel> state in UiChecks.PreviewStates(catalog))
                using (PreviewCanvas c = (PreviewCanvas)UiChecks.Render(state.Value))
                {
                    c.Save(Path.Combine(output, state.Key + ".png"));
                    if (state.Key.StartsWith("04-", StringComparison.Ordinal))
                    { c.SaveDocked(Path.Combine(output, "18-停靠-1280x720.png"), 1280, 720, false); c.SaveDocked(Path.Combine(output, "19-停靠-2560x1440.png"), 2560, 1440, false); }
                }
        }
        catch (Exception ex) { UiChecks.Check(false, "unexpected error: " + ex); }
        return UiChecks.Result("System.Drawing");
    }
}
