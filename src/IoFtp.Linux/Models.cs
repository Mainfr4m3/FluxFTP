using System.ComponentModel;
using IoFtp.Core.Models;
namespace IoFtp.Linux;
internal sealed record FileItem(string Name, string Path, bool Directory, long Size)
{
    public override string ToString() => $"{(Directory ? "[DIR]" : "     ")}  {Name}    {(Directory ? "" : Size.ToString("N0") + " B")}";
}
internal sealed class Job : INotifyPropertyChanged
{
    public Guid Id { get; } = Guid.NewGuid();
    public required FileItem File { get; init; }
    public ConnectionProfile? Source { get; init; }
    public ConnectionProfile? Target { get; init; }
    public required string Destination { get; init; }
    public int Order { get; set; }
    public string State { get; set; } = "Queued";
    public string Error { get; set; } = "";
    public long Bytes { get; set; }
    public long Speed { get; set; }
    public string Line => $"{State,-12} {File.Name}  {(File.Size > 0 ? Math.Min(100, Bytes * 100d / File.Size).ToString("0") + "%" : "")}  {Speed / 1_000_000d:0.00} MB/s  {Destination}  {Error}";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new(nameof(Line)));
}
