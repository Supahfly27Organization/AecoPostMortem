using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// §3.8's first non-functional requirement: <em>no network call in v1 — not "no telemetry", no
/// socket</em>. Stated as a property of the compiled assemblies rather than of a review, because
/// the reason it matters is that the store holds the operator's prompts and source code.
/// </summary>
/// <remarks>
/// The check reads metadata rather than loading and reflecting, so an assembly that references a
/// networking type it never reaches at run time is still an offender — which is the point: the
/// requirement is that no outbound transport is referenced at all.
///
/// Its reach is the product's own assemblies and every product assembly ingestion pulls in. A
/// third-party package's internals are outside it; the guard is on the code this repository writes,
/// which is where a socket would be introduced.
/// </remarks>
public sealed class NoOutboundTransportTests
{
    static readonly string[] BannedNamespacePrefixes = ["System.Net"];

    static readonly string[] BannedTypeNames =
    [
        "HttpClient",
        "HttpListener",
        "HttpMessageHandler",
        "Socket",
        "TcpClient",
        "TcpListener",
        "UdpClient",
        "WebClient",
        "WebRequest",
        "ClientWebSocket",
        "SmtpClient",
        "NetworkStream",
        "Dns",
    ];

    static IEnumerable<string> ProductAssemblyPaths() =>
        Directory.EnumerateFiles(AppContext.BaseDirectory, "AecoPostMortem.*.dll")
            .Where(path => !Path.GetFileNameWithoutExtension(path)
                .EndsWith(".Tests", StringComparison.Ordinal));

    public static TheoryData<string> ProductAssemblies()
    {
        var data = new TheoryData<string>();
        foreach (var path in ProductAssemblyPaths())
        {
            data.Add(path);
        }

        return data;
    }

    [Fact]
    public void The_ingestion_assembly_and_what_it_writes_through_are_among_those_inspected()
    {
        // Without this, a build that stopped emitting the assemblies would make every other test in
        // this class pass over an empty set.
        var inspected = ProductAssemblyPaths().Select(Path.GetFileName).ToArray();

        Assert.Contains("AecoPostMortem.Ingestion.dll", inspected);
        Assert.Contains("AecoPostMortem.Data.dll", inspected);
    }

    [Theory]
    [MemberData(nameof(ProductAssemblies))]
    public void No_product_assembly_references_a_networking_assembly(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();

        var offenders = metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .Where(IsBannedNamespace)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"{Path.GetFileName(path)} references {string.Join(", ", offenders)}. §3.8 allows no "
            + "outbound transport in v1.");
    }

    [Theory]
    [MemberData(nameof(ProductAssemblies))]
    public void No_product_assembly_references_an_http_client_socket_or_other_transport(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();

        var offenders = metadata.TypeReferences
            .Select(metadata.GetTypeReference)
            .Select(type => (
                Namespace: metadata.GetString(type.Namespace),
                Name: metadata.GetString(type.Name)))
            .Where(type => IsBannedNamespace(type.Namespace) || IsBannedType(type.Name))
            .Select(type => $"{type.Namespace}.{type.Name}")
            .ToImmutableSortedSet();

        Assert.True(
            offenders.Count == 0,
            $"{Path.GetFileName(path)} references {string.Join(", ", offenders)}. §3.8 allows no "
            + "outbound transport in v1.");
    }

    static bool IsBannedNamespace(string candidate) =>
        BannedNamespacePrefixes.Any(prefix =>
            candidate.Equals(prefix, StringComparison.Ordinal)
            || candidate.StartsWith(prefix + ".", StringComparison.Ordinal));

    static bool IsBannedType(string candidate) =>
        BannedTypeNames.Contains(candidate, StringComparer.Ordinal);
}
