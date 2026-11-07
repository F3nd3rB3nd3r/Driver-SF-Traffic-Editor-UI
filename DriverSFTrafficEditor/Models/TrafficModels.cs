using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DriverSFTrafficEditor.Core;

namespace DriverSFTrafficEditor.Models;

public abstract class NotifyRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class SpoolRow : NotifyRow
{
    private int _vehicleId;
    public int Slot { get; }
    public int FileOffset { get; }
    public int OriginalId { get; }
    public string OffsetHex => $"0x{FileOffset:X6}";
    public string IdHex => $"0x{_vehicleId:X4}";
    public bool IsModified => _vehicleId != OriginalId;

    public int VehicleId
    {
        get => _vehicleId;
        set
        {
            if (value < 0 || value > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(VehicleId), "A spooling ID must be 0 through 65535.");
            if (_vehicleId == value) return;
            _vehicleId = value;
            Raise(); Raise(nameof(IdHex)); Raise(nameof(IsModified));
        }
    }

    public SpoolRow(int slot, int fileOffset, int vehicleId)
    {
        Slot = slot;
        FileOffset = fileOffset;
        OriginalId = vehicleId;
        _vehicleId = vehicleId;
    }
}

public sealed class SpoolGroup
{
    public int Index { get; }
    public int FirstIdOffset { get; }
    public int Count => Rows.Count;
    public ObservableCollection<SpoolRow> Rows { get; }
    public string Display => $"Group {Index}   ({Count} vehicles)";
    public string OffsetHex => Count == 0 ? "Not used" : $"0x{FirstIdOffset:X6}";

    public SpoolGroup(int index, int firstIdOffset, ObservableCollection<SpoolRow> rows)
    {
        Index = index; FirstIdOffset = firstIdOffset; Rows = rows;
    }
}

public sealed class TrafficSet
{
    public int Index { get; }
    public uint Hash { get; }
    public string Name { get; }
    public int MetadataOffset { get; }
    public IReadOnlyList<SpoolGroup> Groups { get; }
    public string DisplayName => $"{Name}  [{Index}]";
    public string HashHex => $"0x{Hash:X8}";
    public string MetaHex => $"0x{MetadataOffset:X6}";
    public int IdCount => Groups.Sum(x => x.Count);

    public TrafficSet(int index, uint hash, string name, int metadataOffset, IReadOnlyList<SpoolGroup> groups)
    { Index = index; Hash = hash; Name = name; MetadataOffset = metadataOffset; Groups = groups; }
}

public sealed class WeightRow : NotifyRow
{
    private double _weight;
    public int Slot { get; }
    public uint VehicleId { get; }
    public int IdFileOffset { get; }
    public int WeightFileOffset => IdFileOffset + 4;
    public string IdHex => $"0x{VehicleId:X4}";
    public string OffsetHex => $"0x{WeightFileOffset:X6}";
    public string IdOffsetHex => $"0x{IdFileOffset:X6}";
    public float OriginalWeight { get; }
    public bool IsModified => (float)_weight != OriginalWeight;
    public string FloatHex => $"0x{BitConverter.SingleToUInt32Bits((float)_weight):X8}";

    public double Weight
    {
        get => _weight;
        set
        {
            if (!double.IsFinite(value) || value < 0 || value > float.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(Weight), "The weight must be a finite nonnegative Float32 value.");
            if (_weight == value) return;
            _weight = value;
            Raise(); Raise(nameof(IsModified)); Raise(nameof(FloatHex));
        }
    }

    public WeightRow(int slot, int idFileOffset, uint vehicleId, float weight)
    {
        Slot = slot; IdFileOffset = idFileOffset; VehicleId = vehicleId;
        OriginalWeight = weight; _weight = weight;
    }
}

public sealed class WeightProfile
{
    public ProfileDefinition Definition { get; }
    public int MetadataOffset { get; }
    public int DataOffset { get; }
    public float OriginalTotal { get; }
    public ObservableCollection<WeightRow> Rows { get; }
    public int Number => Definition.Number;
    public string DisplayName => Definition.Display;
    public string MetaHex => $"0x{MetadataOffset:X6}";
    public string DataHex => $"0x{DataOffset:X6}";
    public double CurrentTotal => Rows.Sum(r => r.Weight);

    public WeightProfile(ProfileDefinition definition, int metaOffset, int dataOffset, float originalTotal, ObservableCollection<WeightRow> rows)
    { Definition = definition; MetadataOffset = metaOffset; DataOffset = dataOffset; OriginalTotal = originalTotal; Rows = rows; }
}
