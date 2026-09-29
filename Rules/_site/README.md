# FluxFTP site rules

Place one `*.rules.json` file per site in `Rules\_site` **next to the FluxFTP EXE**.
The JSON `site` must match the name of a saved FTP site (case insensitive).
Files ending in `.example` are inactive. To try the fictional example, copy
`ExampleSite.rules.json.example` to `ExampleSite.rules.json` and review it first.

Use **Sections > Site rules...** to open this folder, reload/validate files and
test release names. Rules are read again before each outgoing file transfer and
PRE, including queued transfers resumed later. Downloads to your PC are not
filtered. Removing a site's file restores the previous behavior for that site.
An invalid active file blocks rule-checked operations until corrected; unknown
JSON properties and unsupported operators are errors, not silently ignored.

```json
{
  "version": 1,
  "site": "MySite",
  "rules": [
    { "field": "release", "match": "regex", "values": ["\\.DUBBED\\."], "action": "drop", "reason": "Dubbed releases not allowed" },
    { "field": "group", "match": "in", "values": ["ExampleGroup"], "action": "drop" }
  ],
  "sections": [
    {
      "name": "MP3",
      "path": "/MP3/",
      "defaultAction": "allow",
      "rules": [
        { "field": "release", "match": "regex", "values": ["AUDIOBOOK"], "action": "drop" }
      ]
    }
  ]
}
```

## Matching

- `field`: `release` is the complete release folder name; `group` is the part
  after the last hyphen; `tag` matches individual tokens separated by `.`, `_`
  or `-`. Tags are derived from the name, not external metadata.
- `match`: `regex` uses .NET regular expressions; `glob` supports `*` and `?`;
  `in` is an exact match against any entry in `values`; `always` needs no values.
- Matching is case insensitive. `negate: true` inverts the entire condition.
  Multiple values mean **any** value, before negation.
- `action`: `allow` or `drop`. The first matching rule wins in each list.
  Global `drop` always blocks; global `allow` still proceeds to section rules.
  With no matching section rule, `defaultAction` decides (default `allow`).
- `reason` is optional text displayed when a rule matches.
- `requiresMetadata: true` blocks the section with an explicit explanation.
  IMDb/TV/music lookups are not yet implemented. It cannot be overridden by an
  `allow` rule. Turn it off only if you deliberately remove that requirement.

## Paths and scope

The section paths can be used by API `src_section`/`dst_section` and section
resolution for raw API commands. Rules-file mappings take precedence over
existing Sections mappings, but do not overwrite `FluxFTP-sections.json`.
The regular Sections editor continues to edit that separate file; the Site
rules window displays the rules-file mappings.

Uploads and FXP are checked against the **destination** site's rules. The first
path component below the section path is treated as the release name, so every
file inside a release is checked against that release rather than its filename.
Use `SITE PRE <section> <release>` for PRE checks in the command window and API.
Custom commands/scripts and external FTP clients are not a server-side policy
boundary; FluxFTP does not inspect arbitrary script actions.

For manual transfers Flux uses the longest matching section path. If several
section aliases share that path, **all** matching policies must allow the
release. Use the API's explicit `dst_section` to choose a particular alias.
An active site's transfers outside its defined paths are blocked. FTP paths
are case sensitive and must be absolute, without `.` or `..` components.
Changes to paths can therefore block already queued jobs, intentionally.

## Example setup

ExampleSite.irc-setup.txt.example contains fictional paths and credentials. See docs/irc.md for commands.
