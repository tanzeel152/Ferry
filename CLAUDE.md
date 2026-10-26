# Ferry

Ferry is a Windows download manager in C# / .NET 8 (Windows Forms), with a Chrome/Edge extension in `browser-extension/`. README.md describes the features and project layout; CONTRIBUTING.md covers building, releasing and code signing.

## Commits and pull requests

- Do not add `Co-Authored-By: Claude` lines, or any other Claude or AI attribution, to commit messages or pull request descriptions.
- Commit as `Tanzeel Ahmed <20097820+tanzeel152@users.noreply.github.com>` (set in this repository's git config). Never commit with a personal email address.

## Building and releasing

- CI runs `dotnet build Ferry.sln -c Release -warnaserror`; keep it free of warnings.
- A release is made by pushing a `vX.Y.Z` tag. Before tagging, the version must match in `Ferry/Ferry.csproj`, `browser-extension/manifest.json` and the user agent in `Ferry.Core/Tools.cs`, and `CHANGELOG.md` needs a `## X.Y.Z` section (the release notes come from it).
