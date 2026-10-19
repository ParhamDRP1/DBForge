# Security

Do not post credentials, personal records, production datasets or unredacted connection
details in a public issue. For a vulnerability, use GitHub's **Report a vulnerability**
entry under the repository Security tab when available, or contact the maintainer
privately through their GitHub profile before disclosing sensitive details.

Passwords use environment variables, runtime prompts or Windows DPAPI-encrypted Manual values. Project JSON may contain ciphertext. Runtime Remember encrypts the execution profile and credentials for the current Windows user. Reports can contain table names, dataset names and utility output; review them
before sharing. The Microsoft provider and its licensing remain external requirements.

TLS certificate checks are enabled for explicit FTPS. Plain FTP provides no transport
encryption; choose a transport policy appropriate to your network.

Version 1.2 is the current maintenance line. Application and installer binaries are not
code-signed; verify their release SHA-256 checksums and source/build manifest.
