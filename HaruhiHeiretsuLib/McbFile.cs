﻿using HaruhiHeiretsuLib.Graphics;
using HaruhiHeiretsuLib.Strings;
using Kontract.Models.Archive;
using plugin_shade.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HaruhiHeiretsuLib
{
    public class McbFile
    {
        public Bln BlnFile { get; set; } = new Bln();
        public List<BlnArchiveFileInfo> ArchiveFiles { get; set; }
        public List<StringsFile> StringsFiles { get; set; } = new();
        public List<GraphicsFile> GraphicsFiles { get; set; } = new();
        public List<FileInArchive> LoadedFiles { get; set; } = new();
        Dictionary<ArchiveIndex, Dictionary<int, int>> OffsetIndexDictionaries { get; set; } = new();
        public FontFile FontFile { get; set; }

        private MemoryStream _indexFileStream;
        private MemoryStream _dataFileStream;

        public enum ArchiveIndex
        {
            GRP = 0,
            DAT = 1,
            SCR = 2,
            EVT = 3,
        }

        public McbFile(string indexFile, string dataFile)
        {
            _indexFileStream = new MemoryStream(File.ReadAllBytes(indexFile));
            _dataFileStream = new MemoryStream(File.ReadAllBytes(dataFile));
            ArchiveFiles = BlnFile.Load(_indexFileStream, _dataFileStream).Select(a => (BlnArchiveFileInfo)a).ToList();
        }

        public async Task Save(string indexFile, string dataFile)
        {
            foreach (StringsFile stringsFile in StringsFiles)
            {
                if (stringsFile.Edited)
                {
                    using Stream archiveStream = await ArchiveFiles[stringsFile.Location.parent].GetFileData();
                    BlnSub blnSub = new();
                    List<IArchiveFileInfo> blnSubFiles = (List<IArchiveFileInfo>)blnSub.Load(archiveStream);
                    MemoryStream childFileStream = new(stringsFile.Data.ToArray());
                    blnSubFiles[stringsFile.Location.child].SetFileData(childFileStream);

                    MemoryStream parentFileStream = new();
                    blnSub.Save(parentFileStream, blnSubFiles, leaveOpen: true);
                    ArchiveFiles[stringsFile.Location.parent].SetFileData(parentFileStream);
                }
            }

            foreach (GraphicsFile graphicsFile in GraphicsFiles)
            {
                if (graphicsFile.Edited)
                {
                    using Stream archiveStream = await ArchiveFiles[graphicsFile.Location.parent].GetFileData();
                    BlnSub blnSub = new();
                    List<IArchiveFileInfo> blnSubFiles = (List<IArchiveFileInfo>)blnSub.Load(archiveStream);
                    MemoryStream childFileStream = new(graphicsFile.Data.ToArray());
                    blnSubFiles[graphicsFile.Location.child].SetFileData(childFileStream);

                    MemoryStream parentFileStream = new();
                    blnSub.Save(parentFileStream, blnSubFiles, leaveOpen: true);
                    ArchiveFiles[graphicsFile.Location.parent].SetFileData(parentFileStream);
                }
            }

            if (FontFile is not null && (FontFile.Edited || FontFile.CompressedData is not null))
            {
                byte[] data;
                if (FontFile.CompressedData is not null)
                {
                    data = FontFile.CompressedData;
                }
                else
                {
                    data = FontFile.GetBytes();
                }
                using Stream fontParentStream = await ArchiveFiles[0].GetFileData();
                BlnSub fontBlnSub = new();
                List<IArchiveFileInfo> fontParentSubFiles = (List<IArchiveFileInfo>)fontBlnSub.Load(fontParentStream);
                MemoryStream fontFileStream = new(data);
                fontParentSubFiles[5].SetFileData(fontFileStream);

                MemoryStream fontParentFileStream = new();
                fontBlnSub.Save(fontParentFileStream, fontParentSubFiles, leaveOpen: true);
                ArchiveFiles[0].SetFileData(fontParentFileStream);
            }

            foreach (FileInArchive file in LoadedFiles)
            {
                if (file.Edited)
                {
                    using Stream archiveStream = await ArchiveFiles[file.Location.parent].GetFileData();
                    BlnSub blnSub = new();
                    List<IArchiveFileInfo> blnSubFiles = (List<IArchiveFileInfo>)blnSub.Load(archiveStream);
                    MemoryStream childFileStream = new(file.Data.ToArray());
                    blnSubFiles[file.Location.child].SetFileData(childFileStream);

                    MemoryStream parentFileStream = new();
                    blnSub.Save(parentFileStream, blnSubFiles, leaveOpen: true);
                    ArchiveFiles[file.Location.parent].SetFileData(parentFileStream);
                }
            }

            using FileStream indexFileStream = File.OpenWrite(indexFile);
            using FileStream dataFileStream = File.OpenWrite(dataFile);
            BlnFile.Save(indexFileStream, dataFileStream, ArchiveFiles);
        }

        public async Task AdjustOffsets(string indexFile, string dataFile, string binArchiveAdjustmentFile)
        {
            Dictionary<int, int> offsetAdjustments = new();
            string[] archiveAdjustmentFileLines = File.ReadAllLines(binArchiveAdjustmentFile);

            foreach (string line in archiveAdjustmentFileLines.Skip(1))
            {
                string[] adjustments = line.Split(',');
                offsetAdjustments.Add(int.Parse(adjustments[0]), int.Parse(adjustments[1]));
            }

            await AdjustOffsets(indexFile, dataFile, archiveAdjustmentFileLines[0], offsetAdjustments);
        }

        public async Task AdjustOffsets(string indexFile, string dataFile, string archiveToAdjust, Dictionary<int, int> offsetAdjustments)
        {
            int archiveIndexToAdjust;
            switch (archiveToAdjust)
            {
                case "grp.bin":
                    archiveIndexToAdjust = (int)ArchiveIndex.GRP;
                    break;

                case "dat.bin":
                    archiveIndexToAdjust = (int)ArchiveIndex.DAT;
                    break;

                case "scr.bin":
                    archiveIndexToAdjust = (int)ArchiveIndex.SCR;
                    break;

                case "evt.bin":
                    archiveIndexToAdjust = (int)ArchiveIndex.EVT;
                    break;

                default:
                    Console.WriteLine($"Invalid archive loaded: {archiveToAdjust}");
                    return;
            }

            foreach (IArchiveFileInfo archiveFile in ArchiveFiles)
            {
                using Stream archiveInputStream = await archiveFile.GetFileData();
                BlnSub blnSub = new();
                List<IArchiveFileInfo> blnSubFiles = (List<IArchiveFileInfo>)blnSub.Load(archiveInputStream);
                
                MemoryStream archiveOutputStream = new();
                blnSub.Save(archiveOutputStream, blnSubFiles, archiveIndexToAdjust: archiveIndexToAdjust, offsetAdjustments: offsetAdjustments, leaveOpen: true);
                archiveFile.SetFileData(archiveOutputStream);
            }

            using FileStream indexFileStream = File.OpenWrite(indexFile);
            using FileStream dataFileStream = File.OpenWrite(dataFile);
            BlnFile.Save(indexFileStream, dataFileStream, ArchiveFiles);
        }

        public async Task ResolveGraphicsFileNames()
        {
            Stream archiveFFFFStream = await ArchiveFiles[0].GetFileData();
            BlnSub blnSub = new();
            List<BlnSubArchiveFileInfo> blnSubFiles = blnSub.Load(archiveFFFFStream).Select(f => (BlnSubArchiveFileInfo)f).ToList();

            byte[] textureNameMap = blnSubFiles[57].GetFileDataBytes();
            int numTextures = BitConverter.ToInt32(textureNameMap.Skip(0x10).Take(4).Reverse().ToArray());

            Dictionary<int, string> indexToNameMap = new();
            for (int i = 0; i < numTextures; i++)
            {
                indexToNameMap.Add(BitConverter.ToInt32(textureNameMap.Skip(0x14 * (i + 1)).Take(4).Reverse().ToArray()), Encoding.ASCII.GetString(textureNameMap.Skip(0x14 * (i + 1) + 0x04).TakeWhile(b => b != 0x00).ToArray()));
            }

            foreach (GraphicsFile graphicsFile in GraphicsFiles)
            {
                graphicsFile.TryResolveName(OffsetIndexDictionaries[ArchiveIndex.GRP], indexToNameMap);
            }
        }

        public async Task ResolveScriptFileName()
        {
            Stream archive4200Stream = await ArchiveFiles[75].GetFileData();
            BlnSub blnSub = new();
            List<BlnSubArchiveFileInfo> blnSubFiles = blnSub.Load(archive4200Stream).Select(f => (BlnSubArchiveFileInfo)f).ToList();

            byte[] scriptNameList = blnSubFiles[1].GetFileDataBytes();
            List<string> scriptNames = ScriptFile.ParseScriptListFile(scriptNameList);
            Dictionary<int, string> indexToNameMap = scriptNames.ToDictionary(keySelector: n => scriptNames.IndexOf(n) + 1);

            foreach (StringsFile stringsFile in StringsFiles)
            {
                if (stringsFile.GetType() == typeof(ScriptFile))
                {
                    ScriptFile scriptFile = (ScriptFile)stringsFile;
                    scriptFile.Name = indexToNameMap[OffsetIndexDictionaries[ArchiveIndex.SCR][scriptFile.McbEntryData.archiveOffset]];
                }
            }
        }

        public void LoadIndexOffsetDictionary(string binArchiveFile)
        {
            var archive = ArchiveFile<FileInArchive>.FromFile(binArchiveFile);
            switch (Path.GetFileName(binArchiveFile).ToLower())
            {
                case "grp.bin":
                    if (!OffsetIndexDictionaries.ContainsKey(ArchiveIndex.GRP))
                    {
                        OffsetIndexDictionaries.Add(ArchiveIndex.GRP, new());
                    }
                    OffsetIndexDictionaries[ArchiveIndex.GRP] = new();
                    foreach (var file in archive.Files)
                    {
                        OffsetIndexDictionaries[ArchiveIndex.GRP].Add(file.Offset, file.Index);
                    }
                    ResolveGraphicsFileNames().GetAwaiter().GetResult();
                    break;
                case "dat.bin":
                    if (!OffsetIndexDictionaries.ContainsKey(ArchiveIndex.DAT))
                    {
                        OffsetIndexDictionaries.Add(ArchiveIndex.DAT, new());
                    }
                    OffsetIndexDictionaries[ArchiveIndex.DAT] = new();
                    foreach (var file in archive.Files)
                    {
                        OffsetIndexDictionaries[ArchiveIndex.DAT].Add(file.Offset, file.Index);
                    }
                    break;
                case "scr.bin":
                    if (!OffsetIndexDictionaries.ContainsKey(ArchiveIndex.SCR))
                    {
                        OffsetIndexDictionaries.Add(ArchiveIndex.SCR, new());
                    }
                    OffsetIndexDictionaries[ArchiveIndex.SCR] = new();
                    foreach (var file in archive.Files)
                    {
                        OffsetIndexDictionaries[ArchiveIndex.SCR].Add(file.Offset, file.Index);
                    }
                    ResolveScriptFileName().GetAwaiter().GetResult();
                    break;
                case "evt.bin":
                    if (!OffsetIndexDictionaries.ContainsKey(ArchiveIndex.EVT))
                    {
                        OffsetIndexDictionaries.Add(ArchiveIndex.EVT, new());
                    }
                    OffsetIndexDictionaries[ArchiveIndex.EVT] = new();
                    foreach (var file in archive.Files)
                    {
                        OffsetIndexDictionaries[ArchiveIndex.EVT].Add(file.Offset, file.Index);
                    }
                    break;
            }

        }

        public async Task<Dictionary<int, List<(int, int)>>> GetFileMap(string binArchiveFile)
        {
            Dictionary<int, List<(int, int)>> fileMap = new();
            int archiveIndexToSearch;
            switch (Path.GetFileName(binArchiveFile).ToLower())
            {
                case "grp.bin":
                    archiveIndexToSearch = (int)ArchiveIndex.GRP;
                    break;

                case "dat.bin":
                    archiveIndexToSearch = (int)ArchiveIndex.DAT;
                    break;

                case "scr.bin":
                    archiveIndexToSearch = (int)ArchiveIndex.SCR;
                    break;

                case "evt.bin":
                    archiveIndexToSearch = (int)ArchiveIndex.EVT;
                    break;

                default:
                    Console.WriteLine($"Invalid archive loaded: {binArchiveFile}");
                    return fileMap;
            }

            var binArchive = ArchiveFile<FileInArchive>.FromFile(binArchiveFile);

            for (int i = 0; i < ArchiveFiles.Count; i++)
            {
                Stream fileData = await ArchiveFiles[i].GetFileData();
                BlnSub blnSub = new();
                List<BlnSubArchiveFileInfo> blnSubAfis = blnSub.Load(fileData).Cast<BlnSubArchiveFileInfo>().ToList();
                
                for (int j = 0; j < blnSubAfis.Count; j++)
                {
                    if (blnSubAfis[j].Entry.archiveIndex == archiveIndexToSearch)
                    {
                        int correspondingBinIndex = binArchive.Files.First(f => f.Offset == blnSubAfis[j].Entry.archiveOffset).Index;

                        if (!fileMap.ContainsKey(correspondingBinIndex))
                        {
                            fileMap.Add(correspondingBinIndex, new List<(int, int)>());
                        }

                        fileMap[correspondingBinIndex].Add((i, j));
                        Console.WriteLine($"Mapped {correspondingBinIndex:D4} to MCB {i:D3}-{j:D3}");
                    }
                }
            }

            return fileMap;
        }

        public void LoadFiles(string[] filesLocations)
        {
            LoadFiles(string.Join('\n', filesLocations.Where(l => Regex.IsMatch(l, @"\d{3}-\d{3}")).Select(l => Path.GetFileNameWithoutExtension(l).Replace('-', ','))));
        }

        public void LoadFiles(string fileLoactions)
        {
            foreach (string line in fileLoactions.Replace("\r\n", "\n").Split("\n"))
            {
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                string[] lineSplit = line.Split(',');
                int parentLoc = int.Parse(lineSplit[0]);
                int childLoc = int.Parse(lineSplit[1]);

                using Stream archiveStream = ArchiveFiles[parentLoc].GetFileData().GetAwaiter().GetResult();
                BlnSub blnSub = new();
                BlnSubArchiveFileInfo blnSubFile = (BlnSubArchiveFileInfo)blnSub.GetFile(archiveStream, childLoc);

                byte[] subFileData = blnSubFile.GetFileDataBytes();

                LoadedFiles.Add(new FileInArchive() { Data = subFileData.ToList(), Location = (parentLoc, childLoc), McbId = ArchiveFiles[parentLoc].Entry.id });
            }
        }

        public void LoadStringsFiles(string[] stringsFilesLocations)
        {
            LoadStringsFiles(string.Join('\n', stringsFilesLocations.Where(l => Regex.IsMatch(l, @"\d{3}-\d{3}")).Select(l => Path.GetFileNameWithoutExtension(l).Replace('-', ','))));
        }

        public void LoadStringsFiles(string stringFileLocations, List<ScriptCommand> scriptCommands = null)
        {
            foreach (string line in stringFileLocations.Replace("\r\n", "\n").Split("\n"))
            {
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }
                string[] lineSplit = line.Split(',');
                int parentLoc = int.Parse(lineSplit[0]);
                int childLoc = int.Parse(lineSplit[1]);

                using Stream archiveStream = ArchiveFiles[parentLoc].GetFileData().GetAwaiter().GetResult();
                BlnSub blnSub = new();
                BlnSubArchiveFileInfo blnSubFile = (BlnSubArchiveFileInfo)blnSub.GetFile(archiveStream, childLoc);

                byte[] subFileData = blnSubFile.GetFileDataBytes();
                short mcbId = ArchiveFiles[parentLoc].Entry.id;

                switch ((ArchiveIndex)blnSubFile.Entry.archiveIndex)
                {
                    case ArchiveIndex.DAT:
                        ShadeStringsFile shadeStringsFile = new();
                        shadeStringsFile.Location = (parentLoc, childLoc);
                        shadeStringsFile.McbId = mcbId;
                        shadeStringsFile.McbEntryData = (blnSubFile.Entry.archiveIndex, blnSubFile.Entry.archiveOffset);
                        shadeStringsFile.Initialize(subFileData);
                        StringsFiles.Add(shadeStringsFile);
                        break;
                    case ArchiveIndex.SCR:
                        ScriptFile scriptFile = new(parentLoc, childLoc, subFileData, mcbId);
                        scriptFile.AvailableCommands = scriptCommands;
                        scriptFile.McbEntryData = (blnSubFile.Entry.archiveIndex, blnSubFile.Entry.archiveOffset);
                        scriptFile.PopulateCommandBlocks();
                        StringsFiles.Add(scriptFile);
                        break;
                    case ArchiveIndex.EVT:
                        StringsFiles.Add(new EventFile(parentLoc, childLoc, subFileData, mcbId) { McbEntryData = (blnSubFile.Entry.archiveIndex, blnSubFile.Entry.archiveOffset) });
                        break;
                }
            }
        }

        public void LoadGraphicsFiles(string[] graphicsFilesLocations)
        {
            LoadGraphicsFiles(string.Join('\n', graphicsFilesLocations.Where(l => Regex.IsMatch(l, @"\d{3}-\d{3}")).Select(l => Path.GetFileNameWithoutExtension(l).Replace('-', ','))));
        }

        public void LoadGraphicsFiles(string graphicsFilesLocations)
        {
            foreach (string line in graphicsFilesLocations.Replace("\r\n", "\n").Split("\n"))
            {
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }
                string[] lineSplit = line.Split(',');
                int parentLoc = int.Parse(lineSplit[0]);
                int childLoc = int.Parse(lineSplit[1]);

                using Stream archiveStream = ArchiveFiles[parentLoc].GetFileData().GetAwaiter().GetResult();
                BlnSub blnSub = new();
                BlnSubArchiveFileInfo blnSubFile = (BlnSubArchiveFileInfo)blnSub.GetFile(archiveStream, childLoc);

                byte[] subFileData = blnSubFile.GetFileDataBytes();

                GraphicsFile graphicsFile = new() { Location = (parentLoc, childLoc), McbEntryData = (blnSubFile.Entry.archiveIndex, blnSubFile.Entry.archiveOffset), McbId = ArchiveFiles[parentLoc].Entry.id };
                graphicsFile.Initialize(subFileData, 0);
                graphicsFile.Offset = (int)blnSubFile.Offset;
                GraphicsFiles.Add(graphicsFile);
            }
        }

        public async Task LoadGraphicsFiles()
        {
            for (int parent = 0; parent < ArchiveFiles.Count; parent++)
            {
                Stream mcbData = await ArchiveFiles[parent].GetFileData();
                BlnSub mcb = new();
                List<BlnSubArchiveFileInfo> blnSubFiles = mcb.Load(mcbData).Select(a => (BlnSubArchiveFileInfo)a).ToList();

                for (int child = 0; child < blnSubFiles.Count; child++)
                {
                    if (blnSubFiles[child].Entry.archiveIndex == (int)ArchiveIndex.GRP)
                    {
                        byte[] fileData = blnSubFiles[child].GetFileDataBytes();

                        GraphicsFile graphicsFile = new() { Location = (parent, child), McbEntryData = (blnSubFiles[child].Entry.archiveIndex, blnSubFiles[child].Entry.archiveOffset), McbId = ArchiveFiles[parent].Entry.id };
                        graphicsFile.Initialize(fileData, 0);
                        graphicsFile.Offset = (int)blnSubFiles[child].Offset;
                        GraphicsFiles.Add(graphicsFile);
                    }
                }
            }
        }

        public void LoadFontFile()
        {
            using Stream archiveStream = ArchiveFiles[0].GetFileData().GetAwaiter().GetResult();
            BlnSub blnSub = new();
            IArchiveFileInfo blnSubFile = blnSub.GetFile(archiveStream, 5);

            FontFile = new FontFile(blnSubFile.GetFileDataBytes());
        }

        public async Task<List<(int, int)>> FindStringFiles()
        {
            List<(int, int)> fileLocations = new();

            for (int i = 75; i < ArchiveFiles.Count; i++)
            {
                using Stream fileStream = await ArchiveFiles[i].GetFileData();

                BlnSub blnSub = new();
                List<IArchiveFileInfo> subFiles = (List<IArchiveFileInfo>)blnSub.Load(fileStream);

                for (int j = 0; j < subFiles.Count; j++)
                {
                    byte[] data = subFiles[j].GetFileDataBytes();

                    if (data.Length > 0)
                    {
                        string idBytes = Encoding.ASCII.GetString(data);
                        if (Regex.IsMatch(idBytes, StringsFile.VOICE_REGEX))
                        {
                            fileLocations.Add((i, j));
                            Console.WriteLine($"File {j} in archive {i} contains voiced lines");
                        }
                    }
                }
                Console.WriteLine($"Finished searching {subFiles.Count} files in file {i}");
            }

            return fileLocations;
        }

        public async Task<List<(int, int)>> FindStringInFiles(string search)
        {
            List<(int, int)> fileLocations = new();

            for (int i = 0; i < ArchiveFiles.Count; i++)
            {
                using Stream fileStream = await ArchiveFiles[i].GetFileData();

                BlnSub blnSub = new();
                List<IArchiveFileInfo> subFiles = (List<IArchiveFileInfo>)blnSub.Load(fileStream);

                for (int j = 0; j < subFiles.Count; j++)
                {
                    byte[] data = subFiles[j].GetFileDataBytes();

                    if (data.Length > 0)
                    {
                        string idBytes = Encoding.GetEncoding("Shift-JIS").GetString(data);
                        if (Regex.IsMatch(idBytes, search, RegexOptions.IgnoreCase))
                        {
                            fileLocations.Add((i, j));
                            Console.WriteLine($"File {j} in archive {i} contains string '{search}'");
                        }
                    }
                }
                Console.WriteLine($"Finished searching {subFiles.Count} files in file {i}");
            }

            return fileLocations;
        }

        public async Task<List<(int, int)>> CheckHexInFile(byte[] search)
        {
            List<(int, int)> fileLocations = new();

            for (int i = 0; i < ArchiveFiles.Count; i++)
            {
                using Stream fileStream = await ArchiveFiles[i].GetFileData();

                BlnSub blnSub = new();
                List<IArchiveFileInfo> subFiles = (List<IArchiveFileInfo>)blnSub.Load(fileStream);

                for (int j = 0; j < subFiles.Count; j++)
                {
                    byte[] data = subFiles[j].GetFileDataBytes();

                    if (data.Length > 0)
                    {
                        for (int k = 0; k < data.Length - search.Length; k++)
                        {
                            if (data.Skip(k).Take(search.Length).SequenceEqual(search))
                            {
                                fileLocations.Add((i, j));
                                Console.WriteLine($"File {j} in archive {i} contains sequence '{string.Join(' ', search.Select(b => $"{b:X2}"))}'");
                                break;
                            }
                        }
                    }
                }
                Console.WriteLine($"Finished searching {subFiles.Count} files in file {i}");
            }

            return fileLocations;
        }
    }
}
