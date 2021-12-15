﻿using HaruhiHeiretsuLib;
using Kontract.Models.Archive;
using Mono.Options;
using plugin_shade.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace HaruhiHeiretsuCLI
{
    class Program
    {
        public enum Mode
        {
            HELP,
            EXTRACT_ARCHIVE,
            EXTRACT_LIST_OF_FILES,
            EXTRACT_SGE_FILES,
            EXTRACT_STRING_FILES,
            FIND_STRINGS,
            GENERATE_PATCH,
            HEX_SEARCH,
            STRING_SEARCH,
        }

        public static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        public static async Task MainAsync( string[] args)
        {
            Mode mode = Mode.HELP;
            string file = "", output = "", search = "", fileList = "";
            int archiveIndex = 0;

            OptionSet options = new()
            {
                "Usage: HaruhiHeiretsuCLI -f MCB_FILE",
                { "f|file=", f => file = f },
                { "o|output=", o => output = o },
                { "s|search=", s => search = s },
                { "l|file-list=", l => fileList = l },
                { "i|archive-index=", i => archiveIndex = int.Parse(i) },
                { "extract-archive", m => mode = Mode.EXTRACT_ARCHIVE },
                { "extract-list-of-files", m => mode = Mode.EXTRACT_LIST_OF_FILES },
                { "extract-sge-files", m => mode = Mode.EXTRACT_SGE_FILES },
                { "extract-string-files", m => mode = Mode.EXTRACT_STRING_FILES },
                { "hex-search", m => mode = Mode.HEX_SEARCH },
                { "find-strings", m => mode = Mode.FIND_STRINGS },
                { "string-search", m => mode = Mode.STRING_SEARCH },
                { "generate-patch", m => mode = Mode.GENERATE_PATCH },
                { "h|help", m => mode = Mode.HELP },
            };

            options.Parse(args);

            if (mode == Mode.HELP)
            {
                options.WriteOptionDescriptions(Console.Out);
                return;
            }
            else if (mode == Mode.GENERATE_PATCH)
            {
                GeneratePatch(output);
                return;
            }

            string indexFile = "", dataFile = "";
            if (file.Contains("0"))
            {
                indexFile = file;
                dataFile = file.Replace("0", "1");
            }
            else
            {
                indexFile = file.Replace("1", "0");
                dataFile = file;
            }

            McbFile mcb = new(indexFile, dataFile);

            switch (mode)
            {
                case Mode.EXTRACT_ARCHIVE:
                    await ExtractArchive(mcb, output, archiveIndex);
                    break;

                case Mode.EXTRACT_LIST_OF_FILES:
                    ExtractListOfFiles(mcb, output, fileList);
                    break;

                case Mode.EXTRACT_SGE_FILES:
                    await ExtractSgeFiles(mcb, output);
                    break;

                case Mode.EXTRACT_STRING_FILES:
                    ExtractListOfFiles(mcb, output);
                    break;

                case Mode.FIND_STRINGS:
                    await FindStrings(mcb);
                    break;

                case Mode.HEX_SEARCH:
                    List<byte> searchBytes = new();
                    for (int i = 0; i < search.Length; i += 2)
                    {
                        searchBytes.Add(byte.Parse(search.Substring(i, 2), System.Globalization.NumberStyles.HexNumber));
                    }
                    await HexSearch(mcb, searchBytes.ToArray());
                    break;

                case Mode.STRING_SEARCH:
                    await StringSearch(mcb, search);
                    break;

                default:
                    options.WriteOptionDescriptions(Console.Out);
                    break;
            }
        }

        public static async Task FindStrings(McbFile mcb)
        {
            List<(int, int)> stringFileLocations = await mcb.FindStringFiles();
            using StreamWriter fs = File.CreateText("string_file_locations.csv");
            foreach ((int file, int subFile) in stringFileLocations)
            {
                fs.WriteLine($"{file},{subFile}");
            }
        }

        public static async Task ExtractArchive(McbFile mcb, string outputDirectory, int archive)
        {
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            using Stream archiveStream = await mcb.ArchiveFiles[archive].GetFileData();
            BlnSub blnSub = new();
            List<IArchiveFileInfo> blnSubFiles = (List<IArchiveFileInfo>)blnSub.Load(archiveStream);

            for (int i = 0; i < blnSubFiles.Count; i++)
            {
                File.WriteAllBytes(Path.Combine(outputDirectory, $"{i:D3}.bin"), blnSubFiles[i].GetFileDataBytes());
            }
        }

        public static void ExtractListOfFiles(McbFile mcb, string outputDirectory, string locationsFile = "string_file_locations.csv")
        {
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            string stringFileLocations = File.ReadAllText(locationsFile);
            foreach (string line in stringFileLocations.Split("\r\n"))
            {
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }
                string[] lineSplit = line.Split(',');
                int parentLoc = int.Parse(lineSplit[0]);
                int childLoc = int.Parse(lineSplit[1]);

                using Stream fileStream = mcb.ArchiveFiles[parentLoc].GetFileData().GetAwaiter().GetResult();
                BlnSub blnSub = new();
                IArchiveFileInfo blnSubFile = blnSub.GetFile(fileStream, childLoc);

                byte[] subFileData = blnSubFile.GetFileDataBytes();

                File.WriteAllBytes(Path.Combine(outputDirectory, $"{parentLoc}-{childLoc}.bin"), subFileData);
                Console.WriteLine($"Wrote file {parentLoc}-{childLoc}.bin");
            }
        }

        public static async Task ExtractSgeFiles(McbFile mcb, string outputDirectory)
        {
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            for ( int i = 0; i < mcb.ArchiveFiles.Count; i++)
            {
                using Stream archiveStream = await mcb.ArchiveFiles[i].GetFileData();
                BlnSub blnSub = new();
                List<IArchiveFileInfo> blnSubFiles = (List<IArchiveFileInfo>)blnSub.Load(archiveStream);

                for (int j = 0; j < blnSubFiles.Count; j++)
                {
                    using Stream fileStream = await blnSubFiles[j].GetFileData();
                    byte[] initBytes = new byte[3];
                    fileStream.Read(initBytes, 0, initBytes.Length);
                    fileStream.Position = 0;

                    if (Encoding.ASCII.GetString(initBytes) == "SGE")
                    {
                        if (!Directory.Exists(Path.Combine(outputDirectory, $"{i:D3}")))
                        {
                            Directory.CreateDirectory(Path.Combine(outputDirectory, $"{i:D3}"));
                        }

                        byte[] data = blnSubFiles[j].GetFileDataBytes();

                        // get (first) model name
                        string name = Encoding.ASCII.GetString(data.Skip(0xF0)
                            .SkipWhile(b => (b < '0' || b > '9') && (b < 'A' || b > 'Z'))
                            .TakeWhile(b => (b >= '0' && b <= '9') || (b >= 'A' && b <= 'Z') || b == '_').ToArray());

                        File.WriteAllBytes(Path.Combine(outputDirectory, $"{i:D3}", $"{j:D3}-{name}.sge"), data);
                        Console.WriteLine($"Extracted SGE file {i:D3}/{j:D3}-{name}.sge");
                    }
                }
                Console.WriteLine($"Finished searching {blnSubFiles.Count} files in {i:D3}.bin");
            }
        }

        public static async Task StringSearch(McbFile mcb, string search)
        {
            List<(int, int)> fileLocations = await mcb.FindStringInFiles(search);
            using StreamWriter fs = File.CreateText("search_result_locations.csv");
            foreach ((int file, int subFile) in fileLocations)
            {
                fs.WriteLine($"{file},{subFile}");
            }
        }

        public static async Task HexSearch(McbFile mcb, byte[] search)
        {
            List<(int, int)> fileLocations = await mcb.CheckHexInIdentifier(search);
            using StreamWriter fs = File.CreateText("search_result_locations.csv");
            foreach ((int file, int subFile) in fileLocations)
            {
                fs.WriteLine($"{file},{subFile}");
            }
        }

        public static void GeneratePatch(string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            XmlDocument xml = new();
            XmlElement root = xml.CreateElement("wiidisc");
            root.SetAttribute("version", "1");
            xml.AppendChild(root);

            XmlElement id = xml.CreateElement("id");
            id.SetAttribute("game", "R44J8P");
            root.AppendChild(id);

            XmlElement options = xml.CreateElement("options");
            XmlElement section = xml.CreateElement("section");
            section.SetAttribute("name", "Heiretsu Translation");
            XmlElement option = xml.CreateElement("option");
            option.SetAttribute("name", "Heiretsu Translation");
            XmlElement choice = xml.CreateElement("choice");
            choice.SetAttribute("name", "Enabled");
            XmlElement choicePatch = xml.CreateElement("patch");
            choicePatch.SetAttribute("id", "HeiretsuFolder");
            choice.AppendChild(choicePatch);
            option.AppendChild(choice);
            section.AppendChild(option);
            options.AppendChild(section);
            root.AppendChild(options);

            XmlElement patch = xml.CreateElement("patch");
            patch.SetAttribute("id", "HeiretsuFolder");
            XmlElement folderRecurse = xml.CreateElement("folder");
            folderRecurse.SetAttribute("external", "/Heiretsu/files");
            folderRecurse.SetAttribute("recursive", "true");
            XmlElement folderDisc = xml.CreateElement("folder");
            folderDisc.SetAttribute("external", "/Heiretsu/files");
            folderRecurse.SetAttribute("disc", "/");
            patch.AppendChild(folderRecurse);
            patch.AppendChild(folderDisc);
            root.AppendChild(patch);

            xml.Save(Path.Combine(outputDir, "Heiretsu_base.xml"));
        }
    }
}
