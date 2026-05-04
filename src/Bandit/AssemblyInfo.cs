using System.Runtime.CompilerServices;

// Required so [LibraryImport] source generators can handle our ETW interop
// structs directly (no built-in runtime marshaller). All P/Invokes in this
// assembly must therefore use blittable types and explicit string marshalling.
[assembly: DisableRuntimeMarshalling]