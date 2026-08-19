namespace AecoPostMortem.Rules.Tests;

/// <summary>Scenario 1 of issue #34: the vocabulary comes from the corpus, not from a list named in
/// source code.</summary>
public sealed class ToolVocabularyTests
{
    [Fact]
    public void The_vocabulary_contains_exactly_the_tools_observed()
    {
        ToolInvocationShape[] invocations =
        [
            new() { ToolName = "alpha", HasPath = true },
            new() { ToolName = "beta", HasPattern = true },
            new() { ToolName = "alpha", HasPath = true },
            new() { ToolName = "gamma", HasCommand = true },
        ];

        var vocabulary = ToolVocabulary.Build(invocations);

        Assert.Equal(
            new HashSet<string> { "alpha", "beta", "gamma" },
            vocabulary);
    }

    [Fact]
    public void An_empty_corpus_produces_an_empty_vocabulary()
    {
        var vocabulary = ToolVocabulary.Build([]);

        Assert.Empty(vocabulary);
    }
}
