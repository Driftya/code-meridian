Security Policy

Deployment Scope

CodeMeridian is designed for local developer use.

The recommended deployment model is:

* run the MCP server on localhost
* run Neo4j locally through the provided development stack
* connect from local tools such as VS Code, Copilot, Codex, or other MCP clients

CodeMeridian should not be exposed directly to the public internet.

A local network deployment is acceptable only in a trusted environment, such as a private home network, office network, or VPN-restricted developer network.

If CodeMeridian is hosted on a local network, users should:

* require the configured CodeMeridian API key
* restrict access with firewall rules
* avoid exposing Neo4j Browser publicly
* avoid exposing Neo4j Bolt publicly
* avoid forwarding CodeMeridian ports through a router
* avoid indexing secrets, tokens, credentials, or private environment files
* treat indexed graph data as sensitive project data

Public internet hosting is not a supported deployment mode unless explicit hardening documentation is added in the future.

Supported Versions

Only the latest released version of CodeMeridian is currently supported with security fixes.

Version	Supported
Latest release	Yes
Older releases	No
Unreleased local changes	No official support

Reporting a Vulnerability

Please do not report security vulnerabilities in public GitHub issues.

Use GitHub private vulnerability reporting if it is available for this repository. If private reporting is not available, contact the maintainer privately.

When reporting a vulnerability, include:

* affected version, tag, or commit
* operating system and runtime environment
* deployment mode, for example localhost, trusted LAN, or container stack
* steps to reproduce
* expected behavior
* actual behavior
* impact assessment
* relevant logs, if safe to share

Do not include:

* API keys
* access tokens
* credentials
* private source code
* private repository contents
* secrets from .env, appsettings, CI/CD, or shell history
* full indexed graph exports from private repositories

If logs are needed, redact sensitive values before sharing them.

Security Scope

Security reports may include issues related to:

* unintended exposure of indexed source code structure
* unsafe MCP server access
* API key validation problems
* Neo4j access exposure
* container or Docker Compose configuration risks
* path traversal or unsafe file access
* secrets being logged unintentionally
* secrets being indexed unintentionally
* unsafe default network binding
* unsafe authentication or authorization behavior
* dependency vulnerabilities with practical impact
* cross-project data leakage in the graph
* access to another project context without permission

Out of Scope

The following are generally out of scope unless they demonstrate a realistic security impact:

* reports against public internet hosting without acknowledging that public hosting is unsupported
* denial-of-service reports against a local-only development instance without practical impact
* issues requiring physical access to the developer machine
* issues requiring full control of the host machine
* social engineering
* phishing
* spam
* missing security headers on local-only development endpoints
* reports without reproduction steps
* reports generated only by automated scanners without explanation or impact
* vulnerabilities in third-party services not controlled by CodeMeridian

Sensitive Data Handling

CodeMeridian may index metadata about a repository, including:

* file paths
* class, method, interface, enum, and property names
* call relationships
* diagnostics
* documentation
* configuration keys
* API endpoints
* database table names
* message topics
* external service references

Treat this indexed graph as sensitive project data.

Do not intentionally index:

* secrets
* API keys
* passwords
* private keys
* access tokens
* refresh tokens
* connection strings with credentials
* production .env files
* private certificates
* personal data
* customer data

If sensitive data is accidentally indexed:

1. stop the CodeMeridian server
2. remove the sensitive source from the indexed repository or exclusion rules
3. clear the affected project graph
4. re-index the project
5. rotate any exposed secrets if they may have been accessible

Recommended Local Hardening

For local use:

* bind services to localhost whenever possible
* keep the CodeMeridian API key private
* do not commit .env files containing secrets
* do not share Neo4j volumes from private repositories
* do not publish indexed graph backups
* keep Docker, .NET, Node.js, and dependencies updated
* run CodeMeridian only for repositories you trust

For trusted local network use:

* restrict access to trusted devices only
* use firewall rules to limit inbound access
* keep the MCP/API port private to the trusted network
* do not expose Neo4j Browser or Neo4j Bolt outside the trusted network
* avoid router port forwarding
* prefer VPN access over direct LAN exposure when possible
* rotate the CodeMeridian API key if access is shared too broadly

Authentication Notes

CodeMeridian should be treated as a developer tool, not a public multi-tenant service.

The API key is intended to reduce accidental access in local or trusted-network deployments. It is not a complete replacement for network isolation, firewall rules, or a proper production security model.

Do not rely on the API key alone for public internet exposure.

Responsible Disclosure

Please give maintainers a reasonable opportunity to investigate and fix reported security issues before public disclosure.

A good report includes enough detail to reproduce the issue without exposing private data.

Maintainers may ask for clarification, a reduced reproduction, or confirmation of the affected version.

Security Fix Process

When a valid security issue is confirmed, maintainers should aim to:

1. assess impact and affected versions
2. prepare a fix
3. add or update regression tests where practical
4. release a patched version
5. document any required mitigation steps
6. credit the reporter if they want to be credited

Final Note

CodeMeridian exists to help local AI coding agents understand a repository.

That means the graph can contain useful but sensitive project knowledge. Keep it local, keep it guarded, and do not put the lantern in the public window unless the project has explicit hardening support for that deployment model.