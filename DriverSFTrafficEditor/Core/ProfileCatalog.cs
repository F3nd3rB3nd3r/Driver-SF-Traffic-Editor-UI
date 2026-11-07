using System;
using System.Collections.Generic;
using System.Linq;

namespace DriverSFTrafficEditor.Core;

// Observations were collected at runtime (213 setting/frequency/table records).
// An observed association does NOT prove a specific road, lane, or every possible
// lookup path. Multiple traffic settings can share one weight table.
public sealed record ProfileObservation(string Setting, int Frequency, int? DiscoveryPicks, string Evidence);

public sealed class ProfileDefinition
{
    public int Number { get; }
    public IReadOnlyList<ProfileObservation> Associations { get; }
    // Internal coverage flag used by catalog validation, not shown in the UI.
    public bool Observed => Associations.Count > 0;

    // A table can be reached through several traffic settings. Show each distinct
    // in-game name once, without inventing an exclusive category or mission type.
    // SM is Story Mission, not Side Mission, and is intentionally kept verbatim.
    public string Name { get; }
    public string Display => $"{Number,3}  —  {Name}";
    // Contains search, case-insensitive, for partial names and table numbers.
    public string SearchText => $"{Display} 0x{Number:X}";

    public ProfileDefinition(int number, IReadOnlyList<ProfileObservation> associations)
    {
        Number = number;
        Associations = associations;
        Name = associations.Count == 0 ? "(unnamed)" :
            string.Join(" / ", associations.Select(a => a.Setting).Distinct(StringComparer.OrdinalIgnoreCase));
    }
}

public static class ProfileCatalog
{
    private sealed record AssociationRecord(int Table, string Setting, int Frequency, int? DiscoveryPicks, string Evidence);
    // Archive: DriverSF_Traffic_Verified_213_Associations.csv
    private static readonly AssociationRecord[] RuntimeAssociations = new AssociationRecord[]
    {
        new(166, "SM2 Heat From Above", 0, 2055, "ASI discovery; TrafficRandomizer_tables(5).csv"),
        new(178, "SM2 Heat From Above", 0, 9, "ASI discovery; TrafficRandomizer_tables(5).csv"),
        new(172, "Chapter 6 - Its for charity", 0, null, "ASI runtime DISCOVER; TrafficRandomizer(2).log line 2142 at 2026-10-11 13:31:54.209 (count unavailable)"),
        new(175, "Chapter 6 - Its for charity", 0, 1306, "ASI discovery"),
        new(168, "Chapter 3 Shakedown", 0, 4721, "ASI discovery"),
        new(162, "Chapter 7 Tickingclock", 0, 1279, "ASI discovery"),
        new(26, "Chase traffic 1", 0, 3, "ASI discovery"),
        new(34, "Chase traffic 1", 0, 66, "ASI discovery"),
        new(42, "Chase traffic 1", 0, 7, "ASI discovery"),
        new(58, "Chase traffic 1", 0, 158, "ASI discovery"),
        new(90, "Chase traffic 1", 0, 131, "ASI discovery"),
        new(186, "Chase traffic 1", 0, 71, "ASI discovery"),
        new(25, "Chase traffic 5", 0, null, "ASI runtime DISCOVER; TrafficRandomizer(2).log line 2711 at 2026-10-11 13:58:43.084 (count unavailable)"),
        new(37, "Chase traffic 5", 0, 52, "ASI discovery"),
        new(54, "Chase traffic 5", 0, null, "ASI runtime DISCOVER; TrafficRandomizer(2).log line 2713 at 2026-10-11 13:58:45.514 (count unavailable)"),
        new(61, "Chase traffic 5", 0, 50, "ASI discovery"),
        new(68, "Chase traffic 5", 0, 72, "ASI discovery"),
        new(75, "Chase traffic 5", 0, 175, "ASI discovery"),
        new(108, "Chase traffic 5", 0, 3, "ASI discovery"),
        new(147, "Chase traffic 5", 0, 97, "ASI discovery"),
        new(150, "Online Traffic Easy", 0, 0, "earlier x32dbg"),
        new(181, "Online Traffic Easy", 1, 0, "earlier x32dbg"),
        new(182, "Online Traffic Easy", 2, 0, "earlier x32dbg"),
        new(30, "Chapter 6", 0, 515, "ASI discovery"),
        new(40, "Chapter 6", 0, 466, "ASI discovery"),
        new(47, "Chapter 6", 0, 39, "ASI discovery"),
        new(62, "Chapter 6", 0, 703, "ASI discovery"),
        new(69, "Chapter 6", 0, 212, "ASI discovery"),
        new(76, "Chapter 6", 0, 968, "ASI discovery"),
        new(114, "Chapter 6", 0, 7, "ASI discovery"),
        new(115, "Chapter 6", 0, null, "TrafficRandomizer(1).log (v0.5.1 2026-10-11)"),
        new(116, "Chapter 6", 0, null, "TrafficRandomizer(1).log (v0.5.1 2026-10-11)"),
        new(119, "Chapter 6", 0, null, "ASI runtime DISCOVER; TrafficRandomizer(2).log line 2089 at 2026-10-11 13:29:48.999 (count unavailable)"),
        new(137, "Chapter 6", 0, 580, "ASI discovery"),
        new(28, "Chapter 2", 0, 0, "earlier x32dbg"),
        new(43, "Chapter 2", 0, 0, "earlier x32dbg"),
        new(72, "Chapter 2", 0, 0, "earlier x32dbg"),
        new(187, "Chapter 2", 0, 0, "earlier x32dbg"),
        new(34, "Chapter 1 Kidnapped", 0, 0, "earlier x32dbg"),
        new(165, "Chapter 1 Kidnapped", 0, 0, "earlier x32dbg"),
        new(27, "Chapter 3", 0, 895, "ASI discovery"),
        new(36, "Chapter 3", 0, 1097, "ASI discovery"),
        new(44, "Chapter 3", 0, 99, "ASI discovery"),
        new(59, "Chapter 3", 0, 1191, "ASI discovery"),
        new(66, "Chapter 3", 0, 441, "ASI discovery"),
        new(73, "Chapter 3", 0, 1539, "ASI discovery"),
        new(91, "Chapter 3", 0, 2029, "ASI discovery"),
        new(188, "Chapter 3", 0, 1296, "ASI discovery"),
        new(202, "Chapter 3", 1, 1597, "ASI discovery; TrafficRandomizer_tables(5).csv"),
        new(33, "Epilogue", 0, 1784, "ASI discovery"),
        new(191, "Chapter 1 Breaking News", 0, 0, "earlier x32dbg"),
        new(31, "Chapter 7", 0, 260, "ASI discovery"),
        new(41, "Chapter 7", 0, 467, "ASI discovery"),
        new(48, "Chapter 7", 0, 76, "ASI discovery"),
        new(56, "Chapter 7", 0, 13, "ASI discovery"),
        new(63, "Chapter 7", 0, 479, "ASI discovery"),
        new(70, "Chapter 7", 0, 407, "ASI discovery"),
        new(77, "Chapter 7", 0, 990, "ASI discovery"),
        new(121, "Chapter 7", 0, null, "ASI runtime DISCOVER; TrafficRandomizer(2).log line 2348 at 2026-10-11 13:41:13.276 (count unavailable)"),
        new(123, "Chapter 7", 0, 82, "ASI discovery"),
        new(125, "Chapter 7", 0, 36, "ASI discovery"),
        new(127, "Chapter 7", 0, 14, "ASI discovery"),
        new(129, "Chapter 7", 0, 130, "ASI discovery"),
        new(131, "Chapter 7", 0, null, "ASI runtime DISCOVER; TrafficRandomizer(2).log line 2406 at 2026-10-11 13:43:43.829 (count unavailable)"),
        new(133, "Chapter 7", 0, null, "ASI runtime DISCOVER; TrafficRandomizer(2).log line 2332 at 2026-10-11 13:40:27.212 (count unavailable)"),
        new(135, "Chapter 7", 0, null, "ASI runtime DISCOVER; TrafficRandomizer(2).log line 2284 at 2026-10-11 13:38:34.156 (count unavailable)"),
        new(138, "Chapter 7", 0, 727, "ASI discovery"),
        new(153, "DeactivatingBombs", 0, 1162, "ASI discovery"),
        new(154, "DeactivatingBombs", 0, 78, "ASI discovery"),
        new(176, "Online Traffic Pursuit", 0, 1847, "ASI discovery (CSV 4)"),
        new(185, "Online Traffic Pursuit", 1, 1244, "ASI discovery (CSV 4)"),
        new(159, "Chapter 6 - Special Edition", 0, 1446, "ASI discovery"),
        new(25, "Chapter 5 Raceaway", 0, 25, "ASI discovery"),
        new(35, "Chapter 5 Raceaway", 0, 509, "ASI discovery"),
        new(37, "Chapter 5 Raceaway", 0, 267, "ASI discovery"),
        new(46, "Chapter 5 Raceaway", 0, 23, "ASI discovery"),
        new(61, "Chapter 5 Raceaway", 0, 168, "ASI discovery"),
        new(68, "Chapter 5 Raceaway", 0, 9, "ASI discovery"),
        new(75, "Chapter 5 Raceaway", 0, 420, "ASI discovery"),
        new(106, "Chapter 5 Raceaway", 0, 36, "ASI discovery"),
        new(111, "Chapter 5 Raceaway", 0, null, "TrafficRandomizer(1).log (v0.5.1 2026-10-11)"),
        new(112, "Chapter 5 Raceaway", 0, null, "TrafficRandomizer(1).log (v0.5.1 2026-10-11)"),
        new(147, "Chapter 5 Raceaway", 0, 24, "ASI discovery"),
        new(140, "Exposition Drive", 0, 1387, "ASI discovery"),
        new(142, "Exposition Drive", 0, 44, "ASI discovery"),
        new(201, "Online Traffic Survival", 0, 1311, "ASI discovery (CSV 4)"),
        new(192, "Chapter 6 Best Defence", 0, 1413, "ASI discovery"),
        new(150, "Online Traffic Sprint GP", 0, 652, "ASI discovery (CSV 4)"),
        new(181, "Online Traffic Sprint GP", 1, 634, "ASI discovery (CSV 4)"),
        new(182, "Online Traffic Sprint GP", 2, 698, "ASI discovery (CSV 4)"),
        new(177, "Chase traffic 2", 0, 798, "ASI discovery"),
        new(160, "Cut scene traffic", 0, 2180, "ASI discovery"),
        new(170, "Chapter 5 - Test Drive", 0, 1497, "ASI discovery"),
        new(174, "Chapter 7 s", 0, 1824, "ASI discovery"),
        new(34, "Chapter 1 College Fund", 0, 0, "earlier x32dbg"),
        new(164, "Exposition Chase", 0, 1953, "ASI discovery"),
        new(146, "SM5 Something Weird", 0, 2395, "ASI discovery"),
        new(151, "Online Traffic Hard", 0, 74, "ASI discovery"),
        new(198, "Online Traffic Hard", 0, 1552, "ASI discovery"),
        new(183, "Online Traffic Hard", 1, 98, "ASI discovery"),
        new(199, "Online Traffic Hard", 1, 1183, "ASI discovery"),
        new(184, "Online Traffic Hard", 2, 38, "ASI discovery"),
        new(200, "Online Traffic Hard", 2, 1437, "ASI discovery"),
        new(23, "Free Drive", 0, 2189, "ASI discovery"),
        new(161, "Exposition Shift", 0, 1234, "ASI discovery"),
        new(29, "SM4 Collateral Damage", 0, null, "TrafficRandomizer(1).log (v0.5.1 2026-10-11)"),
        new(39, "SM4 Collateral Damage", 0, 325, "ASI discovery"),
        new(45, "SM4 Collateral Damage", 0, 216, "ASI discovery"),
        new(60, "SM4 Collateral Damage", 0, 83, "ASI discovery"),
        new(67, "SM4 Collateral Damage", 0, 414, "ASI discovery"),
        new(74, "SM4 Collateral Damage", 0, 728, "ASI discovery"),
        new(100, "SM4 Collateral Damage", 0, 42, "ASI discovery"),
        new(101, "SM4 Collateral Damage", 0, 99, "ASI discovery"),
        new(102, "SM4 Collateral Damage", 0, null, "TrafficRandomizer(1).log (v0.5.1 2026-10-11)"),
        new(103, "SM4 Collateral Damage", 0, 94, "ASI discovery"),
        new(104, "SM4 Collateral Damage", 0, 814, "ASI discovery"),
        new(36, "Chapter 3 Team Colours", 0, 609, "ASI discovery"),
        new(44, "Chapter 3 Team Colours", 0, 2, "ASI discovery"),
        new(52, "Chapter 3 Team Colours", 0, null, "TrafficRandomizer(1).log (v0.5.1 2026-10-11)"),
        new(59, "Chapter 3 Team Colours", 0, 105, "ASI discovery"),
        new(66, "Chapter 3 Team Colours", 0, 37, "ASI discovery"),
        new(73, "Chapter 3 Team Colours", 0, 453, "ASI discovery"),
        new(91, "Chapter 3 Team Colours", 0, 430, "ASI discovery"),
        new(25, "Chapter 5", 0, 52, "ASI discovery"),
        new(37, "Chapter 5", 0, 672, "ASI discovery"),
        new(46, "Chapter 5", 0, 32, "ASI discovery"),
        new(54, "Chapter 5", 0, null, "TrafficRandomizer(1).log (v0.5.1 2026-10-11)"),
        new(61, "Chapter 5", 0, 423, "ASI discovery"),
        new(68, "Chapter 5", 0, 91, "ASI discovery"),
        new(75, "Chapter 5", 0, 842, "ASI discovery"),
        new(105, "Chapter 5", 0, null, "TrafficRandomizer(1).log (v0.5.1 2026-10-11)"),
        new(106, "Chapter 5", 0, 109, "ASI discovery"),
        new(107, "Chapter 5", 0, null, "TrafficRandomizer(1).log (v0.5.1 2026-10-11)"),
        new(108, "Chapter 5", 0, null, "TrafficRandomizer(1).log (v0.5.1 2026-10-11)"),
        new(110, "Chapter 5", 0, null, "TrafficRandomizer(1).log (v0.5.1 2026-10-11)"),
        new(111, "Chapter 5", 0, null, "TrafficRandomizer(1).log (v0.5.1 2026-10-11)"),
        new(112, "Chapter 5", 0, null, "TrafficRandomizer(1).log (v0.5.1 2026-10-11)"),
        new(147, "Chapter 5", 0, 287, "ASI discovery"),
        new(27, "Challenge traffic heavy", 0, 124, "ASI discovery"),
        new(36, "Challenge traffic heavy", 0, 237, "ASI discovery"),
        new(44, "Challenge traffic heavy", 0, 41, "ASI discovery"),
        new(52, "Challenge traffic heavy", 0, 5, "ASI discovery"),
        new(59, "Challenge traffic heavy", 0, 202, "ASI discovery"),
        new(66, "Challenge traffic heavy", 0, 7, "ASI discovery"),
        new(73, "Challenge traffic heavy", 0, 280, "ASI discovery"),
        new(91, "Challenge traffic heavy", 0, 320, "ASI discovery"),
        new(188, "Challenge traffic heavy", 0, 148, "ASI discovery"),
        new(26, "Chapter 1", 0, 0, "earlier x32dbg"),
        new(42, "Chapter 1", 0, 0, "earlier x32dbg"),
        new(58, "Chapter 1", 0, 0, "earlier x32dbg"),
        new(186, "Chapter 1", 0, 0, "earlier x32dbg"),
        new(28, "Chapter 2 Streetrace", 0, 88, "ASI discovery"),
        new(38, "Chapter 2 Streetrace", 0, 553, "ASI discovery"),
        new(43, "Chapter 2 Streetrace", 0, 29, "ASI discovery"),
        new(51, "Chapter 2 Streetrace", 0, 42, "ASI discovery"),
        new(65, "Chapter 2 Streetrace", 0, 25, "ASI discovery"),
        new(72, "Chapter 2 Streetrace", 0, 586, "ASI discovery"),
        new(88, "Chapter 2 Streetrace", 0, 541, "ASI discovery"),
        new(89, "Chapter 2 Streetrace", 0, 380, "ASI discovery"),
        new(167, "Chapter 2 LFK", 0, 1319, "ASI discovery"),
        new(141, "Exposition Bullrun", 0, 1132, "ASI discovery"),
        new(161, "Exposition Bullrun", 0, 1280, "ASI discovery"),
        new(173, "Chapter 7 Alone", 0, 1566, "ASI discovery"),
        new(26, "Challenge traffic", 0, 123, "ASI discovery"),
        new(34, "Challenge traffic", 0, 1266, "ASI discovery"),
        new(58, "Challenge traffic", 0, 23, "ASI discovery"),
        new(194, "Challenge traffic", 1, 787, "ASI discovery; TrafficRandomizer_tables(6).csv"),
        new(195, "Challenge traffic", 2, 760, "ASI discovery; TrafficRandomizer_tables(6).csv"),
        new(196, "Challenge traffic", 3, 794, "ASI discovery; TrafficRandomizer_tables(6).csv"),
        new(49, "Chapter 8", 0, 799, "ASI discovery"),
        new(139, "Chapter 8", 0, 625, "ASI discovery"),
        new(27, "SM3 Peroxide Convoy", 0, 160, "ASI discovery"),
        new(36, "SM3 Peroxide Convoy", 0, 269, "ASI discovery"),
        new(44, "SM3 Peroxide Convoy", 0, 5, "ASI discovery"),
        new(52, "SM3 Peroxide Convoy", 0, 30, "ASI discovery"),
        new(59, "SM3 Peroxide Convoy", 0, 295, "ASI discovery"),
        new(66, "SM3 Peroxide Convoy", 0, 27, "ASI discovery"),
        new(73, "SM3 Peroxide Convoy", 0, 118, "ASI discovery"),
        new(149, "SM3 Peroxide Convoy", 0, 203, "ASI discovery"),
        new(158, "SM3 Peroxide Convoy", 0, 152, "ASI discovery"),
        new(29, "Chapter 4", 0, 20, "ASI discovery"),
        new(39, "Chapter 4", 0, 327, "ASI discovery"),
        new(45, "Chapter 4", 0, 28, "ASI discovery"),
        new(53, "Chapter 4", 0, null, "TrafficRandomizer(1).log (v0.5.1 2026-10-11)"),
        new(60, "Chapter 4", 0, 298, "ASI discovery"),
        new(67, "Chapter 4", 0, 109, "ASI discovery"),
        new(74, "Chapter 4", 0, 484, "ASI discovery"),
        new(100, "Chapter 4", 0, null, "TrafficRandomizer(1).log (v0.5.1 2026-10-11)"),
        new(101, "Chapter 4", 0, 55, "ASI discovery"),
        new(102, "Chapter 4", 0, null, "TrafficRandomizer(1).log (v0.5.1 2026-10-11)"),
        new(103, "Chapter 4", 0, null, "TrafficRandomizer(1).log (v0.5.1 2026-10-11)"),
        new(104, "Chapter 4", 0, 451, "ASI discovery"),
        new(189, "Chapter 4", 0, 209, "ASI discovery"),
        new(188, "Chapter 4", 1, 34, "ASI discovery; TrafficRandomizer_tables(5).csv"),
        new(193, "Chapter 4", 1, 1174, "ASI discovery; TrafficRandomizer_tables(5).csv"),
        new(203, "Chapter 4", 2, 1564, "ASI discovery; TrafficRandomizer_tables(5).csv"),
        new(19, "Online Traffic", 0, 1398, "ASI discovery, 14 archived sessions; Lua name ScriptsLuaScripts/gamelogic/phasemanager/onlinemissionsync/onlinemissionsync.lua (CRC32 init FFFFFFFF)"),
        new(19, "Online Traffic Trailblazer", 0, 2116, "ASI discovery (CSV 4)"),
        new(179, "Online Traffic Trailblazer", 1, 183, "ASI discovery (CSV 4)"),
        new(180, "Online Traffic Trailblazer", 2, 1791, "ASI discovery; TrafficRandomizer_tables(5).csv"),
        new(140, "Exposition", 0, 1383, "ASI discovery"),
        new(190, "Online Traffic Jump", 0, 323, "ASI discovery; TrafficRandomizer_tables(5).csv"),
        new(71, "Mind Control", 0, 2118, "ASI discovery"),
        new(27, "Chase traffic 3", 0, 28, "ASI discovery"),
        new(36, "Chase traffic 3", 0, 112, "ASI discovery"),
        new(44, "Chase traffic 3", 0, 6, "ASI discovery"),
        new(59, "Chase traffic 3", 0, 66, "ASI discovery"),
        new(66, "Chase traffic 3", 0, 33, "ASI discovery"),
        new(73, "Chase traffic 3", 0, 19, "ASI discovery"),
        new(91, "Chase traffic 3", 0, 108, "ASI discovery"),
        new(188, "Chase traffic 3", 0, 37, "ASI discovery"),
        new(157, "Chapter 6 - The target", 0, 1306, "ASI discovery"),
        new(197, "Online Traffic Takedown", 0, 939, "ASI discovery (CSV 4)"),
    };

    public static IReadOnlyList<ProfileDefinition> All { get; } = Build();
    public static int ObservedCount => All.Count(p => p.Observed);
    public static int UnconfirmedCount => All.Count(p => !p.Observed);

    private static IReadOnlyList<ProfileDefinition> Build()
    {
        var grouped = RuntimeAssociations.GroupBy(r => r.Table).ToDictionary(g => g.Key, g =>
            (IReadOnlyList<ProfileObservation>)g.Select(r => new ProfileObservation(r.Setting.Trim(), r.Frequency, r.DiscoveryPicks, r.Evidence)).ToArray());
        if (RuntimeAssociations.Length != 213 || grouped.Count != 135 || grouped.Keys.Any(t => t < 0 || t >= 208))
            throw new InvalidOperationException("Runtime profile catalog failed validation.");
        var definitions = new List<ProfileDefinition>(208);
        for (int t = 0; t < 208; t++)
        {
            IReadOnlyList<ProfileObservation> associations = grouped.TryGetValue(t, out var rows)
                ? rows : Array.Empty<ProfileObservation>();
            definitions.Add(new ProfileDefinition(t, associations));
        }
        if (definitions.Count(p => p.Observed) != 135 || definitions.Count(p => !p.Observed) != 73)
            throw new InvalidOperationException("Runtime profile coverage failed validation.");
        return definitions.OrderBy(p => p.Number).ToArray();
    }
}
