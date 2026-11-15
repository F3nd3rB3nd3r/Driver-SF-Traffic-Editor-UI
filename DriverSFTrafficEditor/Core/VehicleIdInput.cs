using System.Globalization;
using System.Text.RegularExpressions;

namespace DriverSFTrafficEditor.Core;

public static class VehicleIdInput
{
    // A numeric list, not vehicle names. Accept decimal/hex and inclusive ranges.
    public static uint[] Parse(string input)
    {
        var result = new HashSet<uint>();
        uint Number(string text)
        {
            bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (!uint.TryParse(hex ? text[2..] : text, hex ? NumberStyles.HexNumber : NumberStyles.None,
                    CultureInfo.InvariantCulture, out uint id) || id is 0 or > ushort.MaxValue)
                throw new FormatException($"Invalid vehicle ID '{text}'. Use IDs 1 through 65535.");
            return id;
        }
        foreach (string token in Regex.Split(input.Trim(), @"[\s,;]+"))
        {
            if (token.Length == 0) continue;
            string[] range = token.Split('-');
            if (range.Length > 2) throw new FormatException($"Invalid range '{token}'.");
            uint first = Number(range[0]), last = range.Length == 2 ? Number(range[1]) : first;
            if (last < first) throw new FormatException($"Range '{token}' is reversed.");
            for (uint id = first; id <= last; id++) result.Add(id);
        }
        if (result.Count == 0) throw new FormatException("Enter one or more vehicle IDs.");
        return result.ToArray();
    }
}
