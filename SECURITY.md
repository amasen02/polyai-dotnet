# Security Policy

## Supported versions

| Version | Supported |
| ------- | --------- |
| 1.x     | ✅ Yes    |

## Reporting a vulnerability

**Do not open a public issue for security vulnerabilities.**

Email [amasen02@gmail.com](mailto:amasen02@gmail.com) with the subject line `[SECURITY] polyai-dotnet`. Include:

- A description of the vulnerability and its potential impact.
- Steps to reproduce or proof-of-concept code.
- Any suggested mitigation.

You will receive an acknowledgement within 48 hours and a resolution timeline within 7 days.

## Scope

This policy covers the `PolyAI.DotNet` NuGet package. Security issues in upstream provider APIs (OpenAI, Anthropic, etc.) should be reported directly to those vendors.

## Note on API keys

PolyAI.DotNet accepts API keys as strings and passes them in request headers. Keys are never logged or stored by the library. Use environment variables or a secrets manager (Azure Key Vault, AWS Secrets Manager, etc.) — never hard-code keys in source code.
