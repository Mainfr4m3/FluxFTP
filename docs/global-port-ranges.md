# Global active and passive port ranges

Global Settings > Network contains both port ranges. Active From/To selects the local listening ports used by FluxFTP's active FTP transfers and directory-listing fallback. If a port is occupied, Flux searches inside the selected interval. An exhausted interval produces an error without using an unrelated port. Forward/firewall these local ports as appropriate for active FTP.

Allowed passive From/To restricts server-offered PASV/EPSV/CPSV ports for all sites, including direct FXP. Blank fields allow all server ports. This does not set the server's passive range or select its offered port. The active FTP range does not constrain the server-to-server PORT command; that command points to the passive server's offered port.

Passive range fields were removed from Site Options. Old per-site INI values are no longer used by the desktop: enter the desired common passive range in Global Settings. Reconnect browsing sessions after changes; newly rented workers use the current global settings. Site Options still contains FXP active/passive role selection.
