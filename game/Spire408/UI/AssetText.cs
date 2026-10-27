using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;

namespace Spire408.UI;

public static partial class AssetText
{
    private static Dictionary<string, Dictionary<string, string>>? _presentation;
    private static Dictionary<string, Dictionary<string, string>>? _essays;
    private static readonly Dictionary<string, Texture2D> Textures = [];
    [GeneratedRegex(@"@@img\|([^|]+)\|(\d+)\|(\d+)@@")]
    private static partial Regex Images();
    public static string Get(string id, string field, string fallback)
    {
        _presentation ??= JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
            Godot.FileAccess.GetFileAsString("res://Spire408/Rendered/presentation.json")) ?? [];
        return _presentation.TryGetValue(id, out var q) && q.TryGetValue(field, out var text) ? text : Escape(fallback);
    }
    public static string Escape(string text) => text.Replace("[", "[lb]");
    public static string Essay(string id, string field, string fallback)
    {
        _essays ??= JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
            Godot.FileAccess.GetFileAsString("res://Spire408/Rendered/essay-presentation.json")) ?? [];
        return _essays.TryGetValue(id, out var entry) && entry.TryGetValue(field, out var text) ? text : Escape(fallback);
    }
    public static void Fill(RichTextLabel label, string text)
    {
        label.Clear();
        int offset = 0;
        foreach (Match match in Images().Matches(text))
        {
            label.AppendText(text[offset..match.Index]);
            string path = "res://" + match.Groups[1].Value;
            if (!Textures.TryGetValue(path, out var texture))
            {
                var image = new Image();
                byte[] bytes = Godot.FileAccess.GetFileAsBytes(path);
                var ext = path.GetExtension().ToLowerInvariant();
                Error error = ext is "jpg" or "jpeg" ? image.LoadJpgFromBuffer(bytes) : ext == "webp"
                    ? image.LoadWebpFromBuffer(bytes) : image.LoadPngFromBuffer(bytes);
                if (error != Error.Ok) { label.AddText("[图片读取失败]"); offset = match.Index + match.Length; continue; }
                texture = ImageTexture.CreateFromImage(image);
                Textures[path] = texture;
            }
            int width = int.Parse(match.Groups[2].Value), height = int.Parse(match.Groups[3].Value);
            double scale = Math.Min(1, Math.Max(300, label.Size.X - 28) / width);
            label.AddImage(texture, (int)(width * scale), (int)(height * scale));
            offset = match.Index + match.Length;
        }
        label.AppendText(text[offset..]);
    }
}
