
<div align="center">
  
# MemoryPipe
  
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![Runtime](https://img.shields.io/badge/.NET-10.0-512bd4)](https://dotnet.microsoft.com/)
[![Performance](https://img.shields.io/badge/Latency-Ultra--Low-brightgreen)](#performance-benchmarks)

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
