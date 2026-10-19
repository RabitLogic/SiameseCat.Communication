# Publishing

The package is `SiameseCat.Communication`. This page covers the three ways to publish it — from a tag in
CI, by hand from a workstation, or to a private feed — plus the pre-release checks and how to undo a
mistake.

## 1. Prerequisites

1. A NuGet.org account and an API key from <https://www.nuget.org/account/apikeys> with
   **Push new packages and package versions** for the `SiameseCat.Communication` package (or `*` while
   the package does not exist yet). Copy the key once — nuget.org never shows it again.
2. Store the key as the repository secret **`NUGET_API_KEY`** — one of:

   ```powershell
   # a) helper script: prompts without echoing, pipes straight into gh, verifies the name
   gh auth login                       # once per machine; needs admin on the repository
   ./scripts/set-nuget-secret.ps1 -Verify

   # b) GitHub CLI directly
   'oy2...' | gh secret set NUGET_API_KEY --repo RabitLogic/SiameseCat

   # c) web UI
   #    Settings → Secrets and variables → Actions → New repository secret
   #    Name: NUGET_API_KEY   Secret: <the key>
   ```

   The value is never printed by the script, is not stored in shell history when you let it prompt, and
   the workflow only reads it through `${{ secrets.NUGET_API_KEY }}`.
3. Confirm the metadata that ends up in the `.nuspec` — id `SiameseCat.Communication`, version, authors,
   MIT license, repository URL, tags, the bundled `README.md` and the three assets
   `lib/net8.0`, `lib/net10.0`, `lib/netstandard2.0`.

## 2. Pre-release checks (local)

```powershell
# Build, test, format, pack, layout check, SHA256 — fails on any problem
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
dotnet add package SiameseCat.Communication --version 1.0.0
Pop-Location
```

`pack.ps1` refuses to finish if an asset or `README.md` is missing, and prints the SHA256 of every package.

## 3. Publish from a tag (recommended)

[.github/workflows/release.yml](../.github/workflows/release.yml) runs when a `v*` tag is pushed:

1. restore, Release build, all 352 tests, framework matrix on .NET 8 and .NET 10;
2. pack with the version taken from the tag (a tag `v1.0.0` produces `1.0.0`);
3. verify the three assets and `README.md` are inside the `.nupkg`;
4. push `.nupkg` + `.snupkg` to nuget.org with `--skip-duplicate`;
5. create the GitHub release and attach both files.

```powershell
# 1. Update CHANGELOG.md, commit and push the code
git add -A
git commit -m "release: 1.0.0"
git push origin main

# 2. Tag and push — this starts the publish
git tag v1.0.0
git push origin v1.0.0
```

Watch the run under **Actions → Release**. The package becomes visible on nuget.org a few minutes after
the push (indexing delay), and the symbol package enables step-into debugging from SourceLink.

You can also start the workflow manually (**Actions → Release → Run workflow**) and type a version; it
publishes without creating a GitHub release.

## 4. Publish by hand

```powershell
$env:NUGET_API_KEY = 'oy2...'                      # from nuget.org

# Package only (no push)
./scripts/pack.ps1

# Package and push to nuget.org
./scripts/pack.ps1 -Push https://api.nuget.org/v3/index.json

# Pre-release or a version bump
./scripts/pack.ps1 -Version 1.0.1-preview.1 -Push https://api.nuget.org/v3/index.json

# Equivalent raw commands, if you prefer dotnet directly
dotnet pack src/SiameseCat.Communication/SiameseCat.Communication.csproj -c Release -o artifacts
dotnet nuget push artifacts/SiameseCat.Communication.1.0.0.nupkg `
  --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY --skip-duplicate
dotnet nuget push artifacts/SiameseCat.Communication.1.0.0.snupkg `
  --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY --skip-duplicate
```

`--skip-duplicate` makes re-runs harmless: an already published version is reported and skipped instead of
failing the pipeline.

## 5. Publish to a private feed

| Feed | Command |
| --- | --- |
| Folder (file share, USB, artifact drop) | `./scripts/pack.ps1 -Push \\server\feed` |
| Azure Artifacts | `./scripts/pack.ps1 -Push https://pkgs.dev.azure.com/<org>/_packaging/<feed>/nuget/v3/index.json` (key `az` or a PAT in `NUGET_API_KEY`) |
| Nexus / Artifactory | `./scripts/pack.ps1 -Push https://nexus.example/repository/nuget-hosted/` |
| Local verification | `./scripts/pack.ps1 -Push "$env:TEMP\siamesecat-feed"` |

Folder feeds store only `.nupkg` files (symbols need a package server); the script reports that and keeps
the `.snupkg` in the output folder.

## 6. Consumers then use

```bash
dotnet add package SiameseCat.Communication              # latest stable
dotnet add package SiameseCat.Communication --prerelease # previews
```

Asset selection is automatic: .NET 8 → `lib/net8.0`, .NET 10 → `lib/net10.0`, .NET 9 → `lib/net8.0`,
.NET 11+ → `lib/net10.0`, .NET Framework → `lib/netstandard2.0`.

## 7. If something goes wrong

| Situation | Action |
| --- | --- |
| Wrong package pushed, version not yet consumed | On nuget.org open the package → **Unlist** (it stays restorable by exact version) and publish a corrected patch version |
| Bad version already referenced | Publish a patch release; nuget.org versions are immutable |
| Need to withdraw a version entirely | Use **Deprecate** with the replacement version — existing projects keep working, new restores warn |
| Symbol push failed | Re-run the workflow with `--skip-duplicate`; the `.nupkg` push is skipped, the `.snupkg` is retried |
| API key expired | Rotate the key on nuget.org and update the `NUGET_API_KEY` secret (`./scripts/set-nuget-secret.ps1`); no code change |

## 8. Release checklist

- [ ] `CHANGELOG.md` has the version section and the date.
- [ ] `dotnet build` and `dotnet test` are clean (0 warnings, 352 tests).
- [ ] `./scripts/pack.ps1` passes the layout check and prints the SHA256.
- [ ] The package was restored from a folder feed in a scratch project.
- [ ] `gh secret list --repo RabitLogic/SiameseCat` shows `NUGET_API_KEY`.
- [ ] `git tag v<version>` matches the version you intend to publish.
