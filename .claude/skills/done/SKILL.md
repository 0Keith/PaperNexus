---
name: done
description: Create PR, review (architecture/security/performance/tests/code), merge, deploy, and clean up
disable-model-invocation: true
---

Complete the current feature branch: create a PR, review, merge, and clean up.

## Steps

1. **Verify branch state**: If on the default branch, create an appropriately named feature branch carrying over all committed/uncommitted changes.

2. **Commit uncommitted changes**: Run `git status`. Stage and commit any uncommitted changes with an appropriate message.

3. **Sync with default branch**: Fetch and merge the remote default branch. Resolve any conflicts.

4. **Build and test locally**: Auto-detect the build system from project files (`CLAUDE.md`, `package.json`, `Cargo.toml`, `Makefile`, etc.) and run build + test commands. Fix warnings in changed files and any test failures. Commit and re-run until clean.

5. **Review code comments**: Read all changed files (`git diff <default>...HEAD --name-only`). Ensure non-obvious logic has concise comments explaining *why*, not *what*. Add comments between logical blocks of code to describe intent — always with a blank line or opening brace before the comment for readability. Remove stale or misleading comments. Don't over-comment trivial code. Commit if changes were made.

6. **Check for existing PR**: `gh pr list --head <branch>`. Reuse if one exists (skip to step 8).

7. **Create PR**: `gh pr create` targeting the default branch. Title under 70 chars. Body with `## Summary` (bullet points) and `## Test plan`. Derive from `git diff <default>...HEAD` and `git log <default>..HEAD`.

8. **Push the branch**: `git push -u origin <branch>` if not already pushed.

9. **Auto-label PR**: Add labels via `gh pr edit --add-label` based on changed files:
   - `ui` — view/template files (`.axaml`, `.tsx`, `.vue`, `.html`, `.css`)
   - `api` — backend/route files
   - `infra` — CI/CD, config, build files (`.github/`, `.claude/`, `Dockerfile`)
   - `docs` — documentation (`README.md`, `CLAUDE.md`, `CHANGELOG.md`)
   - `tests` — test files
   Create missing labels with `gh label create`. Skip if nothing matches.

10. **Parallel reviews**: Launch **all five** review agents simultaneously using the Agent tool. Each agent receives the PR diff (`gh pr diff`) and `CLAUDE.md` as context. Each agent is **read-only** — it reports findings but does not edit files. Use `subagent_type: "general-purpose"` for each.

   Launch these agents in a **single message** with five parallel Agent tool calls:

   - **Architecture review agent**: "Review this PR diff for architecture issues: violations of patterns in `CLAUDE.md`; wrong dependency direction; circular refs; unnecessary coupling; concurrency/thread-safety issues; SRP violations; inconsistent public API conventions. Return a list of findings with file paths and line numbers, or state 'No architecture issues found.'"

   - **Security review agent**: "Review this PR diff for security issues: injection (command, path traversal, SQL); hardcoded secrets; unsafe deserialization; HTTP where HTTPS expected; overly broad permissions; unsafe FFI/interop. Also run the appropriate vulnerability scanner (`dotnet list package --vulnerable`, `npm audit`, `cargo audit`, etc.). Return a list of findings with file paths and line numbers, or state 'No security issues found.'"

   - **Performance review agent**: "Review this PR diff for performance issues: unnecessary allocations/copies; N+1 patterns; redundant I/O; blocking async calls; expensive ops in hot loops; missing caching; algorithmic inefficiency (O(n²) where O(n log n) is possible). Return a list of findings with file paths and line numbers, or state 'No performance issues found.'"

   - **Test coverage review agent**: "Review this PR diff for test coverage gaps: new public APIs without tests; uncovered edge cases; existing tests that no longer match modified behavior. Return a list of missing tests with file paths, or state 'No test coverage issues found.'"

   - **Code quality review agent**: "Review this PR diff for code quality issues: bugs; style violations; missing error handling; dead code; debug leftovers; TODOs; naming issues. Return a list of findings with file paths and line numbers, or state 'No code quality issues found.'"

11. **Post review comments**: Collect results from all five agents. For each agent that found issues, post a single batched PR review comment grouping findings by category. Note which reviews were clean.

12. **Fix all review issues**: Address findings from all agents. Write missing tests. Commit and push fixes. Re-run build and tests until clean.

13. **Parallel doc syncs**: Launch **three** agents simultaneously in a **single message**. Each agent reads the PR diff and updates its target file if needed, then commits and pushes. Use `subagent_type: "general-purpose"` for each.

    - **CLAUDE.md sync agent**: "Check if project structure, patterns, or architectural decisions changed in this PR. If so, update `CLAUDE.md` accordingly. Commit and push if changes were made. Skip if nothing changed."

    - **README.md sync agent**: "Check if user-facing behavior changed in this PR. If so, update `README.md` to reflect the changes — match the existing tone (keep it fun and witty). Commit and push if changes were made. Skip if no README exists or nothing changed."

14. **Parallel maintenance**: Launch **two** agents simultaneously in a **single message**. Use `subagent_type: "general-purpose"` for each.

    - **Skill sync agent**: "Reflect on this `/done` run. If any step was ambiguous, wrong, missing, or could be improved, update `.claude/skills/done/SKILL.md`. Examples: a step that always gets skipped, a missing edge case, a better command, or a reordering that would save time. Commit and push if changes were made."

    - **Compact docs agent**: "Check character counts of all `.md` files loaded into context (`CLAUDE.md`, skill files, memory files). Each file must stay under **3% of the token context window**. If any file exceeds this, compact it — remove redundancy, tighten wording, and consolidate without losing meaning. Commit and push if changes were made."

15. **Update PR description**: `gh pr edit --body` to reflect final state of all changes. Keep `## Summary` and `## Test plan` format.

16. **Wait for CI**: Poll `gh pr checks`. If failures, fix, commit, push, and re-poll.

17. **Merge**: `gh pr merge --squash --delete-branch`. Fallback to `--merge`, then `--rebase`.

18. **Clean up locally**: Switch to default branch, pull, delete feature branch. Prune remotes (`git fetch --prune`) and delete local branches merged to default. Report cleaned branches.

19. **Verify deploy**: Check for CI/CD on default branch (`gh run list --branch <default> --limit 1`). Watch with `gh run watch`. On failure: read logs, create fix branch, restart from step 1. Skip if no deploy workflow.

20. **Report**: Summarize what merged, review fixes applied, and the PR URL.

21. **Celebrate**: Draw a funny ASCII art of a pokemon saying random phrase like "good job" or "job's done".

## Notes

- User arguments = PR title: $ARGUMENTS
- Merge conflicts: resolve, commit, push, re-check CI.
- Never force-push or skip CI.
- Detect default branch via `gh repo view --json defaultBranchRef --jq '.defaultBranchRef.name'`.
