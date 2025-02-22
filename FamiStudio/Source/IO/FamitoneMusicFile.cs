﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FamiStudio
{
    public class FamitoneMusicFile
    {
        private Project project;

        private List<string> lines = new List<string>();

        private string db = ".byte";
        private string dw = ".word";
        private string ll = "@";
        private string lo = ".lobyte";

        private List<List<byte>> globalPacketPatternBuffers = new List<List<byte>>();

        private FamiToneKernel kernel = FamiToneKernel.FamiTone2FS;

        private int maxRepeatCount = MaxRepeatCountFT2;

        private const int MinPatternLength = 6;
        private const int MaxRepeatCountFT2FS = 58; // 2 less for release notes.
        private const int MaxRepeatCountFT2   = 60;
        private const int MaxSongs = (256 - 5) / 14;
        private const int MaxPatterns = 128 * MaxSongs;
        private const int MaxPackedPatterns = (5 * MaxPatterns * MaxSongs);

        public enum FamiToneKernel
        {
            FamiTone2FS,
            FamiTone2
        };

        public enum OutputFormat
        {
            NESASM,
            CA65,
            ASM6
        };

        public FamitoneMusicFile(FamiToneKernel kernel)
        {
            this.kernel = kernel;
            this.maxRepeatCount = kernel == FamiToneKernel.FamiTone2FS ? MaxRepeatCountFT2FS : MaxRepeatCountFT2;
        }

        private void CleanupEnvelopes()
        {
            // All instruments must have a volume envelope.
            foreach (var instrument in project.Instruments)
            {
                var env = instrument.Envelopes[Envelope.Volume];
                if (env == null)
                {
                    env = new Envelope(); 
                    instrument.Envelopes[Envelope.Volume] = env;
                }
                if (env.Length == 0)
                {
                    env.Length = 1;
                    env.Loop = -1;
                    env.Values[0] = 15;
                }
            }
        }

        private int OutputHeader(bool separateSongs)
        {
            string name = Utils.MakeNiceAsmName(separateSongs ? project.Songs[0].Name : project.Name);

            lines.Add($";this file for FamiTone2 library generated by FamiStudio");
            lines.Add("");
            lines.Add($"{name}_music_data:");
            lines.Add($"\t{db} {project.Songs.Count}");
            lines.Add($"\t{dw} {ll}instruments");
            lines.Add($"\t{dw} {ll}samples-3");

            int size = 5;

            for (int i = 0; i < project.Songs.Count; i++)
            {
                var song = project.Songs[i];
                var line = $"\t{dw} ";

                for (int chn = 0; chn < 5; ++chn)
                {
                    line += $"{ll}song{i}ch{chn},";
                }

                int tempoPal  = 256 * song.Tempo / (50 * 60 / 24);
                int tempoNtsc = 256 * song.Tempo / (60 * 60 / 24);

                line += $"{tempoPal},{tempoNtsc}";
                lines.Add(line);

                size += 14;
            }

            lines.Add("");

            return size;
        }

        private byte[] ProcessEnvelope(Envelope env, bool allowReleases)
        {
            if (env.IsEmpty)
                return null;

            var data = new byte[256];

            byte ptr = (byte)(allowReleases ? 1 : 0);
            byte ptr_loop = 0xff;
            byte rle_cnt = 0;
            byte prev_val = (byte)(env.Values[0] + 1);//prevent rle match
            bool found_release = false;

            for (int j = 0; j < env.Length; j++)
            {
                byte val;

                if (env.Values[j] < -64)
                    val = unchecked((byte)-64);
                else if (env.Values[j] > 63)
                    val = 63;
                else
                    val = (byte)env.Values[j];

                val += 192;

                if (prev_val != val || j == env.Loop || (allowReleases && j == env.Release) || j == env.Length - 1)
                {
                    if (rle_cnt != 0)
                    {
                        if (rle_cnt == 1)
                        {
                            data[ptr++] = prev_val;
                        }
                        else
                        {
                            while (rle_cnt > 126)
                            {
                                data[ptr++] = 126;
                                rle_cnt -= 126;
                            }

                            data[ptr++] = rle_cnt;
                        }

                        rle_cnt = 0;
                    }

                    if (j == env.Loop) ptr_loop = ptr;

                    if (j == env.Release && allowReleases)
                    {
                        // A release implies the end of the loop.
                        Debug.Assert(ptr_loop != 0xff && data[ptr_loop] >= 128); // Cant be jumping back to the middle of RLE.
                        found_release = true;
                        data[ptr++] = 0;
                        data[ptr++] = ptr_loop;
                        data[0] = ptr;
                    }

                    data[ptr++] = val;

                    prev_val = val;
                }
                else
                {
                    ++rle_cnt;
                }
            }

            if (ptr_loop == 0xff || found_release)
            {
                ptr_loop = (byte)(ptr - 1);
            }
            else
            {
                Debug.Assert(data[ptr_loop] >= 128); // Cant be jumping back to the middle of RLE.
            }

            data[ptr++] = 0;
            data[ptr++] = ptr_loop;

            Array.Resize(ref data, ptr);

            return data;
        }

        private int OutputInstruments()
        {
            // Process all envelope, make unique, etc.
            var uniqueEnvelopes = new SortedList<uint, byte[]>();
            var instrumentEnvelopes = new Dictionary<Envelope, uint>();

            var defaultEnv = new byte[] { 0xc0, 0x00, 0x00 };
            var defaultEnvCRC = CRC32.Compute(defaultEnv);
            uniqueEnvelopes.Add(defaultEnvCRC, defaultEnv);

            foreach (var instrument in project.Instruments)
            {
                for (int i = 0; i < Envelope.Max; i++)
                {
                    var env = instrument.Envelopes[i];
                    var processed = ProcessEnvelope(env, i == Envelope.Volume && kernel == FamiToneKernel.FamiTone2FS);

                    if (processed == null)
                    {
                        instrumentEnvelopes[env] = defaultEnvCRC;
                    }
                    else
                    {
                        uint crc = CRC32.Compute(processed);
                        uniqueEnvelopes[crc] = processed;
                        instrumentEnvelopes[env] = crc;
                    }
                }
            }

            int size = 0;

            // Write instruments
            lines.Add($"{ll}instruments:");

            for (int i = 0; i < project.Instruments.Count; i++)
            {
                var instrument = project.Instruments[i];

                var volumeEnvIdx   = uniqueEnvelopes.IndexOfKey(instrumentEnvelopes[instrument.Envelopes[Envelope.Volume]]);
                var arpeggioEnvIdx = uniqueEnvelopes.IndexOfKey(instrumentEnvelopes[instrument.Envelopes[Envelope.Arpeggio]]);
                var pitchEnvIdx    = uniqueEnvelopes.IndexOfKey(instrumentEnvelopes[instrument.Envelopes[Envelope.Pitch]]);

                lines.Add($"\t{db} ${(instrument.DutyCycle << 6) | 0x30:x2} ;instrument {i:x2} ({instrument.Name})");
                lines.Add($"\t{dw} {ll}env{volumeEnvIdx},{ll}env{arpeggioEnvIdx},{ll}env{pitchEnvIdx}");
                lines.Add($"\t{db} $00");

                size += 2 * 3 + 2;
            }

            lines.Add("");

            // Write samples.
            lines.Add($"{ll}samples:");

            if (project.UsesSamples)
            {
                for (int i = 1; i < project.SamplesMapping.Length; i++)
                {
                    var mapping = project.SamplesMapping[i];
                    var sampleOffset = 0;
                    var sampleSize = 0;
                    var samplePitchAndLoop = 0;
                    var sampleName = "";

                    if (mapping != null && mapping.Sample != null)
                    {
                        sampleOffset = project.GetAddressForSample(mapping.Sample) >> 6;
                        sampleSize = mapping.Sample.Data.Length >> 4;
                        sampleName = $"({mapping.Sample.Name})";
                        samplePitchAndLoop = mapping.Pitch | ((mapping.Loop ? 1 : 0) << 6);
                    }

                    lines.Add($"\t{db} ${sampleOffset:x2}+{lo}(FT_DPCM_PTR),${sampleSize:x2},${samplePitchAndLoop:x2}\t;{i} {sampleName}");
                    size += 3;
                }

                lines.Add("");
            }

            // Write envelopes.
            int idx = 0;
            foreach (var env in uniqueEnvelopes.Values)
            {
                lines.Add($"{ll}env{idx++}:");
                lines.Add($"\t{db} {String.Join(",", env.Select(i => $"${i:x2}"))}");
                size += env.Length;
            }

            return size;
        }

        private void OutputSamples(string filename, string dmcFilename)
        {
            if (project.UsesSamples)
            {
                var sampleData = new byte[project.GetTotalSampleSize()];
                foreach (var sample in project.Samples)
                {
                    Array.Copy(sample.Data, 0, sampleData, project.GetAddressForSample(sample), sample.Data.Length);
                }

                // TODO: Once we have a real project name, we will use that.
                var path = Path.GetDirectoryName(filename);
                var projectname = Utils.MakeNiceAsmName(project.Name);

                if (dmcFilename == null)
                {
                    dmcFilename = Path.Combine(path, projectname + ".dmc");
                }

                File.WriteAllBytes(dmcFilename, sampleData);
            }
        }

        private int FindEffectParam(Song song, int patternIdx, int noteIdx, int effect)
        {
            foreach (var channel in song.Channels)
            {
                var pattern = channel.PatternInstances[patternIdx];
                if (pattern != null && pattern.Notes[noteIdx].Effect == effect)
                {
                    return pattern.Notes[noteIdx].EffectParam;
                }
            }

            return -1;
        }

        private int FindEffectParam(Song song, int effect)
        {
            for (int p = 0; p < song.Length; p++)
            {
                for (int i = 0; i < song.PatternLength; i++)
                {
                    int fx = FindEffectParam(song, p, i, effect);
                    if (fx >= 0)
                    {
                        return fx;
                    }
                }
            }

            return -1;
        }

        private int FindEffectPosition(Song song, int patternIdx, int effect)
        {
            for (int i = 0; i < song.PatternLength; i++)
            {
                var fx = FindEffectParam(song, patternIdx, i, effect);
                if (fx >= 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindSkip(Song song, int patternIdx)
        {
            for (int i = 0; i < song.PatternLength; i++)
            {
                var skip = FindEffectParam(song, patternIdx, i, Note.EffectSkip);
                if (skip >= 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private byte EncodeNoteValue(int channel, int value, int numNotes)
        {
            if (kernel == FamiToneKernel.FamiTone2)
            {
                // 0 = stop, 1 = C-1 ... 63 = D-6
                if (value != 0 && channel != Channel.Noise) value = Math.Max(1, value - 12); 
                return (byte)(((value & 63) << 1) | numNotes);
            }
            else
            {
                if (value == Note.NoteRelease)
                    return (byte)value;

                // 0 = stop, 1 = A0 ... 87 = B7
                if (value != 0)
                {
                    if (channel == Channel.DPCM)
                        value = Utils.Clamp(value - Note.DPCMNoteMin, 1, 63);
                    else if (channel != Channel.Noise)
                        value = Utils.Clamp(value - 9, 1, 87);
                }
                return (byte)(value);
            }
        }

        private int OutputSong(Song song, int songIdx, int speedChannel, int factor, bool test)
        {
            var packedPatternBuffers = new List<List<byte>>(globalPacketPatternBuffers);
            var size = 0;
            var loopPoint = Math.Max(0, FindEffectParam(song, Note.EffectJump)) * factor;
            var emptyPattern = new Pattern(-1, song, 0, "");

            for (int c = 0; c < song.Channels.Length; c++)
            {
                if (!test)
                    lines.Add($"\n{ll}song{songIdx}ch{c}:");

                var channel = song.Channels[c];
                var isSpeedChannel = c == speedChannel;
                var instrument = (Instrument)null;

                if (isSpeedChannel)
                {
                    if (!test)
                        lines.Add($"\t{db} $fb, ${song.Speed:x2}");
                    size += 2;
                }

                var isSkipping = false;

                for (int p = 0; p < song.Length; p++)
                {
                    var pattern = channel.PatternInstances[p] == null ? emptyPattern : channel.PatternInstances[p];
                    var patternBuffer = new List<byte>();

                    // If we had split the song and we find a skip to the next
                    // pattern, we need to ignore the extra patterns we generated.
                    if (isSkipping && (p % factor) != 0)
                    {
                        continue;
                    }

                    if (!test && p == loopPoint)
                    {
                        lines.Add($"{ll}song{songIdx}ch{c}loop:");
                    }

                    var i = 0;
                    var patternLength = FindEffectPosition(song, p, Note.EffectSkip);
                    var jumpFound = false;

                    if (patternLength >= 0)
                    {
                        isSkipping = true;
                    }
                    else
                    {
                        isSkipping = false;

                        patternLength = FindEffectPosition(song, p, Note.EffectJump);
                        if (patternLength >= 0)
                        {
                            jumpFound = true;
                        }
                        else
                        {
                            patternLength = song.PatternLength;
                        }
                    }

                    var numValidNotes = patternLength;

                    while (i < patternLength)
                    {
                        var note = pattern.Notes[i];

                        if (isSpeedChannel)
                        {
                            var speed = FindEffectParam(song, p, i, Note.EffectSpeed);
                            if (speed >= 0)
                            {
                                patternBuffer.Add(0xfb);
                                patternBuffer.Add((byte)speed);
                            }
                        }

                        i++;

                        if (note.HasVolume && kernel == FamiToneKernel.FamiTone2FS)
                        {
                            patternBuffer.Add((byte)(0x70 | note.Volume));
                        }

                        if (note.IsValid)
                        {
                            // Instrument change.
                            if (note.IsMusical && note.Instrument != instrument)
                            {
                                int idx = project.Instruments.IndexOf(note.Instrument);
                                patternBuffer.Add((byte)(0x80 | (idx << 1)));
                                instrument = note.Instrument;
                            }

                            int numNotes = 0;

                            if (kernel == FamiToneKernel.FamiTone2)
                            {
                                // Note -> Empty -> Note special encoding.
                                if (i < patternLength - 1)
                                {
                                    var nextNote1 = pattern.Notes[i + 0];
                                    var nextNote2 = pattern.Notes[i + 1];

                                    var valid1 = nextNote1.IsValid || (isSpeedChannel && FindEffectParam(song, p, i + 0, Note.EffectSpeed) >= 0);
                                    var valid2 = nextNote2.IsValid || (isSpeedChannel && FindEffectParam(song, p, i + 1, Note.EffectSpeed) >= 0);

                                    if (!valid1 && valid2)
                                    {
                                        i++;
                                        numValidNotes--;
                                        numNotes = 1;
                                    }
                                }
                            }

                            patternBuffer.Add(EncodeNoteValue(c, note.Value, numNotes));
                        }
                        else
                        {
                            int numEmptyNotes = 0;

                            while (i < patternLength)
                            {
                                var emptyNote = pattern.Notes[i];

                                if (numEmptyNotes >= maxRepeatCount || emptyNote.IsValid || (emptyNote.HasVolume && kernel == FamiToneKernel.FamiTone2FS) || (isSpeedChannel && FindEffectParam(song, p, i, Note.EffectSpeed) >= 0))
                                    break;

                                i++;
                                numEmptyNotes++;
                            }

                            numValidNotes -= numEmptyNotes;
                            patternBuffer.Add((byte)(0x81 | (numEmptyNotes << 1)));
                        }
                    }

                    int matchingPatternIdx = -1;

                    if (patternBuffer.Count > 0)
                    {
                        if (patternBuffer.Count > 4)
                        {
                            for (int j = 0; j < packedPatternBuffers.Count; j++)
                            {
                                if (packedPatternBuffers[j].SequenceEqual(patternBuffer))
                                {
                                    matchingPatternIdx = j;
                                    break;
                                }
                            }
                        }

                        if (matchingPatternIdx < 0)
                        {
                            if (packedPatternBuffers.Count > MaxPackedPatterns)
                                return -1; // TODO: Error.

                            packedPatternBuffers.Add(patternBuffer);

                            size += patternBuffer.Count;

                            if (!test)
                            {
                                lines.Add($"{ll}ref{packedPatternBuffers.Count - 1}:");
                                lines.Add($"\t{db} {String.Join(",", patternBuffer.Select(x => $"${x:x2}"))}");
                            }
                        }
                        else
                        {
                            if (!test)
                            {
                                lines.Add($"\t{db} $ff,${numValidNotes:x2}");
                                lines.Add($"\t{dw} {ll}ref{matchingPatternIdx}");
                            }

                            size += 4;
                        }
                    }

                    if (jumpFound)
                    {
                        break;
                    }
                }

                if (!test)
                {
                    lines.Add($"\t{db} $fd");
                    lines.Add($"\t{dw} {ll}song{songIdx}ch{c}loop");
                }

                size += 3;
            }

            if (!test)
            {
                globalPacketPatternBuffers = packedPatternBuffers;
            }

            return size;
        }

        private int ProcessAndOutputSong(int songIdx)
        {
            var song = project.Songs[songIdx];

            int minSize = 65536;
            int bestChannel = 0;
            int bestFactor = 1;

            for (int speedChannel = 0; speedChannel < Channel.Count; speedChannel++)
            {
                for (int factor = 1; factor <= song.PatternLength; factor++)
                {
                    if ((song.PatternLength % factor) == 0 && 
                        (song.PatternLength / factor) >= MinPatternLength)
                    {
                        var splitSong = song.Clone();
                        if (splitSong.Split(factor))
                        {
                            int size = OutputSong(splitSong, songIdx, speedChannel, factor, true);

                            if (size < minSize)
                            {
                                minSize = size;
                                bestChannel = speedChannel;
                                bestFactor = factor;
                            }
                        }
                    }
                }
            }

            var bestSplitSong = song.Clone();
            bestSplitSong.Split(bestFactor);

            return OutputSong(bestSplitSong, songIdx, bestChannel, bestFactor, false);
        }
        
        private void SetupFormat(OutputFormat format)
        {
            switch (format)
            {
                case OutputFormat.NESASM:
                    db = ".db";
                    dw = ".dw";
                    ll = ".";
                    lo = "LOW";
                    break;
                case OutputFormat.CA65:
                    db = ".byte";
                    dw = ".word";
                    ll = "@";
                    lo =  ".lobyte";
                    break;
                case OutputFormat.ASM6:
                    db = "db";
                    dw = "dw";
                    ll = "@";
                    lo = "<";
                    break;
            }
        }

        private void RemoveUnsupportedFeatures()
        {
            if (kernel == FamiToneKernel.FamiTone2)
            {
                foreach (var song in project.Songs)
                {
                    foreach (var channel in song.Channels)
                    {
                        foreach (var pattern in channel.Patterns)
                        {
                            for (int i = 0; i < song.PatternLength; i++)
                            {
                                if (pattern.Notes[i].IsRelease)
                                {
                                    pattern.Notes[i].Value = Note.NoteInvalid;
                                }
                            }
                        }
                    }
                }

                foreach (var instrument in project.Instruments)
                {
                    var env = instrument.Envelopes[Envelope.Volume];
                    if (env.Release >= 0)
                    {
                        env.Length  = env.Release;
                        env.Release = -1;
                    }
                }
            }
        }

        private void SetupProject(Project originalProject, int[] songIds)
        {
            // Work on a temporary copy.
            project = originalProject.Clone();
            project.Filename = originalProject.Filename;

            // NULL = All songs.
            if (songIds != null)
            {
                for (int i = 0; i < project.Songs.Count; i++)
                {
                    if (!songIds.Contains(project.Songs[i].Id))
                    {
                        project.DeleteSong(project.Songs[i]);
                        i--;
                    }
                }
            }

            RemoveUnsupportedFeatures();
            project.DeleteUnusedInstruments(); 
        }

        public bool Save(Project originalProject, int[] songIds, OutputFormat format, bool separateSongs, string filename, string dmcFilename)
        {
            SetupProject(originalProject, songIds);
            SetupFormat(format);
            CleanupEnvelopes();
            OutputHeader(separateSongs);
            OutputInstruments();
            OutputSamples(filename, dmcFilename);

            for (int i = 0; i < project.Songs.Count; i++)
            {
                ProcessAndOutputSong(i);
            }

            File.WriteAllLines(filename, lines);

            return true;
        }

        private byte[] ParseAsmFile(string filename, int songOffset, int dpcmOffset)
        {
            var labels = new Dictionary<string, int>();
            var labelsToPatch = new List<Tuple<string, int>>();
            var bytes = new List<byte>();

            string[] lines = File.ReadAllLines(filename);

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                int commentIdx = trimmedLine.IndexOf(';');
                if (commentIdx >= 0)
                {
                    trimmedLine = trimmedLine.Substring(0, commentIdx);
                }

                bool isByte = trimmedLine.StartsWith("db");
                bool isWord = trimmedLine.StartsWith("dw");

                if (isByte || isWord)
                {
                    var splits = trimmedLine.Substring(3).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < splits.Length; i++)
                    {
                        var hex = false;
                        var valStr = splits[i].Trim();
                        var valNum = 0;

                        if (valStr.StartsWith("$"))
                        {
                            hex = true;
                            valStr = valStr.Substring(1).Trim();
                        }

                        if (labels.ContainsKey(valStr))
                        {
                            valNum = labels[valStr];
                        }
                        else
                        {
                            if (valStr.StartsWith("@"))
                            {
                                labelsToPatch.Add(new Tuple<string, int>(valStr, bytes.Count));
                            }
                            else if (valStr.Contains("FT_DPCM_PTR"))
                            {
                                valNum = Convert.ToInt32(valStr.Split('+')[0], 16) + ((dpcmOffset & 0x3fff) >> 6);
                            }
                            else
                            {
                                valNum = Convert.ToInt32(valStr, hex ? 16 : 10);
                            }
                        }

                        if (isByte)
                        {
                            bytes.Add((byte)(valNum & 0xff));
                        }
                        else
                        {
                            bytes.Add((byte)((valNum >> 0) & 0xff));
                            bytes.Add((byte)((valNum >> 8) & 0xff));
                        }
                    }
                }
                else if (trimmedLine.EndsWith(":"))
                {
                    labels[trimmedLine.TrimEnd(':')] = bytes.Count + songOffset;
                }
            }

            foreach (var pair in labelsToPatch)
            {
                int val;
                if (pair.Item1.Contains("-"))
                {
                    var splits = pair.Item1.Split('-');
                    val = labels[splits[0]];
                    val -= Convert.ToInt32(splits[1]);
                }
                else
                {
                    val = labels[pair.Item1];
                }

                bytes[pair.Item2 + 0] = ((byte)((val >> 0) & 0xff));
                bytes[pair.Item2 + 1] = ((byte)((val >> 8) & 0xff));
            }

            return bytes.ToArray();
        }

        // HACK: This is pretty stupid. We write the ASM and parse it to get the bytes. Kind of backwards.
        public bool GetBytes(Project project, int[] songIds, int songOffset, int dpcmOffset, out byte[] songBytes, out byte[] dpcmBytes)
        {
            var tempFolder = Path.Combine(Path.GetTempPath(), "FamiStudio");

            try
            {
                Directory.Delete(tempFolder, true);
            }
            catch {}

            Directory.CreateDirectory(tempFolder);

            var tempAsmFilename = Path.Combine(tempFolder, "nsf.asm");
            var tempDmcFilename = Path.Combine(tempFolder, "nsf.dmc");

            Save(project, songIds, OutputFormat.ASM6, false, tempAsmFilename, tempDmcFilename);

            songBytes = ParseAsmFile(tempAsmFilename, songOffset, dpcmOffset);
            dpcmBytes = project.UsesSamples ? File.ReadAllBytes(tempDmcFilename) : null;

            return true;
        }
    }
}
