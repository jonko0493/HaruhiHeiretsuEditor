﻿using HaruhiHeiretsuLib.Util;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HaruhiHeiretsuLib.Archive
{
    public class McbSubArchive
    {
        public ushort Id { get; set; }
        public short Padding { get; set; }
        public int Offset { get; set; }
        public int Size { get; set; }

        public List<FileInArchive> Files { get; set; } = [];

        public McbSubArchive(int parentLoc, ushort id, short padding, int offset, int size, byte[] data)
        {
            Id = id;
            Padding = padding;
            Offset = offset;
            Size = size;
            int childLoc = 0;

            for (int i = Offset; i < Offset + Size;)
            {
                int archiveIndex = IO.ReadIntLE(data, i);
                int archiveOffset = IO.ReadIntLE(data, i + 4);
                int compressedSize = IO.ReadIntLE(data, i + 8);

                if (archiveIndex == 0x7FFF)
                {
                    break;
                }

                byte[] compressedData = data.Skip(i + 12).Take(compressedSize).ToArray();

                Files.Add(new() { Location = (parentLoc, childLoc++), Offset = i, McbId = Id, McbEntryData = (archiveIndex, archiveOffset), CompressedData = compressedData, Data = [.. Helpers.DecompressData(compressedData)] });

                i += compressedSize + 12;
            }
        }

        public byte[] GetBytes()
        {
            List<byte> bytes = [];

            foreach (FileInArchive file in Files)
            {
                bytes.AddRange(BitConverter.GetBytes(file.McbEntryData.ArchiveIndex));
                bytes.AddRange(BitConverter.GetBytes(file.McbEntryData.ArchiveOffset));

                if (!file.Edited)
                {
                    bytes.AddRange(BitConverter.GetBytes(file.CompressedData.Length));
                    bytes.AddRange(file.CompressedData);
                }
                else
                {
                    byte[] compressedData = Helpers.CompressData(file.GetBytes());
                    int padding = (compressedData.Length % 0x800) == 0 ? 0x800 : 0x800 - (compressedData.Length % 0x800);
                    
                    bytes.AddRange(BitConverter.GetBytes(compressedData.Length + padding));
                    bytes.AddRange(compressedData);
                    bytes.AddRange(new byte[padding]);
                }
            }
            bytes.AddRange(BitConverter.GetBytes(0x7FFF)); // end bytes
            bytes.AddRange(new byte[bytes.Count % 0x1000 == 0 ? 0x1000 : 0x1000 - (bytes.Count % 0x1000)]);

            return [.. bytes];
        }
    }
}
