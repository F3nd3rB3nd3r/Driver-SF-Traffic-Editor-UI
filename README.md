This is an AI generated tool that would let you change the existing sets with the original limitations of the game. This is not for limit extension


# DriverSF Traffic Editor v0.1.6.1 — Build fix (plain names and Contains search)

Standalone **C# WPF/.NET 10** project for Visual Studio 2026.
This version extends v0.1.4 and retains the v0.1.3 theme/contrast fixes.
It is a file editor; **no debugger, ASI, Codex, or runtime game access is needed.**

## v0.1.6.1 fix

Restores the `ProfileDefinition.Observed` computed property for internal catalog coverage validation. This fixes the four `CS1061` errors in `Core/ProfileCatalog.cs` on v0.1.6. No visual, search, file format, or editor logic changes.

**Minimal update:** Replace only `DriverSFTrafficEditor/Core/ProfileCatalog.cs` from the update ZIP, then rebuild.

## What's new in v0.1.6

- **One flat list of all 208 probability tables**, sorted by table number (0–207).
  No chapters, groups, category headers, source names, or discovery tags.
- Display names come directly from the recorded traffic-setting names, e.g.
  `166 — SM2 Heat From Above`. **SM means Story Mission.**
- If a table has several recorded associations, its names are shown together
  on the same row separated by `/`. This is not a claim of exclusive ownership.
- Tables without a recorded name display `(unnamed)`; compatibility guesses
  are intentionally **not** shown as names.
- **Case-insensitive Contains** filtering works on any substring of a name or
  table number: `Heat`, `SM2`, `166`, `66`, and `0xA6` all work as typed.
- The selected-table detail and vehicle-search result show plain names and
  file offsets only; no runtime/ASI/file provenance in the interface.
- All 58 traffic settings, groups 0–4, 1,318 spool ID slots, and 208 × 191
  vehicle weights remain editable. Save-as and theme toggling are unchanged.

## How to build

1. Extract the source ZIP and open `DriverSF_TrafficEditor.sln`.
2. In Visual Studio 2026, install the .NET desktop development workload and
   .NET 10 SDK if needed. Build Release | Any CPU.
3. Run the editor and open an original **retail PC** `san_franciscoAI.fchunk`
   with size 4,403,200 bytes (0x433000), CHNK/AIRT header.
4. Choose any traffic-setting group, or use Probability weights > Search.
5. Edit existing IDs and/or float32 weights. Use **Save patched copy**; it
   will not overwrite the source file, and writes a `.changes.txt` report.

## Applying the small update to v0.1.5

Close the running editor and overlay files from
`DriverSF_TrafficEditor_v0.1.6_Update_Only.zip` over the existing project,
preserving directory structure. Open the solution and rebuild. No original
resource files are included or modified.

## Evidence and safety

- 58 settings and 208 profiles are verified binary structures in the original
  4,403,200-byte PC resource. File offsets: spooling metadata `0x280A18`, ID
  pool `0x281080`, profile metadata `0x2DDA80`, profile data `0x2DE440`.
- **135 distinct tables** have at least one association in the 213-row runtime
  dataset (see `verification/associations_source.csv`). Association means the
  table was observed under a setting/frequency, not an exhaustive proof of
  every possible traffic lane/road condition.
- **73 tables** lack a confirmed observation; table 50 is only a known
  compatibility candidate. There are no invented browser categories; multiple recorded names on a
  table are not proof of exclusive ownership.
- Changing a positive weight does not automatically spool a vehicle. Changing
  a spooling ID does not automatically transfer weights. Keep group counts
  unchanged and preserve mission vehicles where possible.
- Replacing a vehicle in an existing slot is *not* the same as increasing the
  game's resource residency capacity.
- No retail PC game files are included in either source archive.

## Offline checks

```powershell
py verification\verify_catalog.py
py verification\verify_theme.py
py verification\verify_resources.py "C:\path\to\san_franciscoAI.fchunk"
```

The catalog, name filtering logic, parser assumptions and XAML are checked outside Windows. WPF
compilation and UI interaction **were not executable in this Linux environment**;
rebuild in Visual Studio and verify theme switching with the larger list.
