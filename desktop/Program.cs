using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace CassetteStudio;

public class Track
{
    public string Key { get; set; } = "t" + Guid.NewGuid().ToString("N")[..10];
    public string Title { get; set; } = "";
    public string TitleEn { get; set; } = "";
    public string Description { get; set; } = "";
    public string DescriptionEn { get; set; } = "";
    public string Audio { get; set; } = "";
    public string Sprite { get; set; } = "cassette1";
}

public class Pack
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "cassette_" + Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "我的磁带包";
    public string Author { get; set; } = Environment.UserName;
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = "音乐磁带礼盒";
    public string GameVersion { get; set; } = "0.46D";
    public string ReceiptKey { get; set; } = "gift_v1";
    public int DeliveryDelayDays { get; set; } = 1;
    public string GiftName { get; set; } = "音乐磁带礼盒";
    public string GiftDescription { get; set; } = "打开礼盒，取出磁带放入播放器。";
    public string Artwork { get; set; } = "artwork/gift_box.png";
    public bool Normalize { get; set; } = true;
    public double TargetLufs { get; set; } = -18;
    public ObservableCollection<Track> Tracks { get; set; } = new();
    public WorkshopSettings Workshop { get; set; } = new();
}

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new Application();
        app.DispatcherUnhandledException += (_, e) => {
            MessageBox.Show(e.Exception.Message, "Cassette Studio", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        app.Run(new StudioWindow(args.FirstOrDefault()));
    }
}

public class StudioWindow : Window
{
    private readonly string root;
    private string? packFile;
    private Pack pack = new();
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly Dictionary<string, TextBox> fields = new();
    private readonly DataGrid songs = new();
    private readonly TextBox log = new();
    private readonly Image artwork = new();
    private readonly TextBlock status = new();
    private readonly CheckBox normalize = new() { Content = "统一曲目响度（保留歌曲动态）", IsChecked = true };
    private readonly List<Button> buttons = new();
    private readonly TextBox game = new() { Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "Probably Stolen") };
    private Process? preview;
    private string? outputFolder;
    private bool dirty;
    private bool busy;

    public StudioWindow(string? initialPack)
    {
        string? cursor = AppContext.BaseDirectory;
        while (cursor != null && !File.Exists(Path.Combine(cursor, "builder", "build_pack.py")))
            cursor = Directory.GetParent(cursor)?.FullName;
        root = cursor ?? throw new InvalidOperationException("找不到项目根目录，请使用 start-studio.ps1 启动。");
        Title = "Cassette Studio · 磁带包工作室";
        Width = 1320; Height = 900; MinWidth = 1080; MinHeight = 760;
        Background = new SolidColorBrush(Color.FromRgb(242, 244, 249));
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var page = new DockPanel { Margin = new Thickness(20) };
        Content = page;
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock { Text = "磁带包工作室", FontSize = 27, FontWeight = FontWeights.Bold });
        heading.Children.Add(new TextBlock { Text = "导入歌曲与礼盒包装 → 编辑资料 → 构建、检查与导出", Foreground = Brushes.SlateGray });
        DockPanel.SetDock(heading, Dock.Top); page.Children.Add(heading);
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        AddButton(toolbar, "新建包", NewPack);
        AddButton(toolbar, "打开工程", () => { if (ConfirmDiscard()) ChoosePack(); });
        AddButton(toolbar, "保存工程", Save);
        AddButton(toolbar, "构建 / 导出 ZIP", async () => await Build(false));
        AddButton(toolbar, "构建并安装", async () => await Build(true));
        AddButton(toolbar, "打开产物目录", () => OpenFolder(outputFolder ?? Path.Combine(root, "dist")));
        AddButton(toolbar, "创意工坊发布", () => {
            Save();
            var window = new WorkshopWindow(pack, PackFolder, Save, async () => {
                await Build(false); return outputFolder;
            }) { Owner = this };
            window.ShowDialog();
        });
        DockPanel.SetDock(toolbar, Dock.Top); page.Children.Add(toolbar);
        var footer = new StackPanel();
        status.Text = "就绪"; status.Margin = new Thickness(0, 8, 0, 6);
        footer.Children.Add(status);
        log.IsReadOnly = true; log.Height = 145; log.TextWrapping = TextWrapping.Wrap;
        log.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        log.Background = new SolidColorBrush(Color.FromRgb(24, 31, 47)); log.Foreground = Brushes.LightGray;
        log.FontFamily = new FontFamily("Consolas"); log.FontSize = 12;
        footer.Children.Add(log); DockPanel.SetDock(footer, Dock.Bottom); page.Children.Add(footer);
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(325) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        page.Children.Add(body);
        var metadataScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 18, 0) };
        var metadata = new StackPanel(); metadataScroll.Content = metadata; body.Children.Add(metadataScroll);
        Field(metadata, "名称", "Name"); Field(metadata, "作者", "Author"); Field(metadata, "版本", "Version");
        Field(metadata, "Mod ID（发布后保持不变）", "Id");
        Field(metadata, "游戏兼容版本", "GameVersion"); Field(metadata, "简介", "Description");
        Field(metadata, "礼盒名称", "GiftName"); Field(metadata, "礼盒介绍", "GiftDescription");
        Field(metadata, "配送延迟天数（1–365）", "DeliveryDelayDays");
        Field(metadata, "领取批次 ID（修改会重新配送）", "ReceiptKey");
        normalize.Margin = new Thickness(0, 10, 0, 2); metadata.Children.Add(normalize);
        normalize.Checked += (_, _) => dirty = true; normalize.Unchecked += (_, _) => dirty = true;
        Field(metadata, "期望响度 LUFS（-30 到 -12）", "TargetLufs");
        metadata.Children.Add(new TextBlock { Text = "峰值余量不足时会自动降低整组目标。", Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap });
        metadata.Children.Add(new TextBlock { Text = "游戏目录", Margin = new Thickness(0, 12, 0, 3) });
        metadata.Children.Add(game);
        AddButton(metadata, "选择游戏目录", () => {
            var picker = new OpenFolderDialog { Title = "选择包含 Probably Stolen.exe 的目录" };
            if (picker.ShowDialog() == true) game.Text = picker.FolderName;
        });
        var right = new DockPanel(); Grid.SetColumn(right, 1); body.Children.Add(right);
        var artArea = new DockPanel { Height = 140, Background = Brushes.White, Margin = new Thickness(0, 0, 0, 10) };
        artwork.Width = 128; artwork.Height = 128; artwork.Margin = new Thickness(6);
        RenderOptions.SetBitmapScalingMode(artwork, BitmapScalingMode.NearestNeighbor);
        artArea.Children.Add(artwork);
        var artControls = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(15, 0, 0, 0) };
        artControls.Children.Add(new TextBlock { Text = "礼盒包装", FontSize = 17, FontWeight = FontWeights.SemiBold });
        artControls.Children.Add(new TextBlock { Text = "导入 PNG，自动缩放为 64×64，保留透明背景。" });
        AddButton(artControls, "导入包装 PNG", ImportArtwork); artArea.Children.Add(artControls);
        DockPanel.SetDock(artArea, Dock.Top); right.Children.Add(artArea);
        var songBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) };
        AddButton(songBar, "添加歌曲", ImportSongs); AddButton(songBar, "删除选中", RemoveSong);
        AddButton(songBar, "上移", () => Move(-1)); AddButton(songBar, "下移", () => Move(1));
        AddButton(songBar, "试听原曲", Preview); AddButton(songBar, "停止试听", StopPreview);
        DockPanel.SetDock(songBar, Dock.Top); right.Children.Add(songBar);
        var tip = new TextBlock { Text = "双击表格编辑曲名和简介。稳定 ID 不随排序改变；新包自动使用独立 Mod ID。", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(tip, Dock.Top); right.Children.Add(tip);
        songs.AutoGenerateColumns = false; songs.CanUserAddRows = false; songs.CanUserDeleteRows = false;
        songs.CanUserSortColumns = false; songs.SelectionMode = DataGridSelectionMode.Single;
        songs.Columns.Add(Column("稳定 ID", "Key", 85, true));
        songs.Columns.Add(Column("曲名", "Title", 200));
        songs.Columns.Add(Column("英文曲名（可选）", "TitleEn", 150));
        songs.Columns.Add(Column("简介", "Description", 250));
        songs.Columns.Add(Column("英文简介（可选）", "DescriptionEn", 200));
        songs.Columns.Add(Column("音频文件", "Audio", 230, true));
        songs.CellEditEnding += (_, _) => dirty = true;
        right.Children.Add(songs);
        Closing += (_, e) => {
            if (busy) { MessageBox.Show("正在构建，请等待任务结束。", "Cassette Studio"); e.Cancel = true; return; }
            if (!ConfirmDiscard()) { e.Cancel = true; return; } StopPreview();
        };
        string localPack = Path.Combine(root, "packs", "ffxiv-vol1", "pack.json");
        var defaultPack = initialPack ?? (File.Exists(localPack) ? localPack : Path.Combine(root, "packs", "demo", "pack.json"));
        if (File.Exists(defaultPack)) LoadPack(defaultPack); else BindPack();
    }

    private static DataGridTextColumn Column(string title, string path, double width, bool readOnly = false) => new() {
        Header = title, Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
        Width = width, IsReadOnly = readOnly
    };
    private void AddButton(Panel panel, string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 3, 7, 3) };
        button.Click += (_, _) => { try { action(); } catch (Exception error) { ShowError(error); } };
        buttons.Add(button); panel.Children.Add(button);
    }
    private void Field(Panel panel, string caption, string key)
    {
        panel.Children.Add(new TextBlock { Text = caption, Margin = new Thickness(0, 8, 0, 3) });
        var input = new TextBox { Padding = new Thickness(5) }; fields[key] = input;
        input.TextChanged += (_, _) => dirty = true; panel.Children.Add(input);
    }
    private void BindPack()
    {
        foreach (var (key, input) in fields) input.Text = Convert.ToString(typeof(Pack).GetProperty(key)!.GetValue(pack), System.Globalization.CultureInfo.InvariantCulture) ?? "";
        normalize.IsChecked = pack.Normalize; songs.ItemsSource = pack.Tracks;
        RefreshArtwork(); dirty = false;
        status.Text = packFile == null ? "新工程" : packFile;
        Title = "Cassette Studio · " + pack.Name;
    }
    private void ReadFields()
    {
        songs.CommitEdit(DataGridEditingUnit.Cell, true); songs.CommitEdit(DataGridEditingUnit.Row, true);
        foreach (var (key, input) in fields) {
            var property = typeof(Pack).GetProperty(key)!;
            object value = property.PropertyType == typeof(int) ? int.Parse(input.Text) :
                property.PropertyType == typeof(double) ? double.Parse(input.Text, System.Globalization.CultureInfo.InvariantCulture) : input.Text;
            property.SetValue(pack, value);
        }
        pack.Normalize = normalize.IsChecked == true;
    }
    private bool ConfirmDiscard() => !dirty || MessageBox.Show("放弃尚未保存的修改？", "Cassette Studio", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private void LoadPack(string file)
    {
        var loaded = JsonSerializer.Deserialize<Pack>(File.ReadAllText(file), JsonOptions) ?? throw new Exception("无法读取工程。");
        if (loaded.SchemaVersion != 1) throw new Exception("不支持此工程格式版本。");
        loaded.Workshop ??= new WorkshopSettings();
        pack = loaded; packFile = Path.GetFullPath(file); outputFolder = null; BindPack();
    }
    private void ChoosePack()
    {
        var picker = new OpenFileDialog { Filter = "磁带包工程|pack.json|JSON|*.json", InitialDirectory = Path.Combine(root, "packs") };
        if (picker.ShowDialog() == true) LoadPack(picker.FileName);
    }
    private void NewPack()
    {
        if (!ConfirmDiscard()) return;
        var picker = new OpenFolderDialog { Title = "选择工程存放位置（会自动创建独立子文件夹）", InitialDirectory = Path.Combine(root, "packs") };
        if (picker.ShowDialog() != true) return;
        pack = new Pack();
        var newFolder = Path.Combine(picker.FolderName, pack.Id);
        Directory.CreateDirectory(newFolder);
        packFile = Path.Combine(newFolder, "pack.json"); outputFolder = null;
        var artFolder = Path.Combine(Path.GetDirectoryName(packFile)!, "artwork"); Directory.CreateDirectory(artFolder);
        File.Copy(Path.Combine(root, "assets", "default-gift-box.png"), Path.Combine(artFolder, "gift_box.png"), true);
        BindPack(); Save();
    }
    private void Save()
    {
        if (packFile == null) throw new Exception("请先新建或打开工程。");
        ReadFields(); File.WriteAllText(packFile, JsonSerializer.Serialize(pack, JsonOptions), new UTF8Encoding(false));
        dirty = false; status.Text = "已保存 · " + packFile;
    }
    private string PackFolder => Path.GetDirectoryName(packFile ?? throw new Exception("请先新建或打开工程。"))!;
    private string SafeAsset(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(PackFolder, relative));
        if (!path.StartsWith(Path.GetFullPath(PackFolder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new Exception("资源必须在工程文件夹内。");
        return path;
    }
    private void ImportSongs()
    {
        var folder = PackFolder;
        var picker = new OpenFileDialog { Filter = "音频|*.ogg;*.wav;*.mp3;*.flac;*.m4a", Multiselect = true };
        if (picker.ShowDialog() != true) return;
        Directory.CreateDirectory(Path.Combine(folder, "audio"));
        string[] sprites = { "cassette1", "cassette10", "cassette3", "cassette4", "cassette5", "cassette6", "cassette7", "cassette8", "cassette9" };
        foreach (string file in picker.FileNames) {
            var track = new Track { Title = Path.GetFileNameWithoutExtension(file), Sprite = sprites[pack.Tracks.Count % sprites.Length] };
            track.Audio = "audio/" + track.Key + Path.GetExtension(file).ToLowerInvariant();
            File.Copy(file, SafeAsset(track.Audio), false); pack.Tracks.Add(track);
        }
        dirty = true;
    }
    private void RemoveSong()
    {
        if (songs.SelectedItem is Track track) { pack.Tracks.Remove(track); dirty = true; }
    }
    private void Move(int delta)
    {
        if (songs.SelectedItem is not Track track) return;
        int old = pack.Tracks.IndexOf(track), next = old + delta;
        if (next < 0 || next >= pack.Tracks.Count) return;
        pack.Tracks.Move(old, next); songs.SelectedItem = track; dirty = true;
    }
    private void ImportArtwork()
    {
        _ = PackFolder;
        var picker = new OpenFileDialog { Filter = "透明 PNG|*.png" };
        if (picker.ShowDialog() != true) return;
        var bitmap = LoadBitmap(picker.FileName);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        double scale = Math.Min(64.0 / bitmap.PixelWidth, 64.0 / bitmap.PixelHeight);
        double w = bitmap.PixelWidth * scale, h = bitmap.PixelHeight * scale;
        using (var drawing = visual.RenderOpen()) drawing.DrawImage(bitmap, new Rect((64-w)/2, (64-h)/2, w, h));
        var rendered = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32); rendered.Render(visual);
        Directory.CreateDirectory(Path.Combine(PackFolder, "artwork"));
        pack.Artwork = "artwork/gift_box.png";
        using (var stream = File.Create(SafeAsset(pack.Artwork))) {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(rendered)); encoder.Save(stream);
        }
        RefreshArtwork(); dirty = true;
    }
    private static BitmapImage LoadBitmap(string path)
    {
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(Path.GetFullPath(path)); image.EndInit(); image.Freeze(); return image;
    }
    private void RefreshArtwork()
    {
        artwork.Source = null;
        if (packFile != null && File.Exists(SafeAsset(pack.Artwork))) artwork.Source = LoadBitmap(SafeAsset(pack.Artwork));
    }
    private void Preview()
    {
        StopPreview(); if (songs.SelectedItem is not Track track) return;
        var info = new ProcessStartInfo("ffplay") { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "-nodisp", "-autoexit", "-loglevel", "quiet", SafeAsset(track.Audio) }) info.ArgumentList.Add(arg);
        preview = Process.Start(info);
    }
    private void StopPreview() { if (preview is { HasExited: false }) preview.Kill(); preview?.Dispose(); preview = null; }
    private void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }
    private void Append(string line) { log.AppendText(line + Environment.NewLine); log.ScrollToEnd(); }
    private void ShowError(Exception error) { Append("错误：" + error.Message); MessageBox.Show(error.Message, "Cassette Studio", MessageBoxButton.OK, MessageBoxImage.Error); }
    private async Task Build(bool install)
    {
        if (busy) return;
        try {
            Save(); StopPreview(); busy = true; outputFolder = null;
            foreach (var button in buttons) button.IsEnabled = false;
            songs.IsEnabled = false; foreach (var field in fields.Values) field.IsEnabled = false;
            normalize.IsEnabled = false; game.IsEnabled = false;
            log.Clear(); status.Text = "正在构建：首次处理音频需要一些时间…";
            var info = new ProcessStartInfo("python") {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            info.Environment["PYTHONUTF8"] = "1";
            foreach (string arg in new[] { "-u", Path.Combine(root, "builder", "build_pack.py"), packFile!, "--game", game.Text }) info.ArgumentList.Add(arg);
            if (install) info.ArgumentList.Add("--install");
            using var process = new Process { StartInfo = info };
            process.Start();
            async Task Pump(StreamReader reader) {
                while (await reader.ReadLineAsync() is string line) {
                    Append(line);
                    if (line.StartsWith("RESULT_JSON:")) {
                        using var result = JsonDocument.Parse(line[12..]);
                        outputFolder = result.RootElement.GetProperty("package").GetString();
                    }
                }
            }
            await Task.WhenAll(Pump(process.StandardOutput), Pump(process.StandardError), process.WaitForExitAsync());
            if (process.ExitCode != 0) throw new Exception("构建或安装失败，请查看下方日志。");
            status.Text = install ? "构建、扫描和安装完成。产物目录旁已生成 ZIP。" : "构建和扫描完成，产物目录旁已生成 ZIP。";
        }
        catch (Exception error) { status.Text = "操作失败"; ShowError(error); }
        finally {
            busy = false; foreach (var button in buttons) button.IsEnabled = true;
            songs.IsEnabled = true; foreach (var field in fields.Values) field.IsEnabled = true;
            normalize.IsEnabled = true; game.IsEnabled = true;
        }
    }
}
