using System.IO;

namespace AdbCore.Scripting;

/// <summary>The real <see cref="IFileSystem"/> backed by <see cref="System.IO.File"/>.</summary>
public sealed class LiveFileSystem : IFileSystem
{
    public string Read(string path) => File.ReadAllText(path);
    public void Write(string path, string content) => File.WriteAllText(path, content);
    public void Copy(string source, string destination) => File.Copy(source, destination, overwrite: true);
    public void Move(string source, string destination) => File.Move(source, destination, overwrite: true);
    public bool Exists(string path) => File.Exists(path);
    public void Delete(string path) => File.Delete(path);
}
