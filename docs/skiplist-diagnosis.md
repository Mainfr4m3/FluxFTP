# Skiplist diagnosis

Global Settings > Skiplist > Log skiplist matches (rule and file size) enables SKIP-MATCH records in RACE-Log and the connection log. It is off by default. Each distinct item/reason/size is logged once until settings are saved again, limiting repetitive RACE scans. The bounded deduplication cache may clear during very large sessions.

Records identify a global regex, a global wildcard, Skip Zero Byte File, Skip Empty Directory or a VISIONARY file/folder filter. Global regex/wildcard records include the exact pattern, action, configured file type and scope. They also show Auto/Manual mode and known source size or unknown when that call did not have a size.

This diagnostic does not alter rules or disable configured filtering. A normal positive-size RAR/SFV file is not denied simply because the skiplist is enabled. If every file is skipped, use these records to identify the matching rule, a parent folder filter or incorrect listing sizes. In v1.0.112 and later, RACE also waits for zero/unknown-size source payload to grow rather than sending empty files.
