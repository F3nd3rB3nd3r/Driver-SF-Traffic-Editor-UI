# Driver SF TrafficEditor 0.3 — bulk weights and expandable probability tables

This is an AI-generated tool for editing Driver: San Francisco traffic data. Version 0.3 can expand existing probability tables; increased runtime capacities require a separate Unlimiter mod.

The editor can add vehicle records to existing probability tables in the retail PC
`san_franciscoAI.fchunk`. It reads native counts and pointers, so an expanded file
can be reopened and edited again. Neither the original 191 rows per table nor the
original file size is a fixed editor limit.

## Set a list of vehicles to weight 1 across selected tables

1. Open the AI file and select the desired tables with Ctrl/Shift in **Probability
   weights**. For your Chapter 7 case, select those 19 tables. Leave **All tables**
   unchecked when you want only that selection.
2. Click **Import vehiclestats.lua** and choose the intended vanilla or modded Lua
   file. The editor reads the direct keys of `vehicleStats`, such as `[62]`, as
   vehicle IDs. It does not use the `Model` or `Manufacturer` fields as IDs.
   You can also enter numeric IDs/ranges directly in the ID box.
3. Set **Weight** to `1`, then click **Set weights**. This updates matching existing
   rows in every selected table. The checked **Add missing IDs when setting weights**
   option also inserts missing rows; uncheck it for existing rows only.
4. Review the result counts and **Unlimiter capacity report**, then save a patched
   copy. All listed IDs receive the requested weight; unlisted IDs and unselected
   tables retain their current values. Weight `0` disables matching rows without
   deleting them.

**Add missing IDs** retains its original behaviour: it inserts absent records and
preserves existing weights. Importing a Lua file only fills the ID box; it does
not change any tables until you apply an action. Import includes all direct keys,
regardless of `Selectable`, chapter or unlock properties. The text reader never
executes Lua. Comments, strings and nested keys are excluded; computed keys,
unclosed input or additional post-table assignments/references are refused.
Duplicate literal keys are deduplicated. Use a literal expanded table or a numeric
ID list for unsupported script forms.

In the supplied example, the extracted vanilla `vehiclestats.lua` contains 146 IDs.
Setting those to 1 in the 19 expanded tables while keeping their existing Forza
weights produces a largest routed union of **280 IDs**, up from 152. This was
verified on a scratch copy only. The prior160-request INI cannot cover that result.
Use the capacity report for your chosen file and update the Unlimiter configuration
accordingly; AudioModelEntries and ReplayRetainedReferences must also satisfy
their request-capacity dependencies. No game/INI changes are made by this editor.

## Add vehicles

1. Open a PC AI file or a previously expanded copy.
2. In **Probability weights**, choose a table. Use Ctrl/Shift for multiple tables,
   or check **All tables** to apply the operation to every table, including hidden
   search results.
3. Enter decimal/hex vehicle IDs separated by spaces, commas, semicolons or newlines.
   Inclusive ranges work: `304-700, 0x3E8`.
4. Set the initial weight and click **Add missing IDs**. Existing IDs and their
   weights remain unchanged. A zero weight creates a disabled record.
5. Use **Save patched copy**. The editor verifies the output before publishing it
   and writes a `.changes.txt` report. It refuses to overwrite the loaded source.

**Remove IDs** uses the same ID box and selected/all-table scope. With an empty ID
box, it uses the vehicle IDs selected in the grid. Deletions affect probability
records, not installed assets. The existing transfer-weight operation remains.

## Using it with Unlimiter

With Unlimiter USER CAP R3 and `RosterSource=AssetWeights`, positive-weight IDs in
the routed tables join the native requested IDs. This supports adding mod vehicle
IDs without replacing the native spool lists. Set adequate INI capacities and
install every referenced model and its audio/assets separately.

Table row count is not the active roster size: zero-weight rows are disabled;
several tables may reference the same IDs; native requests also count. Table
names are historical runtime associations, not exclusive categories. Giving an
ID a weight in every table also affects mission/challenge traffic: use a separate
test copy when experimenting.

Use **Find vehicle → Unlimiter capacity report** for the exact static native-plus-routed
positive-ID union for each active setting/frequency and its maximum. This gives
the request count to cover, rather than a guessed MiB-per-car estimate. It is
also included in save reports and reflects unsaved edits. Shared/audio/lifetime
budgets still need their own headroom and runtime checks.

The editor does not change the executable, supply cars, or adjust runtime pools.
Without the AssetWeights policy, positive weights alone do not ensure a car is
spooled. Native spool groups remain replacement-only in this version. Adding new
table descriptors, settings, or route selectors is outside this change.

## Format and preservation

- Parses the CHNK hierarchy and PC AIEX version 0x16. Unsupported formats,
  malformed bounds, duplicate IDs and invalid/nonfinite weights are rejected.
- Corrects the earlier metadata offset: the supplied PC resource has **59** native
  settings starting at `0x280A00`, including the previously omitted first setting.
  Names resolve by hash. The original 208 tables have 191 rows each.
- Growing/shrinking tables rebuilds their contiguous ID/Float32 row array, updates
  its count/pointer and per-table first/count/total, and rewrites CHNK ancestor
  sizes and sibling offsets with native alignment. AIRF and other opaque payloads
  are preserved. The existing AIEX bytes remain in place on first expansion.
- Subsequent growth reuses the appended tail array. No-edit output is byte-identical;
  ordinary weight edits retain the existing layout. Conflicting edits to aliased
  storage are refused rather than silently overwriting another table.
- Added IDs are 1–65535, matching the native vehicle/request ID domain. Weights and
  totals must fit finite, nonnegative Float32 values. File/offset and allocation
  bounds still apply; there is no arbitrary 191-row restriction.

The format was statically checked against the PC loader: `0040A7C0` relocates the
AIEX relative-pointer fields, including `+0x164` at `0040A89A`. CHNK descriptors
provide child offsets, sizes and alignment. Expanded output still needs a game
test with matching installed assets and Unlimiter settings; offline serialization
of 1,000 rows does not establish 1,000-car gameplay support.

## Build and run

Requires the .NET 10 SDK and Windows desktop/WPF support to build:

```powershell
dotnet build DriverSFTrafficEditor/DriverSFTrafficEditor.csproj -c Release
dotnet publish DriverSFTrafficEditor/DriverSFTrafficEditor.csproj -c Release --self-contained false -o publish/bulk-weights-v0.3
```

The framework-dependent package requires the **.NET 10 Windows Desktop Runtime
(x64)**. Keep all package files together and run `DriverSFTrafficEditor.exe`.
`build-portable.ps1` can produce a self-contained build if the required runtime
packs are available; it may need to download them.

## Offline verification

No game files are bundled, and the input file is read-only:

```powershell
dotnet run --project verification/CoreChecks/CoreChecks.csproj -c Release -- "C:/path/san_franciscoAI.fchunk" "C:/scratch/editor-fixtures"
python verification/verify_resources.py "C:/scratch/editor-fixtures/all-1000.fchunk" --compare "C:/path/san_franciscoAI.fchunk"
python verification/verify_catalog.py
python verification/verify_theme.py
```

Core checks use the supplied retail-layout 208×191 input, including modified
weights. They cover no-op/value-only saves, all 59 settings, single/multiple/all
table growth, 208×1,000 serialization, removal, repeated growth/shrink, unrelated
payload preservation, dirty state, numeric input, aliases and invalid files.
The Python resource validator also accepts arbitrary expanded row counts and
does not require a particular vanilla weight assignment.
