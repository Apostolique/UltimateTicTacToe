# Relay

The server that puts two players in the same game. It hands out codes, runs the matchmaking
queue, and forwards messages between the two seats. It never reads a game message, so the
rules live entirely in the game and this never needs to change when they do.

It's a Cloudflare Worker with one Durable Object. Every connection lands in that one object,
because matchmaking needs all the waiting players in one place and a room needs both of its
seats in one place. A Durable Object runs single threaded, so the pairing needs no locking
and can't race. That's also its ceiling: one object handles every game. For this traffic,
a few messages per game, that's nowhere close to a problem.

## Run it locally

```
cd Server
npm install
npm run dev
```

That serves on `http://127.0.0.1:8090`, with the websocket at `/ws` and a health page at `/`.
Point the game at it by setting `RelayUrl` to `ws://127.0.0.1:8090/ws` in the `Settings.json`
next to the executable.

Use `127.0.0.1` rather than `localhost`. The name resolves to IPv6 first, wrangler only
listens on IPv4, and the failed attempt costs about two seconds on every connect.

## Deploy it

```
cd Server
npx wrangler login
npm run deploy
```

It lands on `https://ultimatetictactoe-relay.apostolique.workers.dev`, and the game is already
pointed at the websocket form of that in `RelayUrl` in `Game/Settings.cs`. That value has to
live in the source rather than a config file because the browser build has nowhere to keep
one. Deploying under a different account means changing it there.

Pushing anything under `Server/` to `main` deploys it through `.github/workflows/relay.yml`,
which needs a `CLOUDFLARE_API_TOKEN` secret. `npm run tail` streams live logs.

## Protocol

One line of text per message, words separated by spaces. It's small enough that a line beats
a serializer: there's nothing to version, and `websocat ws://127.0.0.1:8090/ws` is a working
client.

Client to relay:

| Line | Meaning |
| --- | --- |
| `host` | Mint a code and hold a room open. |
| `join <code>` | Take the open seat in that room. Case insensitive. |
| `queue` | Wait for an opponent. |
| `cancel` | Stop waiting. |
| `leave` | Give up the current seat. |
| `msg <rest...>` | Forward the rest verbatim to the other seat. |

Relay to client:

| Line | Meaning |
| --- | --- |
| `hosted <code>` | The room is open under this code. |
| `joined <code>` | You have a seat. An opponent may or may not be in the other one. |
| `matched <code> host\|guest` | Matchmaking paired you. Both seats are filled already, so no `peer in` follows. |
| `searching` | You're in the queue. |
| `cancelled` | You're out of the queue. |
| `peer in` / `peer out` | The other seat filled or emptied. |
| `msg <rest...>` | The other seat said this. |
| `error <reason>` | The last command didn't work. |

A room keeps its code while anyone is still sitting in it, so an opponent who dropped can
reconnect with the same code instead of the host having to mint a new one.

The game's own messages ride inside `msg`, and the relay never looks at them: `play <macro>
<micro>`, `reset`, and `hover <macro> <micro>` with `-1 -1` for nothing. Hover is a cell
rather than a position so it lands on the right square whatever size the other window is.
