# Skiplist filter modes

Global Settings > Skiplist now uses either the normal wildcard list or Advanced filters. The Advanced button opens the rule editor. Saving regex rules automatically activates Use advanced filters and disables the normal list. The inactive list is preserved and can be restored by switching modes.

Advanced mode evaluates its ordered Allow/Deny rules only. No advanced match means the normal wildcard list is not consulted. Normal mode ignores Advanced rules. Common enable, Auto/Manual mode, zero-byte and empty-directory options remain shared. The master checkbox label identifies the selected mode.

The selected mode is saved in global settings and skiplist exports. Older configurations with Advanced rules infer Advanced mode until an explicit choice is saved. Skip-match logging remains available to identify active filter decisions. RACE metadata handling and zero-source waiting are independent.
