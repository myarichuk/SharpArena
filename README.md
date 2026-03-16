# SharpArena
Zero-alloc arena allocator + collections for high-perf parsers.

*This is a pet project originally written for a JsonPath parser as a challenge, but I decided to keep it as a separate lib because I think it's useful.*

## Samples

Check out the samples directory for complete, end-to-end examples of building a math expression parser with zero managed allocations (outside the arena).
**Now with Blazor WebAssembly demo — zero managed allocations in the browser**

- [**SimpleMathParser**](samples/SimpleMathParser/) [![Try SimpleMathParser](https://img.shields.io/badge/Try%20it-SimpleMathParser-blue)](samples/SimpleMathParser/): A standard .NET console app using `ArenaList`, `ArenaPtrStack`, and `ArenaString` to tokenize and evaluate expressions.
- [**BlazorMathSymbolicCalculator**](samples/BlazorMathSymbolicCalculator/) [![Try BlazorMathSymbolicCalculator](https://img.shields.io/badge/Try%20it-BlazorMathSymbolicCalculator-purple)](samples/BlazorMathSymbolicCalculator/): A symbolic calculator Blazor WebAssembly app evaluating math equations in-browser natively without garbage collection.

### Reusable Parser Example
The `ArenaMathParser` extracted for the above samples showcases reusing arena allocations efficiently:

```csharp
using SharpArena.Allocators;

using var arena = new ArenaAllocator();

// Tokenize the mathematical string into unmanaged tokens inside the arena
var tokens = ArenaMathParser.Tokenize("2 + 3 * x".Replace("x", "10"), arena);

// Evaluate safely allocating on the unmanaged stack internally
double result = ArenaMathParser.Evaluate(tokens, arena);

// Resets pointers and drops all tokens without GC involvement
arena.Reset();
```

## Usage
```csharp
using SharpArena.Allocators;

using var arena = new ArenaAllocator();
// Memory allocated from arena, invalid after arena is Reset or Disposed
var ptr = arena.Alloc(1024);
arena.Reset();
```

## Collections

SharpArena includes several collection types designed to be backed by the arena allocator. These collections allocate memory from the arena and become invalid when the arena is reset or disposed. They avoid normal GC allocations.

### ArenaString
A non-owning view of UTF-16 text stored in unmanaged (arena) memory.

```csharp
using SharpArena.Collections;

// Clone a managed string or span into the arena
var str = ArenaString.Clone("Hello, World!", arena);

// Can be implicitly cast to ReadOnlySpan<char>
ReadOnlySpan<char> span = str;

// Or explicitly converted back to a managed string
Console.WriteLine(str.ToString());
```

`ArenaString` equality and hash codes are both content-based (character data + length), so separately cloned but equal text compares and hashes the same.
**When to use:** Use `ArenaString` when you need to store substrings, tokens, or parsed text during parsing or processing without creating `System.String` allocations for every token.

### ArenaList
A dynamic array (list) backed by the arena for unmanaged structs.

```csharp
using SharpArena.Collections;

var list = new ArenaList<int>(arena, initialCapacity: 16);
list.Add(1);
list.Add(2);
list.Add(3);

foreach (var item in list.AsSpan()) {
    Console.WriteLine(item);
}
```
**When to use:** Use `ArenaList<T>` when you need a fast, resizable list of items (like AST nodes or tokens) during a single operation, avoiding GC overhead for arrays.

### ArenaPtrStack
A fast, unmanaged stack specifically for pointers.

```csharp
using SharpArena.Collections;

var stack = new ArenaPtrStack<int>(arena, initialCapacity: 16);
int a = 42;
stack.Push(&a);

var ptr = stack.Pop();
Console.WriteLine(*ptr);
```
**When to use:** Use `ArenaPtrStack<T>` when writing parsers or state machines that need to push and pop pointers to unmanaged memory rapidly.

## Comparison with Varena

[Varena](https://github.com/xoofx/Varena) is another excellent arena allocator library for .NET. Here is a quick comparison to help you choose:

### SharpArena
* **Internal workings:** Uses a linked list of segments (`ArenaSegment`), dynamically allocating memory via `NativeMemory` or `P/Invoke` as needed. Memory is requested in chunks and bumped within the active segment.
* **Limitations:** Over-allocates slightly if many small objects are allocated, since segment sizes double up to a maximum. Requires careful management of unmanaged memory since there are no GC roots.
* **When to use:** Best for fast parsing scenarios (like parsers or temporary request states) where you want to allocate a batch of objects, possibly strings or lists, and drop them all at once. Includes built-in collections.

### Varena
* **Internal workings:** Leverages virtual memory directly (e.g., `VirtualAlloc` on Windows). It reserves a massive contiguous virtual address space upfront and commits physical memory pages strictly as needed.
* **Limitations:** Not available in WASM environments because it requires virtual memory OS APIs.
* **When to use:** Better when you need to continuous blocks of very large memory without worrying about re-allocations or segment limits, and you are on a supported platform (Windows/Linux/macOS).

## Thread Safety

> [!IMPORTANT]
> **SharpArena is strictly NOT thread-safe.**

To achieve maximum performance and zero overhead on the hot path, `ArenaAllocator` and its associated collections do not use any synchronization primitives (locks, interlocked operations, or volatile reads).

- **One Arena Per Thread:** You should create a separate `ArenaAllocator` instance for each thread or use a `[ThreadStatic]` field.
- **No Concurrent Access:** Do not call `Alloc`, `Reset`, or `Dispose` concurrently from multiple threads on the same instance.
- **Single-Threaded Collections:** Collections like `ArenaList<T>` and `ArenaString` are intended to be used within the same thread that owns the arena.

## Benchmarks
See `bench/ArenaBench.md` for performance numbers compared to NativeMemory and Varena.
