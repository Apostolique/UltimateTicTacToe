// A relay for two-player games. It pairs clients into rooms and forwards whatever they say
// to the other side without reading it, so the rules live entirely in the game.
//
// The wire format is one line of text per message, words separated by spaces. The protocol
// is small enough that a line beats a serializer: there's nothing to version and
// `websocat ws://localhost:8787/ws` is a working client.
//
//   in  : host | join <code> | queue | cancel | leave | msg <rest...>
//   out : hosted <code> | joined <code> host|guest | matched <code> host|guest | searching
//         cancelled | peer in | peer out | msg <rest...> | error <reason>

// No 0/O/1/I/L, so a code read aloud or typed off a screenshot can't be ambiguous.
const ALPHABET = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';
const CODE_LENGTH = 5;

/**
 * Every connection lands in this one object, because matchmaking needs all the waiting
 * players in one place and a room needs both its seats in one place. A Durable Object is
 * single threaded, so the pairing below needs no locking and can't race.
 *
 * Sockets are held in memory rather than hibernated: the object only has to stay awake
 * while somebody is actually connected, which for a game lobby is exactly when you want it.
 */
export class Relay {
  constructor(state, env) {
    this.state = state;
    this.env = env;
    /** @type {Map<string, {host: WebSocket|null, guest: WebSocket|null}>} */
    this.rooms = new Map();
    /** @type {WebSocket[]} */
    this.queue = [];
    /** @type {Map<WebSocket, {room: string|null, queued: boolean}>} */
    this.peers = new Map();
  }

  async fetch(request) {
    const url = new URL(request.url);

    if (url.pathname === '/') {
      return new Response(
        `ultimatetictactoe relay: ${this.rooms.size} rooms, ${this.queue.length} queued\n`,
        { headers: { 'content-type': 'text/plain' } },
      );
    }

    if (request.headers.get('Upgrade') !== 'websocket') {
      return new Response('expected a websocket upgrade', { status: 426 });
    }

    const [client, server] = Object.values(new WebSocketPair());
    server.accept();
    this.peers.set(server, { room: null, queued: false });

    server.addEventListener('message', (event) => {
      if (typeof event.data !== 'string') return;
      try {
        this.handle(server, event.data.trim());
      } catch (err) {
        this.send(server, `error ${err.message}`);
      }
    });
    const drop = () => this.drop(server);
    server.addEventListener('close', drop);
    server.addEventListener('error', drop);

    return new Response(null, { status: 101, webSocket: client });
  }

  handle(ws, line) {
    // Split once: everything after the verb is payload, and `msg` keeps its spaces.
    const space = line.indexOf(' ');
    const verb = space < 0 ? line : line.slice(0, space);
    const rest = space < 0 ? '' : line.slice(space + 1);

    switch (verb) {
      case 'host': return this.host(ws);
      case 'join': return this.join(ws, rest);
      case 'queue': return this.enqueue(ws);
      case 'cancel': return this.cancel(ws);
      case 'leave': return this.leave(ws);
      case 'msg': return this.relay(ws, rest);
      default: return this.send(ws, 'error unknown command');
    }
  }

  host(ws) {
    this.leave(ws, { quiet: true });

    const code = this.newCode();
    this.rooms.set(code, { host: ws, guest: null });
    this.peerState(ws).room = code;
    this.send(ws, `hosted ${code}`);
  }

  join(ws, rawCode) {
    const code = rawCode.trim().toUpperCase();
    const room = this.rooms.get(code);

    if (!room) return this.send(ws, 'error no game with that code');
    if (room.host && room.guest) return this.send(ws, 'error that game is full');

    this.leave(ws, { quiet: true });

    // A room whose host left keeps its code, so the open seat may be either one. Which one
    // decides who plays first, and the client has no way to work that out for itself: assume
    // guest and two players who both took over a host seat will both wait for the other.
    const seat = room.host ? 'guest' : 'host';
    if (seat === 'host') room.host = ws;
    else room.guest = ws;
    this.peerState(ws).room = code;

    this.send(ws, `joined ${code} ${seat}`);
    const other = this.other(room, ws);
    if (other) {
      this.send(other, 'peer in');
      this.send(ws, 'peer in');
    }
  }

  enqueue(ws) {
    this.leave(ws, { quiet: true });

    // Pair with whoever has been waiting longest. With one object holding the queue there's
    // nothing to arbitrate: no symmetry break and no propose/accept handshake.
    let partner = null;
    while (this.queue.length > 0 && !partner) {
      const candidate = this.queue.shift();
      const state = this.peers.get(candidate);
      if (state) state.queued = false;
      if (state && candidate !== ws && candidate.readyState === WebSocket.READY_STATE_OPEN) partner = candidate;
    }

    if (!partner) {
      this.peerState(ws).queued = true;
      this.queue.push(ws);
      return this.send(ws, 'searching');
    }

    const code = this.newCode();
    this.rooms.set(code, { host: partner, guest: ws });
    this.peerState(partner).room = code;
    this.peerState(ws).room = code;

    this.send(partner, `matched ${code} host`);
    this.send(ws, `matched ${code} guest`);
  }

  cancel(ws) {
    this.dequeue(ws);
    this.send(ws, 'cancelled');
  }

  leave(ws, { quiet = false } = {}) {
    this.dequeue(ws);

    const state = this.peers.get(ws);
    if (!state || !state.room) return;

    const room = this.rooms.get(state.room);
    state.room = null;
    if (!room) return;

    const other = this.other(room, ws);
    if (room.host === ws) room.host = null;
    if (room.guest === ws) room.guest = null;

    // The code stays alive while anyone is still sitting in it, so an opponent who dropped
    // can reconnect with the same code instead of the host minting a new one.
    if (!room.host && !room.guest) {
      for (const [code, r] of this.rooms) if (r === room) this.rooms.delete(code);
    }
    if (other && !quiet) this.send(other, 'peer out');
  }

  drop(ws) {
    this.leave(ws);
    this.peers.delete(ws);
  }

  relay(ws, payload) {
    const state = this.peers.get(ws);
    const room = state && state.room ? this.rooms.get(state.room) : null;
    const other = room ? this.other(room, ws) : null;
    if (other) this.send(other, `msg ${payload}`);
  }

  dequeue(ws) {
    const state = this.peers.get(ws);
    if (!state || !state.queued) return;
    state.queued = false;
    const i = this.queue.indexOf(ws);
    if (i >= 0) this.queue.splice(i, 1);
  }

  peerState(ws) {
    let state = this.peers.get(ws);
    if (!state) {
      state = { room: null, queued: false };
      this.peers.set(ws, state);
    }
    return state;
  }

  other(room, ws) {
    return room.host === ws ? room.guest : room.host;
  }

  send(ws, line) {
    try {
      ws.send(line);
    } catch {
      // The peer is gone; its own close handler will clean up.
    }
  }

  newCode() {
    for (;;) {
      const bytes = crypto.getRandomValues(new Uint8Array(CODE_LENGTH));
      let code = '';
      for (const b of bytes) code += ALPHABET[b % ALPHABET.length];
      if (!this.rooms.has(code)) return code;
    }
  }
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname !== '/ws' && url.pathname !== '/') {
      return new Response('not found', { status: 404 });
    }
    // One well-known instance: the queue and the room list have to agree with each other.
    return env.RELAY.get(env.RELAY.idFromName('hub')).fetch(request);
  },
};
