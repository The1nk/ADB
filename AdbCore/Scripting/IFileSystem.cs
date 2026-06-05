namespace AdbCore.Scripting;

/// <summary>Filesystem operations the Lua <c>fs</c> host module needs. Injectable so the module is unit-testable
/// without touching the real disk. Failures throw (the module maps them to Lua errors).</summary>
public interface IFileSystem
{
    string Read(string path);
    void Write(string path, string content);
    void Copy(string source, string destination);
    void Move(string source, string destination);
    bool Exists(string path);
    void Delete(string path);
}
