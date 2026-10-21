using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DawnTrainer;

// Cross-platform exporter: runs the shared UI checks and writes SVG previews without System.Drawing,
// so the checks also run on macOS or Linux. Text widths use an approximate CJK/Latin metric.
internal sealed class SvgCanvas : CheckCanvas
{
    private readonly StringBuilder body = new StringBuilder();
    private readonly float width, height;
    private int clipId;
    internal SvgCanvas(float width, float height) { this.width = width; this.height = height; }
    private static string F(float v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }
    private static string Escape(string s) { return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;"); }
    protected override void DrawFill(UiRect r, string color, float radius)
    {
        float rx = Math.Min(radius, Math.Min(r.W, r.H) / 2);
        body.Append("<rect x=\"" + F(r.X) + "\" y=\"" + F(r.Y) + "\" width=\"" + F(r.W) + "\" height=\"" + F(r.H) + "\" rx=\"" + F(rx) + "\" fill=\"" + color + "\"/>\n");
    }
    protected override void DrawText(UiRect r, string text, int size, string color, bool bold, int align)
    {
        if (text.Length == 0) return;
        float x = align == 1 ? r.X + r.W / 2 : align == 2 ? r.X + r.W : r.X;
        body.Append("<text x=\"" + F(x) + "\" y=\"" + F(r.Y + r.H / 2) + "\" font-size=\"" + size + "\" fill=\"" + color + "\"" + (bold ? " font-weight=\"700\"" : "")
            + " text-anchor=\"" + (align == 1 ? "middle" : align == 2 ? "end" : "start") + "\" dominant-baseline=\"central\">" + Escape(text) + "</text>\n");
    }
    protected override void DrawLine(float x1, float y1, float x2, float y2, string color, float w)
    { body.Append("<line x1=\"" + F(x1) + "\" y1=\"" + F(y1) + "\" x2=\"" + F(x2) + "\" y2=\"" + F(y2) + "\" stroke=\"" + color + "\" stroke-width=\"" + F(w) + "\" stroke-linecap=\"round\"/>\n"); }
    protected override void DrawImage(UiRect r, object image)
    {
        string path = image as string; if (String.IsNullOrEmpty(path) || !File.Exists(path)) return;
        string data = Convert.ToBase64String(File.ReadAllBytes(path));
        body.Append("<image x=\"" + F(r.X) + "\" y=\"" + F(r.Y) + "\" width=\"" + F(r.W) + "\" height=\"" + F(r.H) + "\" preserveAspectRatio=\"xMidYMid meet\" href=\"data:image/png;base64," + data + "\"/>\n");
    }
    protected override void PushClip(UiRect v, float position)
    {
        string id = "clip" + (++clipId);
        body.Append("<clipPath id=\"" + id + "\"><rect x=\"" + F(v.X) + "\" y=\"" + F(v.Y) + "\" width=\"" + F(v.W) + "\" height=\"" + F(v.H) + "\"/></clipPath>\n");
        body.Append("<g clip-path=\"url(#" + id + ")\"><g transform=\"translate(" + F(v.X) + " " + F(v.Y - position) + ")\">\n");
    }
    protected override void PopClip() { body.Append("</g></g>\n"); }
    internal override float Measure(string text, int size, bool bold)
    {
        float total = 0;
        foreach (char ch in text ?? "")
        {
            float em = ch >= 0x2E80 || ch == '…' || ch == '·' ? 1f : ch == ' ' ? .3f : ch == '×' ? .6f : Char.IsDigit(ch) ? .56f : Char.IsUpper(ch) ? .64f : .52f;
            total += em * size;
        }
        return bold ? total * 1.04f : total;
    }
    internal string Svg()
    {
        return "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"" + F(width) + "\" height=\"" + F(height) + "\" viewBox=\"0 0 " + F(width) + " " + F(height)
            + "\" font-family=\"'Microsoft YaHei UI','PingFang SC','Noto Sans CJK SC',sans-serif\">\n" + body + "</svg>\n";
    }
}

internal static class UiSvgPreview
{
    private static int Main(string[] args)
    {
        UiChecks.Create = (w, h) => new SvgCanvas(w, h);
        try
        {
            string catalog = args.Length == 0 ? "--fixtures" : args[0];
            UiChecks.Run(catalog);
            if (args.Length >= 2)
            {
                string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
                foreach (KeyValuePair<string, UiModel> state in UiChecks.PreviewStates(catalog))
                    using (SvgCanvas c = (SvgCanvas)UiChecks.Render(state.Value))
                        File.WriteAllText(Path.Combine(output, state.Key + ".svg"), c.Svg(), new UTF8Encoding(false));
            }
        }
        catch (Exception ex) { UiChecks.Check(false, "unexpected error: " + ex); }
        return UiChecks.Result("SVG");
    }
}
