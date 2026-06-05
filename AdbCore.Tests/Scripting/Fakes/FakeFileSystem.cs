using System;
using System.Collections.Generic;
using System.IO;
using AdbCore.Scripting;

namespace AdbCore.Tests.Scripting.Fakes;

/// <summary>In-memory <see cref="IFileSystem"/> for tests. Read of a missing path throws (like the real one).</summary>
public sealed class FakeFileSystem : IFileSystem
{
    public Dictionary<string, string> Files { get; } = new();

    public string Read(string path) => Files.TryGetValue(path, out var c) ? c : throw new FileNotFoundException(path);
    public void Write(string path, string content) => Files[path] = content;
    public void Copy(string source, string destination) => Files[destination] = Read(source);
    public void Move(string source, string destination) { Files[destination] = Read(source); Files.Remove(source); }
    public bool Exists(string path) => Files.ContainsKey(path);
    public void Delete(string path) => Files.Remove(path);
}
