# Security Policy

## Supported Versions

Only the latest release is actively maintained.

## Reporting a Vulnerability

**Do not open a public GitHub issue for security vulnerabilities.**

Please report security issues via GitHub's private vulnerability reporting:
[Report a vulnerability](https://github.com/maxrenke/game-optimizer/security/advisories/new)

Or email **maxrenke@gmail.com** with:
- A description of the vulnerability
- Steps to reproduce
- Potential impact

You'll receive a response within 72 hours. If the report is confirmed, a patch
will be released as quickly as possible. Reporters are credited in the release
notes unless they prefer to remain anonymous.

## Scope

Gaming Optimizer runs with administrator privileges by design (affinity and
priority changes require it). The app makes no outbound network requests except
the configurable latency ping target (default: `1.1.1.1`). No telemetry,
analytics, or update-check traffic is transmitted.
