using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace CassetteStudio;

public sealed class WorkshopWindow : Window
{
    private readonly Pack pack;
    private readonly string folder;
    private readonly Action save;
    private readonly Func<Task<string?>> build;
    private readonly Dictionary<string, TextBox> fields = new();
    private readonly ComboBox visibility = new() { ItemsSource = new[] { "公开", "仅好友", "隐藏（建议首次测试）" }, SelectedIndex = 2 };
    private readonly Image preview = new() { Width = 160, Height = 160, Stretch = Stretch.Uniform };
    private readonly TextBox summary = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock status = new() { Text = "先编辑资料，再准备发布。", TextWrapping = TextWrapping.Wrap };
    private readonly List<Button> buttons = new();
    private PreparedUpload? prepared;
    private string? preparedSettings;
    private string? preparedFingerprint;
    private bool working;
    private static string ConnectionFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CassetteStudio", "steamcmd.json");

    public WorkshopWindow(Pack pack, string folder, Action save, Func<Task<string?>> build)
    {
        this.pack = pack; this.folder = folder; this.save = save; this.build = build;
        Title = "创意工坊发布 · " + pack.Name; Width = 940; Height = 850; MinWidth = 800; MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
        var panel = new StackPanel { Margin = new Thickness(22) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "Steam 创意工坊", FontSize = 24, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = "准备本次文件 → 核对上传内容 → 手动完成 Steam 登录。不会保存密码或验证码。", Margin = new Thickness(0, 5, 0, 14) });
        Field(panel, "标题", "title", string.IsNullOrWhiteSpace(pack.Workshop.Title) ? pack.Name : pack.Workshop.Title);
        if (string.IsNullOrWhiteSpace(pack.Workshop.Description) || pack.Workshop.Description == pack.Description ||
            (!string.IsNullOrEmpty(pack.Workshop.GeneratedDescription) && pack.Workshop.Description == pack.Workshop.GeneratedDescription)) {
            pack.Workshop.Description = WorkshopService.GenerateDescription(pack);
            pack.Workshop.GeneratedDescription = pack.Workshop.Description;
        }
        Field(panel, "介绍", "description", pack.Workshop.Description, true);
        fields["description"].Height = 160;
        AddButton(panel, "根据当前歌曲生成介绍", () => {
            string generated = WorkshopService.GenerateDescription(pack);
            fields["description"].Text = generated;
            pack.Workshop.GeneratedDescription = generated;
            prepared = null;
            status.Text = "介绍已按当前歌曲顺序、配送天数和响度设置生成，可继续编辑；保存发布资料后生效。";
        });
        Field(panel, "条目 ID（首次发布填 0；更新填写原条目 ID）", "id", pack.Workshop.PublishedFileId);
        panel.Children.Add(new TextBlock { Text = "可见性", Margin = new Thickness(0, 8, 0, 3) });
        visibility.SelectedIndex = pack.Workshop.Visibility; panel.Children.Add(visibility);
        Field(panel, "本次更新说明", "changeNote", pack.Workshop.ChangeNote, true);
        var art = new DockPanel { Margin = new Thickness(0, 10, 0, 8) }; art.Children.Add(preview);
        var artControls = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        artControls.Children.Add(new TextBlock { Text = "工坊封面 · PNG / JPG，小于 1 MB", FontWeight = FontWeights.SemiBold });
        AddButton(artControls, "选择封面", ChoosePreview);
        AddButton(artControls, "保存发布资料", () => { SaveSettings(); status.Text = "发布资料已保存到当前工程。"; });
        art.Children.Add(artControls); panel.Children.Add(art);
        var connection = ReadConnection();
        Field(panel, "SteamCMD 路径（本机设置，不放入发布包）", "steamcmd", connection.Path);
        var connectionButtons = new WrapPanel(); AddButton(connectionButtons, "选择 steamcmd.exe", ChooseSteamCmd);
        AddButton(connectionButtons, "SteamCMD 官方安装说明", () => OpenUrl("https://developer.valvesoftware.com/wiki/SteamCMD"));
        panel.Children.Add(connectionButtons);
        Field(panel, "Steam 登录账号名（不是昵称，密码与验证码在 SteamCMD 窗口输入）", "account", connection.Account);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 8) };
        AddButton(actions, "① 构建并准备发布", async () => await Prepare());
        AddButton(actions, "② 上传 / 更新", async () => await Upload());
        AddButton(actions, "打开工坊条目", OpenItem);
        AddButton(actions, "打开协议页面", () => OpenUrl("https://steamcommunity.com/sharedfiles/workshoplegalagreement"));
        panel.Children.Add(actions); panel.Children.Add(summary);
        status.Margin = new Thickness(0, 10, 0, 0); panel.Children.Add(status);
        RefreshPreview();
        Closing += (_, e) => { if (working) { MessageBox.Show("正在准备或运行 SteamCMD，请等待该操作结束。", "创意工坊"); e.Cancel = true; } };
    }
    private void Field(Panel panel, string label, string key, string value, bool multiline = false)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 3) });
        var input = new TextBox { Text = value, Padding = new Thickness(5), TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            AcceptsReturn = multiline, Height = multiline ? 68 : double.NaN, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden };
        fields[key] = input; panel.Children.Add(input);
    }
    private void AddButton(Panel panel, string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 3, 8, 3) };
        button.Click += (_, _) => { try { action(); } catch (Exception error) { ShowError(error); } };
        buttons.Add(button); panel.Children.Add(button);
    }
    private void ShowError(Exception error) { status.Text = error.Message; MessageBox.Show(error.Message, "创意工坊", MessageBoxButton.OK, MessageBoxImage.Error); }
    private void SetWorking(bool value)
    {
        working = value; foreach (var button in buttons) button.IsEnabled = !value;
        foreach (var field in fields.Values) field.IsEnabled = !value; visibility.IsEnabled = !value;
    }
    private void SaveSettings()
    {
        pack.Workshop.Title = fields["title"].Text;
        pack.Workshop.Description = fields["description"].Text;
        pack.Workshop.PublishedFileId = WorkshopService.ValidateItemId(fields["id"].Text.Trim());
        pack.Workshop.Visibility = visibility.SelectedIndex;
        pack.Workshop.ChangeNote = fields["changeNote"].Text;
        save();
        Directory.CreateDirectory(Path.GetDirectoryName(ConnectionFile)!);
        File.WriteAllText(ConnectionFile, JsonSerializer.Serialize(new Connection(fields["steamcmd"].Text, fields["account"].Text)));
    }
    private sealed record Connection(string Path, string Account);
    private static Connection ReadConnection()
    {
        try { return JsonSerializer.Deserialize<Connection>(File.ReadAllText(ConnectionFile)) ?? new("", ""); }
        catch { return new("", ""); }
    }
    private void ChooseSteamCmd()
    {
        var picker = new OpenFileDialog { Filter = "SteamCMD|steamcmd.exe", Title = "选择 Valve SteamCMD" };
        if (picker.ShowDialog() == true) fields["steamcmd"].Text = picker.FileName;
    }
    private void ChoosePreview()
    {
        var picker = new OpenFileDialog { Filter = "封面图片|*.png;*.jpg;*.jpeg" };
        if (picker.ShowDialog() != true) return;
        if (new FileInfo(picker.FileName).Length >= 1024 * 1024) throw new ArgumentException("封面需小于1 MB，请先压缩图片。");
        string relative = "artwork/workshop-cover-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(picker.FileName).ToLowerInvariant();
        Directory.CreateDirectory(Path.Combine(folder, "artwork")); File.Copy(picker.FileName, Path.Combine(folder, relative));
        pack.Workshop.Preview = relative; prepared = null; RefreshPreview();
    }
    private void RefreshPreview()
    {
        preview.Source = null;
        if (string.IsNullOrWhiteSpace(pack.Workshop.Preview)) return;
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(WorkshopService.SafeFile(folder, pack.Workshop.Preview)); bitmap.EndInit(); bitmap.Freeze(); preview.Source = bitmap;
    }
    private async Task Prepare()
    {
        try {
            SaveSettings(); _ = WorkshopService.SafeFile(folder, pack.Workshop.Preview);
            prepared = null; SetWorking(true); status.Text = "正在构建、扫描并准备本次上传内容…";
            string? output = await build();
            if (output == null) throw new Exception("构建未完成，无法准备发布。请查看主窗口日志。");
            prepared = WorkshopService.Prepare(pack, folder, output);
            preparedSettings = JsonSerializer.Serialize(pack.Workshop, WorkshopService.JsonOptions);
            preparedFingerprint = WorkshopService.Fingerprint(Path.GetDirectoryName(prepared.Vdf)!);
            long bytes = Directory.GetFiles(prepared.Content, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);
            summary.Text = $"标题：{prepared.Title}\n游戏 App ID：{WorkshopService.AppId}\n条目：{(prepared.ItemId == "0" ? "新建" : prepared.ItemId)}\n可见性：{visibility.SelectedItem}\n上传大小：{bytes / 1048576.0:F2} MB\n内容：{prepared.Content}\n配置：{prepared.Vdf}";
            status.Text = "发布内容已准备好。点击上传后会再次展示目标条目和可见性。";
        } catch (Exception error) { ShowError(error); }
        finally { SetWorking(false); }
    }
    private async Task Upload()
    {
        try {
            SaveSettings();
            if (prepared == null || preparedSettings != JsonSerializer.Serialize(pack.Workshop, WorkshopService.JsonOptions))
                throw new Exception("发布资料有变化或尚未准备，请先点击「构建并准备发布」。");
            if (preparedFingerprint != WorkshopService.Fingerprint(Path.GetDirectoryName(prepared.Vdf)!)) throw new Exception("上传文件或配置已变化，请重新准备发布。");
            string executable = fields["steamcmd"].Text.Trim(), account = fields["account"].Text.Trim();
            if (!File.Exists(executable) || !Path.GetFileName(executable).Equals("steamcmd.exe", StringComparison.OrdinalIgnoreCase))
                throw new Exception("请先安装并选择 Valve steamcmd.exe。");
            if (!Regex.IsMatch(account, @"^[A-Za-z0-9_.@-]{1,128}$")) throw new Exception("请输入 Steam 登录账号名，不要输入密码、昵称或验证码。");
            string destination = prepared.ItemId == "0" ? "新建工坊条目" : "更新条目 " + prepared.ItemId;
            if (MessageBox.Show($"将{destination}\n标题：{prepared.Title}\n可见性：{visibility.SelectedItem}\n游戏：Probably Stolen ({WorkshopService.AppId})\n\n继续后会启动 SteamCMD，请在新窗口自行登录。", "确认上传", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            SetWorking(true); status.Text = "SteamCMD 已启动。请在其窗口完成登录与 Steam Guard；结束后返回这里核对条目。";
            var info = new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable)!, WindowStyle = ProcessWindowStyle.Normal };
            foreach (string arg in new[] { "+login", account, "+workshop_build_item", prepared.Vdf, "+quit" }) info.ArgumentList.Add(arg);
            // Intentionally do not redirect or inspect SteamCMD's authentication input/output.
            using var process = Process.Start(info) ?? throw new Exception("无法启动 SteamCMD。");
            await process.WaitForExitAsync();
            string? newId = WorkshopService.ReadPublishedId(prepared.Vdf);
            if (newId != null) {
                pack.Workshop.PublishedFileId = newId; fields["id"].Text = newId; save();
            }
            status.Text = $"SteamCMD 已结束（退出码 {process.ExitCode}）。" + (newId == null ? "尚未取得条目 ID，请检查 SteamCMD 的上传结果。" : "条目 ID 已保存。请打开工坊页面确认上传状态；失败时可重新准备后重试。");
            // Exit code alone cannot prove upload success, particularly for existing items.
            prepared = null;
        } catch (Exception error) { ShowError(error); }
        finally { SetWorking(false); }
    }
    private void OpenItem()
    {
        string id = WorkshopService.ValidateItemId(fields["id"].Text.Trim());
        if (id == "0") throw new Exception("尚无条目 ID，请先上传或填写现有条目 ID。");
        OpenUrl("https://steamcommunity.com/sharedfiles/filedetails/?id=" + id);
    }
    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
