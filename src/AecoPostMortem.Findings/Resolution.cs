namespace AecoPostMortem.Findings;

/// <summary>
/// One operand's resolution as FR-56's suggestion templates consume it: the two reserved
/// placeholders <c>{OperandLayer}</c> and <c>{CallCount}</c> that <see cref="SuggestionRenderer"/>
/// binds directly rather than through an evidence field. Carried on <c>Finding.Resolution</c> only
/// where one applies.
///
/// <para>This is <em>not</em> the shape FR-33's served figure uses — see
/// <see cref="AdherenceFigure"/> (S-24, issue #38), which carries an
/// <see cref="OperandResolution"/> per operand and derives the percentage from their call counts.
/// The two are deliberately separate: a suggestion template names one concrete operand in a
/// sentence, while a served figure has to state every operand's layer at once or it cannot be
/// judged. Keeping this record single-operand is what lets <c>SuggestionRenderer</c> stay a pure
/// substitution with nothing to summarise or join.</para>
/// </summary>
public sealed record Resolution
{
    public required string OperandLayer { get; init; }

    public required int CallCount { get; init; }
}
