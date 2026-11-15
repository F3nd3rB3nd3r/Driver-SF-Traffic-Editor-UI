using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DriverSFTrafficEditor.Models;

namespace DriverSFTrafficEditor.Core;

public sealed class TrafficDocument
{
    public sealed record CapacityRequirement(string Setting, int Index, int Frequency, int NativeIds, int PositiveIds, int UnionIds);
    private readonly byte[] _original;
    private readonly ChunkResource _container;
    private readonly int _root, _payloadSize, _weightData, _weightCount;
    private readonly uint[][] _originalIds;
    private readonly float[][] _originalWeights;
    private readonly HashSet<NotifyRow> _subscribed = new();
    private bool _batch;
    private readonly Dictionary<(int Setting, int Frequency), int[]> _routedTables = new();
    public event EventHandler? Changed;
    public string SourcePath { get; }
    public IReadOnlyList<TrafficSet> Sets { get; }
    public IReadOnlyList<WeightProfile> Profiles { get; }
    public string OriginalSha256 => Convert.ToHexString(SHA256.HashData(_original));
    public int SpoolEdits => Sets.SelectMany(s => s.Groups).SelectMany(g => g.Rows).Count(r => r.IsModified);
    public int WeightEdits => Profiles.Sum(ProfileEdits);
    public bool HasChanges => SpoolEdits != 0 || Profiles.Any(p => ProfileEdits(p) != 0);

    private TrafficDocument(string path, byte[] raw, ChunkResource container, int root, int payloadSize,
        int weightData, int weightCount, List<TrafficSet> sets, List<WeightProfile> profiles)
    {
        SourcePath = Path.GetFullPath(path); _original = raw; _container = container;
        _root = root; _payloadSize = payloadSize; _weightData = weightData; _weightCount = weightCount;
        Sets = sets; Profiles = profiles;
        _originalIds = profiles.Select(p => p.Rows.Select(r => r.VehicleId).ToArray()).ToArray();
        _originalWeights = profiles.Select(p => p.Rows.Select(r => (float)r.Weight).ToArray()).ToArray();
        int groups = checked(root + (int)U32(raw, root + 0xEC));
        int routes = checked(root + (int)U32(raw, root + 0x12C));
        int groupCount = checked((int)U32(raw, root + 0xE8)), routeCount = checked((int)U32(raw, root + 0x128));
        if (groups < root + 0x180 || routes < root + 0x180 || (long)groups + groupCount * 12L > root + payloadSize ||
            (long)routes + routeCount * 20L > root + payloadSize)
            throw new InvalidDataException("Invalid traffic group/route arrays.");
        foreach (var set in sets) foreach (var group in set.Groups.Where(g => g.Count > 0))
        {
            var tables = new HashSet<int>();
            for (int g = 0; g < groupCount; g++)
            {
                int route = checked(U16(raw, groups + g * 12 + 8) + set.Index);
                if (route >= routeCount) throw new InvalidDataException("Traffic route index exceeds the route array.");
                int table = raw[routes + route * 20 + 13 + group.Index];
                if (table >= profiles.Count) throw new InvalidDataException("Traffic route selects an absent table.");
                tables.Add(table);
            }
            _routedTables[(set.Index, group.Index)] = tables.Order().ToArray();
        }
        foreach (var p in profiles) p.Rows.CollectionChanged += (_, _) => { if (!_batch) SubscribeRows(); NotifyChanged(); };
        SubscribeRows();
    }

    private void SubscribeRows()
    {
        var current = Sets.SelectMany(s => s.Groups).SelectMany(g => g.Rows).Cast<NotifyRow>()
            .Concat(Profiles.SelectMany(p => p.Rows)).ToHashSet();
        foreach (var old in _subscribed.Except(current).ToArray()) { old.PropertyChanged -= RowChanged; _subscribed.Remove(old); }
        foreach (var row in current.Except(_subscribed)) { row.PropertyChanged += RowChanged; _subscribed.Add(row); }
    }
    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(WeightRow.Weight) or nameof(SpoolRow.VehicleId)) NotifyChanged(); }
    private void NotifyChanged() { if (!_batch) Changed?.Invoke(this, EventArgs.Empty); }

    public IReadOnlyList<CapacityRequirement> CapacityRequirements()
    {
        var positive = Profiles.Select(p => p.Rows.Where(r => (float)r.Weight > 0).Select(r => r.VehicleId).ToHashSet()).ToArray();
        var result = new List<CapacityRequirement>();
        foreach (var set in Sets) foreach (var group in set.Groups.Where(g => g.Count > 0))
        {
            var native = group.Rows.Select(r => checked((uint)r.VehicleId)).ToHashSet();
            var weighted = new HashSet<uint>();
            foreach (int table in _routedTables[(set.Index, group.Index)]) weighted.UnionWith(positive[table]);
            var union = new HashSet<uint>(native); union.UnionWith(weighted);
            result.Add(new CapacityRequirement(set.Name, set.Index, group.Index, native.Count, weighted.Count, union.Count));
        }
        return result;
    }

    public string CapacityReport()
    {
        var rows = CapacityRequirements();
        var report = new StringBuilder();
        report.AppendLine("UNLIMITER ASSETWEIGHTS — ROUTED REQUEST COUNTS");
        report.AppendLine($"Largest native + positive-weight union: {rows.Select(r => r.UnionIds).DefaultIfEmpty().Max()} IDs");
        report.AppendLine("RequestCapacity must cover this union; allow separate audio/transition/shared-pool headroom.");
        report.AppendLine("This is static routing coverage, not a measurement of asset RAM or proof of game support. Native requests are included; duplicate IDs count once. Empty native frequency groups are skipped, matching Unlimiter R3/R4.");
        foreach (var r in rows) report.AppendLine($"[{r.Index}] {r.Setting} / frequency {r.Frequency}: native {r.NativeIds}, weighted {r.PositiveIds}, union {r.UnionIds}");
        return report.ToString();
    }
    private int ProfileEdits(WeightProfile p)
    {
        var ids = _originalIds[p.Number]; var weights = _originalWeights[p.Number];
        int edits = Math.Abs(ids.Length - p.Rows.Count);
        for (int i = 0; i < Math.Min(ids.Length, p.Rows.Count); ++i)
            if (ids[i] != p.Rows[i].VehicleId || BitConverter.SingleToUInt32Bits(weights[i]) != BitConverter.SingleToUInt32Bits((float)p.Rows[i].Weight)) edits++;
        return edits;
    }

    public int AddVehicles(IEnumerable<WeightProfile> targets, IEnumerable<uint> vehicleIds, float initialWeight)
    {
        if (!float.IsFinite(initialWeight) || initialWeight < 0) throw new ArgumentOutOfRangeException(nameof(initialWeight));
        uint[] ids = vehicleIds.Distinct().ToArray();
        if (ids.Any(id => id == 0 || id > ushort.MaxValue)) throw new ArgumentException("Vehicle IDs must be 1 through 65535.");
        var profiles = targets.Distinct().ToArray();
        if (profiles.Any(p => !Profiles.Contains(p))) throw new ArgumentException("Table belongs to another document.");
        int added = 0; _batch = true;
        try
        {
            foreach (var p in profiles)
            {
                var existing = p.Rows.Select(r => r.VehicleId).ToHashSet();
                foreach (uint id in ids)
                    if (existing.Add(id)) { p.Rows.Add(new WeightRow(p.Rows.Count, -1, id, initialWeight)); added++; }
            }
        }
        finally { _batch = false; SubscribeRows(); NotifyChanged(); }
        return added;
    }

    public int RemoveVehicles(IEnumerable<WeightProfile> targets, IEnumerable<uint> vehicleIds)
    {
        var profiles = targets.Distinct().ToArray();
        if (profiles.Any(p => !Profiles.Contains(p))) throw new ArgumentException("Table belongs to another document.");
        var ids = vehicleIds.ToHashSet(); int removed = 0; _batch = true;
        try { foreach (var p in profiles) foreach (var row in p.Rows.Where(r => ids.Contains(r.VehicleId)).ToArray()) { p.Rows.Remove(row); removed++; } }
        finally { _batch = false; SubscribeRows(); NotifyChanged(); }
        return removed;
    }

    public sealed record WeightUpdateResult(int Updated, int Added, int MissingSkipped);

    public WeightUpdateResult SetVehicleWeights(IEnumerable<WeightProfile> targets, IEnumerable<uint> vehicleIds,
        float weight, bool addMissing)
    {
        if (!float.IsFinite(weight) || weight < 0) throw new ArgumentOutOfRangeException(nameof(weight));
        uint[] ids = vehicleIds.Distinct().ToArray();
        if (ids.Any(id => id is 0 or > ushort.MaxValue)) throw new ArgumentException("Vehicle IDs must be 1 through 65535.");
        var profiles = targets.Distinct().ToArray();
        if (profiles.Any(p => !Profiles.Contains(p))) throw new ArgumentException("Table belongs to another document.");
        var selected = ids.ToHashSet();
        // Check every destination before changing any rows. One overflowing total
        // must not leave the first tables updated and later ones untouched.
        foreach (var p in profiles)
        {
            var existing = p.Rows.Select(r => r.VehicleId).ToHashSet();
            if (existing.Count != p.Rows.Count) throw new InvalidDataException($"Table {p.Number} contains duplicate IDs. No tables were changed.");
            double sum = p.Rows.Sum(r => selected.Contains(r.VehicleId) ? (double)weight : (float)r.Weight);
            if (addMissing) sum += (ids.Length - ids.Count(existing.Contains)) * (double)weight;
            if (!double.IsFinite(sum) || sum > float.MaxValue)
                throw new InvalidDataException($"The new total weight would overflow Float32 in table {p.Number}. No tables were changed.");
        }
        int updated = 0, added = 0, skipped = 0; _batch = true;
        try
        {
            foreach (var p in profiles)
            {
                var existing = p.Rows.ToDictionary(r => r.VehicleId);
                foreach (uint id in ids)
                {
                    if (existing.TryGetValue(id, out var row))
                    {
                        if ((float)row.Weight == weight) continue;
                        row.Weight = weight; updated++;
                    }
                    else if (addMissing) { p.Rows.Add(new WeightRow(p.Rows.Count, -1, id, weight)); added++; }
                    else skipped++;
                }
            }
        }
        finally { _batch = false; SubscribeRows(); NotifyChanged(); }
        return new WeightUpdateResult(updated, added, skipped);
    }

    private static ushort U16(byte[] b, int p) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p, 2));
    private static uint U32(byte[] b, int p) => ChunkResource.U32(b, p);
    private static float F32(byte[] b, int p) => BitConverter.UInt32BitsToSingle(U32(b, p));
    private static void Put32(byte[] b, int p, uint v) => ChunkResource.Put32(b, p, v);
    private static void PutFloat(byte[] b, int p, float v) => Put32(b, p, BitConverter.SingleToUInt32Bits(v));

    public static TrafficDocument Load(string path)
    {
        try { return Parse(path, File.ReadAllBytes(path)); }
        catch (OverflowException ex) { throw new InvalidDataException("Resource count or offset exceeds the supported PC format.", ex); }
    }

    private static TrafficDocument Parse(string path, byte[] raw)
    {
        var chunk = ChunkResource.Parse(raw); var matches = chunk.Find("AIEX").ToArray();
        if (chunk.Children.Count != 1 || chunk.Children[0].Tag != "AIRT" || matches.Length != 1)
            throw new InvalidDataException("Expected a retail PC CHNK/AIRT resource with one AIEX payload.");
        var (parent, leaf) = matches[0];
        if ((U32(raw, parent.FileOffset + leaf.Descriptor + 8) & 0xFF) != 0x16 || leaf.Size < 0x180)
            throw new InvalidDataException("Unsupported AIEX version; expected retail PC version 0x16.");
        int root = checked(parent.FileOffset + leaf.Offset), end = checked(root + leaf.Size);
        (int Count, int Offset) ReadArray(int field, int stride)
        {
            int count = checked((int)U32(raw, root + field));
            int offset = checked(root + (int)U32(raw, root + field + 4));
            if (offset < root + 0x180 || (long)offset + (long)count * stride > end)
                throw new InvalidDataException($"AIEX array 0x{field:X} is out of bounds.");
            return (count, offset);
        }
        var settings = ReadArray(0x118, 28); var ids = ReadArray(0x120, 2);
        var meta = ReadArray(0x158, 12); var weights = ReadArray(0x160, 8);
        if (settings.Count is < 1 or > 256 || meta.Count is < 1 or > 256)
            throw new InvalidDataException("Setting/table counts exceed the byte-sized native selectors.");
        var ranges = new[] { (settings.Offset, settings.Count * 28L), (ids.Offset, ids.Count * 2L), (meta.Offset, meta.Count * 12L), (weights.Offset, weights.Count * 8L) };
        for (int i = 0; i < ranges.Length; i++) for (int j = i + 1; j < ranges.Length; j++)
            if (ranges[i].Item1 < ranges[j].Item1 + ranges[j].Item2 && ranges[j].Item1 < ranges[i].Item1 + ranges[i].Item2)
                throw new InvalidDataException("Overlapping traffic arrays.");
        for (int field = 0x50; field <= 0x178; field += 8)
        {
            if (U32(raw, root + field) == 0 || field is 0x118 or 0x120 or 0x158 or 0x160) continue;
            long start = (long)root + U32(raw, root + field + 4);
            if (ranges.Any(a => start >= a.Item1 && start < a.Item1 + a.Item2))
                throw new InvalidDataException("Writable traffic arrays overlap another declared array.");
        }
        var sets = new List<TrafficSet>();
        for (int s = 0; s < settings.Count; s++)
        {
            int m = settings.Offset + s * 28; uint hash = U32(raw, m);
            if (raw[m + 24] != s) throw new InvalidDataException($"Unexpected native selector in setting {s}.");
            var groups = new List<SpoolGroup>();
            for (int g = 0; g < 5; g++)
            {
                int first = U16(raw, m + 4 + g * 2), count = U16(raw, m + 14 + g * 2);
                if ((long)first + count > ids.Count) throw new InvalidDataException($"Invalid spool range in setting {s}, group {g}.");
                var entries = new ObservableCollection<SpoolRow>();
                for (int n = 0; n < count; n++) entries.Add(new SpoolRow(n, ids.Offset + (first + n) * 2, U16(raw, ids.Offset + (first + n) * 2)));
                groups.Add(new SpoolGroup(g, ids.Offset + first * 2, entries));
            }
            sets.Add(new TrafficSet(s, hash, TrafficNames.Resolve(s, hash), m, groups));
        }
        var profiles = new List<WeightProfile>();
        for (int t = 0; t < meta.Count; t++)
        {
            int m = meta.Offset + t * 12;
            int first = checked((int)U32(raw, m)), count = checked((int)U32(raw, m + 4));
            if ((long)first + count > weights.Count) throw new InvalidDataException($"Invalid weight range in table {t}.");
            var rows = new ObservableCollection<WeightRow>(); var seen = new HashSet<uint>();
            for (int n = 0; n < count; ++n)
            {
                int offset = weights.Offset + (first + n) * 8;
                uint id = U32(raw, offset); float weight = F32(raw, offset + 4);
                if (id is 0 or > ushort.MaxValue || !seen.Add(id) || !float.IsFinite(weight) || weight < 0)
                    throw new InvalidDataException($"Invalid/duplicate vehicle ID or weight in table {t}.");
                rows.Add(new WeightRow(n, offset, id, weight));
            }
            float total = F32(raw, m + 8); double sum = rows.Sum(r => r.Weight);
            if (!float.IsFinite(total) || total < 0 || Math.Abs(sum - total) > Math.Max(0.01, sum * 0.00001))
                throw new InvalidDataException($"Weight sum mismatch in table {t}.");
            var def = ProfileCatalog.All.FirstOrDefault(p => p.Number == t) ?? new ProfileDefinition(t, Array.Empty<ProfileObservation>());
            profiles.Add(new WeightProfile(def, m, weights.Offset + first * 8, total, rows));
        }
        return new TrafficDocument(path, raw, chunk, root, leaf.Size, weights.Offset, weights.Count, sets, profiles);
    }

    public (byte[] Data, string Report) PreparePatch()
    {
        var report = new StringBuilder();
        report.AppendLine($"Driver: San Francisco traffic edit report\nSource: {SourcePath}\nOriginal SHA256: {OriginalSha256}");
        report.AppendLine("Loaded offsets refer to the SOURCE; relocated OUTPUT offsets are listed separately.");
        var body = _original.AsSpan(_root, _payloadSize).ToArray();
        var spoolWrites = new Dictionary<int, ushort>();
        foreach (var set in Sets) foreach (var group in set.Groups)
        {
            var seen = new HashSet<int>();
            foreach (var row in group.Rows)
            {
                if (row.VehicleId < 0 || row.VehicleId > ushort.MaxValue || !seen.Add(row.VehicleId))
                    throw new InvalidDataException($"Invalid/duplicate ID in {set.Name}, group {group.Index}.");
                ushort value = checked((ushort)row.VehicleId);
                if (spoolWrites.TryGetValue(row.FileOffset, out var other) && other != value)
                    throw new InvalidDataException("Native groups share a spool slot with conflicting edits. Give shared slots the same ID.");
                spoolWrites[row.FileOffset] = value;
                if (row.IsModified) report.AppendLine($"SPOOL 0x{row.FileOffset:X}: {row.OriginalId} -> {value} ({set.Name}/G{group.Index})");
            }
        }
        foreach (var w in spoolWrites) BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(w.Key - _root, 2), w.Value);
        bool resize = Profiles.Any(p => p.Rows.Count != _originalIds[p.Number].Length);
        int newOffset = _weightData - _root, rowCount = checked(Profiles.Sum(p => p.Rows.Count));
        if (resize)
        {
            // On later saves, resize the appended tail rather than orphaning another copy.
            bool tail = (long)_weightData + _weightCount * 8L == (long)_root + _payloadSize;
            newOffset = tail ? _weightData - _root : checked((body.Length + 31) & -32);
            Array.Resize(ref body, checked(newOffset + checked(rowCount * 8)));
            if (!tail && newOffset > _payloadSize) body.AsSpan(_payloadSize, newOffset - _payloadSize).Fill(0x3E);
            Put32(body, 0x160, checked((uint)rowCount)); Put32(body, 0x164, checked((uint)newOffset));
        }
        int next = 0; var writes = new Dictionary<int, (uint Id, uint Weight)>();
        foreach (var p in Profiles)
        {
            var seen = new HashSet<uint>(); double sum = 0;
            for (int i = 0; i < p.Rows.Count; i++)
            {
                var row = p.Rows[i];
                if (row.VehicleId is 0 or > ushort.MaxValue || !seen.Add(row.VehicleId) || !double.IsFinite(row.Weight) || row.Weight < 0 || row.Weight > float.MaxValue)
                    throw new InvalidDataException($"Invalid/duplicate ID or Float32 weight in table {p.Number}.");
                float weight = (float)row.Weight; sum += weight;
                int offset = resize ? newOffset + (next + i) * 8 : p.DataOffset - _root + i * 8;
                var value = (row.VehicleId, BitConverter.SingleToUInt32Bits(weight));
                if (writes.TryGetValue(offset, out var other) && other != value)
                    throw new InvalidDataException("Weight tables share storage with conflicting edits; change a row count to rebuild independent storage.");
                writes[offset] = value;
            }
            if (!double.IsFinite(sum) || sum > float.MaxValue) throw new InvalidDataException($"Total weight overflows Float32 in table {p.Number}.");
            int m = p.MetadataOffset - _root;
            if (resize) { Put32(body, m, checked((uint)next)); Put32(body, m + 4, checked((uint)p.Rows.Count)); }
            if (ProfileEdits(p) != 0)
            {
                PutFloat(body, m + 8, (float)sum);
                var oldIds = _originalIds[p.Number].ToHashSet(); var newIds = p.Rows.Select(r => r.VehicleId).ToHashSet();
                report.AppendLine($"TABLE {p.Number}: {_originalIds[p.Number].Length} -> {p.Rows.Count} rows; total {p.OriginalTotal:G9} -> {(float)sum:G9}");
                report.AppendLine($"  Added IDs: {string.Join(", ", newIds.Except(oldIds))}\n  Removed IDs: {string.Join(", ", oldIds.Except(newIds))}");
                foreach (var row in p.Rows.Where(r => r.IsModified)) report.AppendLine($"  ID {row.VehicleId}: {row.OriginalWeight:G9} -> {(float)row.Weight:G9}{(row.IdFileOffset < 0 ? " (new row)" : $" (loaded offset 0x{row.WeightFileOffset:X})")}");
            }
            next = checked(next + p.Rows.Count);
        }
        foreach (var w in writes) { Put32(body, w.Key, w.Value.Id); Put32(body, w.Key + 4, w.Value.Weight); }
        byte[] result = HasChanges ? _container.Replace(_root, body) : (byte[])_original.Clone();
        if (HasChanges)
        {
            var output = Parse(SourcePath, result);
            foreach (var p in output.Profiles.Where(p => resize || ProfileEdits(p) != 0))
                report.AppendLine($"OUTPUT TABLE {p.Number}: metadata 0x{p.MetadataOffset:X}; rows 0x{p.DataOffset:X}; count {p.Rows.Count}");
        }
        report.AppendLine($"Output length: {result.Length} (source {_original.Length}); weight array {(resize ? "rebuilt" : "retained")}");
        report.AppendLine($"Patched SHA256: {Convert.ToHexString(SHA256.HashData(result))}");
        report.AppendLine("Probability rows do not install models or increase game capacity. Unlimiter R3 AssetWeights adds routed positive-weight IDs to native requests.");
        report.AppendLine(CapacityReport());
        return (result, report.ToString());
    }

    public void SaveAs(string destination)
    {
        string target = Path.GetFullPath(destination);
        if (string.Equals(target, SourcePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose a different filename; the loaded source is protected.");
        if (!HasChanges) throw new InvalidOperationException("No changes to save.");
        var (data, report) = PreparePatch();
        string dir = Path.GetDirectoryName(target) ?? throw new IOException("Invalid output directory.");
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException(dir);
        string temp = Path.Combine(dir, $".driversf-patch-{Guid.NewGuid():N}.tmp");
        try { File.WriteAllBytes(temp, data); _ = Load(temp); File.Move(temp, target, overwrite: true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        File.WriteAllText(target + ".changes.txt", report, new UTF8Encoding(false));
    }
}
