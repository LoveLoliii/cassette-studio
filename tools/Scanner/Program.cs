using System;
using System.IO;
using System.Xml.Linq;
using System.Linq;

string folder = Path.GetFullPath(args[0]);
var manifest = XDocument.Load(Path.Combine(folder, "manifest.xml"));
var listed = manifest.Root.Element("Assemblies").Elements("Assembly")
    .Select(e => Path.Combine(folder, e.Value)).ToArray();
// Outside Unity the loader has not populated its game-assembly whitelist.
// Recreate that whitelist from the installed game's Managed folder.
string managed = args.Length > 1 ? args[1] :
    @"R:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
var gameAssemblies = System.IO.Directory.GetFiles(managed, "*.dll").Select(Path.GetFileNameWithoutExtension);
var result = ModScanner.Scan(listed, gameAssemblies, folderPath: folder, localFolder: false);
Console.WriteLine("Error: " + result.Error);
Console.WriteLine(result.FormatFindings(30));
Console.WriteLine("Detected capabilities: " + string.Join(", ", result.Capabilities));
Console.WriteLine("Refused: " + result.Refused);
if (result.Refused || result.Capabilities.Count > 0) Environment.Exit(1);
