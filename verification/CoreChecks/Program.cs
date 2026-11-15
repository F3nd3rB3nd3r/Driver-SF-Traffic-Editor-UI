using System.Buffers.Binary;
using DriverSFTrafficEditor.Core;
using DriverSFTrafficEditor.Models;

if (args.Length is not 2 and not 4) throw new ArgumentException("Usage: CoreChecks <PC AI file> <fixture output directory> [vehicleStats.lua expanded-AI-file]");
string source = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
byte[] original = File.ReadAllBytes(source);
int checks = 0;
void Check(bool condition, string description) { if (!condition) throw new Exception(description); checks++; Console.WriteLine($"PASS {description}"); }
void Reject(Action action, string description)
{
    bool rejected = false;
    try { action(); } catch (Exception ex) when (ex is InvalidDataException or FormatException or ArgumentException or InvalidOperationException) { rejected = true; }
    Check(rejected, description);
}
TrafficDocument Fresh() => TrafficDocument.Load(source);
string Fixture(string name, byte[] bytes) { string path = Path.Combine(output, name + ".fchunk"); File.WriteAllBytes(path, bytes); return path; }
uint U32(byte[] b, int p) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p, 4));
void Put(byte[] b, int p, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p, 4), v);
var resource = ChunkResource.Parse(original);
var leaf = resource.Find("AIEX").Single(); int root = leaf.Parent.FileOffset + leaf.Entry.Offset;
byte[] Airf(byte[] raw)
{
    var item = ChunkResource.Parse(raw).Find("AIRF").Single();
    return raw.AsSpan(item.Parent.FileOffset + item.Entry.Offset, item.Entry.Size).ToArray();
}

var doc = Fresh();
Check(doc.Sets.Count == 59 && doc.Sets[0].Hash == 0xE3517AD3 && doc.Sets[0].IdCount == 51, "all 59 native settings, including omitted first setting");
Check(doc.Profiles.Count == 208 && doc.Profiles.All(p => p.Rows.Count == 191), "original 208 x 191 shape");
Check(doc.Sets.Any(s => s.Name == "Chapter 1"), "hash-based setting names survive corrected indices");
Check(doc.CapacityRequirements().Count == 80 && doc.CapacityRequirements().All(r => r.UnionIds >= r.NativeIds && r.UnionIds >= r.PositiveIds), "R3 preview covers 80 active setting/frequency groups and includes native IDs");
Check(!doc.HasChanges && doc.PreparePatch().Data.SequenceEqual(original), "no-op is byte-identical");
Reject(() => doc.SaveAs(source), "source overwrite refused");
Check(VehicleIdInput.Parse("304-306, 0x3E8;304\n65535").SequenceEqual(new uint[] {304,305,306,1000,65535}), "numeric list/ranges/hex deduplicate");
foreach (string bad in new[] { "", "0", "65536", "6-2", "-2", "NaN", "1-2-3", "0x10000" }) Reject(() => VehicleIdInput.Parse(bad), $"invalid input '{bad}' rejected");

int notifications = 0; doc.Changed += (_, _) => notifications++;
var row = doc.Profiles[26].Rows[0]; row.Weight += 0.25;
byte[] valueEdit = doc.PreparePatch().Data;
var allowed = Enumerable.Range(row.WeightFileOffset, 4).Concat(Enumerable.Range(doc.Profiles[26].MetadataOffset + 8, 4)).ToHashSet();
Check(valueEdit.Length == original.Length && Enumerable.Range(0, original.Length).All(i => original[i] == valueEdit[i] || allowed.Contains(i)), "value-only save changes only weight/total bytes");
Check(notifications == 1 && doc.HasChanges, "value edit dirty notification");
row.Weight = row.OriginalWeight; Check(!doc.HasChanges, "reverting value clears dirty state");

int added = doc.AddVehicles(new[] { doc.Profiles[26] }, new uint[] {1000,1000}, 1);
Check(added == 1 && doc.HasChanges && notifications == 3, "adding a row marks dirty and emits one batch notification");
int before = notifications; doc.Profiles[26].Rows.Last().Weight = 2;
Check(notifications == before + 1, "new-row edits are observed");
Check(doc.AddVehicles(new[] {doc.Profiles[26]}, new uint[] {1000}, 99) == 0 && doc.Profiles[26].Rows.Last().Weight == 2, "duplicate add preserves existing weight");
var expanded = doc.PreparePatch(); string single = Fixture("single-expanded", expanded.Data);
var reopened = TrafficDocument.Load(single);
Check(reopened.Profiles[26].Rows.Count == 192 && reopened.Profiles[26].Rows.Last().VehicleId == 1000 && reopened.Profiles[26].Rows.Last().Weight == 2, "expanded single table round-trip");
Check(reopened.Profiles.Where(p => p.Number != 26).All(p => p.Rows.Count == 191), "unselected table counts preserved");
Check(Airf(original).SequenceEqual(Airf(expanded.Data)), "opaque AIRF preserved across relocation");
Check(!reopened.HasChanges && reopened.PreparePatch().Data.SequenceEqual(expanded.Data), "expanded no-op byte identity");
var newLeaf = ChunkResource.Parse(expanded.Data).Find("AIEX").Single();
int newRoot = newLeaf.Parent.FileOffset + newLeaf.Entry.Offset;
var permitted = new HashSet<int>(Enumerable.Range(0x160, 8));
for (int t = 0; t < 208; t++) foreach (int i in Enumerable.Range(doc.Profiles[t].MetadataOffset - root, 12)) permitted.Add(i);
Check(Enumerable.Range(0, leaf.Entry.Size).All(i => original[root + i] == expanded.Data[newRoot + i] || permitted.Contains(i)), "all unrelated AIEX bytes preserved in structural save");
Check(expanded.Report.Contains("Added IDs: 1000") && expanded.Report.Contains("OUTPUT TABLE 26"), "structural change report includes IDs and output locations");

reopened.AddVehicles(new[] {reopened.Profiles[26]}, new uint[] {1001}, 3);
byte[] twice = reopened.PreparePatch().Data; string twicePath = Fixture("twice-expanded", twice);
var twoLeaf = ChunkResource.Parse(twice).Find("AIEX").Single();
Check(twoLeaf.Entry.Size == newLeaf.Entry.Size + 8, "repeated growth reuses appended tail (only 8 more payload bytes)");
var shrunk = TrafficDocument.Load(twicePath);
shrunk.RemoveVehicles(new[] {shrunk.Profiles[26]}, new uint[] {1000,1001});
var shrunkBytes = shrunk.PreparePatch().Data; Fixture("shrunk", shrunkBytes);
Check(ChunkResource.Parse(shrunkBytes).Find("AIEX").Single().Entry.Size == newLeaf.Entry.Size - 8, "tail shrink releases removed rows");
Check(Airf(original).SequenceEqual(Airf(shrunkBytes)), "opaque data survives shrinking");
var incremental = Fresh();
for (uint id = 1000; id < 1010; id++)
{
    incremental.AddVehicles(new[] {incremental.Profiles[26]}, new[] {id}, 1);
    incremental = TrafficDocument.Load(Fixture("incremental", incremental.PreparePatch().Data));
}
var once = Fresh(); once.AddVehicles(new[] {once.Profiles[26]}, Enumerable.Range(1000, 10).Select(i => (uint)i), 1);
byte[] onceBytes = once.PreparePatch().Data; Fixture("once-ten", onceBytes);
Check(incremental.PreparePatch().Data.SequenceEqual(onceBytes), "ten reopen/grow saves match one bulk save byte-for-byte, without padding drift");

doc = Fresh();
Check(doc.AddVehicles(doc.Profiles, Enumerable.Range(1000, 809).Select(i => (uint)i), 1) == 208 * 809, "bulk extension to 1000 rows per table");
var large = doc.PreparePatch(); string largePath = Fixture("all-1000", large.Data);
var largeDoc = TrafficDocument.Load(largePath);
Check(largeDoc.Profiles.All(p => p.Rows.Count == 1000 && p.Rows.Select(r => r.VehicleId).Distinct().Count() == 1000), "208 x 1000 rows reopen without truncation");
Check(largeDoc.CapacityRequirements().Zip(Fresh().CapacityRequirements()).All(pair => pair.First.UnionIds == pair.Second.UnionIds + 809), "R3 routed union increases by exactly 809 added positive IDs");
Check(Airf(original).SequenceEqual(Airf(large.Data)) && largeDoc.Sets.SelectMany(s => s.Groups).SelectMany(g => g.Rows).Select(r => r.VehicleId).SequenceEqual(Fresh().Sets.SelectMany(s => s.Groups).SelectMany(g => g.Rows).Select(r => r.VehicleId)), "bulk extension preserves road payload and native spool IDs");
largeDoc.Profiles[0].Rows.Last().Weight = 0.5;
largeDoc.SaveAs(Path.Combine(output, "all-1000-saved.fchunk"));
Check(File.Exists(Path.Combine(output, "all-1000-saved.fchunk.changes.txt")), "SaveAs validates and writes expanded file/report");

doc = Fresh();
Reject(() => doc.AddVehicles(doc.Profiles, new uint[] {0}, 1), "ID zero rejected atomically");
Reject(() => doc.AddVehicles(doc.Profiles, new uint[] {65536}, 1), "ID above ushort domain rejected");
Reject(() => doc.AddVehicles(doc.Profiles, new uint[] {1000}, float.NaN), "nonfinite initial weight rejected");
Reject(() => doc.Profiles[0].Rows[0].Weight = -1, "negative weight edit rejected");
doc.AddVehicles(new[] {doc.Profiles[0]}, new uint[] {1000,1001}, float.MaxValue);
Reject(() => doc.PreparePatch(), "total Float32 overflow rejected");
doc = Fresh(); doc.Profiles[0].Rows.Add(new WeightRow(191, -1, doc.Profiles[0].Rows[0].VehicleId, 1));
Reject(() => doc.PreparePatch(), "duplicate stored row rejected");
doc = Fresh(); doc.RemoveVehicles(new[] {doc.Profiles[0]}, new[] {doc.Profiles[0].Rows[0].VehicleId});
Check(doc.HasChanges, "removing an unchanged row marks document dirty");
_ = TrafficDocument.Load(Fixture("removed", doc.PreparePatch().Data));
doc = Fresh(); doc.AddVehicles(new[] {doc.Profiles[0], doc.Profiles[5]}, new uint[] {65535}, 0);
var selected = TrafficDocument.Load(Fixture("selected-two", doc.PreparePatch().Data));
Check(selected.Profiles.Where(p => p.Rows.Count == 192).Select(p => p.Number).SequenceEqual(new[] {0,5}), "selected-table batch and highest valid vehicle ID survive");
Check(selected.CapacityRequirements().Zip(Fresh().CapacityRequirements()).All(pair => pair.First.UnionIds == pair.Second.UnionIds), "zero-weight added rows do not inflate capacity preview");

// Aliased metadata is legal to read; independent edits must never silently clobber it.
byte[] aliased = (byte[])original.Clone();
int metadata = root + checked((int)U32(aliased, root + 0x15C));
Array.Copy(aliased, metadata, aliased, metadata + 12, 12);
doc = TrafficDocument.Load(Fixture("aliased-input", aliased)); doc.Profiles[0].Rows[0].Weight += 1;
Reject(() => doc.PreparePatch(), "conflicting aliased table edits refused");
doc.AddVehicles(new[] {doc.Profiles[0]}, new uint[] {1000}, 1);
_ = TrafficDocument.Load(Fixture("alias-detached", doc.PreparePatch().Data));
Check(true, "structural rebuild safely detaches aliases");

foreach (var (name, mutate) in new (string, Action<byte[]>)[] {
    ("bad-count", b => Put(b, root + 0x160, uint.MaxValue)),
    ("bad-pointer", b => Put(b, root + 0x164, 0xFFFFFFF0)),
    ("overlapping-array", b => Put(b, root + 0x164, U32(b, root + 0x15C))),
    ("bad-total", b => Put(b, metadata + 8, 0x7FC00000)),
    ("bad-container-size", b => Put(b, 4, 16)),
    ("overlapping-chunks", b => Put(b, 0x1024, 0x80)),
    ("bad-version", b => Put(b, 0x1018, 0x00070000)) })
{
    byte[] bad = (byte[])original.Clone(); mutate(bad);
    Reject(() => TrafficDocument.Load(Fixture(name, bad)), name + " rejected");
}
Reject(() => TrafficDocument.Load(Fixture("truncated", original[..100])), "truncated container rejected");
Check(File.ReadAllBytes(source).SequenceEqual(original), "authorized source asset unchanged");
doc = Fresh(); var firstSpool = doc.Sets[0].Groups[0].Rows[0]; firstSpool.VehicleId = 65535;
var spoolBytes = doc.PreparePatch().Data;
Check(spoolBytes.Length == original.Length && Enumerable.Range(0, original.Length).All(i => original[i] == spoolBytes[i] || i == firstSpool.FileOffset || i == firstSpool.FileOffset + 1), "first native setting can be edited without touching unrelated bytes");
// Bulk weights must update existing rows, unlike AddVehicles, and retain scope.
doc = Fresh(); var targets = doc.Profiles.Take(19).ToArray();
uint existingId = targets[0].Rows[0].VehicleId, untouchedId = targets[0].Rows[1].VehicleId;
double untouched = targets[0].Rows[1].Weight; notifications = 0; doc.Changed += (_, _) => notifications++;
foreach (var table in targets) table.Rows.First(r => r.VehicleId == existingId).Weight = 0;
notifications = 0;
var update = doc.SetVehicleWeights(targets, new[] {existingId, existingId, 1000u}, 1, true);
Check(update.Updated == 19 && update.Added == 19 && update.MissingSkipped == 0 && notifications == 1, "bulk upsert updates existing and adds missing IDs in one change notification");
Check(targets.All(p => p.Rows.Single(r => r.VehicleId == existingId).Weight == 1 && p.Rows.Single(r => r.VehicleId == 1000).Weight == 1), "all 19 target tables receive the requested weight");
Check(targets[0].Rows.Single(r => r.VehicleId == untouchedId).Weight == untouched && doc.Profiles.Skip(19).All(p => !p.Rows.Any(r => r.VehicleId == 1000)), "unlisted IDs and unselected tables retain their values");
var repeated = doc.SetVehicleWeights(targets, new[] {existingId, 1000u}, 1, true);
Check(repeated.Updated == 0 && repeated.Added == 0, "repeating bulk set is idempotent");
var skip = doc.SetVehicleWeights(targets, new uint[] {1001}, 1, false);
Check(skip.MissingSkipped == 19 && skip.Added == 0 && !targets.Any(p => p.Rows.Any(r => r.VehicleId == 1001)), "existing-only mode skips missing IDs");
string bulkPath = Fixture("bulk-19", doc.PreparePatch().Data); var bulkDoc = TrafficDocument.Load(bulkPath);
Check(bulkDoc.Profiles.Take(19).All(p => p.Rows.Single(r => r.VehicleId == existingId).Weight == 1 && p.Rows.Single(r => r.VehicleId == 1000).Weight == 1), "bulk updates and rebuilt totals round-trip");
var disabled = bulkDoc.SetVehicleWeights(bulkDoc.Profiles.Take(19), new[] {existingId, 1000u}, 0, false);
Check(disabled.Updated == 38 && bulkDoc.Profiles.Take(19).All(p => p.Rows.Single(r => r.VehicleId == existingId).Weight == 0 && p.Rows.Count == 192), "bulk zero weight disables matching IDs while retaining rows");
Reject(() => doc.SetVehicleWeights(targets, new uint[] {0}, 1, true), "bulk ID validation rejects zero");
Reject(() => doc.SetVehicleWeights(targets, new uint[] {1}, float.PositiveInfinity, true), "bulk Float32 validation rejects infinity");
doc = Fresh();
foreach (var entry in doc.Profiles[0].Rows) entry.Weight = 0;
doc.Profiles[1].Rows[0].Weight = float.MaxValue;
uint other = doc.Profiles[1].Rows[1].VehicleId;
double firstBefore = doc.Profiles[0].Rows.Single(r => r.VehicleId == other).Weight;
Reject(() => doc.SetVehicleWeights(doc.Profiles.Take(2), new[] {other}, float.MaxValue, false), "overflow in a later target is rejected before any bulk mutation");
Check(doc.Profiles[0].Rows.Single(r => r.VehicleId == other).Weight == firstBefore && doc.Profiles[0].Rows.Count == 191, "failed bulk preflight leaves earlier table unchanged");

const string lua = "-- vehicleStats = { [99] = {} }\nlocal vehicleStats = { [62] = { Model=123, Nested={ [555]={} }, Name=\"[777] = {\" }, -- [888] = {}\n[0x76] = {Note=[=[ [999] = {} ]=]}, [62] = {}, [65535] = {} }";
Check(VehicleStatsInput.Parse(lua).SequenceEqual(new uint[] {62,118,65535}), "Lua import reads direct numeric keys, deduplicates and ignores nested keys/comments/strings");
Check(VehicleStatsInput.Parse("--[==[ vehicleStats={[9]={}} ]==]\nvehicleStats={ [63]={Name='escaped \\\' } [8] = {'} }").SequenceEqual(new uint[] {63}), "Lua long comments and escaped strings do not contribute IDs/braces");
Check(VehicleStatsInput.Parse("vehicleStats={ [62]={} }; return vehicleStats;").SequenceEqual(new uint[] {62}), "literal Lua inventory can have a terminal return");
Reject(() => VehicleStatsInput.Parse("vehicleStats={ [62]={} }; rawset(vehicleStats, 63, {})"), "post-table rawset mutation is not silently imported as a complete inventory");
foreach (string invalid in new[] {"vehicleStats = generate()", "vehicleStats = { [0] = {} }", "vehicleStats = { [65536] = {} }", "vehicleStats = { [62+1] = {} }", "vehicleStats = { [-1] = {} }", "vehicleStats = { [62] = build() }", "vehicleStats = { [62] = {}", "vehicleStats={ [62]={} }; vehicleStats[63]={}", "vehicleStats={ [62]={} }; vehicleStats={ [63]={} }", "vehicleStats={ [62]={Note='unterminated} }", "--[[ unclosed"})
    Reject(() => VehicleStatsInput.Parse(invalid), "unsupported/malformed Lua inventory rejected");

if (args.Length == 4)
{
    byte[] luaBytes = File.ReadAllBytes(args[2]), expandedBytes = File.ReadAllBytes(args[3]);
    uint[] imported = VehicleStatsInput.Parse(File.ReadAllText(args[2]));
    Check(imported.Length > 0 && imported.All(id => id is > 0 and <= 65535), "actual vehicleStats file imports valid unique IDs");
    var expandedDoc = TrafficDocument.Load(args[3]);
    var selectedTables = expandedDoc.Profiles.Where(p => p.Rows.Count > 191).ToArray();
    Check(selectedTables.Length == 19, "supplied expanded AI supplies the requested 19-table case");
    var originalRows = expandedDoc.Profiles.Select(p => p.Rows.Select(r => (r.VehicleId, Weight:(float)r.Weight)).ToArray()).ToArray();
    var result = expandedDoc.SetVehicleWeights(selectedTables, imported, 1, true);
    Check(selectedTables.All(p => imported.All(id => p.Rows.Single(r => r.VehicleId == id).Weight == 1)), "every imported vehicle ID has weight 1 in all 19 actual expanded tables");
    var selectedNumbers = selectedTables.Select(p => p.Number).ToHashSet();var importedSet = imported.ToHashSet();
    Check(expandedDoc.Profiles.All(p => originalRows[p.Number].Where(r => !selectedNumbers.Contains(p.Number) || !importedSet.Contains(r.VehicleId)).All(r => p.Rows.Single(x => x.VehicleId == r.VehicleId).Weight == r.Weight)), "actual-file bulk set preserves all unrelated rows/tables");
    string path = Path.Combine(output, "vehiclestats-19.fchunk");expandedDoc.SaveAs(path);var loaded = TrafficDocument.Load(path);
    Check(selectedNumbers.All(n => imported.All(id => loaded.Profiles[n].Rows.Single(r => r.VehicleId == id).Weight == 1)), "actual-file bulk output saves and reopens with updated totals");
    Check(File.ReadAllBytes(args[2]).SequenceEqual(luaBytes) && File.ReadAllBytes(args[3]).SequenceEqual(expandedBytes), "Lua and expanded game input files remain unchanged");
    Console.WriteLine($"ACTUAL BULK CASE imported={imported.Length} tables={selectedTables.Length} updated={result.Updated} added={result.Added} maximumRoutedRoster={expandedDoc.CapacityRequirements().Max(r => r.UnionIds)}");
}
Console.WriteLine($"ALL {checks} CHECKS PASSED");
