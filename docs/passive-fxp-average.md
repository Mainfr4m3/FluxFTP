# Passive FXP file averages

Global Settings > FXP > Measure average speed per completed FXP file enables passive measurement for new direct FXP transfers. The existing saved EnableFxpSpeedMonitoring setting is retained for compatibility, but it no longer starts live monitoring.

FluxFTP times the final STOR/RETR attempt using a monotonic clock, waits for accepted completion replies from both servers and calculates the queued file's byte count divided by that duration. Login, queue wait, directory listing and FXP route setup are excluded. Failed attempts produce no average; a successful reverse-route retry uses its own transfer duration. Unknown or zero file sizes produce no average.

The average appears on completed file jobs and as FXP average in the connection log and FXP-AVERAGE in RACE-Log. Completed file averages are not added to Metrics live throughput. Large files produce no speed update until completion. Direct FXP currently transfers whole files; this calculation does not infer resume offsets from server text. Client relay retains its existing local progress reporting.

Measurement adds no FTP status commands, telemetry requests, connections or monitor slot reservations. Existing pre-transfer skip checks and RACE verification are independent and continue to run. Unchecked means new direct FXP files do not calculate or log the average.
