using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CassetteStudio;

public class WorkshopSettings
{
    public string PublishedFileId { get; set; } = "0";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Preview { get; set; } = "";
    public int Visibility { get; set; } = 2;
    public string ChangeNote { get; set; } = "首次发布";
    public string GeneratedDescription { get; set; } = "";
}

public sealed record PreparedUpload(string Vdf, string Content, string Title, string ItemId, int Visibility);

public static class WorkshopService
{
    public const string AppId = "4348910";
    public static string GenerateDescription(Pack pack)
    {
        static string Inline(string value) => value.Replace("\r", " ").Replace("\n", " ").Trim();
        var text = new StringBuilder();
        text.AppendLine(Inline(pack.Name)).AppendLine();
        if (!string.IsNullOrWhiteSpace(pack.Description)) text.AppendLine(pack.Description.Trim()).AppendLine();
        string gift = string.IsNullOrWhiteSpace(pack.GiftName) ? "音乐磁带礼盒" : Inline(pack.GiftName);
        text.AppendLine($"本模组新增「{gift}」，内含 {pack.Tracks.Count} 盘可独立播放的音乐磁带。").AppendLine();
        text.AppendLine("【歌曲列表】");
        for (int index = 0; index < pack.Tracks.Count; index++) {
            string title = Regex.Replace(Inline(pack.Tracks[index].Title), @"^\d+\s*[·、.\-]\s*", "");
            text.AppendLine($"{index + 1:00} · {(title.Length == 0 ? "未命名歌曲" : title)}");
        }
        text.AppendLine().AppendLine("【如何领取】")
            .AppendLine("订阅后，在游戏主菜单的 Mods 中启用本模组，并重新启动游戏。")
            .AppendLine(pack.DeliveryDelayDays == 1
                ? "加载存档后，在下一个游戏日开店，等待磁带配送员来访。"
                : $"加载存档后，等待 {pack.DeliveryDelayDays} 个游戏日再开店，磁带配送员会前来送达。")
            .AppendLine("完成对话即可免费领取礼盒，每个存档领取一次。")
            .AppendLine().AppendLine("【如何播放】")
            .AppendLine("打开礼盒，取出磁带并放入店内的磁带播放器，即可使用原版播放、切歌和音量功能。");
        if (pack.Normalize) text.AppendLine("歌曲已进行响度匹配，减少切歌时的音量差异，并保留原有强弱变化。");
        text.AppendLine().AppendLine("【存档与兼容】")
            .AppendLine("支持已有存档，无需重新开局。")
            .AppendLine("若配送时柜台和背包空间不足，配送员会在后续营业日再次尝试。")
            .AppendLine("无需额外前置模组。");
        return text.ToString().Trim();
    }
    public static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    public static string SafeFile(string folder, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) throw new ArgumentException("请先选择创意工坊封面。");
        string path = Path.GetFullPath(Path.Combine(folder, relative));
        if (!path.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new ArgumentException("封面必须存在于工程文件夹内。");
        return path;
    }
    public static string ValidateItemId(string value)
    {
        if (!Regex.IsMatch(value, @"^\d+$") || !ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId))
            throw new ArgumentException("工坊条目 ID 必须是有效的数字；首次发布填 0。");
        return itemId.ToString(CultureInfo.InvariantCulture);
    }
    public static string Quote(string value)
    {
        if (value.Any(c => c == '\0' || (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')))
            throw new ArgumentException("发布资料含不支持的控制字符。");
        // SteamCMD does not decode literal backslash-n in Workshop metadata.
        // KeyValues quoted strings can span physical lines, so keep real LF.
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r\n", "\n").Replace("\r", "\n") + "\"";
    }
    public static string Fingerprint(string folder)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal)) {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(folder, path)));
            hash.AppendData(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    public static PreparedUpload Prepare(Pack pack, string packFolder, string outputFolder)
    {
        var settings = pack.Workshop;
        string itemId = ValidateItemId(settings.PublishedFileId);
        if (settings.Visibility is < 0 or > 2) throw new ArgumentException("不支持的可见性。");
        if (string.IsNullOrWhiteSpace(settings.Title) || Encoding.UTF8.GetByteCount(settings.Title) > 128)
            throw new ArgumentException("标题不能为空，且 UTF-8 长度不能超过128字节。");
        string preview = SafeFile(packFolder, settings.Preview);
        string extension = Path.GetExtension(preview).ToLowerInvariant();
        if (extension is not ".png" and not ".jpg" and not ".jpeg") throw new ArgumentException("封面只支持 PNG / JPG。");
        if (new FileInfo(preview).Length >= 1024 * 1024) throw new ArgumentException("封面需小于1 MB。");
        string fullOutput = Path.GetFullPath(outputFolder);
        if (!File.Exists(Path.Combine(fullOutput, "manifest.xml"))) throw new ArgumentException("找不到构建产物，请重新准备发布。");
        if (XDocument.Load(Path.Combine(fullOutput, "manifest.xml")).Root?.Element("ID")?.Value != pack.Id)
            throw new ArgumentException("构建产物属于另一个磁带包。");
        // Keep publication files outside the uploaded content, with an immutable
        // content snapshot so another local build cannot change the reviewed files.
        string session = Path.Combine(packFolder, ".workshop", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(session);
        string content = Path.Combine(session, "content");
        foreach (string file in Directory.GetFiles(fullOutput, "*", SearchOption.AllDirectories)) {
            string target = Path.Combine(content, Path.GetRelativePath(fullOutput, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
        string previewCopy = Path.Combine(session, "preview" + extension); File.Copy(preview, previewCopy);
        string vdf = Path.Combine(session, "workshop.vdf");
        var fields = new Dictionary<string, string> {
            ["appid"] = AppId, ["publishedfileid"] = itemId, ["contentfolder"] = content,
            ["previewfile"] = previewCopy, ["visibility"] = settings.Visibility.ToString(CultureInfo.InvariantCulture),
            ["title"] = settings.Title, ["description"] = settings.Description, ["changenote"] = settings.ChangeNote
        };
        string text = "\"workshopitem\"\n{\n" + string.Join("\n", fields.Select(pair => "    " + Quote(pair.Key) + " " + Quote(pair.Value))) + "\n}\n";
        File.WriteAllText(vdf, text, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(session, "review.json"), JsonSerializer.Serialize(new {
            modId = pack.Id, workshopId = itemId, title = settings.Title, visibility = settings.Visibility,
            contentSha256 = Fingerprint(content), preparedAt = DateTimeOffset.Now
        }, JsonOptions), new UTF8Encoding(false));
        return new PreparedUpload(vdf, content, settings.Title, itemId, settings.Visibility);
    }
    public static string? ReadPublishedId(string vdf)
    {
        var match = Regex.Match(File.ReadAllText(vdf), "\"publishedfileid\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        string id = ValidateItemId(match.Groups[1].Value);
        return id == "0" ? null : id;
    }
}
