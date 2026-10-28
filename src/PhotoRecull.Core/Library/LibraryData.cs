namespace PhotoRecull.Core.Library;

public record LibraryData
{
    public List<Cleanup> Cleanups { get; init; } = [];
}
