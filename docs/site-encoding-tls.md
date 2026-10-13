# FTP site encoding and TLS compatibility

Site Options > Connection behavior contains:

- FTP character encoding: Auto detects advertised UTF8, otherwise Windows-1252; Force UTF-8 sends OPTS UTF8 ON and uses UTF-8 even if FEAT omits it or OPTS is rejected; Windows-1252 keeps legacy ANSI path encoding. Control commands, STAT replies and data listings use the selected encoding.
- UTF-8 username and password: independently encodes the login phase. Default off.
- Explicit FTPS command: Auto tries AUTH TLS then AUTH SSL if rejected; forced AUTH TLS or AUTH SSL uses that command only. AUTH SSL still negotiates TLS.
- Send PBSZ 0 after TLS handshake, before login: optional compatibility ordering. Default remains after login, needed by servers that require an authenticated user. PROT remains after login.
- TLS 1.2 only: applies to both Windows TLS and bundled OpenSSL control/client data handshakes. Otherwise TLS 1.2/1.3 is allowed.
- Use bundled OpenSSL directly: bypasses the Windows TLS backend. Default remains Windows TLS with automatic compatibility fallback.

These saved site options also apply to transfer workers and RACE listing connections. Reconnect existing browsing connections after changing them; restart FluxFTP before a controlled RACE comparison so pooled connections are recreated. Direct FXP data encryption is negotiated between the two servers, so the client TLS version setting cannot impose a cipher or TLS version on their direct data link.

The Rush screenshots for ARES show AUTH SSL and secure listing/transfer/FXP, with Forced UTF-8 and UTF-8 Username unchecked. Start with Auto character encoding, UTF-8 login off, AUTH SSL, secure listings/transfers, Auto secure FXP and the checked early PBSZ option if that ordering is needed. Compare protocol/TLS logs to identify the actual negotiated backend, protocol and cipher.

References: https://www.rfc-editor.org/rfc/rfc4217 and https://www.rfc-editor.org/rfc/rfc2640
