<script lang="ts">
  import type { Message, WsStatus } from './lib/types.js';
  import { loadHistory, saveHistoryEntry, clearHistory } from './lib/history.js';
  import PairOverlay from './components/PairOverlay.svelte';
  import Header from './components/Header.svelte';
  import MessageList from './components/MessageList.svelte';
  import InputBar from './components/InputBar.svelte';

  // ── Constants ────────────────────────────────────────────────────────────────
  const TOKEN_KEY = 'cws_token';
  const RECONNECT_DELAYS = [1000, 2000, 5000, 10000, 30000];

  // ── State ────────────────────────────────────────────────────────────────────
  let token = $state(localStorage.getItem(TOKEN_KEY) ?? '');
  let paired = $derived(token.length > 0);

  let messages = $state<Message[]>([]);
  let wsStatus = $state<WsStatus>('disconnected');
  let thinking = $state(false);
  let waiting = $state(false);

  // WebSocket bookkeeping — not reactive (no need to track in DOM)
  let ws: WebSocket | null = null;
  let reconnectN = 0;
  let reconnectTimer: ReturnType<typeof setTimeout> | null = null;

  // Streaming state
  let streamingId: string | null = null;
  let streamBuf = '';

  // ── ID helper ────────────────────────────────────────────────────────────────
  function uid(): string {
    return Math.random().toString(36).slice(2, 10);
  }

  // ── Lifecycle — init on mount ────────────────────────────────────────────────
  $effect(() => {
    if (paired) {
      replayHistory();
      connect();
    }
    // Cleanup on destroy
    return () => {
      if (reconnectTimer !== null) clearTimeout(reconnectTimer);
      ws?.close();
    };
  });

  // ── History replay ───────────────────────────────────────────────────────────
  function replayHistory() {
    const hist = loadHistory();
    if (hist.length === 0) return;
    const replayed: Message[] = hist.map((h) => ({
      id: uid(),
      role: h.role,
      text: h.text,
      time: h.time,
    }));
    messages = [...replayed, systemMsg('— previous conversation —')];
  }

  // ── Message helpers ──────────────────────────────────────────────────────────
  function systemMsg(text: string): Message {
    return { id: uid(), role: 'system', text, time: new Date().toISOString() };
  }

  function addMessage(msg: Message) {
    messages = [...messages, msg];
  }

  function appendSystem(text: string) {
    addMessage(systemMsg(text));
  }

  // ── WebSocket ────────────────────────────────────────────────────────────────
  let wsAuthenticated = false;

  function connect() {
    if (reconnectTimer !== null) {
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }
    wsStatus = 'connecting';
    wsAuthenticated = false;

    // S4: No token in URL — use first-frame auth protocol instead
    const proto = location.protocol === 'https:' ? 'wss' : 'ws';
    ws = new WebSocket(`${proto}://${location.host}/ws`);

    ws.onopen = () => {
      // Send auth frame as first message
      ws!.send(JSON.stringify({ type: 'auth', token }));
    };

    ws.onclose = () => {
      wsStatus = 'disconnected';
      wsAuthenticated = false;
      ws = null;
      finalizeStream();

      const delay = RECONNECT_DELAYS[Math.min(reconnectN, RECONNECT_DELAYS.length - 1)];
      reconnectN++;
      appendSystem(`Disconnected. Reconnecting in ${delay / 1000}s…`);
      reconnectTimer = setTimeout(connect, delay);
    };

    ws.onerror = () => {
      wsStatus = 'error';
    };

    ws.onmessage = (event: MessageEvent<string>) => {
      const data = event.data;

      // End-of-stream sentinel
      if (data === '[DONE]') {
        finalizeStream();
        return;
      }

      // Parse JSON frame
      let parsed: Record<string, unknown>;
      try {
        parsed = JSON.parse(data) as Record<string, unknown>;
      } catch {
        return;
      }

      // Handle auth responses (first-frame protocol)
      if (parsed['type'] === 'auth_ok') {
        wsAuthenticated = true;
        reconnectN = 0;
        wsStatus = 'connected';
        return;
      }
      if (parsed['type'] === 'auth_error') {
        wsStatus = 'error';
        // Clear stored token — it's invalid
        localStorage.removeItem(TOKEN_KEY);
        token = '';
        ws?.close();
        return;
      }

      if (typeof parsed['delta'] === 'string') {
        // Streaming delta: {"delta":"..."}
        thinking = false;
        if (streamingId === null) {
          const newMsg: Message = {
            id: uid(),
            role: 'agent',
            text: '',
            time: new Date().toISOString(),
            streaming: true,
          };
          streamingId = newMsg.id;
          streamBuf = '';
          addMessage(newMsg);
        }
        streamBuf += parsed['delta'];
        // Update the streaming message in place
        messages = messages.map((m) =>
          m.id === streamingId ? { ...m, text: streamBuf } : m,
        );
      } else if (typeof parsed['text'] === 'string') {
        // Non-streaming full reply: {"text":"..."}
        finalizeStream();
        const fullMsg: Message = {
          id: uid(),
          role: 'agent',
          text: parsed['text'],
          time: new Date().toISOString(),
        };
        addMessage(fullMsg);
        saveHistoryEntry('agent', parsed['text']);
      }
    };
  }

  // ── Finalize streaming message ───────────────────────────────────────────────
  function finalizeStream() {
    if (streamingId !== null) {
      const finalText = streamBuf;
      // Mark the streaming message as done
      messages = messages.map((m) =>
        m.id === streamingId ? { ...m, text: finalText, streaming: false } : m,
      );
      saveHistoryEntry('agent', finalText);
      streamingId = null;
      streamBuf = '';
    }
    thinking = false;
    waiting = false;
  }

  // ── Send ─────────────────────────────────────────────────────────────────────
  function handleSend(text: string) {
    if (!text || ws === null || ws.readyState !== WebSocket.OPEN || waiting) return;

    const userMsg: Message = {
      id: uid(),
      role: 'user',
      text,
      time: new Date().toISOString(),
    };
    addMessage(userMsg);
    saveHistoryEntry('user', text);

    // Server expects raw text
    ws.send(text);
    waiting = true;
    thinking = true;
  }

  // ── New chat ─────────────────────────────────────────────────────────────────
  function handleNewChat() {
    clearHistory();
    finalizeStream();
    messages = [];
    appendSystem('New conversation started.');
  }

  // ── Pairing callback ─────────────────────────────────────────────────────────
  function handlePaired(newToken: string) {
    localStorage.setItem(TOKEN_KEY, newToken);
    token = newToken;
    // paired becomes true via $derived, $effect will fire connect()
  }
</script>

<svelte:head>
  <link
    rel="preconnect"
    href="https://fonts.googleapis.com"
  />
  <link
    rel="preconnect"
    href="https://fonts.gstatic.com"
    crossorigin="anonymous"
  />
  <link
    href="https://fonts.googleapis.com/css2?family=Geist+Mono:wght@300;400;500;600&family=Instrument+Serif:ital@0;1&display=swap"
    rel="stylesheet"
  />
</svelte:head>

{#if !paired}
  <PairOverlay onPaired={handlePaired} />
{:else}
  <div class="layout">
    <Header status={wsStatus} onNewChat={handleNewChat} />
    <MessageList {messages} {thinking} />
    <InputBar status={wsStatus} {waiting} onSend={handleSend} />
  </div>
{/if}

<style>
  :global(*, *::before, *::after) {
    box-sizing: border-box;
    margin: 0;
    padding: 0;
  }

  :global(:root) {
    --bg: #0a0a0b;
    --surface: #111114;
    --border: #1e1e24;
    --border-hi: #2e2e38;
    --text: #e8e8ed;
    --muted: #5a5a6e;
    --accent: #7c6af7;
    --accent-lo: #7c6af718;
    --accent-hi: #a89dff;
    --green: #4ade80;
    --red: #f87171;
    --amber: #fbbf24;
    --radius: 10px;
    --font-mono: 'Geist Mono', 'Fira Code', monospace;
    --font-serif: 'Instrument Serif', Georgia, serif;
  }

  :global(html, body) {
    height: 100%;
    background: var(--bg);
    color: var(--text);
    font-family: var(--font-mono);
    font-size: 13px;
    line-height: 1.6;
    overflow: hidden;
  }

  :global(#app) {
    height: 100vh;
  }

  .layout {
    display: flex;
    flex-direction: column;
    height: 100vh;
  }
</style>
