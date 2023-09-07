﻿using HaruhiHeiretsuLib.Archive;
using HaruhiHeiretsuLib.Strings.Events;
using Mono.Options;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace HaruhiHeiretsuCLI
{
    public class ExportEventJsonCommand : Command
    {
        private string _evt, _output;
        private int _index;

        public ExportEventJsonCommand() : base("export-event-json", "Export an event file to JSON")
        {
            Options = new()
            {
                { "e|evt=", "evt.bin", e => _evt = e },
                { "i|index=", "The index of the evt file to export", i => _index = int.Parse(i) },
                { "o|output=", "The location to output the JSON file", o => _output = o },
            };
        }

        public override int Invoke(IEnumerable<string> arguments)
        {
            Options.Parse(arguments);

            BinArchive<EventFile> evt = BinArchive<EventFile>.FromFile(_evt);
            EventFile eventFile = evt.Files.First(f => f.Index == _index);
            File.WriteAllText(_output, JsonSerializer.Serialize(eventFile.CutsceneData, new JsonSerializerOptions() { IncludeFields = true }));

            return 0;
        }
    }
}
