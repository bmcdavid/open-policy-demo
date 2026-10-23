namespace Core.Features.Documents;

public static class DocumentSeed
{
    public static IReadOnlyList<Document> Documents { get; } =
    [
        new(Guid.Parse("00000000-0000-0000-0000-00000000ac3e", System.Globalization.CultureInfo.InvariantCulture), "acme", "Acme quarterly plan"),
        new(Guid.Parse("00000000-0000-0000-0000-0000000610be", System.Globalization.CultureInfo.InvariantCulture), "globex", "Globex roadmap"),
    ];
}
