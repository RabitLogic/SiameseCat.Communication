# Repository settings

Settings that live outside the code but decide how safe the repository is. The two rulesets below are
checked in so the policy is reviewable and can be re-imported on a fresh clone or a fork.

## Protect `main` and the release tags

| Ruleset | File | Effect |
| --- | --- | --- |
| `main protection` | [main-protection.json](../.github/rulesets/main-protection.json) | No force push, no deletion, required checks before a pull request can merge |
| `release tags` | [release-tag-protection.json](../.github/rulesets/release-tag-protection.json) | `v*` tags cannot be deleted or moved once published |

### Apply them

1. **Settings → Rules → Rulesets → New ruleset → Import a ruleset**
2. Choose `.github/rulesets/main-protection.json`, review the rules and create the ruleset.
3. Repeat with `release-tag-protection.json` — do this **after the first version is live**, because it
   makes published tags immutable.

The same rules can be created by hand: target the default branch, enable *Restrict deletions* and
*Block force pushes*, and add *Require status checks to pass* with the job names from
[ci.yml](../.github/workflows/ci.yml):

- `build`
- `Framework matrix`
- `dotnet-framework`
- `Soak (fault injection)`

### What this changes

- Direct pushes to `main` still work (there is no `pull_request` rule), so solo maintenance stays fast.
- A pull request cannot merge while one of the required jobs is red.
- A published `v*` tag can no longer be moved or deleted; fix a bad release with a new version, not by
  rewriting history.
- To also require pull requests for `main`, add a `pull_request` rule with `required_approving_review_count`
  set to 0. Direct pushes then stop working, including scripted ones.

If the UI asks for a plan upgrade, the current organization plan does not include rulesets for private
repositories. Either make the repository public or upgrade; until then, at minimum never force-push
`main` and never delete a published tag.

## Workflow permissions

The release workflow mirrors the package to GitHub Packages, which needs `packages: write`.
**Settings → Actions → General → Workflow permissions** must allow read and write (or the organization
policy must allow it), otherwise the mirror step fails with `403`.
