// Extracted from retail-PC san_franciscoAI.fchunk cross-reference.
namespace DriverSFTrafficEditor.Core;

public static class TrafficNames
{
    private static readonly (uint Hash, string Name)[] Names =
    {
        (0x6BC6C949u, "Chapter 5 Raceaway"), // record 0
        (0xC071E02Au, "[Unknown name]"), // record 1
        (0xB066DC97u, "Chapter 2 Streetrace"), // record 2
        (0x825A838Cu, "Chase traffic 2"), // record 3
        (0xABF0BD85u, "Challenge traffic heavy"), // record 4
        (0xD754509Fu, "Chapter 8"), // record 5
        (0x0F2F8F6Bu, "Chapter 6 - Its for charity"), // record 6
        (0x9CB17769u, "[Unknown name]"), // record 7
        (0x11DF09F4u, "[Unknown name]"), // record 8
        (0x4DF9D266u, "DeactivatingBombs"), // record 9
        (0x960394CAu, "Online Traffic Hard"), // record 10
        (0x7EA32CA7u, "Online Traffic Sprint GP"), // record 11
        (0xDEE21CB4u, "Chapter 4"), // record 12
        (0x934D7C19u, "SM5 Something Weird "), // record 13
        (0x859FB6BDu, "Chapter 5 - Test Drive"), // record 14
        (0x5F88E6C4u, "Online Traffic Pursuit"), // record 15
        (0x40CF70C9u, "[Unknown name]"), // record 16
        (0x30EC7D98u, "Chapter 6"), // record 17
        (0x8489976Fu, "Cut scene traffic"), // record 18
        (0x1C3E162Fu, "Chase traffic 5"), // record 19
        (0x9BD98676u, "Free Drive"), // record 20
        (0x3DCD8BC2u, "Chapter 1 Kidnapped"), // record 21
        (0xD433D631u, "Challenge traffic"), // record 22
        (0x3781B981u, "Chapter 2"), // record 23
        (0x40868917u, "Chapter 3"), // record 24
        (0x6CDE3ABDu, "Exposition Drive"), // record 25
        (0x0440BCDBu, "SM2 Heat From Above "), // record 26
        (0xA9E52C22u, "Chapter 5"), // record 27
        (0x19D51C62u, "Chapter 7 Tickingclock"), // record 28
        (0x409E0517u, "Epilogue"), // record 29
        (0xFA0027C6u, "Chapter 6 - The target"), // record 30
        (0x47EB4D0Eu, "Chapter 7"), // record 31
        (0xBA946CD9u, "Chapter 2 LFK"), // record 32
        (0xF277ADC8u, "Mind Control"), // record 33
        (0x1E74118Fu, "Online Traffic Easy"), // record 34
        (0x6B933525u, "Chapter 6 - Special Edition"), // record 35
        (0xCC9F35B5u, "[Unknown name]"), // record 36
        (0xEF19694Au, "Exposition"), // record 37
        (0x19467768u, "Chapter 3 Shakedown"), // record 38
        (0x8FCB768Eu, "Chapter 1 College Fund"), // record 39
        (0x763F2E5Cu, "Online Traffic Survival"), // record 40
        (0x41E3DE48u, "Chapter 1 Breaking News"), // record 41
        (0xFFF9B540u, "Online Traffic Takedown"), // record 42
        (0x8BBF4193u, "Chapter 7 s"), // record 43
        (0x7A77B252u, "Chapter 6 Best Defence"), // record 44
        (0xA2606541u, "SM4 Collateral Damage"), // record 45
        (0xA1C8F477u, "Exposition Shift"), // record 46
        (0xC9E262C0u, "Chapter 7 Alone"), // record 47
        (0xDDA6CCDAu, "SM3 Peroxide Convoy"), // record 48
        (0x923EF4ABu, "Exposition Chase"), // record 49
        (0xAE88E83Bu, "Chapter 1"), // record 50
        (0xF55DB31Au, "Chase traffic 3"), // record 51
        (0x5956218Au, "[Unknown name]"), // record 52
        (0xA5CD5915u, "Chapter 3 Team Colours "), // record 53
        (0xE3D5685Au, "Online Traffic Trailblazer"), // record 54
        (0xF0A58D0Eu, "Online Traffic Jump"), // record 55
        (0x1B53D236u, "Chase traffic 1"), // record 56
        (0xBE043E76u, "Exposition Bullrun"), // record 57
    };

    public static string Resolve(int record, uint hash)
    {
        foreach (var entry in Names)
            if (entry.Hash == hash) return entry.Name;
        return $"[Unknown name] (hash 0x{hash:X8})";
    }
}
