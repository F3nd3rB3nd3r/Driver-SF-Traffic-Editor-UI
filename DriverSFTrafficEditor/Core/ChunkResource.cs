using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace DriverSFTrafficEditor.Core;

// CHNK child offsets are relative to their container, not to the file.
// Keep opaque children, descriptor attributes and non-padding gaps/tails intact.
internal sealed class ChunkResource
{
    internal sealed record Child(int Descriptor, string Tag, int Offset, int Size, int Alignment, int NameLength, ChunkResource? Container);
    private readonly byte[] _bytes;
    internal IReadOnlyList<Child> Children { get; }
    internal int FileOffset { get; }

    private ChunkResource(byte[] bytes, int fileOffset, List<Child> children)
    { _bytes = bytes; FileOffset = fileOffset; Children = children; }

    internal static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    internal static void Put32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
    private static int Int(uint value) => checked((int)value);
    private static int Align(int value, int alignment) => checked((value + alignment - 1) & -alignment);

    internal static ChunkResource Parse(byte[] bytes, int fileOffset = 0, int depth = 0)
    {
        if (depth > 8 || bytes.Length < 16 || Encoding.ASCII.GetString(bytes, 0, 4) != "CHNK" || U32(bytes, 4) != bytes.Length)
            throw new InvalidDataException("Invalid CHNK container size/header.");
        int count = Int(U32(bytes, 8));
        if (count > 4096 || 16L + count * 16L > bytes.Length)
            throw new InvalidDataException("Invalid CHNK child count.");
        var children = new List<Child>();
        for (int i = 0; i < count; ++i)
        {
            int d = 16 + i * 16;
            string tag = Encoding.ASCII.GetString(bytes, d, 4);
            int offset = Int(U32(bytes, d + 4)), size = Int(U32(bytes, d + 12));
            int exponent = bytes[d + 10], nameLength = bytes[d + 9];
            if (exponent > 20 || offset < 16 + count * 16 || (long)offset + size + nameLength > bytes.Length ||
                offset % (1 << exponent) != 0)
                throw new InvalidDataException($"Invalid {tag} chunk bounds/alignment.");
            ChunkResource? nested = size >= 4 && Encoding.ASCII.GetString(bytes, offset, 4) == "CHNK"
                ? Parse(bytes.AsSpan(offset, size).ToArray(), checked(fileOffset + offset), depth + 1) : null;
            children.Add(new Child(d, tag, offset, size, 1 << exponent, nameLength, nested));
        }
        int end = 16 + count * 16;
        foreach (var child in children.OrderBy(c => c.Offset))
        {
            if (child.Offset < end) throw new InvalidDataException("Overlapping CHNK children.");
            end = checked(child.Offset + child.Size + child.NameLength);
        }
        return new ChunkResource(bytes, fileOffset, children);
    }

    internal IEnumerable<(ChunkResource Parent, Child Entry)> Find(string tag)
    {
        foreach (var c in Children)
        {
            if (c.Tag == tag) yield return (this, c);
            if (c.Container is not null)
                foreach (var match in c.Container.Find(tag)) yield return match;
        }
    }

    internal byte[] Replace(int leafFileOffset, byte[] payload)
    {
        using var stream = new MemoryStream();
        int oldEnd = 0, previousNameLength = 0;
        void WriteGap(int length, int alignment)
        {
            // Descriptors give the name length; names immediately follow payloads.
            // Preserve names/non-filler bytes and full extra padding blocks, while
            // replacing the old alignment contribution with the new one.
            int content = length;
            while (content > previousNameLength && _bytes[oldEnd + content - 1] == 0x3E) content--;
            int oldPadding = Align(oldEnd + content, alignment) - (oldEnd + content);
            int filler = length - content;
            if (filler < oldPadding) { stream.Write(_bytes, oldEnd, length); }
            else { stream.Write(_bytes, oldEnd, content + filler - oldPadding); }
            while (stream.Length % alignment != 0) stream.WriteByte(0x3E);
        }
        var updates = new List<(int Descriptor, int Offset, int Size)>();
        foreach (var child in Children.OrderBy(c => c.Offset))
        {
            WriteGap(child.Offset - oldEnd, child.Alignment);
            int start = checked((int)stream.Length);
            byte[] body = FileOffset + child.Offset == leafFileOffset ? payload :
                child.Container?.Replace(leafFileOffset, payload) ?? _bytes.AsSpan(child.Offset, child.Size).ToArray();
            stream.Write(body);
            updates.Add((child.Descriptor, start, body.Length));
            oldEnd = child.Offset + child.Size;
            previousNameLength = child.NameLength;
        }
        // PC outer CHNK wrappers are sector-aligned; the leaf container is 16-byte
        // aligned even when a particular expanded size happens to divide by 2048.
        int containerAlignment = Children.All(c => c.Container is not null) && _bytes.Length % 2048 == 0
            ? 2048 : _bytes.Length % 16 == 0 ? 16 : 1;
        WriteGap(_bytes.Length - oldEnd, containerAlignment);
        byte[] result = stream.ToArray();
        Put32(result, 4, checked((uint)result.Length));
        foreach (var update in updates)
        {
            Put32(result, update.Descriptor + 4, checked((uint)update.Offset));
            Put32(result, update.Descriptor + 12, checked((uint)update.Size));
        }
        return result;
    }
}
