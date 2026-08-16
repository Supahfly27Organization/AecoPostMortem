---
name: github-issue-commit
description: Use right before making the closing commit for work that implements a tracked GitHub issue. Appends "(#N)" to the commit message and moves the issue's Project Status to "Waiting to review".
---

# GitHub Issue Commit

When Superpowers finishes implementing a tracked GitHub issue, the commit that closes out the work must reference the issue (so it's clickable/searchable on GitHub) and the issue's board status must move to "Waiting to review" (work is done, awaiting human review — not yet "Done").

**Announce at start:** "I'm using the github-issue-commit skill to link this commit to #N and move it to Waiting to review."

<HARD-GATE>
Only run this if you know the specific issue number the commit closes out. If the work isn't tied to a tracked issue, skip this skill entirely — do not guess an issue number.
</HARD-GATE>

## Checklist

1. Resolve `owner`/`repo` from `git remote get-url origin`
2. Format the commit message with a trailing `(#N)` reference
3. Move the issue's Project `Status` field to `Waiting to review`
4. Confirm both steps succeeded before reporting the commit as done

## Step 1: Resolve owner/repo

```
git remote get-url origin
```

Parse `owner/repo` from the URL.

## Step 2: Commit message format

Append `(#N)` to the end of the commit's summary line, matching the existing convention in this repo's history:

```
<type>: <summary> (#N)
```

Do **not** use closing keywords (`Fixes #N`, `Closes #N`) — those auto-close the issue on merge, which is wrong here since the work is only moving to review, not done. A plain `(#N)` still makes GitHub auto-link the commit on the issue's timeline and makes it searchable via `repo:owner/repo #N`.

## Step 3: Move Status to "Waiting to review"

**Auth note:** this org's `gh` auth may have a stale/invalid `GITHUB_TOKEN` env var cached in the current shell process even after it's fixed elsewhere. If `gh` commands fail with "Bad credentials", prefix with `env -u GITHUB_TOKEN`.

1. Find the Project item for issue `N`: `gh project item-list <project-number> --owner <org> --format json`, match on the issue's URL/number. If the issue isn't on any Project board, tell the user and skip the status update (still make the commit).
2. Look up the `Status` field's option id for `Waiting to review` (cache these ids per run if handling multiple issues — don't re-query per issue):
   ```
   gh api graphql -f query='
   query {
     node(id: "<status-field-id>") {
       ... on ProjectV2SingleSelectField { options { id name } }
     }
   }'
   ```
3. Set it:
   ```
   gh project item-edit --id <item-id> --field-id <status-field-id> --project-id <project-id> --single-select-option-id <waiting-to-review-option-id>
   ```
4. If the option lookup or edit fails, tell the user what failed rather than silently skipping.

## Step 4: Confirm

Report the commit hash/message and whether the Status update succeeded. If either step failed, say so explicitly rather than reporting the commit as fully done.
