# Publishing

The package family is `SiameseCat.Communication.*` (Core, Tcp, Modbus, S7, Simulation.*, Hosting and the All aggregator). CI publishes every package with **NuGet Trusted Publishing** — no
long-lived API key is stored in the repository. This page covers the one-time nuget.org setup, the
tag-driven release, manual publishing from a workstation, private feeds and how to undo a mistake.

## How Trusted Publishing works

1. GitHub Actions issues a signed OIDC token for the repository, workflow file and ref.
2. The workflow sends it to nuget.org (`NuGet/login@v1`).
3. nuget.org checks it against the trusted publishing policy registered for this repository and returns a
   **temporary API key valid for one hour**.
4. The workflow pushes the packages with that key.

The key is short-lived, is never stored anywhere and cannot be leaked from the repository. One token
yields one key, so the login step sits right before the push.

## 1. One-time setup on nuget.org

1. Sign in to <https://www.nuget.org> with an account that can publish for the **RabitLogic** owner.
2. Open **Trusted Publishing** (<https://www.nuget.org/account/trustedpublishing>) and create a policy:

   | Field | Value |
   | --- | --- |
   | Policy name | `SiameseCat.Communication release` (any label) |
   | Package Owner | `RabitLogic` |
   | CI/CD Provider | `GitHub Actions` |
   | Repository Owner | `RabitLogic` |
   | Repository | `SiameseCat.Communication` — the **GitHub repository name** |
   | Workflow File | `release.yml` — the **file name only**, not `.github/workflows/release.yml` |
   | Environment | leave **empty** (the workflow does not use environments; fill it only if you add `environment:` to the job) |
   | Scopes | **Push new packages and package versions** (switch to *Push only new package versions* after the first release) |
   | Glob patterns | `SiameseCat.Communication.*` — the **package id family** (`*` also works) |

   The owner, repository and workflow file are matched case-insensitively against the run that requests
   the token. `Repository` is the **GitHub repository** the workflow runs in — the OIDC token carries
   that name, so it cannot be pointed at a repository you do not run from. The **package id** is a
   separate field: it belongs in `Glob patterns`. A mismatch (including a renamed repository or
   workflow file) is the most common cause of a failed push.

3. In the GitHub repository, add a **variable** (Settings → Secrets and variables → Actions → Variables):

   | Name | Value |
   | --- | --- |
   | `NUGET_USER` | your nuget.org **profile name** (username), not the email address |

   A secret named `NUGET_USER` is also accepted; nothing sensitive is involved, a variable is enough.

A policy created for a **private** repository starts as *temporarily active for 7 days*: nuget.org cannot
learn the repository and owner IDs until the first token exchange, so the first publish has to happen
inside that window. One successful publish makes the policy permanent; if the window lapses the policy
becomes inactive and **Activate for 7 days** restarts it.
No `NUGET_API_KEY` secret is needed. The workflow requests `id-token: write`, which is the only
permission nuget.org relies on.

## 2. Pre-release checks (local)

```powershell
dotnet build SiameseCat.slnx -c Release
dotnet test SiameseCat.slnx -c Release --no-build
dotnet format SiameseCat.slnx --verify-no-changes
./scripts/pack.ps1

# Prove the produced package really works from a feed
$feed = "$env:TEMP\siamesecat-feed"
./scripts/pack.ps1 -Output "$env:TEMP\pack" -Push $feed
dotnet new console -o "$env:TEMP\consumer" --force
Push-Location "$env:TEMP\consumer"
dotnet nuget add source $feed --name siamesecat-local
dotnet add package SiameseCat.Communication.All --version 0.10.0
Pop-Location
```

`pack.ps1` refuses to finish if an asset or `README.md` is missing, and prints the SHA256 of every package.

## 3. Publish from a tag (the normal path)

[.github/workflows/release.yml](../.github/workflows/release.yml) runs when a `v*` tag is pushed:

1. restore, Release build, all 352 tests, framework matrix on .NET 8 and .NET 10;
2. pack with the version taken from the tag (a tag `v0.9.0` produces `0.9.0`);
3. verify the three assets and `README.md` are inside the `.nupkg`;
4. `NuGet/login@v1` exchanges the OIDC token for a one-hour API key;
5. push `.nupkg` + `.snupkg` to nuget.org with `--skip-duplicate`;
6. create the GitHub release and attach both files.

```powershell
# 1. Update CHANGELOG.md, commit and push the code
git add -A
git commit -m "release: 0.9.0"
git push origin main

# 2. Tag and push — this starts the publish
git tag v0.9.0
git push origin v0.9.0
```

Watch **Actions → Release**. The package appears on nuget.org a few minutes after the push (indexing
delay); the symbol package enables step-into debugging through SourceLink.

A manual run (**Actions → Release → Run workflow**) can pass a version and publishes without creating a
GitHub release. It uses the same trusted publishing policy, because the policy matches the workflow file
rather than the trigger.

## 4. GitHub Packages mirror

Every tag also publishes the `.nupkg` to GitHub Packages (`https://nuget.pkg.github.com/RabitLogic`).
No extra secret is required: the job requests `packages: write` and authenticates with
`secrets.GITHUB_TOKEN`, so the mirror keeps working when NuGet.org or its Trusted Publishing policy is
unavailable.

Consumers add the feed once and then install from it:

```bash
dotnet nuget add source "https://nuget.pkg.github.com/RabitLogic/index.json" --name github \
  --username <github-user> --password <PAT with read:packages> --store-password-in-clear-text
dotnet add package SiameseCat.Communication.All --source github
```

A consuming GitHub Actions workflow can pass `secrets.GITHUB_TOKEN` with `packages: read` instead of a
PAT. Symbol packages are not mirrored: the `.snupkg` goes to NuGet.org only.

## 5. Publish by hand (workstation)

Trusted Publishing only works from CI, so a manual push still needs a normal API key from
<https://www.nuget.org/account/apikeys> with *Push new packages and package versions*:

```powershell
$env:NUGET_API_KEY = 'oy2...'

./scripts/pack.ps1                                                     # package only
./scripts/pack.ps1 -Push https://api.nuget.org/v3/index.json           # pack + push
./scripts/pack.ps1 -Version 1.0.1-preview.1 -Push https://api.nuget.org/v3/index.json

# The script packs and pushes every package in dependency order.
```

Keep that key in a password manager or your user profile — never in the repository, never in a workflow
file. `--skip-duplicate` makes re-runs harmless.

## 6. Publish to a private feed

| Feed | Command |
| --- | --- |
| Folder (file share, USB, artifact drop) | `./scripts/pack.ps1 -Push \\server\feed` |
| Azure Artifacts | `./scripts/pack.ps1 -Push https://pkgs.dev.azure.com/<org>/_packaging/<feed>/nuget/v3/index.json` |
| Nexus / Artifactory | `./scripts/pack.ps1 -Push https://nexus.example/repository/nuget-hosted/` |
| Local verification | `./scripts/pack.ps1 -Push "$env:TEMP\siamesecat-feed"` |

Folder feeds store only `.nupkg` files (symbols need a package server); the script reports that and keeps
the `.snupkg` in the output folder.

## 7. Consumers then use

```bash
dotnet add package SiameseCat.Communication.All              # everything (old single package)
dotnet add package SiameseCat.Communication.S7               # S7 only: Core + S7
dotnet add package SiameseCat.Communication.S7 --prerelease  # previews
```

Asset selection is automatic: .NET 8 → `lib/net8.0`, .NET 10 → `lib/net10.0`, .NET 9 → `lib/net8.0`,
.NET 11+ → `lib/net10.0`, .NET Framework → `lib/netstandard2.0`.

## 8. If something goes wrong

| Symptom | Cause and fix |
| --- | --- |
| `Check NUGET_USER` fails | The repository variable `NUGET_USER` is missing; add it under Settings → Secrets and variables → Actions → Variables with your nuget.org profile name. |
| `Push to GitHub Packages` fails with 403/422 | The job needs `packages: write`, and the package's `repository` URL must point at this repository (`RepositoryUrl` in `Directory.Build.props`). |
| `NuGet/login` returns no key, or the push gets 403 | Policy mismatch: check owner `RabitLogic`, repository `SiameseCat.Communication`, workflow file `release.yml`, scope and glob pattern. Also confirm the job has `id-token: write`. |
| Workflow ran but the policy did not match | Renaming the workflow file or moving the job under a different `environment:` breaks the match; update the policy accordingly. |
| Pushed a wrong version that nobody consumes yet | On nuget.org open the package → **Unlist**; it stays restorable by exact version. Publish a corrected patch. |
| Bad version already referenced | nuget.org versions are immutable — publish a patch release. |
| Need to withdraw a version entirely | Use **Deprecate** with the replacement version; existing projects keep restoring, new restores warn. |
| Symbol push failed | Re-run the workflow: the `.nupkg` push is skipped (`--skip-duplicate`) and the `.snupkg` is retried with a fresh key. |
| Local push rejected | The key is missing, expired or lacks the *Push new packages* scope; trusted publishing never applies outside CI. |

When a tag run fails before the push, nothing was published. Fix the cause and either

- re-run the failed jobs of the same run (**Actions → Release → Re-run failed jobs**), or
- run **Release → Run workflow** on `main` with version `0.9.0`; this uses the latest workflow
  definition but skips the GitHub release step, which you can add afterwards with
  `gh release create v0.9.0 --notes-file CHANGELOG.md`.

Both paths are safe to repeat: the push uses `--skip-duplicate`, so an already published version is
skipped instead of failing.

## 9. Repository name and package id

The GitHub repository is `RabitLogic/SiameseCat.Communication` and the package ids form the
`SiameseCat.Communication.*` family — the repository and the package ids are separate values with
different meanings, and the policy only matches on the repository the workflow runs in.

If the repository is renamed again, the policy must be recreated with the new name and these places
followed:

| Place | What to update |
| --- | --- |
| nuget.org policy | `Repository`, then re-save the policy |
| `Directory.Build.props`, `src/Directory.Build.props` | `RepositoryUrl` and package versions (shown on every nuget.org page) |
| `README.md`, `README.zh-CN.md`, `CHANGELOG.md`, `SECURITY.md`, `docs/*.md` | absolute `github.com/...` links, which are what the packaged README resolves when rendered on nuget.org |

Renaming the repository does not affect consumers: the package id is independent of it, and GitHub
redirects the old URLs. Updating the absolute links anyway keeps the nuget.org page independent of that
redirect.

## 10. Release checklist


- [ ] `CHANGELOG.md` has the version section and the date.
- [ ] `dotnet build` and `dotnet test` are clean (0 warnings, 352 tests).
- [ ] `./scripts/pack.ps1` passes the layout check and prints the SHA256.
- [ ] The packages were restored from a folder feed in a scratch project.
- [ ] The Trusted Publishing policy exists for `RabitLogic/SiameseCat.Communication` + `release.yml` and the
      `NUGET_USER` variable is set.
- [ ] `git tag v<version>` matches the version you intend to publish.
