interface ComingSoonProps {
  surface: string
  story: string
  release: string
}

/** S-48, Scenario 1: a surface not yet built shows a placeholder naming the release it arrives
 * in. Every one of the three routes uses this today — none of them has its real content built
 * yet — but each names its own story and release rather than sharing one generic message. */
export function ComingSoon({ surface, story, release }: ComingSoonProps) {
  return (
    <div className="coming-soon">
      <h2>{surface}</h2>
      <p>
        This surface is not built yet. It arrives in {release} ({story}).
      </p>
    </div>
  )
}
