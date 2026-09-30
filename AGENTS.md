# Agent guidance

This file is the rule set for **SpicetifyDeck** (`com.cjhackeryt.spicetifydeck`), an
out-of-process Macro Deck 3 plugin that drives the Spotify desktop client through
Spicetify.

[README.md](README.md) is the human-facing guide: what the plugin does, how a user
installs the bridge, the security model, the tunables, and how to build and pack. This
file is for changing the code. Read it before changing anything.

The general Macro Deck plugin rules, the store gate, the builder shape, the localization
and logging rules, and the packing commands are the same as the repository root
`AGENTS.md` and are not repeated here. What follows is only what is specific to this
plugin.

## The one thing to understand first

There is exactly one transport, and it is a WebSocket the Spicetify extension opens to
this plugin.

**The plugin never talks to `api.spotify.com` and never asks the user for a client id, a
secret, a redirect URI or any premium authentication.** That is not a style preference, it
is the design: everything the plugin does happens inside a Spotify client the user is
already signed in to, so there is nothing to authenticate against. If a change tempts you
to add a token, a client id or a REST call to Spotify, it is the wrong change. The answer
to "the client does not expose this" is a capability flag and a clear error, not OAuth.

## The bridge owns a fixed port, and that is not negotiable

The bridge **listens on its own port**, `127.0.0.1:8975` by default and configurable with
`SPICETIFYDECK_BRIDGE_PORT`. It is a second, minimal Kestrel listener (`BridgeListener`), not
a route on the host's.

This is the fix for the failure that made the whole plugin look broken. An earlier version
served the bridge from the listener the Macro Deck supervisor assigns, and that port is
**reassigned on every start**. So the address baked into the extension inside Spotify went
stale the moment Macro Deck restarted, and the extension was left pointing at a port nothing
was listening on, forever. Repairing it worked for exactly as long as the next restart
allowed. A port the plugin owns does not move, so the file is written once and keeps
working across restarts, updates and version changes.

Consequences of that decision:

- `BridgeEndpoint` is the single source of the address, and returns `null` until the
  listener has bound. An installer must refuse rather than write a file with a guessed port.
- The listener must **skip a busy port** rather than fail. `IsAddressInUse` unwraps
  Kestrel's `AddressInUseException`, because a bare `SocketException` check misses the real
  case and the whole listener dies on a port that is merely occupied.
- **The port it lands on is persisted and tried first next time.** Skipping a busy port
  without remembering the result is the original bug reached by a different road: one run
  takes a fallback, writes the file with it, the next run takes the default back, and the
  extension is left pointing at a dead address. The remembered port is what makes a fixed
  port actually stable. `BridgeCredentials.RememberedPort` holds it.
- Never bind a wildcard address here. This listener is the security boundary, and
  `BridgeConnectionTests` asserts loopback rather than assuming it.
- `/_macrodeck/*` stays reserved and untouched. The bridge's own paths are outside it.

## Every socket authenticates before it is believed

The first message on a socket must be a `hello` carrying the token
`BridgeCredentials` holds. Anything else, or the wrong token, is answered with an
`auth-failed` reason and the socket is closed. The session is published to the rest of
the plugin **only after** the token is accepted, so a client about to be refused never
shows up as connected.

The client holds state until it receives a `welcome`, so nothing is ever pushed to a
plugin that was not going to believe it.

**There is exactly one token, and a refusal does not change it.** A mismatch is a fact
about the file inside Spotify, not a reason to mint a new secret: regenerating on a
refusal would invalidate the bridge that was working and leave the user repairing a loop
that repairs nothing. The token is **persisted** in `bridge.json` in the plugin data
directory and reused across restarts, and it is replaced only by
`SpicetifyBridgeService.ResetAuthenticationAsync`.

**Rotation is a separate method, not a parameter, and that is load-bearing.**
`InstallAsync` takes no flag and cannot rotate; there is nothing to pass `true` to. This
was not always so. Rotation used to be a `bool regenerateToken` argument, and the ordinary
install action passed `true`, which produced a failure that looked like a bug and was a
design error:

1. Install mints a new token and writes it into the bridge file on disk.
2. `spicetify apply` is refused because Spotify is running, so the change is never applied.
3. Spotify keeps executing the bridge it loaded at start, which holds the **old** token.
4. The listener accepts only the new one, so every connection is refused with 1008.
5. The remedy appears to be running the install again, which rotates the token once more.

The user cannot fix that by repeating an action, and nothing in the status said why. Two
changes close it, and both are needed. Rotation moved out of `InstallAsync` into its own
method, so the mistake is no longer expressible. And `UpdatePending` was added as a status,
so "the file is ahead of the client" reads as a change waiting for a restart rather than as
a broken bridge, which is what sends someone to reset the one thing that would break it.

Install and Repair are the same operation and both call `InstallAsync`. The settings page
and the actions must not reintroduce a flag, and a new caller wanting a fresh token has to
name `ResetAuthenticationAsync` explicitly, which is the point.

**A bridge that needs no change is reported as synchronized, not rewritten.**
`IsSynchronized` compares the file, the Spicetify configuration and the copy inside the
client, and `InstallAsync` returns before writing anything when all three already match.
Rewriting something correct is how an operation that should be free turns into one that
demands a restart. It runs before the token can be rotated, so a routine install cannot
change anything at all.

Endpoint and token are separate concepts. The port is remembered and preferred, and the
file is rewritten with the live address when it moves, but the rewrite **reuses the stored
token**, so a port change never knocks out an installed bridge. Three copies have to agree
and `BridgeReconcileTests` holds them to it: what is persisted, what is written into the
extension, and what the listener compares against. A token is 64 lower-case hex characters
with nothing around it, asserted rather than assumed, because a stray quote or a case change
would look exactly like a wrong token.

**The connection state distinguishes a refused client from an absent one.**
`Disconnected`, `Connecting`, `Authenticating`, `Connected`, `AuthenticationFailed`.
`Authenticating` covers the window between accepting a socket and accepting its token, and
`AuthenticationFailed` is its own state rather than a generic error, because "not connected"
is not a useful thing to say about a client that connected and was turned away: it sends the
reader to look for a Spotify that is not running.

Authentication is decided once, in the handshake, and is **never** revisited because of
state. A client reporting no track, or no groups at all, is still authenticated, and a
stopped player is still connected.


## The plugin reconciles the installed file on every start

The extension file inside Spotify is written once. The plugin cannot rewrite what the client
already loaded, so the file on disk and the plugin drift apart whenever the address or the
token changes: a run that bound a different port, an install done by another process, a
release that ships a new extension, or a reset token. `SpicetifyBridgeService.ReconcileAsync`
runs from `InitializeAsync` and closes that gap by itself.

This exists because of a failure that was invisible from the inside. The plugin reported the
bridge as installed, enabled and configured, and reported "Not Connected", and both were
true and neither was useful: the file carried a token and a port from an earlier run, so the
client retried an address nothing was listening on for ever, and nothing anywhere said the
file was not the file the plugin needed. A sticky port does not prevent this, it only makes
it rarer.

Three rules make it safe to run unattended:

- **It is silent unless the file is actually wrong.** `BridgeScriptGenerator.IsCurrent`
  compares what is on disk against what this run would write, which catches the address, the
  token and the extension version in one check. Matching means no write, no `spicetify apply`
  and no restart request, or every start would demand one.
- **It never installs.** An absent file returns null. Installing the bridge is the user's
  decision, taken through an action.
- **It reuses the token.** Regenerating one would knock out a bridge that was otherwise
  working, which is the opposite of a repair.

It shells out to the Spicetify CLI, so it is wrapped in a `try` and is never fatal:
a failure there must not stop the plugin loading its variables and actions. What it must not
do is stay quiet, so both outcomes are logged at `Information` or above.

**A current file is not a loaded bridge, and only the client's page says which.**
`IsLoadedByClient` reads `Apps/xpui/index.html` in the Spotify client and looks for a
reference to the extension. This is the check that was missing, and it was found on a real
machine that had every other fact correct: the file installed, the configuration naming it,
the copy present and current in the client, `Verify` returning no problem, and Spotify
running with no bridge in it at all because the page had been rewritten by something that
did not know about the extension. The status said Not Connected, and the remedy that
suggests could not have helped.

Two rules keep it honest:

- **A missing page is not a failure.** A machine where Spicetify has never been applied has
  no `index.html`, and that is not something to report. Reading absence as "not loaded"
  points a user at a repair they cannot act on before they have set anything up.
- **It makes `IsSynchronized` false**, so a routine install cannot short-circuit on
  "already synchronized" and skip the apply that would fix it. It also makes
  `ReconcileAsync` re-apply, with the source file left alone, because the file needed no
  rewriting and only the apply can change the page.

This is the general lesson of this whole area: a check that only looks at files the plugin
wrote cannot see a problem caused by something that did not. Check the thing the consumer
actually reads.

## Never apply while Spotify is running

`spicetify apply` rewrites the Spotify client's own bundle. Running it against a live client
is what produces Spotify's **"something went wrong, reload page"** screen: the process keeps
running with a replaced bundle, the interface fails, and the bridge disconnects because the
extension died with it. It looked like a bridge fault and was nothing of the kind.

So **every** path that applies, `InstallAsync` and `ReconcileAsync` alike, checks
`IsSpotifyRunning` first and refuses rather than doing it. The file and the configuration are
still written, because that half is safe, and `MarkApplyRequired` is set so the change is
finished by the next reconcile after Spotify is closed. A user who runs a bridge action with
Spotify open is told exactly what to do rather than being allowed to break their client.

This is the one operation in the plugin that is not safe to automate, which is the whole
reason the reconcile is gated rather than simply applied.

**Refusing to apply is not the same as failing, and the result has to say which it is.**
"A file was written but is not live" and "the file is wrong" are opposite situations and the
remedies are opposite: the first wants a restart, the second wants a rewrite. So the running
case returns a *pending* result naming the restart, and `ResolveStatus` reports
`UpdatePending` rather than `NotConnected`. It is a Warning, not an Error, and it explicitly
tells the reader not to reset the authentication, because by then a live client is the one
thing that proves the shared secret is still correct.

**A reporting client outranks a pending update.** If Spotify is connected and reporting, the
status is `Connected` even when the copy in the client is older than the file on disk. The
bridge that is actually running works, and the newer file is a fact about the next restart,
not a fault. `BridgeReconcileTests.ALiveClientOutranksAPendingUpdate` holds this.

`IsSpotifyRunning` is reached through the instance field `_spotifyRunning`, which the tests
replace with `UseSpotifyRunningProbe`. Every branch that decides to refuse an apply is
otherwise unexercisable, because the answer depends on whether the developer's Spotify
happens to be open.

## Spicetify's CLI appends, it does not replace

Established by running the CLI, not by reading its help, and both halves matter:

- `spicetify config extensions <name>` **appends** and de-duplicates a name that is
  already present. So adding is a single argument, and this plugin can never disable a
  user's other extensions by rewriting the list.
- There is **no remove verb**. Passing a value that is already present logs "already in
  the list" and changes nothing, and an empty value is ignored. Uninstall therefore edits
  the `extensions` line of `config-xpui.ini` directly (`SpicetifyConfig.RemoveExtension`),
  touching nothing else in the file.

Read the extension list from `config-xpui.ini` rather than the CLI wherever a status
variable needs it: the host refreshes variables on a timer, and a process launch per read
is not acceptable. The CLI is still used for every add and for `apply`.

The Spicetify **root is discovered, never assumed**. A machine observed during
development kept `spicetify.exe` in `%LOCALAPPDATA%\spicetify` while its config, and
therefore its extension list, lived in `%APPDATA%\spicetify`. The directory holding
`config-xpui.ini` is the root, because that is where relative extension paths resolve
from. Hardcoding `%APPDATA%` gets it right on that machine and wrong on others.

The lookup is cached, but a positive answer is only trusted while the executable it named
still exists, and a negative one is retried every 30 s. Caching it for the life of the
process was a real trap: it left the plugin insisting Spicetify was missing on a machine
that had just installed it, and the only cure was restarting Macro Deck. Never cache a
"not found" answer without an expiry.

## Issue severity is a statement about the state, not the volume

`BuildIssues` picks severity by how broken the state actually is, never by how much
attention it deserves. A bridge that has simply never been installed is the *expected*
state minutes after the plugin is installed, so it is a **Warning**. Raising it to an
Error made a normal first run look like a failure, which is the opposite of what an issue
is for. Error is reserved for states that cannot work at all: a corrupted file, and a
client that was refused for a bad token.

Every issue sets `ActionLabel`. The field exists so the host can offer the remedy as
something clickable, and leaving it null is what made a startup warning look like a dead
end. Every issue group carries `Title`, `Description`, `Resolve` and `Action`, and
`LocalizationTests` asserts all four exist for each one.

**Issue text must not describe a procedure the plugin now performs itself.** The 2.0.0
text told users to run `spicetify config extensions` by hand, which the install action had
been doing automatically for a release. When an action gains a step, every string that
describes that step has to be checked, not just the action's own description.

## Everything goes through Spicetify's internals

The extension uses only APIs documented at
<https://spicetify.app/docs/development/api-wrapper>, each behind a feature check, because
those internals change between Spotify versions. Two rules:

- Read the track defensively. Builds expose either a GraphQL object with camelCase fields
  or a `metadata` bag with snake_case keys, and some expose both. `readTrack` tries both
  and a field it cannot find is simply absent, never invented.
- Feature-detect at connect time and report the result in `BridgeCapabilities`. The plugin
  then refuses a command with "this Spotify version does not expose..." instead of sending
  it and waiting out a timeout. A new client-side call needs a guard and a capability
  flag, or it will be a confusing failure on someone else's machine.

## The bridge protocol

One message each way, no more:

- Extension to plugin: `hello` (with the token) once, then `state` on every player event
  and on a one second interval. A `state` message carries the results of the commands
  sent in the previous exchange.
- Plugin to extension: `commands`, holding whatever is queued.

So a command's outcome arrives on the next report, which is why every command carries a
deadline. A command with no deadline would hang its action forever.

Adding a command means touching four places, and missing one is a runtime failure rather
than a compile error: `BridgeCommandKind`, the extension's `HANDLERS` table, a `Payload`
property in `BridgeProtocol` if it takes arguments, and the action that sends it.

## The wire is grouped, and a group that is absent is not a cleared value

A report is `playback`, `track`, `context` and `controls`, each optional, plus `capabilities`
and `client`, which the client sends **once** per connection because a build cannot grow an
API or change its version while it is running.

The distinction the grouping exists to express:

- **A group that is absent means "nothing to say".** `FromReport` emits no entries for it,
  the merge does not touch it, and the values stand.
- **A field inside a group that is present but null means "this really is gone".** That is
  the only thing that may clear a value.

A flat object of nullable properties cannot say which is which, and that ambiguity was the
root of the flicker: a field the client failed to read looked exactly like a field that was
cleared, so a momentary gap had to be guessed at, and guessing wrong blanked a value for a
second at a time.

**`Apply` rebuilds the snapshot in full, so the merge must answer for every field.** A field
missing from the merged map comes out blank, which would make an omitted group mean the
opposite of what it means. `AllFields` exists for this: the merge fills anything it did not
touch from the cache before `Apply` runs. A new snapshot field nobody remembered to carry
is a silent blanking bug, so the list is written out and a new field is a compile error
rather than a mystery.

**The client only sends what changed.** The extension keeps the last sent value per group and
omits a group whose contents are identical, and a report with nothing in it is not sent at
all unless it carries command results. The change tracking is reset on `welcome`, or a
plugin that had just restarted would learn nothing for a tick.

## Flicker is the thing users notice

Four rules, all load-bearing, all covered by `StateStabilityTests` and
`VariableStabilityTests`:

1. A value the client stopped reporting **keeps its last known value** until
   `UnavailableTimeout` elapses, after which it becomes unavailable. The check is `>=` so
   a zero timeout means zero tolerance.
2. `StateChange` is raised only when something actually differs. The position is
   deliberately excluded from that comparison, because it moves on every report and
   including it would run every listener once a second for nothing.
3. A **track change keeps the previous metadata bundle until the new track has a valid
   name.** New fields are staged and committed together, so partial reports neither blank
   the deck nor mix old-track artist or album data with the new title. Once committed,
   missing fields do not fall back to the previous track.
4. **A report with no track is a gap, not a change.** Rule 3 needs this boundary, because
   `Player.data` and `item` are transiently absent often enough that a read comes back
   empty about once a second while a track plays. Treating that as a change wiped every
   track field and made a deck blink to N/V and back in a loop. Only a report naming a
   *different* track starts a staged replacement; one naming none keeps the current bundle,
   and with the grouped protocol it does not even mention the track. The extension also
   holds the value for a few ticks, which is where the gap is actually visible.

**A `*Known` flag must be assigned.** `IsPlayingKnown`, `IsPausedKnown`, `PositionKnown`,
`ShuffleEnabledKnown` and `RepeatModeKnown` separate "reported as false" from "never
reported", and a variable that needs one returns null otherwise. They were once declared and
never assigned, which left is playing, playback state, current position, shuffle and repeat
mode reading as **permanently unavailable** for the whole life of the plugin. `Apply`
derives each from whether the merged value is non-null, which is exactly right because the
merge has already substituted the retained value for one the client stopped reporting.
`ReportedBooleanAndRepeatValuesBecomeKnown` is the guard.

**The link to the client is stamped on read, never trusted from the snapshot.**
`BridgeConnected` and `ConnectionState` are controlled by `BridgeConnectionManager`, not by
individual field values. The manager handles Connecting, Connected, Disconnected and Error
separately, and detects a stale reporting session on reads. `Current` re-stamps the live
state on every read, so missing metadata cannot masquerade as a disconnected bridge.
`OnConnectionChanged` also stamps and announces, because a socket opening or closing is a
discrete event a listener should hear about, and `Diff` compares both fields so the
announcement actually happens.

A reader returns `null` for "not known" and **never** the `Unavailable` marker. The state
manager decides whether that still means the last good value; the provider decides between
the retained value and `VariableReading.Unavailable`. A reader that invented the marker
would make "never known" and "the text happens to be unavailable" the same thing, and
would report a missing number as if it were a piece of text.

**Anything the provider mutates from a read is a `ConcurrentDictionary`, not a
`Dictionary`.** The host dispatches reads across 32 concurrent invocation slots and the
provider is a singleton, so a plain dictionary can throw on a resize, and the throw
surfaces to the user as a variable flickering to N/V for reasons that have nothing to do
with Spotify. `VariableConcurrencyTests` reads every variable from many threads to keep
that honest.

## Layout

```
Program.cs                     builder chain, DI, the listener registration
SpicetifyIntegration.cs        implements every capability, delegates the work
SpicetifyMusicPlayer.cs        Spotify as a Macro Deck music player
Bridge/                        the extension, the wire shapes, the socket, the fixed
                               port endpoint and its listener, the credentials, the
                               Spicetify locator, CLI and config
State/                         the snapshot, the merge and diff, the settings
Artwork/                       album artwork for button icons
Actions/                       action definitions, including bridge management
Variables/                     the playback catalog, the status catalog, the provider
Widgets/                       the widget type, its action, and the paint service
Ui/                            the plugin's settings page
Localization/Strings.resx      every user-facing string
tests/SpicetifyDeck.Tests/     catalog, contract, state-stability, variable-stability,
                               concurrency, bridge-connection and install tests
```

`SpotifyStateManager` is the single source of truth for playback. Variables, the widget,
action states and the issues all read it, so one push feeds every surface and the values
on a deck can never disagree. Nothing else may call into the bridge for a read.

`SpicetifyBridgeService` is the single source of truth for the **installation**,
alongside `BridgeCredentials` for the token and `BridgeConnectionManager` for the live
link. A status variable reads `Describe()`, which never launches the CLI.

The two variable catalogs are separate on purpose. `SpicetifyVariableCatalog` reads a
playback snapshot; `DeckStatusVariableCatalog` reads the bridge service. Folding them
together would mean a reader that can only ever return null for half the variables.

## Things that will break quietly

**A variable's id is public API.** `SpicetifyVariableCatalog` ids are persisted in user
data and bound to existing widgets. Renaming one breaks every deck using it. The same is
true of an event definition id, and of an action id only in that it must stay unique
across the whole plugin, because `Build()` fails on a collision. The lists in
`CatalogTests` are where a duplicate or a regression is caught.

**The `NoIcon` icon snapshot.** MDC0312 requires a snapshot with `NoIcon` set to carry no
reference and an empty version. Returning `Version = "none"` fails conformance.

**`IIntegrationConfig` is not in DI.** There is no config flow any more, so the plugin
never needs it. Do not reintroduce one: a config flow leaves the integration disabled until
the user completes it, and this plugin has nothing to complete.

**Never bind a second listener on the host's address.** The bridge's own listener is a
separate, deliberate component on its own fixed port. Do not set `UseUrls`, do not map
bridge routes onto the host application, and do not read the supervisor's port. See above.

**The widget is never rebuilt wholesale.** A patch is skipped whenever the label and
colours it would produce are identical to what the widget already shows, so a repaint
costs nothing when nothing changed. Recreating a widget on every position tick is what
makes a deck stutter.

## Adding to it

**An action.** Subclass `SpicetifyActionBase`, return `null` for success and a message
for failure, and use `SendAsync` rather than touching the bridge yourself. If the command
needs something the client may not expose, check it with `Guard` first. Put every
user-facing string in `Strings.resx` and reference it explicitly, so a renamed key is a
compile error. Add the id to `CatalogTests.ActionIdsAreUniqueAcrossThePlugin`.

**Never write "no message" as a ternary.** `LocalizedText` has an implicit conversion from
`string?`, and `FromLiteral(null)` returns `default`, so this does **not** produce a null:

```csharp
return result.Ok ? null : LocalizedText.FromLiteral(result.Error);   // wrong
```

The ternary's type is inferred as the non nullable `LocalizedText`, so the `null` goes
through that conversion and becomes a value that is **present but empty**. The executor
cannot tell it from a real message, so it takes the failure branch and the host shows
"The action failed without saying why" for work that actually succeeded. Every action
failed, always, and the plugin looked completely broken while doing its job. It also broke
the MDC0802 conformance check, which is how it was finally caught.

Write it as an if, where a bare `return null` in a `LocalizedText?` method is unambiguous:

```csharp
if (result.Ok) { return null; }
return LocalizedText.FromLiteral(result.Error);
```

A `switch` expression is fine, because its arms are already the nullable type. The general
rule: **anywhere a `LocalizedText?` is returned, never let a ternary choose between `null`
and a `LocalizedText`.** `ActionOutcomeTests` covers the guard and send paths.

Relatedly, a failure must always carry a reason. The host substitutes its own generic
sentence when `ErrorMessage.IsEmpty`, which is the worst possible outcome: the user is
told the action failed and given nothing to act on.

**A variable.** Add one entry to `SpicetifyVariableCatalog`, naming its own resource
keys. Declare the right type, unit, `SemanticKind` and refresh source, and only mark it
writable if `SpicetifyVariableProvider` has a write path for it. Do not add a variable for
something the client cannot report.

**A widget field.** Add the option to `WidgetConfiguration`, the property to the record,
a branch to `NowPlayingWidgetService.Render`, the property to the widget type's JSON
schema, and the resx keys. All four, or the schema and the renderer will disagree.

**A bridge command.** Four places, listed above. A handler that cannot run must **throw**,
not return silently, so the plugin sees a reason instead of a timeout.

**A bridge management action.** Subclass `BridgeActionBase` and call
`Service.InstallAsync` or `Service.UninstallAsync`. The service verifies before it claims
success, and the base class turns its result into a truthful `ActionResult`. Never report
an install as successful on the strength of having written a file.

**A status variable.** Add it to `DeckStatusVariableCatalog` with a reader over
`SpicetifyBridgeService`. A reader must be cheap: no process launches, no network. A new
`BridgeStatus` needs a word in `StatusWord`, and the word must stay fixed English, because
a deck compares against it.

**A settings page row.** `Ui/SpicetifyDeckUiProvider.cs`. The tree is built once and
patched, so a new row is another `Status(...)` in `Build()`. Every value must be computed
through `Text(...)`, which is what registers the dependency that `Refresh()` invalidates.
A direct `UiText` would render once and never update.

## Verifying

```bash
dotnet build
dotnet test
macrodeck-plugin build --output ./artifacts
macrodeck-plugin test --artifact ./artifacts/com.cjhackeryt.spicetifydeck-2.0.0.macroDeckPlugin --report markdown --output conformance.md
```

Conformance must stay at zero failures; a Required check going from pass to fail is a
blocking regression. Run it after any change to the manifest, to the icon snapshot, or to
the shape of a capability.

There is no JavaScript runtime in the toolchain, so a change to the extension is verified
by review and by the bracket balance of the file, not by execution. Be extra careful there.
