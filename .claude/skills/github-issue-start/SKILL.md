---
name: github-issue-start
description: Use right before beginning implementation work on a tracked GitHub issue. Moves the issue's Project Status to "In progress".
---

# GitHub Issue Start

When Superpowers begins implementing a tracked GitHub issue, the board should reflect that work has started — before any code changes. This moves the issue to "In progress" regardless of its current status (typically "Ready", but this skill doesn't check — it just sets the new value).

**Announce at start:** "I'm using the github-issue-start skill to move #N to In progress."

<HARD-GATE>
Only run this if you know the specific issue number the upcoming work is for. If the work isn't tied to a tracked issue, skip this skill entirely — do not guess an issue number.
</HARD-GATE>

## Checklist

1. Resolve `owner`/`repo` from `git remote get-url origin`
2. Find the issue's Project item
3. Move its `Status` field to `In progress`
4. Confirm the update succeeded

## Step 1: Resolve owner/repo

```
git remote get-url origin
```

## Step 2: Find the Project item

**Auth note:** this org's `gh` auth may have a stale/invalid `GITHUB_TOKEN` env var cached in the current shell process even after it's fixed elsewhere. If `gh` commands fail with "Bad credentials", prefix with `env -u GITHUB_TOKEN`.

```
gh project item-list <project-number> --owner <org> --format json
```

Match on the issue's number/URL to get its item id. If the issue isn't on any Project board, tell the user and stop — nothing to update.

## Step 3: Move Status to "In progress"

Look up the `Status` field's option id for `In progress` (cache per run if handling multiple issues):

```
gh api graphql -f query='
query {
  node(id: "<status-field-id>") {
    ... on ProjectV2SingleSelectField { options { id name } }
  }
}'
```

Set it:

```
gh project item-edit --id <item-id> --field-id <status-field-id> --project-id <project-id> --single-select-option-id <in-progress-option-id>
```

## Step 4: Confirm

Report whether the Status update succeeded before proceeding to the actual implementation work. If it failed, tell the user what failed rather than silently continuing.
