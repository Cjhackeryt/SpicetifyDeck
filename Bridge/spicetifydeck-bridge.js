
(function spicetifyDeckBridge() {
	"use strict";

	var ENDPOINT = "__SPICETIFYDECK_ENDPOINT__";
	var TOKEN = "__SPICETIFYDECK_TOKEN__";

	var STATE_INTERVAL_MS = 1000;
	var HEARTBEAT_INTERVAL_MS = 5000;
	var RECONNECT_MIN_MS = 1000;
	var RECONNECT_MAX_MS = 15000;
	var COMMAND_TIMEOUT_MS = 8000;
	var VERSION = "2.5.1";

	var socket = null;
	var reconnectDelay = RECONNECT_MIN_MS;
	var pendingResults = {};
	var stopping = false;
	var authenticated = false;
	var playerWatched = false;
	var stateTimer = null;
	var lastReportAt = 0;

	function log() {
		try {
			console.log.apply(console, ["[SpicetifyDeck]"].concat(Array.prototype.slice.call(arguments)));
		} catch (ignored) {

		}
	}

	window.SpicetifyDeck = {
		version: VERSION,
		endpoint: ENDPOINT,
		connected: function () {
			return !!(socket && socket.readyState === WebSocket.OPEN);
		},
		authenticated: function () {
			return authenticated;
		},
		lastError: null,
		report: sendState
	};

	// ── Startup ─────────────────────────────────────────────────────────────

	function start() {
		log("version " + VERSION + " connecting to " + ENDPOINT);
		connect();

		if (stateTimer === null) {
			stateTimer = setInterval(sendState, STATE_INTERVAL_MS);
		}

		window.addEventListener("beforeunload", function () {
			stopping = true;
			if (socket) {
				try {
					socket.close();
				} catch (error) {

				}
			}
		});
	}

	function spicetifyIsReady() {
		try {
			return typeof Spicetify !== "undefined" && !!Spicetify.Player;
		} catch (error) {
			return false;
		}
	}

	function waitForSpicetify(attempt) {
		if (spicetifyIsReady()) {
			start();
			return;
		}

		if (attempt > 300) {
			log("Spicetify never became ready, the bridge is inactive.");
			window.SpicetifyDeck.lastError = "Spicetify never became ready.";
			return;
		}

		setTimeout(function () {
			waitForSpicetify(attempt + 1);
		}, 100);
	}

	// ── Transport ───────────────────────────────────────────────────────────

	var PLACEHOLDER = ["__SPICETIFY", "DECK_", "ENDPOINT__"].join("");

	function configIsUnsubstituted() {
		return !ENDPOINT || ENDPOINT.indexOf(PLACEHOLDER) >= 0 || TOKEN.indexOf(PLACEHOLDER) >= 0;
	}

	function connect() {
		if (stopping) {
			return;
		}

		if (configIsUnsubstituted()) {
			log("this copy still has placeholders in it, so it cannot connect. Run Install Spicetify Bridge again.");
			window.SpicetifyDeck.lastError = "The bridge file still has placeholders. Reinstall it.";
			return;
		}
		if (socket && (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING)) {
			return;
		}

		try {
			socket = new WebSocket(ENDPOINT);
		} catch (error) {
			log("could not open the socket", error);
			scheduleReconnect();
			return;
		}

		socket.onopen = function () {
			log("connected to " + ENDPOINT);
			reconnectDelay = RECONNECT_MIN_MS;

			watchPlayer();
			send({ type: "hello", token: TOKEN, bridgeVersion: VERSION });
		};

		socket.onmessage = function (event) {
			var message;
			try {
				message = JSON.parse(event.data);
			} catch (error) {
				return;
			}

			if (!message) {
				return;
			}

			if (message.type === "commands") {
				runCommands(message.commands || []);
				return;
			}

			if (message.type === "welcome") {
				if (!authenticated) {
					log("accepted by the plugin");
				}
				authenticated = true;
				reconnectDelay = RECONNECT_MIN_MS;

				lastSentTrack = null;
				lastSentContext = null;
				lastSentControls = null;
				lastSentPlayback = null;
				announcedOnce = false;

				sendState();
				return;
			}

			if (message.type === "auth-failed") {
				log("authentication failed: " + (message.reason || "no reason given"));
				authenticated = false;
				reconnectDelay = RECONNECT_MAX_MS;
			}
		};

		socket.onclose = function (event) {
			socket = null;
			authenticated = false;

			if (event && event.code === 1008) {
				log("refused by the plugin, not retrying until the bridge is repaired");
				window.SpicetifyDeck.lastError = "The plugin refused this bridge. Run Repair Spicetify Bridge.";
				reconnectDelay = RECONNECT_MAX_MS;
				return;
			}

			if (!stopping) {
				log("disconnected, retrying in " + reconnectDelay + "ms");
				scheduleReconnect();
			}
		};

		socket.onerror = function () {

			window.SpicetifyDeck.lastError = "The WebSocket reported an error. Is Macro Deck running?";
		};
	}

	function scheduleReconnect() {

		setTimeout(connect, reconnectDelay);
		reconnectDelay = Math.min(reconnectDelay * 2, RECONNECT_MAX_MS);
	}

	function send(payload) {
		if (!socket || socket.readyState !== WebSocket.OPEN) {
			return false;
		}

		try {
			socket.send(JSON.stringify(payload));
			return true;
		} catch (error) {

			window.SpicetifyDeck.lastError = "A message could not be sent: " + error;
			try {
				socket.close();
			} catch (ignored) {

			}

			return false;
		}
	}

	function watchPlayer() {
		if (playerWatched) {
			return;
		}

		try {
			Spicetify.Player.addEventListener("songchange", function () {
				sendState();
			});
			Spicetify.Player.addEventListener("onplaypause", function () {
				sendState();
			});
			playerWatched = true;
		} catch (error) {

			log("player events unavailable, falling back to the interval");
		}
	}

	// ── State reporting ─────────────────────────────────────────────────────

	var lastSentTrack = null;
	var lastSentContext = null;
	var lastSentControls = null;
	var lastSentPlayback = null;
	var announcedOnce = false;

	var lastTrack = null;
	var lastContext = null;
	var trackMisses = 0;
	var contextMisses = 0;

	var MAX_HELD_TICKS = 3;

	function retainTrack(read) {
		if (read) {
			trackMisses = 0;
			lastTrack = read;
			return read;
		}

		trackMisses++;
		return trackMisses <= MAX_HELD_TICKS ? lastTrack : null;
	}

	function retainContext(read) {
		if (read) {
			contextMisses = 0;
			lastContext = read;
			return read;
		}

		contextMisses++;
		return contextMisses <= MAX_HELD_TICKS ? lastContext : null;
	}

	function sendState() {

		if (!socket || socket.readyState !== WebSocket.OPEN || !authenticated) {
			return;
		}

		var state = buildState();
		var hasResults = Object.keys(pendingResults).length > 0;

		var hasState = state.playback || state.track || state.context || state.controls
			|| state.capabilities || state.client;
		var now = Date.now();
		if (!hasState && !hasResults && now - lastReportAt < HEARTBEAT_INTERVAL_MS) {
			return;
		}

		var body = { type: "state", state: state };
		if (hasResults) {
			body.results = pendingResults;
			pendingResults = {};
		}

		if (!send(body)) {

			if (hasResults) {
				pendingResults = body.results;
			}
		} else {
			lastReportAt = now;
		}
	}

	function changed(value, previous) {
		if (value === null || value === undefined) {
			return previous !== null && previous !== undefined;
		}

		return JSON.stringify(value) !== JSON.stringify(previous);
	}

	function buildState() {
		var data = safeData();
		var state = {};

		var track = retainTrack(readTrack(data));
		if (changed(track, lastSentTrack)) {
			state.track = track;
			lastSentTrack = track;
		}

		var context = retainContext(readContext(data));
		if (changed(context, lastSentContext)) {
			state.context = context;
			lastSentContext = context;
		}

		var controls = {
			volumePercent: readVolumePercent(),
			muted: bool2(function () { return Spicetify.Player.getMute(); }),
			shuffle: bool2(function () { return Spicetify.Player.getShuffle(); }),
			repeat: readRepeatMode()
		};

		if (changed(controls, lastSentControls)) {
			state.controls = controls;
			lastSentControls = controls;
		}

		var playback = {
			isPlaying: playbackBoolean(data, "is_playing", "isPlaying", "isPlaying"),
			isPaused: playbackBoolean(data, "is_paused", "isPaused", "isPaused"),
			isBuffering: playbackBoolean(data, "is_buffering", "isBuffering", "isBuffering"),
			positionMs: number(call(Spicetify.Player, "getProgress")),

			progressFraction: readProgressFraction(),
			durationMs: number(call(Spicetify.Player, "getDuration")) || (track ? track.durationMs : null)
		};

		if (changed(playback, lastSentPlayback)) {
			state.playback = playback;
			lastSentPlayback = playback;
		}

		if (!announcedOnce) {
			announcedOnce = true;
			state.capabilities = readCapabilities();
			state.client = readClient(data);
		}

		return state;
	}

	function safeData() {
		try {
			return Spicetify.Player.data || null;
		} catch (error) {
			return null;
		}
	}

	function call(target, method) {
		try {
			return typeof target[method] === "function" ? target[method]() : null;
		} catch (error) {
			return null;
		}
	}

	function bool2(read) {
		try {
			var value = read();
			return typeof value === "boolean" ? value : null;
		} catch (error) {
			return null;
		}
	}

	function bool(data, read) {
		try {
			if (!data) {
				return null;
			}

			var value = read(data);
			return typeof value === "boolean" ? value : null;
		} catch (error) {
			return null;
		}
	}

	function playbackBoolean(data, snakeCaseName, camelCaseName, playerMethod) {
		var value = bool(data, function (current) {
			if (typeof current[snakeCaseName] === "boolean") {
				return current[snakeCaseName];
			}
			return current[camelCaseName];
		});

		return value === null
			? bool2(function () { return call(Spicetify.Player, playerMethod); })
			: value;
	}

	function number(value) {
		return typeof value === "number" && !isNaN(value) ? value : null;
	}

	function readTrack(data) {
		if (!data || !data.item) {
			return null;
		}

		var item = data.item;
		var meta = item.metadata || {};
		var album = item.album || {};

		var uri = item.uri || meta.entity_uri || null;
		var artistUri = meta.artist_uri || (item.artists && item.artists.length ? item.artists[0].uri : null) || null;
		var albumUri = meta.album_uri || album.uri || null;
		var artistName = meta.artist_name || (item.artists || []).map(function (a) { return a.name; }).join(", ") || null;
		var albumTitle = meta.album_title || album.name || null;
		var trackName = meta.title || item.name || null;

		if (!uri && !trackName) {
			return null;
		}

		return {
			uri: uri,
			uid: item.uid || null,
			name: trackName,
			artistName: artistName,

			albumArtistName: meta.album_artist_name || artistName || null,
			albumTitle: albumTitle,
			albumUri: albumUri,
			artistUri: artistUri,
			imageUrl: firstImageUrl(
				meta.image_url,
				meta.image_large_url,
				item.image_url,
				item.imageUrl,
				album.images,
				item.images),
			durationMs: number(parseInt(meta.duration, 10)) || number(item.duration_ms) || number(data.duration) || null,
			trackNumber: number(parseInt(meta.album_track_number, 10)) || number(item.track_number) || null,
			discNumber: number(parseInt(meta.album_disc_number, 10)) || number(item.disc_number) || null,
			releaseDate: meta.release_date || item.release_date || null,
			isLiked: bool2(function () { return call(Spicetify.Player, "getHeart"); }),
			explicit: bool2(function () {
				if (meta.canvas && meta.canvas.explicit !== undefined) {
					return meta.canvas.explicit === "true";
				}
				return item.is_explicit === true;
			})
		};
	}

	function firstImageUrl() {
		for (var index = 0; index < arguments.length; index++) {
			var candidate = arguments[index];
			if (Array.isArray(candidate)) {
				candidate = candidate.length ? candidate[0] : null;
			}
			if (candidate && typeof candidate === "object") {
				candidate = candidate.url || candidate.uri || null;
			}
			if (typeof candidate !== "string" || !candidate.trim()) {
				continue;
			}

			var value = candidate.trim();
			if (value.indexOf("spotify:image:") === 0) {
				value = "https://i.scdn.co/image/" + value.slice("spotify:image:".length);
			}
			if (/^https?:\/\//i.test(value)) {
				return value;
			}
		}

		return null;
	}

	function readContext(data) {
		if (!data) {
			return null;
		}
		var context = data.context || data.contextInfo || {};
		var meta = data.context_metadata || data.contextMetadata || context.metadata || {};
		var uri = data.context_uri || data.contextUri || meta.context_uri || meta.contextUri
			|| context.uri || context.context_uri || context.contextUri || null;
		var name = meta.name || meta.title || meta.context_description
			|| context.name || context.title || data.context_name || data.contextName || null;
		if (!uri && !name) {
			return null;
		}
		return {
			uri: uri,
			name: name,
			type: uri ? String(uri).split(":")[1] || null : null
		};
	}

	function readClient(data) {
		var platform = (Spicetify.Platform && Spicetify.Platform.PlatformData) || {};
		var origin = (data && data.play_origin) || {};

		return {
			platform: platform.app_platform || null,
			deviceName: origin.device_identifier || null
		};
	}

	function readCapabilities() {
		return {
			player: !!(Spicetify.Player && Spicetify.Player.seek),
			playerApi: !!(Spicetify.Platform && Spicetify.Platform.PlayerAPI),
			cosmos: !!(Spicetify.CosmosAsync && Spicetify.CosmosAsync.get),
			history: !!(Spicetify.Platform && Spicetify.Platform.History && Spicetify.Platform.History.push),
			queue: !!(Spicetify.Queue && typeof Spicetify.Queue.get === "function"),
			removeFromQueue: !!(Spicetify.removeFromQueue || (Spicetify.Platform && Spicetify.Platform.PlayerAPI && Spicetify.Platform.PlayerAPI.removeFromQueue)),
			clearQueue: !!(Spicetify.Platform && Spicetify.Platform.PlayerAPI && Spicetify.Platform.PlayerAPI.clearQueue),
			trackLikeStatus: !!(Spicetify.Player && typeof Spicetify.Player.getHeart === "function")
		};
	}

	function readProgressFraction() {
		var value = number(call(Spicetify.Player, "getProgressPercent"));
		return value === null ? null : value;
	}

	function readVolumePercent() {
		var volume = call(Spicetify.Player, "getVolume");
		if (typeof volume !== "number" || isNaN(volume)) {
			return null;
		}
		return Math.round(Math.max(0, Math.min(1, volume)) * 100);
	}

	function readRepeatMode() {
		var mode = call(Spicetify.Player, "getRepeat");
		if (mode === 2) {
			return "track";
		}
		if (mode === 1) {
			return "context";
		}
		if (mode === 0) {
			return "off";
		}
		return null;
	}

	// ── Command execution ───────────────────────────────────────────────────

	var HANDLERS = {
		ping: function () {
			return "pong";
		},
		play: function () {
			Spicetify.Player.play();
		},
		pause: function () {
			Spicetify.Player.pause();
		},
		togglePlay: function () {
			Spicetify.Player.togglePlay();
		},
		next: function () {
			Spicetify.Player.next();
		},
		previous: function () {
			Spicetify.Player.back();
		},
		restartTrack: function () {
			Spicetify.Player.seek(0);
		},
		seekTo: function (payload) {
			return seekTo(payload.positionMs);
		},
		skipForward: function (payload) {
			Spicetify.Player.skipForward(num(payload.deltaMs, 10000));
		},
		skipBackward: function (payload) {
			Spicetify.Player.skipBack(num(payload.deltaMs, 10000));
		},
		setVolume: function (payload) {
			Spicetify.Player.setVolume(clamp01(num(payload.volumePercent, 50) / 100));
		},
		setMute: function (payload) {
			Spicetify.Player.setMute(payload.muted === true);
		},
		toggleMute: function () {
			Spicetify.Player.toggleMute();
		},
		setShuffle: function (payload) {
			Spicetify.Player.setShuffle(payload.enabled === true);
		},
		setRepeat: function (payload) {
			Spicetify.Player.setRepeat(repeatToNumber(payload.mode));
		},
		addToQueue: function (payload) {
			var uri = String(payload.uri || "");
			require(uri, "a track URI");
			if (typeof Spicetify.addToQueue === "function") {
				return Spicetify.addToQueue([{ uri: uri }]);
			}
			return require(Spicetify.Platform && Spicetify.Platform.PlayerAPI && Spicetify.Platform.PlayerAPI.addToQueue, "queue control")
				.call(Spicetify.Platform.PlayerAPI, [{ uri: uri }]);
		},
		removeFromQueue: function (payload) {
			var uri = String(payload.uri || "");
			require(uri, "a track URI");
			var api = Spicetify.Platform && Spicetify.Platform.PlayerAPI;
			if (typeof Spicetify.removeFromQueue === "function") {
				return Spicetify.removeFromQueue([{ uri: uri }]);
			}
			return require(api && api.removeFromQueue, "queue removal").call(api, [{ uri: uri }]);
		},
		clearQueue: function () {
			var api = require(Spicetify.Platform && Spicetify.Platform.PlayerAPI, "player control");
			return require(api.clearQueue, "queue clearing").call(api);
		},
		setLiked: function (payload) {
			Spicetify.Player.setHeart(payload.liked === true);
		},
		toggleLiked: function () {
			Spicetify.Player.toggleHeart();
		},
		playUri: function (payload) {
			var uri = String(payload.uri || "");
			require(uri, "a URI");
			return Spicetify.Player.playUri(uri, payload.context || {}, payload.options || {});
		},
		openUri: function (payload) {
			return openUri(String(payload.uri || ""));
		},
	};

	function require(value, what) {
		if (!value) {
			throw new Error("This Spotify version does not expose " + what + " to the bridge.");
		}
		return value;
	}

	function num(value, fallback) {
		var parsed = typeof value === "number" ? value : parseInt(value, 10);
		return typeof parsed === "number" && !isNaN(parsed) ? parsed : fallback;
	}

	function clamp01(value) {
		if (isNaN(value)) {
			return 0;
		}
		return Math.max(0, Math.min(1, value));
	}

	function repeatToNumber(mode) {
		if (mode === "track") {
			return 2;
		}
		if (mode === "context") {
			return 1;
		}
		return 0;
	}

	function readPosition() {
		return num(call(Spicetify.Player, "getProgress"), 0);
	}

	function seekTo(positionMs) {
		var target = Math.max(0, num(positionMs, 0));
		if (Spicetify.Player && typeof Spicetify.Player.seek === "function") {
			return Spicetify.Player.seek(target);
		}

		var api = Spicetify.Platform && Spicetify.Platform.PlayerAPI;
		return require(api && api.seekTo, "seeking").call(api, target);
	}

	var OPEN_PATHS = {
		artist: "artist",
		album: "album",
		playlist: "playlist",
		collection: "collection",
		show: "show",
		episode: "episode"
	};

	function openUri(uri) {
		var parts = uri.split(":");
		if (parts.length < 3) {
			throw new Error("'" + uri + "' is not a Spotify URI.");
		}
		var kind = parts[parts.length - 2];
		var id = parts[parts.length - 1];
		var section = OPEN_PATHS[kind];
		if (!section) {
			throw new Error(
				kind === "track"
					? "A track has no page of its own. Open the current album instead."
					: "A " + kind + " cannot be opened from a deck button."
			);
		}
		var history = require(Spicetify.Platform && Spicetify.Platform.History, "the client router");
		history.push("/" + section + "/" + id);
		return kind;
	}

	function runCommands(commands) {
		var chain = Promise.resolve();

		commands.forEach(function (command) {
			chain = chain.then(function () {
				var handler = HANDLERS[command.kind];
				if (!handler) {
					pendingResults[command.id] = { ok: false, error: "Unknown command '" + command.kind + "'." };
					return null;
				}
				return withTimeout(
					Promise.resolve()
						.then(function () {
							return handler(command.payload || {});
						})
						.then(function (value) {
							pendingResults[command.id] = { ok: true };
							return value;
						})
						.catch(function (error) {
							pendingResults[command.id] = {
								ok: false,
								error: String((error && error.message) || error)
							};
						}),
					COMMAND_TIMEOUT_MS
				);
			});
		});

		return chain;
	}

	function withTimeout(promise, milliseconds) {
		return new Promise(function (resolve) {
			var settled = false;
			var timer = setTimeout(function () {
				if (!settled) {
					settled = true;
					resolve();
				}
			}, milliseconds);
			promise.then(function (value) {
				if (!settled) {
					settled = true;
					clearTimeout(timer);
					resolve(value);
				}
			});
		});
	}

	window.SpicetifyDeck.handlers = HANDLERS;
	window.SpicetifyDeck.capabilities = readCapabilities;

	waitForSpicetify(0);
})();
