using System.IO;
using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DriverSFTrafficEditor.Models;

namespace DriverSFTrafficEditor.Core;

public sealed class TrafficDocument
{
    private const int FileSize = 0x433000;
    private const int SpoolMetadata = 0x280A18;
    private const int SpoolPool = 0x281080;
    private const int WeightMetadata = 0x2DDA80;
    private const int WeightData = 0x2DE440;
    private const int TableStride = 0x5F8;
    private const int VehicleCount = 191;
    private const int TableCount = 208;
    private const int SetCount = 58;
    private readonly byte[] _original;

    public string SourcePath { get; }
    public IReadOnlyList<TrafficSet> Sets { get; }
    public IReadOnlyList<WeightProfile> Profiles { get; }
    public string OriginalSha256 => Convert.ToHexString(SHA256.HashData(_original));
    public int SpoolEdits => Sets.SelectMany(s => s.Groups).SelectMany(g => g.Rows).Count(r => r.IsModified);
    public int WeightEdits => Profiles.SelectMany(p => p.Rows).Count(r => r.IsModified);
    public bool HasChanges => SpoolEdits + WeightEdits != 0;

    private TrafficDocument(string sourcePath, byte[] original, List<TrafficSet> sets, List<WeightProfile> profiles)
    { SourcePath = sourcePath; _original = original; Sets = sets; Profiles = profiles; }

    private static ushort U16(byte[] b, int pos) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(pos, 2));
    private static uint U32(byte[] b, int pos) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos, 4));
    private static float F32(byte[] b, int pos) => BitConverter.Int32BitsToSingle(unchecked((int)U32(b, pos)));
    private static void PutU16(byte[] b, int pos, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(pos, 2), v);
    private static void PutF32(byte[] b, int pos, float v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(pos, 4), BitConverter.SingleToUInt32Bits(v));

    public static TrafficDocument Load(string path)
    {
        byte[] raw = File.ReadAllBytes(path);
        if (raw.Length != FileSize || Encoding.ASCII.GetString(raw, 0, 4) != "CHNK" ||
            Encoding.ASCII.GetString(raw, 16, 4) != "AIRT")
            throw new InvalidDataException("Unsupported resource. Expected the 4,403,200-byte retail PC CHNK/AIRT san_franciscoAI.fchunk. Do not use the larger Xbox/pre-release file.");

        var sets = new List<TrafficSet>(SetCount);
        for (int s = 0; s < SetCount; s++)
        {
            int meta = SpoolMetadata + s * 28;
            if (U32(raw, meta) != s)
                throw new InvalidDataException($"Spool record {s} has an unexpected index at 0x{meta:X}.");
            uint hash = U32(raw, meta + 4);
            var groups = new List<SpoolGroup>(5);
            for (int g = 0; g < 5; g++)
            {
                int firstIndex = U16(raw, meta + 8 + g * 2);
                int count = U16(raw, meta + 18 + g * 2);
                int firstOffset = SpoolPool + firstIndex * 2;
                if (firstOffset < SpoolPool || (long)firstOffset + count * 2 > WeightMetadata)
                    throw new InvalidDataException($"Out-of-bounds group at record {s}, group {g}.");
                var entries = new ObservableCollection<SpoolRow>();
                for (int n = 0; n < count; n++)
                {
                    int offset = firstOffset + n * 2;
                    entries.Add(new SpoolRow(n, offset, U16(raw, offset)));
                }
                groups.Add(new SpoolGroup(g, firstOffset, entries));
            }
            sets.Add(new TrafficSet(s, hash, TrafficNames.Resolve(s, hash), meta, groups));
        }

        var profiles = new List<WeightProfile>(ProfileCatalog.All.Count);
        foreach (ProfileDefinition def in ProfileCatalog.All)
        {
            int t = def.Number;
            if (t < 0 || t >= TableCount) throw new InvalidDataException($"Invalid table {t}.");
            int meta = WeightMetadata + t * 12;
            int start = WeightData + t * TableStride;
            if (U32(raw, meta) != t * VehicleCount || U32(raw, meta + 4) != VehicleCount ||
                start + TableStride > raw.Length)
                throw new InvalidDataException($"Profile {t} has unexpected metadata or length.");
            var entries = new ObservableCollection<WeightRow>();
            var seen = new HashSet<uint>();
            for (int n = 0; n < VehicleCount; n++)
            {
                int offset = start + n * 8;
                uint id = U32(raw, offset);
                float weight = F32(raw, offset + 4);
                if (!float.IsFinite(weight) || weight < 0 || !seen.Add(id))
                    throw new InvalidDataException($"Unexpected weight/ID in table {t} at 0x{offset:X}.");
                entries.Add(new WeightRow(n, offset, id, weight));
            }
            float recordedTotal = F32(raw, meta + 8);
            if (!float.IsFinite(recordedTotal) || Math.Abs(entries.Sum(r => r.Weight) - recordedTotal) > 0.01)
                throw new InvalidDataException($"Weight sum mismatch in table {t}.");
            profiles.Add(new WeightProfile(def, meta, start, recordedTotal, entries));
        }
        return new TrafficDocument(Path.GetFullPath(path), raw, sets, profiles);
    }

    public (byte[] Data, string Report) PreparePatch()
    {
        var b = (byte[])_original.Clone();
        var report = new StringBuilder();
        report.AppendLine("Driver: San Francisco — file-level traffic patch report");
        report.AppendLine($"Source: {SourcePath}");
        report.AppendLine($"Original SHA256: {OriginalSha256}");
        report.AppendLine("All offsets are FILE offsets (not runtime memory addresses).");
        report.AppendLine();
        var changes = 0;
        foreach (var set in Sets)
        foreach (var group in set.Groups)
        {
            var ids = new HashSet<int>();
            foreach (var row in group.Rows)
            {
                if (row.VehicleId < 0 || row.VehicleId > ushort.MaxValue)
                    throw new InvalidDataException($"Vehicle ID out of range in {set.Name} / group {group.Index}.");
                if (!ids.Add(row.VehicleId))
                    throw new InvalidDataException($"Duplicate ID {row.VehicleId} in {set.Name} / group {group.Index}. Fix this before saving.");
                if (!row.IsModified) continue;
                PutU16(b, row.FileOffset, checked((ushort)row.VehicleId));
                report.AppendLine($"SPOOL  0x{row.FileOffset:X6}  {set.Name} / G{group.Index} / slot {row.Slot}: {row.OriginalId} -> {row.VehicleId}");
                changes++;
            }
        }
        foreach (var profile in Profiles)
        {
            int edited = 0;
            foreach (var row in profile.Rows)
            {
                if (!double.IsFinite(row.Weight) || row.Weight < 0 || row.Weight > float.MaxValue)
                    throw new InvalidDataException($"Invalid Float32 weight for ID {row.VehicleId} in table {profile.Number}.");
                if (!row.IsModified) continue;
                float val = (float)row.Weight;
                PutF32(b, row.WeightFileOffset, val);
                report.AppendLine($"WEIGHT 0x{row.WeightFileOffset:X6}  table {profile.Number} / ID {row.VehicleId}: {row.OriginalWeight.ToString("G9", CultureInfo.InvariantCulture)} -> {val.ToString("G9", CultureInfo.InvariantCulture)}");
                edited++; changes++;
            }
            if (edited > 0)
            {
                double sum = profile.Rows.Sum(r => (double)(float)r.Weight);
                if (sum < 0 || sum > float.MaxValue || !double.IsFinite(sum))
                    throw new InvalidDataException($"Total float weight overflows in table {profile.Number}.");
                float newTotal = (float)sum;
                PutF32(b, profile.MetadataOffset + 8, newTotal);
                report.AppendLine($"TOTAL  0x{profile.MetadataOffset + 8:X6}  table {profile.Number}: {profile.OriginalTotal.ToString("G9", CultureInfo.InvariantCulture)} -> {newTotal.ToString("G9", CultureInfo.InvariantCulture)}");
            }
        }
        report.AppendLine();
        report.AppendLine($"Changed entry values: {changes} ({SpoolEdits} spooling IDs, {WeightEdits} weight values)");
        report.AppendLine($"Patched SHA256: {Convert.ToHexString(SHA256.HashData(b))}");
        report.AppendLine($"Output length: {b.Length} (original size preserved)");
        report.AppendLine("NOTE: Changing a spooling list does NOT automatically modify probability tables, or vice versa.");
        return (b, report.ToString());
    }

    public void SaveAs(string destination)
    {
        string target = Path.GetFullPath(destination);
        if (string.Equals(target, SourcePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to overwrite the loaded source file. Choose a different filename.");
        if (!HasChanges)
            throw new InvalidOperationException("No changes to save yet.");
        var (result, report) = PreparePatch();
        string dir = Path.GetDirectoryName(target) ?? throw new IOException("Invalid output directory.");
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException(dir);
        string temp = Path.Combine(dir, $".driversf-patch-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, result);
            // Reread the temporary file to verify the same size and readable structures.
            _ = Load(temp);
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        File.WriteAllText(target + ".changes.txt", report, new UTF8Encoding(false));
    }
}
