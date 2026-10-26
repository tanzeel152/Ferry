# Security policy

## Supported versions

Security fixes go into the latest release. Please update to it before reporting.

## Reporting a vulnerability

Please do not open a public issue for security problems.

Report them privately through GitHub: go to the [Security tab](https://github.com/tanzeel152/Ferry/security), click **Report a vulnerability**, and describe the problem, the Ferry version, and how to reproduce it. Only the maintainers can see the report.

You should get a first reply within a week. Once a fix is released, the report is published as a security advisory, crediting you unless you prefer otherwise.

## What is in scope

Examples of what we want to hear about:

- A website being able to make Ferry download or run something without the user's action. Ferry listens on `http://localhost:51515` for its browser extension and should reject requests from web pages.
- Ferry writing files outside the chosen download folder (for example through a crafted file name or `Content-Disposition` header).
- Cookies or other credentials being sent to a site they do not belong to.
- The installer or the video-tools download fetching or running files that could be replaced by an attacker.

Problems in yt-dlp, Python or FFmpeg themselves should be reported to those projects.
