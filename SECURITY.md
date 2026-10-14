# Security policy

Barline runs with administrator rights to read hardware sensors and frame timing, so security reports are taken seriously.

## Reporting a vulnerability

Please **do not open a public issue** for a vulnerability. Use GitHub's private reporting instead: **Security → Report a vulnerability** on this repository. Include the Barline version, your Windows version and the steps to reproduce.

You can expect an acknowledgement within a week. Fixes are released as a new version, and the advisory is published after the fix is available.

## Supported versions

Only the latest release receives security fixes.

## Design notes

The measures that keep the elevated monitor from loading or writing anything an unelevated process could change are described in [docs/how-it-works.md](docs/how-it-works.md#security-notes).
