# SpicetifyDeck

Control Spotify from Macro Deck using Spicetify. Use your existing Spotify sign-in; no developer account, API key, or extra Spotify login is needed.

<img width="886" height="508" alt="spicetify" src="https://github.com/user-attachments/assets/fef53558-ee1f-4629-9206-651dd98ffc5f" />


## What you can do

- Play, pause, skip, seek, and adjust volume.
- Change shuffle, repeat, mute, and liked-song settings.
- Play tracks, albums, playlists, radio, and Liked Songs; manage the queue.
- Use live deck values for playback, track and album details, playlist or radio context, queue tracks, and liked status.
- Show the current track's artwork through Macro Deck's music-player display.

## What you need

- Macro Deck 3
- The Spotify desktop app
- Spicetify installed and applied at least once

## Set up

1. Install SpicetifyDeck in Macro Deck.
2. Quit Spotify completely. Closing its window may leave it running in the background.
3. In Macro Deck, run **Install Spicetify Bridge**.
4. Start Spotify again.

The install action configures the bridge for you. If Spotify is open, the plugin won't apply changes to it; quit Spotify and run the action again when prompted.

## If Spotify does not connect

1. Quit Spotify completely and start it again.
2. Check the SpicetifyDeck settings page for the connection status.
3. If the bridge is missing, out of date, or still disconnected, quit Spotify and run **Repair Spicetify Bridge**, then start Spotify again.

The `spicetifydeck-connection-status` variable also reports the bridge status. A value such as **Bridge Installed - Waiting for Spotify** means the bridge is installed and is waiting for Spotify to start.

## Values on your deck

Playback values include play state, position, duration, progress, and volume. Track values include name, artist, album, and their URIs. Context values show the current playlist or radio source. Queue values show the next and previous tracks. Values the Spotify client cannot provide are shown as unavailable rather than guessed.

## For contributors

Build and test with:

```bash
dotnet build SpicetifyDeck.slnx
dotnet test tests/SpicetifyDeck.Tests/SpicetifyDeck.Tests.csproj
```
