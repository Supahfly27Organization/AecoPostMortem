import type { AdherenceFigure, OperandResolutionLayer } from '../api/digest'
import './AdherenceFigureBlock.css'

/** FR-31's four layers in the operator's words. The wire carries the enum member (camelCased by
 * `ApiHost`'s global converter); this is the only place it is turned into a phrase, so a layer can
 * never render as a raw identifier in one surface and a sentence in another. */
const layerLabels: Record<OperandResolutionLayer, string> = {
  exactToolName: 'Exact tool name',
  mcpServerField: 'MCP server field',
  derivedRole: 'Derived role',
  unresolved: 'Unresolved',
}

/** Trailing zeros dropped, so a clean 75% does not render as "75.0%". */
function formatPercentage(percentage: number): string {
  return `${Number(percentage.toFixed(1))}%`
}

/**
 * Scenario 1 (issue #38, FR-33): "the layer used per operand and the resulting call counts are shown
 * with it." This component is the only place in the app that renders an adherence percentage, and it
 * cannot render one without the operand table directly beneath — the same refusal
 * `AecoPostMortem.Api.FindingEnvelope.Adherence` makes structural on the wire, kept structural here
 * by having exactly one component own both halves rather than two a caller could compose apart.
 *
 * The guard is the measured fivefold spread on one rule that came from the resolution choice alone:
 * an operator who cannot see which layer resolved each operand has no way to reject a number whose
 * mapping is wrong.
 */
export function AdherenceFigureBlock({ figure }: { figure: AdherenceFigure }) {
  return (
    <section className="adherence-figure">
      <p className="adherence-figure__headline">
        {figure.percentage === null ? (
          <span className="adherence-figure__no-percentage">
            No calls were observed for this rule, so it has no adherence percentage.
          </span>
        ) : (
          <>
            <span className="adherence-figure__percentage">{formatPercentage(figure.percentage)}</span>
            <span className="adherence-figure__counts">
              {figure.adherentCalls} of {figure.totalCalls} calls
            </span>
          </>
        )}
      </p>

      <table className="adherence-figure__resolution">
        <caption>Resolution — the layer that resolved each operand, and the calls it produced</caption>
        <thead>
          <tr>
            <th scope="col">Operand</th>
            <th scope="col">Layer</th>
            <th scope="col">Calls</th>
          </tr>
        </thead>
        <tbody>
          {figure.operands.map((operand) => (
            <tr key={`${operand.operandText}:${operand.layer}`} data-layer={operand.layer}>
              <th scope="row">{operand.operandText}</th>
              <td>{layerLabels[operand.layer]}</td>
              <td>{operand.callCount}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <p className="adherence-figure__rule-version">
        Rule set{' '}
        <span className="adherence-figure__rule-version-hash">
          {figure.ruleVersion.repository ?? 'no repository'} @ {figure.ruleVersion.hash}
        </span>
      </p>
    </section>
  )
}
