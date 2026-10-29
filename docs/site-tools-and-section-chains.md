# Site tools and Visionary section chains

In **Sites**, use **Test All Sites** to check login, the configured status command
and logout. Tests have cancellation and a per-site timeout. They replace an idle
worker where necessary and close every test session afterward.

The **Disconnect** menu after Global Settings offers all sites or one named site.
It pauses queued work and closes browser, pool and shared listing connections.
Automatic monitoring does not reopen a manually disconnected site; explicitly
Connect to allow new logins again.

API RAW commands borrow an idle tracked worker rather than adding an uncounted
login. A waiting command takes priority over new transfer rentals. Commands wait
for busy slots within their request timeout and close the rented session afterward.

## Optional section-chain mode

1. In Site Options > Slots & transfers, set **Link speed (trading)** manually to
   1 Gbit/s or 10 Gbit/s. Unknown classes do not enter this mode.
2. In Sections, mark **Trading active** for each participating site/section row
   and provide its remote path. A stored path alone does not activate trading.
3. Enable **Visionary section chains** in Global Settings. It applies to subsequent
   chain announcements; the default is off.

| Source → destination | Allowed |
|---|---|
| 10 → 10 Gbit | Yes |
| 10 → 1 Gbit | Yes |
| 1 → 1 Gbit | Yes |
| 1 → 10 Gbit | No |

Permitted incoming Visionary pairs retain their order and live slot values.
Trading-active membership and link classes only filter those incoming pairs.
The planner never adds routes, destinations or alternate sources that Visionary
did not send for this release. Visionary remains responsible for evaluating its
release rules before sending chains; Trading active is not release-rule approval.
Each destination release retains one shared writer and normal file reservations.
Upload/download permissions, source/target blocks, slot limits and file rules
still apply. A route needs available source files before it can transfer data.
The mode supports up to 64 participants and does not independently catch PREs.

## Names and descriptions

Name is the unique site identifier, compared without case. Description is display
metadata and can be reused freely, including matching another site's name.
API operations continue to address sites by Name.
