<script lang="ts">
  import type { WsStatus } from '../lib/types.js';

  interface Props {
    status: WsStatus;
    onNewChat: () => void;
  }

  const { status, onNewChat }: Props = $props();

  const statusLabel = $derived(
    status === 'connected'
      ? 'connected'
      : status === 'connecting'
        ? 'connecting…'
        : status === 'error'
          ? 'error'
          : 'disconnected',
  );
</script>

<header>
  <div class="logo">claw<em>sharp</em></div>
  <div class="spacer"></div>
  <div class="actions">
    <button class="new-btn" onclick={onNewChat} title="Start new conversation">+ New</button>
    <span class="status-label">{statusLabel}</span>
    <div class="status-dot {status}"></div>
  </div>
</header>

<style>
  header {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 12px 20px;
    border-bottom: 1px solid var(--border);
    background: var(--surface);
    flex-shrink: 0;
  }

  .logo {
    font-family: var(--font-serif);
    font-size: 19px;
    color: var(--text);
    letter-spacing: -0.02em;
  }

  .logo em {
    color: var(--accent-hi);
    font-style: italic;
  }

  .spacer {
    flex: 1;
  }

  .actions {
    display: flex;
    align-items: center;
    gap: 10px;
  }

  .new-btn {
    background: transparent;
    border: 1px solid var(--border-hi);
    border-radius: 6px;
    color: var(--muted);
    font-family: var(--font-mono);
    font-size: 11px;
    padding: 5px 10px;
    cursor: pointer;
    transition:
      border-color 0.2s,
      color 0.2s;
  }

  .new-btn:hover {
    border-color: var(--accent);
    color: var(--accent-hi);
  }

  .status-label {
    font-size: 11px;
    color: var(--muted);
  }

  .status-dot {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: var(--muted);
    transition: background 0.3s;
  }

  .status-dot.connected {
    background: var(--green);
    box-shadow: 0 0 6px var(--green);
  }

  .status-dot.error {
    background: var(--red);
  }

  .status-dot.connecting {
    background: var(--amber);
    animation: statusPulse 1s ease infinite;
  }

  @keyframes statusPulse {
    0%,
    100% {
      opacity: 1;
    }
    50% {
      opacity: 0.4;
    }
  }

  @media (max-width: 600px) {
    header {
      padding: 10px 14px;
    }

    .logo {
      font-size: 17px;
    }
  }
</style>
