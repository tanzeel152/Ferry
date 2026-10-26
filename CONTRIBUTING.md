# Contributing to Ferry

Thanks for helping. Bug reports, ideas, translations of error messages into plain words, and code are all welcome.

## Reporting bugs and asking for features

Use the [Issues page](https://github.com/tanzeel152/Ferry/issues/new/choose) and pick **Bug report** or **Feature request**. Search first; a 👍 on an existing issue helps us see what matters most.

Security problems should not go in a public issue. See [SECURITY.md](SECURITY.md).

## Making a change

1. Fork the repository and clone your fork.
2. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Visual Studio 2022, Rider or VS Code with the C# Dev Kit all work.
3. Create a branch: `git checkout -b fix-resume-after-redirect`.
4. Build and run:

   ```powershell
   dotnet build
   dotnet run --project Ferry
   ```

   Ferry allows only one running copy. Exit the installed Ferry first (right-click the tray icon > **Exit**), or your build will just bring the installed one's window up.

   Your build uses the same settings and download list as an installed Ferry (`%APPDATA%\Ferry`).

5. To try the browser extension, load the `browser-extension` folder from `chrome://extensions` with **Load unpacked**. After changing it, click the reload button on its card.
6. Commit, push to your fork and open a pull request against `main`. Describe what you changed and how you tested it.

For anything bigger than a bug fix, please open an issue first so we can agree on the approach before you spend time on it.

## Code style

- Match the code around your change: C# with nullable reference types on, file-scoped namespaces, and short `///` comments that say *why*, not *what*.
- The UI is drawn by Ferry itself (`Theme.cs`, `Icons.cs`, `FlatRenderer.cs`). Use the theme's colors so light and dark mode both work, and check both.
- Keep download logic in `Ferry.Core` and window code in `Ferry`. `Ferry.Core` has no Windows Forms dependency.
- User-facing text is short, plain English, and says what to do next ("Press Resume to try again").
- The pull request build (`.github/workflows/build.yml`) must pass. It fails on compiler warnings.

## Things to check by hand

There are no automated tests yet (adding some to `Ferry.Core` is a welcome contribution). Before opening a pull request, try what your change touches:

- a large file download with several connections, paused and resumed, and resumed after restarting Ferry
- a download sent from the browser extension
- a YouTube video download, if you changed anything video-related
- light and dark theme, if you changed the UI

## Making a release (maintainers)

1. Set the new version in `Ferry\Ferry.csproj` (`<Version>`) and `browser-extension\manifest.json` (`"version"`), and in the user agent in `Ferry.Core\Tools.cs`.
2. Add a section for it at the top of `CHANGELOG.md`, headed `## <version>`.
3. Commit, then tag and push:

   ```powershell
   git tag v0.5.0
   git push origin main v0.5.0
   ```

The [release workflow](.github/workflows/release.yml) builds the installer, the portable zip and the extension zip on GitHub's servers, writes `SHA256SUMS.txt`, and publishes them as a GitHub release with that version's notes from `CHANGELOG.md`.

## License

By contributing, you agree that your contributions are licensed under the [GNU GPL v3 or later](LICENSE), like the rest of Ferry.
