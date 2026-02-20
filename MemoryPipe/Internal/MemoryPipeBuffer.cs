using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace MemoryPipe.Internal
{
    public readonly unsafe struct MemoryPipeBuffer<T>(byte* ptr, int totalSize, string channelName) where T : unmanaged
    {
        private readonly byte* _ptr = ptr;
        private readonly int _totalSize = totalSize;
        private readonly EventWaitHandle _notifier = new(false, EventResetMode.AutoReset, $"Global_MemoryPipe_{channelName}");

        private const int ControlSize = 128;

        private ref MemoryPipeControl Control => ref Unsafe.AsRef<MemoryPipeControl>(_ptr);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryWrite(in T message)
        {
            var slots = MemoryMarshal.Cast<byte, T>(new Span<byte>(_ptr + ControlSize, _totalSize - ControlSize));
            int capacity = slots.Length; // Debe ser potencia de 2
            int mask = capacity - 1;

            long currentTail = Volatile.Read(ref Control.Tail);
            long currentHead = Volatile.Read(ref Control.Head);

            if (currentTail - currentHead >= capacity) return false;

            slots[(int)(currentTail & mask)] = message;
            Volatile.Write(ref Control.Tail, currentTail + 1);
            _notifier.Set();
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Read()
        {
            var slots = MemoryMarshal.Cast<byte, T>(new Span<byte>(_ptr + ControlSize, _totalSize - ControlSize));
            int mask = slots.Length - 1;
            var spinner = new SpinWait();

            while (true)
            {
                long currentHead = Volatile.Read(ref Control.Head);
                long currentTail = Volatile.Read(ref Control.Tail);

                if (currentHead < currentTail)
                {
                    T data = slots[(int)(currentHead & mask)];
                    Volatile.Write(ref Control.Head, currentHead + 1);
                    return data;
                }

                if (spinner.NextSpinWillYield) _notifier.WaitOne(1);
                else spinner.SpinOnce();
            }
        }
    }
}
