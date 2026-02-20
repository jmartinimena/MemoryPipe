using System.Runtime.InteropServices;

namespace MemoryPipe.Internal
{
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    public struct MemoryPipeControl
    {
        [FieldOffset(0)] public long Head;
        [FieldOffset(64)] public long Tail;
    }
}
