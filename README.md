
<div align="center">
  
# MemoryPipe
  
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![Runtime](https://img.shields.io/badge/.NET-10.0-512bd4)](https://dotnet.microsoft.com/)

</div>

`MemoryPipe<T>` is a high-performance, zero-copy Inter-Process Communication (IPC) library for .NET. Built on top of Shared Memory (Memory Mapped Files) and Lock-Free Ring Buffers, it provides a specialized transport for unmanaged structures.

## 🚀 Performance Benchmarks (.NET 10)

The following table compares the **Full Round-Trip Latency** (Host → Client → Host). This measures the total time to send a message and receive a confirmation/response.

*Tests performed on: Ryzen 5 5600X 3.7GHz, 32 GB DDR4 3600MT/s*
| Transport Method | Round-Trip Latency | Efficiency | Architecture |
| :--- | :--- | :--- | :--- |
| **MemoryPipe** | **~0.59 µs** | **100%** | **Zero-Copy / L1 Cache** |
| Named Pipes (Local) | ~30.80 µs | 1.9% | Kernel Context Switch |
| TCP Sockets (Loopback) | ~90.00 µs | 0.6% | Full Network Stack |
| gRPC (HTTP/2) | ~280.00 µs | 0.2% | Serialization + Overhead |

> **Note:** MemoryPipe's round-trip is faster than a single-way write operation in most other IPC frameworks.

*Benchmarks conducted on .NET 10.0, x64 architecture, sending 1 million 128-byte packets.*

## ✨ Key Features

- **Lock-Free Design:** Uses atomic `Volatile` operations for head/tail synchronization.
- **Zero Allocations:** No Garbage Collector (GC) pressure during transmission.
- **Bi-directional:** Dual-channel communication (Inbox/Outbox) over a single mapped region.
- **Type Safety:** Compile-time validation for `unmanaged` constraints to ensure memory integrity.

## 🧠 Technical Deep Dive: Why is it so fast?

MemoryPipe achieves sub-microsecond latency by eliminating the three primary bottlenecks found in standard .NET IPC implementations.

### 1. Zero Context-Switching (User-Mode Only)
Standard IPC (Named Pipes, Sockets) requires a transition from **User Mode** to **Kernel Mode** for every read/write operation. This "Context Switch" forces the CPU to save register states and flush certain cache lines, costing thousands of clock cycles.
* **MemoryPipe approach:** Communication happens entirely in User Mode via Shared Memory. The CPU never hands control to the OS kernel during the hot-path, keeping the execution flow within the L1/L2 cache boundaries.

### 2. Zero-Copy Architecture
Traditional stacks involve multiple memory copies: `App Buffer -> Serializer -> Socket Buffer -> Kernel Buffer -> NIC/Loopback`.
* **MemoryPipe approach:** It maps the same physical RAM page into the virtual address space of both processes. When you call `Write(in T message)`, you are modifying the exact same bits that the other process is observing. There is no intermediate buffer, no serialization, and no heap allocation.

### 3. Lock-Free Atomic Synchronization
Locks and Mutexes are heavy. Acquiring a `lock` or a `Semaphore` often involves kernel-level arbitration, which is slow.
* **MemoryPipe approach:** We utilize **Memory Barriers** and **Atomic Volatiles**. The `Head` and `Tail` pointers of the Ring Buffer are updated using atomic CPU instructions. This allows one process to write while the other reads without ever stopping to wait for a software lock, provided the buffer is not full/empty.

### 4. .NET 10 Hardware Intrinsics & PGO
By targeting **.NET 10**, MemoryPipe benefits from:
- **Dynamic PGO:** The JIT compiler observes that the `Read/Write` methods are called frequently and inlines them directly into your application's loop.
- **Enhanced Struct Spilling:** Improved handling of `in` parameters to ensure structs are passed via CPU registers rather than the stack whenever possible.

## 📖 Quick Start Examples

To use `MemoryPipe<T>`, the data contract must be an `unmanaged struct`.

```csharp
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MessageFrame
{
    public int Id;
    public double Timestamp;
}
```

### Server Implementation (Host)
The server is responsible for creating and owning the shared memory resource.

```csharp
using MemoryPipe;

try
{
  // 1. Initialize the Server with a global unique name
  using var server = new MemoryPipe<MessageFrame>("MyPipe", isHost: true);
  
  while (true)
  {
      // 2. Read (Uses optimized SpinWait for minimal latency)
      var request = server.Read();
  
      // 3. Write response back to client
      server.Write(new MessageFrame { 
          CommandId = request.CommandId + 1000, 
          Timestamp = DateTime.UnixEpoch.Ticks 
      });
  }
}
catch (Exception)
{
  ...
}
```

### Client Implementation
The client attaches to the existing memory map. It requires the Host to be already running.

```csharp
using MemoryPipe;

try
{
  // 1. Connect to the channel created by the Host
  using var client = new MemoryPipe<MessageFrame>("MyPipe", isHost: false);
  
  // 2. Send data to the server
  client.Write(new MessageFrame { CommandId = 1, Timestamp = 0 });
  
  // 3. Wait for the Round-Trip response
  var response = client.Read();
}
catch (Exception)
{
  ...
}
```
