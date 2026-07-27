using System.IO;

namespace AaronOS.Modules.Medical.Data;

public class MedicalDocument
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public required string FilePath { get; set; }
    public DateOnly AddedOn { get; set; }
    public int? VisitId { get; set; }
    public MedicalVisit? Visit { get; set; }
    public string? Notes { get; set; }

    /// <summary>Only the path is stored, never the bytes — so a moved or deleted file has to be
    /// shown as missing rather than silently failing to open.</summary>
    public bool FileExists => !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);
    public bool IsMissing => !FileExists;
    public string StatusDisplay => FileExists ? "OK" : "File missing";
    public string AddedDisplay => AddedOn.ToString("MMM d, yyyy");
    public string VisitDisplay => Visit?.ShortLabel ?? "—";
}
