using System.Buffers;
using System.IO;
using ProtoBuf;

namespace Core.Features.Documents;

/// <summary>Protocol Buffers encoding (protobuf-net) for cached documents. Field numbers are part of the format: never reuse or renumber.</summary>
internal static class DocumentCacheSerializer
{
    private const int FormatVersion = 1;

    public static byte[] Serialize(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Write(CachedDocument.From(document));
    }

    public static byte[] Serialize(IReadOnlyCollection<Document> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return Write(new CachedDocumentList { Version = FormatVersion, Items = documents.Select(CachedDocument.From).ToList() });
    }

    /// <summary>True for the failures a corrupt or foreign cache entry can cause while decoding; callers treat these as a cache miss.</summary>
    public static bool IsDecodeFailure(Exception exception) =>
        exception is ProtoException or OverflowException or EndOfStreamException;

    public static Document DeserializeDocument(ReadOnlyMemory<byte> bytes) =>
        Serializer.Deserialize<CachedDocument>(bytes).ToDocument();

    public static List<Document> DeserializeList(ReadOnlyMemory<byte> bytes) =>
        Serializer.Deserialize<CachedDocumentList>(bytes).Items.Select(i => i.ToDocument()).ToList();

    private static byte[] Write<T>(T message)
    {
        var buffer = new ArrayBufferWriter<byte>();
        Serializer.Serialize(buffer, message);
        return buffer.WrittenMemory.ToArray();
    }

    [ProtoContract]
    private sealed class CachedDocument
    {
        // Set to 1 on every write, so it is always sent and a payload is never zero bytes.
        [ProtoMember(1)]
        public int Version { get; set; }

        [ProtoMember(2)]
        public Guid Id { get; set; }

        [ProtoMember(3)]
        public string Tenant { get; set; } = string.Empty;

        [ProtoMember(4)]
        public string Title { get; set; } = string.Empty;

        public static CachedDocument From(Document document) =>
            new() { Version = FormatVersion, Id = document.Id, Tenant = document.Tenant, Title = document.Title };

        public Document ToDocument() => new(Id, Tenant, Title);
    }

    [ProtoContract]
    private sealed class CachedDocumentList
    {
        [ProtoMember(1)]
        public int Version { get; set; }

        [ProtoMember(2)]
        public List<CachedDocument> Items { get; set; } = [];
    }
}
