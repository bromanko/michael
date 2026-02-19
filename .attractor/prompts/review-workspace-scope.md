Review scope for this stage is the full workspace change set, not just uncommitted working-copy changes.

Use these artifacts (generated each review pass):
- `.attractor/review/commits.txt`
- `.attractor/review/workspace.summary`
- `.attractor/review/workspace.diff`

Rules:
1. Treat the authoritative review scope as the cumulative delta from `workspace.base_commit` to current workspace tip (`@`).
2. Do **not** conclude "no changes" only because `jj diff` for the working copy is empty.
3. Read `workspace.summary` and `workspace.diff` (page through with offsets if needed), then inspect the relevant source files.
4. If your language/domain truly has no touched files in the summary, state that explicitly and return success.
