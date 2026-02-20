using System.IO.MemoryMappedFiles;

using System.Runtime.CompilerServices;

using MemoryPipe.Internal;

namespace MemoryPipe
{
    public sealed class MemoryPipe<T> : IDisposable where T : unmanaged
    {
        private readonly Mutex? _hostMutex;
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;
        private readonly unsafe byte* _ptr;

        private readonly MemoryPipeBuffer<T> _sendChannel;
        private readonly MemoryPipeBuffer<T> _receiveChannel;

        private const int MetadataSize = 128; // Espacio reservado para el Header
        private const int Capacity = 1024;    // Slots por canal (debe ser potencia de 2)
        private static readonly int ChannelSize = 128 + (Capacity * Unsafe.SizeOf<T>());

        public unsafe MemoryPipe(string mapName, bool isHost)
        {
            int totalSize = MetadataSize + (ChannelSize * 2);

            _mmf = isHost
                ? MemoryMappedFile.CreateOrOpen(mapName, totalSize)
                : MemoryMappedFile.OpenExisting(mapName);

            _accessor = _mmf.CreateViewAccessor();
            byte* basePtr = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);
            _ptr = basePtr;

            // 1. Validar integridad antes de inicializar canales
            VerifyOrInitialize(isHost);

            // 2. IMPORTANTE: Los canales deben empezar DESPUÉS de la metadata
            byte* channelsStartPtr = _ptr + MetadataSize;

            if (isHost)
            {
                _hostMutex = new Mutex(true, $"Global\\QuarkMutex_{mapName}", out bool createdNew);
                if (!createdNew)
                {
                    _hostMutex.Dispose();
                    throw new InvalidOperationException($"Ya existe un Host activo para el canal '{mapName}'.");
                }

                // Host: Envía por el bloque 1, Recibe por el bloque 2
                _sendChannel = new MemoryPipeBuffer<T>(channelsStartPtr, ChannelSize, $"{mapName}_1");
                _receiveChannel = new MemoryPipeBuffer<T>(channelsStartPtr + ChannelSize, ChannelSize, $"{mapName}_2");
            }
            else
            {
                // Cliente: Recibe por el bloque 1, Envía por el bloque 2
                _receiveChannel = new MemoryPipeBuffer<T>(channelsStartPtr, ChannelSize, $"{mapName}_1");
                _sendChannel = new MemoryPipeBuffer<T>(channelsStartPtr + ChannelSize, ChannelSize, $"{mapName}_2");
            }
        }

        private unsafe void VerifyOrInitialize(bool isHost)
        {
            // Hash determinista DJB2 para evitar problemas de ASLR/GetHashCode
            int typeFingerprint = 17;
            string typeName = typeof(T).FullName ?? typeof(T).Name;
            foreach (char c in typeName) typeFingerprint = unchecked(typeFingerprint * 31 + c);
            typeFingerprint ^= Unsafe.SizeOf<T>();

            ref MemoryPipeHeader meta = ref Unsafe.AsRef<MemoryPipeHeader>(_ptr);

            if (isHost)
            {
                meta.Magic = 0x5049504D; // "MPIP"
                meta.TypeHash = typeFingerprint;
                meta.StructSize = Unsafe.SizeOf<T>();
                meta.HostProcessId = Environment.ProcessId;
            }
            else
            {
                if (meta.Magic != 0x5049504D)
                    throw new InvalidOperationException("Memoria no inicializada por un Host de MemoryPipe.");

                if (meta.TypeHash != typeFingerprint)
                    throw new TypeLoadException("El contrato de datos (Struct) no coincide entre procesos.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Send(in T message)
        {
            // Si el buffer está lleno, spineamos brevemente
            while (!_sendChannel.TryWrite(in message))
            {
                Thread.SpinWait(20);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Read() => _receiveChannel.Read();

        public void Dispose()
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _accessor.Dispose();
            _mmf.Dispose();
        }
    }
}
