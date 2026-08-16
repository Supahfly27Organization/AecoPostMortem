---
name: github-issue-sync
description: Use immediately after the product-superpowers user-story-writing skill's User Review Gate has been explicitly approved by the user. First asks whether to sync the approved stories to GitHub at all; only if confirmed, converts them into GitHub issues (native Epic/Story issue types), one confirmation per issue.
---

# GitHub Issue Sync

Turn an approved `docs/product-superpowers/stories/*.md` document into GitHub issues, with epics as parent issues and stories as linked sub-issues.

**Announce at start:** "The stories doc is approved — want me to create these as GitHub issues?"

<HARD-GATE>
Do NOT invoke the issue-creation steps below (Step 1 onward) until:
1. The user has explicitly approved the stories document (a reply like "approved", "looks good", "yes create them" after the User Review Gate prompt in user-story-writing) — a stories doc merely being *written* is not approval, and
2. The user has separately confirmed they want it synced to GitHub (Step 0) — approving the story content is not the same as agreeing to create GitHub issues from it.
</HARD-GATE>

## Checklist

0. Ask whether to sync the approved stories to GitHub; stop here if declined
1. Resolve `owner`/`repo` from `git remote get-url origin`
2. Fetch org issue types via `mcp__github__list_issue_types`
3. Map each epic and story to a type (see Type Mapping)
4. Resolve the target Project board, creating it from this project's board template only if none exists yet, and its `Priority`/`Size` fields (see Step 3b)
5. For each epic: propose the mapped issue, wait for per-issue confirmation, create it
6. For each story under that epic: propose the mapped issue, wait for per-issue confirmation, create it, link it under the epic, then sync its `Priority`/`Size` Project fields (see Step 5b)
7. Append a "GitHub Issues" section to the stories doc mapping story titles → issue URLs
8. Summarize what was created and what was skipped, including any Project field syncs that failed

## Step 0: Confirm GitHub sync

Right after the stories doc's User Review Gate is approved, ask the user whether they want these stories synced to GitHub at all (see Announce line above). If they decline, stop here — do not create anything and do not proceed to Step 1. If they agree, continue.

## Step 1: Resolve owner/repo

```
git remote get-url origin
```

Parse `owner/repo` from the URL. If there's no GitHub remote, stop and tell the user this skill needs one.

## Step 2: Type Mapping

Call `mcp__github__list_issue_types` for the org.

- If types named (or clearly matching) **Epic** and **Story**/**User Story**/**Task** exist, use their exact names.
- If native issue types are **not** configured for the org, do not silently fall back — tell the user native issue types aren't available and ask whether to use labels (`epic` / `user-story`) instead before proceeding.

## Step 3b: Resolve the target Project board

Stories carry `Priority` and `Estimate` values in their body (Step 4). To keep the org's Project board queryable, sync those values into the board's `Priority` and `Size` single-select fields once each story issue is created.

1. `gh project list --owner <org>` — if exactly one open Project exists, use it **as-is** (do not touch its `Status` field options — the board template in step 2 below only ever applies to a Project created in this same step, never to a pre-existing one). If multiple exist, ask which one to use. If **none** exist, ask the user whether to create one now:
   - If yes, ask for a title for the new Project, create it with `gh project create --owner <org> --title "<title>"`, then apply this project's board template (step 2) before creating any issues.
   - If no, ask whether to skip field sync for this run instead (issue creation can still proceed without a Project).
2. **Only immediately after creating a brand-new Project in step 1** — never on a pre-existing one — replace GitHub's default `Status` field (`Todo`/`In Progress`/`Done`) with this project's board template: `Backlog, Ready, In Progress, Waiting to review, Done`.
   - `gh project field-list <number> --owner <org>` to find the default `Status` field's id.
   - `gh project field-delete --id <status-field-id>` — the default field's options can't be replaced via `updateProjectV2Field` (same "Only custom fields can be updated" restriction as `Priority`/`Size` in step 3), so delete and recreate it.
   - `gh project field-create <number> --owner <org> --name "Status" --data-type SINGLE_SELECT --single-select-options "Backlog,Ready,In Progress,Waiting to review,Done"`.
   - New issues land on whichever option is first (`Backlog`) via GitHub's "auto-add to project" default — no explicit action needed to put them there.
3. `gh project field-list <number> --owner <org>` — find fields named `Priority` and `Size` (case-insensitive; `Size` is GitHub's default name for a T-shirt-size field, `Estimate` may instead be a plain number field for story points — don't confuse the two). If either field is missing (including on a Project you just created), or is a `SINGLE_SELECT` field with **zero options**, create/fix it: `gh project field-create <number> --owner <org> --name "Priority" --data-type SINGLE_SELECT --single-select-options "Must Have,Should Have,Could Have,Won't Have"` (match the options used in `.github/ISSUE_TEMPLATE/user_story.yml`; for `Size` use `XS,S,M,L,XL`). If the field already exists but has zero options, do not try to add options with `updateProjectV2Field` — it fails with "Only custom fields can be updated" on default-template fields. Instead delete and recreate it: `gh project field-delete --id <field-id>` then run the `field-create` command above.
4. Cache the project id, the `Status`/`Priority`/`Size` field ids, and each field's option-name → option-id map for the rest of this run — don't re-query per issue.

**Auth note:** this org's `gh` auth may have a stale/invalid `GITHUB_TOKEN` env var cached in the current shell process even after it's fixed elsewhere (a sandboxing quirk, not a real auth problem). If `gh` commands here fail with "Bad credentials" while `gh auth status` in a fresh terminal looks fine, prefix commands with `env -u GITHUB_TOKEN` rather than troubleshooting auth further.

## Step 3: Build issue content per epic

- **Title:** epic name, no prefix
- **Body:**
  ```markdown
  **Outcome:** <epic outcome>

  Part of stories doc: [`docs/product-superpowers/stories/<file>.md`](https://github.com/<owner>/<repo>/blob/<default-branch>/docs/product-superpowers/stories/<file>.md)
  ```
- **Type:** the mapped Epic type from Step 2
- Link target: build the blob URL from the `owner`/`repo` resolved in Step 1 and the repo's default branch (from the origin remote's default, typically `master`/`main`) — not a placeholder.
- Do not list story titles in the body — sub-issue links (added in Step 4) are the single source of truth for which stories belong to the epic; a hand-written checklist drifts out of sync with it.

## Step 4: Build issue content per story

- **Title:** story title
- **Body:**
  ```markdown
  **As a** <persona>
  **I want to** <goal>
  **So that** <benefit>

  **Priority:** <Must/Should/Could Have>
  **Estimate:** <points/T-shirt size>
  **Dependencies:** <list or None>

  ### Acceptance Criteria
  <Gherkin scenarios, verbatim from the stories doc>

  ### Edge Cases
  <list, verbatim>

  ### Definition of Ready
  <checklist, verbatim>
  ```
- **Type:** the mapped Story type from Step 2
- After creation, link it under its epic with `mcp__github__sub_issue_write` (add_sub_issue)

## Step 5: Per-issue confirmation

Before **every single issue creation** (epic or story), show the user the exact title, type, and body you're about to submit, and wait for explicit confirmation. Support:
- **yes** → create as shown
- **edit** → let the user redirect specific fields, then re-show before creating
- **skip** → move to the next item without creating anything

Do not batch multiple issues into one confirmation.

## Step 5b: Sync Priority/Size Project fields (stories only)

Skip this step entirely if Step 3b ended with no Project to sync to, or if the current issue is an epic (epics have no `Priority`/`Estimate` in their body).

1. The "Auto-add to project" workflow adds the new issue to the Project automatically — poll `gh project item-list <number> --owner <org> --format json` for the item matching the issue number (retry briefly; the automation can take a few seconds).
2. Parse the story body's `**Priority:** <value>` and `**Estimate:** <value>` lines. Take just the leading matching phrase (e.g. `Should Have` out of `Should Have (below MVP line — ...)`); ignore trailing parenthetical notes.
3. Look up each value in the cached option maps from Step 3b. If a value doesn't match any existing option, tell the user and skip that one field rather than guessing or creating a new option.
4. `gh project item-edit --id <item-id> --field-id <field-id> --project-id <project-id> --single-select-option-id <option-id>` for each of `Priority` and `Size`.
5. Note any field that couldn't be synced (missing item, unmatched value) so it can be called out in the Step 7 summary.

## Step 6: Record the mapping back into the stories doc

Append to the bottom of the source stories doc:

```markdown
## GitHub Issues

- Epic: [<epic name>](<issue URL>)
  - Story: [<story title>](<issue URL>)
  - Story: [<story title>](<issue URL>)
```

Skip entries the user chose to skip.

## Step 7: Summarize

Report counts: epics created / skipped, stories created / skipped, any type-mapping fallback decisions made, whether a new Project was created, and any Project field syncs that were skipped or failed (with the reason).
