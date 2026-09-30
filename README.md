# SpicetifyDeck

Control the Spotify desktop client from a Macro Deck 3 deck, using Spicetify.

Playback, volume, shuffle, repeat, your library, and music-player artwork, driven through a
local WebSocket bridge between Macro Deck and a small Spicetify extension inside Spotify.

- **25 actions**, all bound directly to this Spotify client rather than a selectable player.
- **23 variables**, and no more than that. This is a deliberate cut, not an oversight.
- **Music-player artwork** from the current track, supplied to Macro Deck's music-player surface.
- **A settings page** for the bridge.

| | |
| --- | --- |
| Playback (5) | is playing, playback state, current position, track duration, progress percentage |
| Current track (8) | name, artist, album, album artist, track URI, album URI, artist URI, track ID |
| Controls (4) | volume, shuffle, repeat mode, muted |
| Connection (3) | SpicetifyDeck connection status, Spicetify bridge status, Spotify status |
| Track metadata (3) | track number, disc number, explicit |

The plugin used to publish 66. The ones that went were the client and Spicetify version
strings, the OS, the locale and the device identity, the queue size, the playback speed,
the interface state (lyrics, fullscreen, mini player, hidden UI), the per-API capability
flags, the last-update timestamp and the last error. None of them are things a deck shows,
several were the most fragile reads in the whole report, and a variable that flickers is
worse than one that does not exist. **Nothing was renamed or re-added**: a removed variable
is gone from the catalog, the resource file, the settings page and this document.

Diagnostics are not lost, they just are not variables. The same information is in the log
and on the settings page, which is where someone debugging a connection actually looks.

## What it does not do

**It never talks to `api.spotify.com`, and never asks for a Spotify client id, a secret, a
redirect URI or any premium authentication.** That is not a preference, it is the design:
everything the plugin does happens inside a Spotify client you are already signed in to, so
there is nothing to authenticate against. If a feature would need a token from Spotify, the
answer is a capability check and a clear message, never OAuth.

## How it works

```
Macro Deck  ──WebSocket──▶  SpicetifyDeck plugin  ◀──localhost only──  Spicetify extension
                                                                          inside Spotify
```

**The WebSocket is inbound.** The extension connects to the plugin, because an extension
cannot be given a port to call. The plugin listens on its **own fixed loopback port**,
`127.0.0.1:8975` by default, so the address written into the extension does not change when
Macro Deck restarts. That matters more than it sounds: an earlier version took whichever
port Macro Deck happened to be using, which is reassigned on every start, so the extension
inside Spotify was left pointing at a dead address and could never reconnect. With a fixed
port the bridge is installed once and keeps working.

Change it with `SPICETIFYDECK_BRIDGE_PORT` if something else already holds the port. The
plugin tries the following few ports and remembers whichever it got. It defaults to 8975
rather than 8974 because 8974 belongs to a different Spicetify bridge for Macro Deck, and
the two would otherwise fight over it.

**Every connection authenticates before it is believed.** The first message on a socket
must be a `hello` carrying a shared token, compared in fixed time. Anything else is
answered with a reason and the socket is closed, and the session is only published to the
rest of the plugin once the token is accepted, so a client about to be refused never shows
up as connected. The token is stored in the plugin data directory and reused, so an
installed bridge keeps working across restarts and updates.

**The token never changes on its own.** Install, reinstall and repair all reuse the stored
token, and so does the plugin's own start-up correction. Only **Reset Bridge Authentication**
mints a new one, because it is the only thing that can strand a Spotify you already have
open: the new token goes into the file, and the running client keeps sending the old one, so
it is refused until you restart. A routine action that quietly did that would look like a
bridge fault with no way to diagnose it, so the plugin does not offer the option.

**The plugin corrects its own installation.** The file inside Spotify is written once, and
the plugin cannot rewrite what the client already loaded, so on every start it compares the
file against what it would write now and rewrites, re-applies and verifies it if they
differ. It also checks that the Spotify client's own page still asks for the extension, since
a client can hold a correct file and still run without the bridge at all. You should never
need to run a repair action; when a correction happens the plugin says so and asks for a
Spotify restart, which is unavoidable because Spicetify bakes the extension into the client at
apply time.

## Setting it up

1. Install Spicetify and apply it at least once, so Spotify starts with Spicetify's
   changes already in place.
2. Install this plugin in Macro Deck.
3. Run the **Install Spicetify Bridge** action. It finds Spicetify, writes the extension
   into its `Extensions` folder, adds it to the configuration without disturbing your other
   extensions, applies, and then verifies all of it before claiming success.
4. **Quit Spotify completely** and start it again. Not close the window, quit it.

That is the whole setup. The plugin performs the Spicetify CLI steps itself.

## If the bridge will not connect

This is the single most common problem, so it is worth saying plainly.

**Quit Spotify completely, then run the action, then start Spotify.** In that order. Not
"close the window", quit it from the tray, because a Spotify that is still running keeps the
extension bundle it loaded at start. Spicetify only bakes an extension into the client when
it applies, and the client only picks that up when it next starts.

**Never run a bridge action while Spotify is open.** Applying a bridge change runs
`spicetify apply`, which rewrites the Spotify client's own bundle. Doing that underneath a
running client is what produces Spotify's **"something went wrong, reload page"** screen:
the process keeps running with a replaced bundle, the interface goes with it, and the bridge
disconnects because the extension died with it. The plugin now refuses to apply while Spotify
is running, and tells you to quit and run it again.

**Check what the bridge says.** The `spicetifydeck_connection_status` variable turns to
"Authentication Failed" when a client presented a token this plugin did not issue, and to
"Spicetify Not Installed" or "Bridge Not Installed" when there is nothing to connect to.
The same information is on the settings page.

**Check the extension in the Spotify console.** Open devtools in the Spotify window and
look at `SpicetifyDeck`:

```js
SpicetifyDeck.endpoint        // the address it is trying
SpicetifyDeck.connected()     // is the socket open
SpicetifyDeck.authenticated() // did the plugin accept the token
SpicetifyDeck.lastError       // one line explaining a refusal
```

**If it says the token does not match**, the file on disk is from an older install. Run
**Repair Spicetify Bridge** with Spotify closed. It rewrites the file with the current token,
so it never changes the token itself and cannot leave a running Spotify holding a different
one.

**If `SpicetifyDeck` is `undefined` in the console**, Spotify is not running the bridge at
all, and no amount of repair or retrying will help. The bridge is only loaded into the client
by `spicetify apply`, and something can rewrite the client's own page without knowing about
the extension. Check the settings page: **Loaded by Spotify** must say yes. If it says no,
quit Spotify and run **Repair Spicetify Bridge** once, which re-applies it.

## Settings

Everything can be set from an environment variable:

| Setting | Environment variable | Default | What it does |
| --- | --- | --- | --- |
| Bridge port | `SPICETIFYDECK_BRIDGE_PORT` | 8975 | The fixed loopback port the bridge listens on. Only change it if the port is taken. |
| Unavailable timeout | `SPICETIFYDECK_UNAVAILABLE_TIMEOUT` | 30 s | How long a value the client stopped reporting is kept before it reads as unavailable |
| Progress interval | `SPICETIFYDECK_PROGRESS_INTERVAL` | 1 s | How often progress-reading variables refresh |
| Command timeout | `SPICETIFYDECK_COMMAND_TIMEOUT` | 10 s | How long an action waits for the client to acknowledge |
| Widget interval | `SPICETIFYDECK_WIDGET_INTERVAL` | 750 ms | The floor between two widget repaints |

## No flickering

A deck that blanks and refills is worse than one that is slightly late, so the plugin is
built around four rules, each covered by a test:

1. **The client sends only what changed.** A report is grouped into `playback`, `track`,
   `context` and `controls`, and a group that has not moved is left out entirely. A paused
   deck sends nothing at all, and a track that is simply playing sends one small group a
   second instead of a large object that is mostly nulls.
2. **A group that is absent means "keep what you know".** A field inside a group that is
   present but null means "this really is gone". Those are different, the wire says which is
   which, and a momentary gap in the client's reporting can no longer be mistaken for a
   cleared value.
3. **A change is only announced when something actually changed.** A steady state costs
   nothing, so the plugin is not pushing updates once a second for no reason.
4. **A track change keeps previous metadata together until the new track name arrives.** The new
   track bundle replaces it atomically; partial reports cannot blank the deck or mix old
   artist and album data into the new title.

Position is extrapolated from the last report rather than polled faster, so a
seconds-resolution value moves smoothly without a request per tick.

## Honest gaps

These are honest gaps, not faked values. Where something is unavailable the variable says so
rather than inventing an answer.

- **Album and disc numbers** are missing on some tracks, and a single is a plain `1`.
- **Album artist** falls back to the track artist when the client does not separate them,
  which is the honest answer rather than a blank.
- Nothing here needs a Spotify Web API token, so nothing here can report anything the client
  does not already know. There is no account, product, plan or country variable for that
  reason.

## Building it

```bash
dotnet build
dotnet test
macrodeck-plugin build --output ./artifacts
macrodeck-plugin test --artifact ./artifacts/com.cjhackeryt.spicetifydeck-<version>.macroDeckPlugin --report markdown --output conformance.md
```

Conformance must stay at zero failures. The extension is JavaScript and there is no
JavaScript runtime in the toolchain, so a change to it is verified by review, by the bracket
balance of the file, and by driving the rendered script in a real browser against the real
bridge with a mocked Spicetify API.

## Layout

```
Program.cs                     builder chain, DI, the listener registration
SpicetifyIntegration.cs        implements every capability, delegates the work
SpicetifyMusicPlayer.cs        Spotify as a Macro Deck music player
Bridge/                        the extension, the wire shapes, the fixed port endpoint
                               and its listener, the credentials, the Spicetify locator,
                               CLI and config
State/                         the snapshot, the merge and diff, the settings
Artwork/                       album artwork for button icons
Actions/                       action definitions, including bridge management
Variables/                     the playback catalog, the status catalog, the provider
Ui/                            the plugin's settings page
Localization/Strings.resx      every user-facing string
tests/SpicetifyDeck.Tests/     catalog, contract, state-stability, variable-stability,
                               concurrency, bridge-connection and install tests
```

[AGENTS.md](AGENTS.md) is the rule set for changing the code. Read it first.
