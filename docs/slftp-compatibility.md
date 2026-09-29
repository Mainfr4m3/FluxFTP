# slftp interoperability reference

Reference: https://gitlab.com/slftp/slftp at commit
`be05ed03726c19bd2ba702a0f2a018643bedf60d` (GPL-3.0).

FluxFTP's C# implementation uses the existing Bouncy Castle Blowfish primitive.
The Pascal implementation is not copied or bundled. The reference source was
read to understand command meanings and wire formats. Relevant reference files:

- `helpfiles/ircchanadd.txt`, `ircchanblow.txt`, and catch command help files.
- `precatcher.pas`: matching every configured word and restricting network,
  channel and bot sender.
- `ircblowfish.ECB.pas`, `ircblowfish.CBC.pas`: FiSH framing, padding and encoding.
- `tests/ircblowfish.ECBTests.pas`, `ircblowfish.CBCTests.pas`, and
  `ircchansettingsTests.pas`: the short “Hello guys!” interoperability vectors.

Implemented: explicit channel configuration, FiSH ECB/CBC receive processing,
JOIN keys, announcement-only additional networks, catch configuration/dry runs,
and recent event retrieval. FluxFTP uses its own JSON routing/rule formats.
Neither metadata processing nor automatic racing is implemented by catches.
Channel keys are not used to authorize administration.

Remaining differences: slftp numeric SSL modes, legacy CWD settings, automatic
BNC tests, country and invite-nickname commands are not yet implemented.
FluxFTP reports an error for those commands instead of accepting inert settings.
