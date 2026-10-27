using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;

var cfg = JsonDocument.Parse(File.ReadAllText("profile.json")).RootElement;

string? Str(string name) =>
    cfg.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
double Num(string name, double fallback) =>
    cfg.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;
static string F(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
static string Esc(string s) => SecurityElement.Escape(s) ?? "";

var rows = cfg.GetProperty("rows").EnumerateArray()
    .Select(r => (Label: r.GetProperty("label").GetString()!, Value: r.GetProperty("value").GetString()!))
    .ToList();
var user = Str("user");
var ascii = File.Exists("ascii.txt") ? File.ReadAllLines("ascii.txt") : Array.Empty<string>();

const int fontSize = 16, lineH = 22, pad = 30, minDots = 4;
var charW = fontSize * Num("charWidthEm", 0.6);
var step = lineH * Num("rowSpacing", 2);
var artScale = Num("artScale", 1);
var cellW = charW * artScale;
var cellH = lineH * artScale;

// ---- columns / layout
var innerCols = rows.Max(r => r.Label.Length + r.Value.Length);
if (user is not null) innerCols = Math.Max(innerCols, "User".Length + user.Length);
innerCols += minDots + 2;
var totalCols = innerCols + 4;

var asciiCols = ascii.Length > 0 ? ascii.Max(l => l.Length) : 0;
var x0 = pad + (asciiCols > 0 ? asciiCols * cellW + 3 * charW : 0);
var rowX = x0 + 2 * charW;

var topRel = user is null ? 0 : step;
var bottomRel = topRel + step * (rows.Count + 1);
var barRel = bottomRel + step;
var infoH = fontSize + barRel + 6;
var artTotalH = ascii.Length * cellH;
var contentH = Math.Max(infoH, artTotalH);
var infoOff = (contentH - infoH) / 2;   // center whichever side is shorter
var artOff = (contentH - artTotalH) / 2;

var y0 = pad + infoOff + fontSize;
var topY = y0 + topRel;
var bottomY = y0 + bottomRel;
var barY = y0 + barRel;
var width = x0 + totalCols * charW + pad;
var height = contentH + pad * 2;

// ---- optional embedded font
var stack = "Consolas, 'Courier New', monospace";
var fontCss = "";
var fontFile = Str("fontFile");
if (!string.IsNullOrWhiteSpace(fontFile) && File.Exists(fontFile))
{
    var family = Str("fontFamily") ?? "ProfileFont";
    var (mime, fmt) = Path.GetExtension(fontFile).ToLowerInvariant() switch
    {
        ".woff2" => ("font/woff2", "woff2"),
        ".woff"  => ("font/woff", "woff"),
        ".otf"   => ("font/otf", "opentype"),
        _        => ("font/ttf", "truetype")
    };
    var b64 = Convert.ToBase64String(File.ReadAllBytes(fontFile));
    fontCss = $"<style>@font-face{{font-family:'{family}';src:url('data:{mime};base64,{b64}') format('{fmt}');}}</style>";
    stack = $"'{family}', {stack}";
}

string[] darkPal  = { "#6e7681", "#ff7b72", "#7ee787", "#e3b341", "#58a6ff", "#d2a8ff", "#76e3ea", "#c9d1d9" };
string[] lightPal = { "#57606a", "#cf222e", "#1a7f37", "#9a6700", "#0969da", "#8250df", "#1b7c83", "#24292f" };

File.WriteAllText("dark_mode.svg",  Render("#0d1117", "#c9d1d9", "#58a6ff", "#6e7681", darkPal));
File.WriteAllText("light_mode.svg", Render("#ffffff", "#24292f", "#0969da", "#8c959f", lightPal));

string Line(double x, double y, int cols, string? fill, string inner)
{
    var f = fill is null ? "" : $" fill=\"{fill}\"";
    return $"""  <text x="{F(x)}" y="{F(y)}"{f} xml:space="preserve" textLength="{F(cols * charW)}" lengthAdjust="spacing">{inner}</text>""";
}

string RowMarkup(string label, string value, string fg, string accent, string dim)
{
    var dots = new string('.', innerCols - label.Length - value.Length - 2);
    return $"""<tspan fill="{accent}">{Esc(label)}</tspan><tspan fill="{dim}"> {dots} </tspan><tspan fill="{fg}">{Esc(value)}</tspan>""";
}

void Art(StringBuilder sb, string color)
{
    for (var r = 0; r < ascii.Length; r++)
    {
        if (ascii[r].Trim().Length == 0) continue;
        var y = pad + artOff + r * cellH + fontSize * artScale;
        sb.AppendLine($"""  <text x="{F(pad)}" y="{F(y)}" font-size="{F(fontSize * artScale)}" fill="{color}" xml:space="preserve" textLength="{F(ascii[r].Length * cellW)}" lengthAdjust="spacing">{Esc(ascii[r])}</text>""");
    }
}

string Render(string bg, string fg, string accent, string dim, string[] pal)
{
    var sb = new StringBuilder();
    sb.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{F(width)}" height="{F(height)}" font-family="{stack}" font-size="{fontSize}">""");
    if (fontCss.Length > 0) sb.AppendLine(fontCss);
    sb.AppendLine($"""  <rect width="{F(width)}" height="{F(height)}" rx="12" fill="{bg}"/>""");

    Art(sb, accent);

    if (user is not null)
        sb.AppendLine(Line(rowX, y0, innerCols, null, RowMarkup("User", user, fg, accent, dim)));

    sb.AppendLine(Line(x0, topY, totalCols, dim, "┌" + new string('─', innerCols + 2) + "┐"));
    for (var i = 0; i < rows.Count; i++)
        sb.AppendLine(Line(rowX, topY + step * (i + 1), innerCols, null,
            RowMarkup(rows[i].Label, rows[i].Value, fg, accent, dim)));
    sb.AppendLine(Line(x0, bottomY, totalCols, dim, "└" + new string('─', innerCols + 2) + "┘"));

    var bar = new StringBuilder();
    var per = totalCols / 8; var extra = totalCols % 8;
    for (var i = 0; i < 8; i++)
        bar.Append($"""<tspan fill="{pal[i]}">{new string('─', per + (i < extra ? 1 : 0))}</tspan>""");
    sb.AppendLine(Line(x0, barY, totalCols, null, bar.ToString()));

    sb.AppendLine("</svg>");
    return sb.ToString();
}