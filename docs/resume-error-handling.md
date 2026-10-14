# Resume error handling

Global Settings > FXP > Auto overwrite file if resume is rejected is on by default as of v1.0.111. Older default-off configurations are migrated once; later explicit choices are preserved. It applies to normal upload/download and direct FXP/RACE resume attempts.

When enabled, a REST rejection indicating resume cannot be used triggers one reconnect/reset and one transfer from byte zero. Both FXP control sessions are reconnected, clearing a restart offset accepted on only one peer. Local partial downloads are truncated only after the reconnect succeeds. Upload input is repositioned to zero. File averages and session byte totals use the restarted full transfer.

No overwrite fallback occurs for cancellation, timeout, network/TLS failure, dupe rejection, file locks, or server closure. ASCII-resume policy remains separate. If the overwrite attempt also fails, the error is propagated rather than looping. Server dupe and write permissions still apply.
