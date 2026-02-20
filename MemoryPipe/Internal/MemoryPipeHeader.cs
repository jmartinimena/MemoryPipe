using System.Runtime.InteropServices;

namespace MemoryPipe.Internal
{
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    internal struct MemoryPipeHeader
    {
        [FieldOffset(0)] public uint Magic;
        [FieldOffset(4)] public int TypeHash;
        [FieldOffset(8)] public int StructSize;
        [FieldOffset(12)] public int HostProcessId;
    }
}
