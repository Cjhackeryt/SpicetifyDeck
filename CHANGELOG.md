# Changelog

## 2.0.3

- Added writable `progress_seconds` and `progress_percentage` variables for seeking within the current track.
- Improved album-cover selection by preferring the high-resolution Spotify artwork URL and accepting safe Spotify image identifiers.
- Restricted artwork downloads to HTTPS Spotify CDN hosts, disabled redirects, and limited responses to image content and 2 MiB.
- Secured **Open Spotify** with an allowlist for `spotify:` URIs and HTTPS links hosted exactly by `open.spotify.com`; invalid targets are rejected.
- Validated custom in-client page navigation before sending it to the Spotify bridge.
