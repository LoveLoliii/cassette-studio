using System.IO;
using System.Text.Json;
using CassetteStudio;

namespace WorkshopChecks;
public static class Program
{
    public static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "CassetteStudio-WorkshopTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int passed = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Throws(Action action, string name) { try { action(); } catch (ArgumentException) { Check(true, name); return; } throw new Exception(name); }
        // Temporary fixture files only. No real Steam account or upload is used.
        string packFolder = Path.Combine(root, "包 含空格"), output = Path.Combine(root, "build");
        Directory.CreateDirectory(Path.Combine(packFolder, "artwork")); Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(packFolder, "artwork/cover.png"), new byte[] { 137, 80, 78, 71 });
        File.WriteAllText(Path.Combine(output, "manifest.xml"), "<Manifest><ID>test_pack</ID></Manifest>");
        File.WriteAllBytes(Path.Combine(output, "Test.dll"), new byte[] { 1, 2, 3 });
        var pack = new Pack { Id = "test_pack", Name = "测试包", Workshop = new WorkshopSettings {
            Title = "测试\"标题", Description = "第一行\n第二行", Preview = "artwork/cover.png"
        } };
        var prepared = WorkshopService.Prepare(pack, packFolder, output);
        string text = File.ReadAllText(prepared.Vdf);
        Check(text.Contains("\"appid\" \"4348910\""), "correct game app ID");
        Check(text.Contains("\"publishedfileid\" \"0\""), "first upload creates item");
        Check(text.Contains("\"visibility\" \"2\""), "hidden by default");
        Check(text.Contains("测试\\\"标题") && text.Contains("第一行\n第二行") && !text.Contains("第一行\\n第二行"), "preserve real line breaks in workshop metadata");
        Check(WorkshopService.Quote("第一行\r\n\r\n第二行") == "\"第一行\n\n第二行\"", "Windows line breaks and blank paragraphs remain real newlines");
        Check(File.Exists(Path.Combine(prepared.Content, "Test.dll")) && !File.Exists(Path.Combine(prepared.Content, "workshop.vdf")), "only mod content is uploaded");
        File.WriteAllBytes(Path.Combine(output, "Test.dll"), new byte[] { 8 });
        Check(File.ReadAllBytes(Path.Combine(prepared.Content, "Test.dll")).Length == 3, "immutable build snapshot");
        string fingerprint = WorkshopService.Fingerprint(Path.GetDirectoryName(prepared.Vdf)!);
        File.AppendAllText(prepared.Vdf, "\n//edited");
        Check(fingerprint != WorkshopService.Fingerprint(Path.GetDirectoryName(prepared.Vdf)!), "detect changed VDF before upload");
        Check(WorkshopService.ReadPublishedId(prepared.Vdf) == null, "zero ID is not treated as successful creation");
        File.WriteAllText(prepared.Vdf, "\"workshopitem\" { \"publishedfileid\" \"3831752512\" }");
        Check(WorkshopService.ReadPublishedId(prepared.Vdf) == "3831752512", "read back assigned workshop ID");
        Throws(() => WorkshopService.ValidateItemId("123 +quit"), "reject invalid item ID");
        Throws(() => WorkshopService.SafeFile(packFolder, "../build/Test.dll"), "reject preview path traversal");
        pack.Workshop.Title = "";
        Throws(() => WorkshopService.Prepare(pack, packFolder, output), "reject empty title");
        pack.Workshop.Title = "Title"; pack.Workshop.Visibility = 99;
        Throws(() => WorkshopService.Prepare(pack, packFolder, output), "reject invalid visibility");
        pack.Workshop.Visibility = 2;
        File.WriteAllBytes(Path.Combine(packFolder, "artwork/cover.png"), new byte[1024 * 1024]);
        Throws(() => WorkshopService.Prepare(pack, packFolder, output), "reject oversized preview");
        var saved = JsonSerializer.Serialize(pack, WorkshopService.JsonOptions);
        Check(JsonSerializer.Deserialize<Pack>(saved, WorkshopService.JsonOptions)!.Workshop.PublishedFileId == "0", "workshop settings round trip");
        Check(JsonSerializer.Deserialize<Pack>("{\"name\":\"old project\"}", WorkshopService.JsonOptions)!.Workshop.Visibility == 2, "older projects remain compatible");
        pack.Tracks.Add(new Track { Key = "a", Title = "01 · 第一首" });
        pack.Tracks.Add(new Track { Key = "b", Title = "02 · 第二首" });
        pack.Tracks.Move(1, 0);
        pack.DeliveryDelayDays = 3; pack.Normalize = false;
        string description = WorkshopService.GenerateDescription(pack);
        Check(description.Contains("内含 2 盘") && description.Contains("01 · 第二首\n02 · 第一首".Replace("\n", Environment.NewLine)), "playlist follows current order without duplicate numbering");
        Check(description.Contains("等待 3 个游戏日") && !description.Contains("响度匹配"), "description reflects delivery and audio settings");
        Console.WriteLine($"{passed} checks passed. No SteamCMD process was started.");
    }
}
