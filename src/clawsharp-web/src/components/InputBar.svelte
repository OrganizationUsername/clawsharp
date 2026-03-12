<script lang="ts">
  import type { WsStatus } from '../lib/types.js';

  interface Props {
    status: WsStatus;
    waiting: boolean;
    onSend: (text: string) => void;
  }

  const { status, waiting, onSend }: Props = $props();

  let text = $state('');

  const canSend = $derived(
    status === 'connected' && !waiting && text.trim().length > 0,
  );

  function send() {
    const trimmed = text.trim();
    if (!canSend || !trimmed) return;
    onSend(trimmed);
    text = '';
  }

  function onKeydown(e: KeyboardEvent) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      send();
    }
  }
</script>

<div class="input-area">
  <div class="input-row">
    <textarea
      placeholder="Message clawsharp…"
      rows="1"
      bind:value={text}
      onkeydown={onKeydown}
      disabled={status !== 'connected'}
    ></textarea>
    <button class="send-btn" onclick={send} disabled={!canSend}>
      <span>↑</span> Send
    </button>
  </div>
  <div class="footer-hint">
    <kbd>Enter</kbd> to send &nbsp;·&nbsp; <kbd>Shift+Enter</kbd> for newline
  </div>
</div>

<style>
  .input-area {
    border-top: 1px solid var(--border);
    background: var(--surface);
    padding: 14px 20px;
    flex-shrink: 0;
    display: flex;
    flex-direction: column;
    gap: 10px;
  }

  .input-row {
    display: flex;
    gap: 10px;
    align-items: flex-end;
  }

  textarea {
    flex: 1;
    background: var(--bg);
    border: 1px solid var(--border-hi);
    border-radius: var(--radius);
    color: var(--text);
    font-family: var(--font-mono);
    font-size: 13px;
    padding: 10px 14px;
    resize: none;
    outline: none;
    min-height: 42px;
    max-height: 180px;
    line-height: 1.6;
    transition:
      border-color 0.2s,
      box-shadow 0.2s;
    field-sizing: content;
  }

  textarea:focus {
    border-color: var(--accent);
    box-shadow: 0 0 0 3px var(--accent-lo);
  }

  textarea::placeholder {
    color: var(--muted);
  }

  textarea:disabled {
    opacity: 0.5;
  }

  .send-btn {
    background: var(--accent);
    color: #fff;
    border: none;
    border-radius: var(--radius);
    font-family: var(--font-mono);
    font-size: 12px;
    padding: 10px 18px;
    cursor: pointer;
    flex-shrink: 0;
    height: 42px;
    transition:
      background 0.15s,
      transform 0.1s,
      opacity 0.15s;
    display: flex;
    align-items: center;
    gap: 6px;
  }

  .send-btn:hover:not(:disabled) {
    background: var(--accent-hi);
  }

  .send-btn:active:not(:disabled) {
    transform: scale(0.97);
  }

  .send-btn:disabled {
    opacity: 0.4;
    cursor: not-allowed;
  }

  .footer-hint {
    font-size: 10px;
    color: #2e2e3e;
    text-align: center;
  }

  .footer-hint kbd {
    background: #1a1a22;
    border: 1px solid var(--border-hi);
    border-radius: 3px;
    padding: 1px 5px;
    font-family: var(--font-mono);
    color: var(--muted);
  }

  @media (max-width: 600px) {
    .input-area {
      padding: 10px 14px;
    }
  }
</style>
