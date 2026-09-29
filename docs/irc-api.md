# Connect IRC through the HTTPS API

FluxFTP must already be running with its HTTPS API enabled. Use the same Basic
authentication and API password as the other API endpoints. No FTP admin login
is needed to configure IRC through this authenticated API.

`POST https://localhost:55477/irc/connect` with `Content-Type: application/json`:

```json
{
  "host": "znc.example.org",
  "port": 29025,
  "useTls": true,
  "allowInvalidCertificate": true,
  "useZnc": true,
  "zncUsername": "exampleuser",
  "zncNetwork": "ExampleNET",
  "password": "YOUR_ZNC_PASSWORD",
  "nick": "FLUXFTP",
  "channel": "#FLUX",
  "networkName": "FLUXFTP"
}
```

This is equivalent to connecting to `znc.example.org:+29025` with ZNC PASS
`exampleuser/ExampleNET:YOUR_ZNC_PASSWORD`. Supply only the password in the password field;
FluxFTP creates the ZNC prefix. This endpoint replaces/reconnects the primary IRC
connection; it does not add another network. Use the actual ZNC network name.

Save the body as `irc-connect.json`, then run:

```powershell
curl.exe -k --user "api:YOUR_FLUXFTP_API_PASSWORD" -H "Content-Type: application/json" --data-binary "@irc-connect.json" https://localhost:55477/irc/connect
```

`-k` accepts the local API's self-signed certificate; it is independent of the
IRC certificate option. Both example passwords are placeholders. The input JSON
contains a plaintext password; FluxFTP stores the saved credential with Windows
protection and does not include it in the response.

HTTP 202 with `{"status":"connecting","saved":true}` means configuration was
saved and the connection process started, not that IRC registration/JOIN has
completed. Follow the normal IRC log for connection results. HTTP 401 means API
authentication failed; 400 means invalid settings. Changing IRC host, port or ZNC
identity clears saved IRC-account mappings so permissions are not carried over
to a different network. Other global settings are retained.
