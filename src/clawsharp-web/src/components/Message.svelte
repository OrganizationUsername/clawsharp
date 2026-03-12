<script lang="ts">
  import type { Message } from '../lib/types.js';
  import { renderMarkdown } from '../lib/markdown.js';

  interface Props {
    message: Message;
  }

  const { message }: Props = $props();

  const timeStr = $derived(
    new Date(message.time).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
  );

  const avatarLabel = $derived(
    message.role === 'user' ? 'YOU' : message.role === 'agent' ? 'AI' : '·',
  );

  const roleName = $derived(
    message.role === 'user' ? 'You' : message.role === 'agent' ? 'Agent' : 'System',
  );

  const rendered = $derived(
    message.role === 'system' ? message.text : renderMarkdown(message.text),
  );
</script>

<div class="msg {message.role}">
  <div class="msg-avatar">{avatarLabel}</div>
  <div class="msg-body">
    <div class="msg-meta">
      <span class="msg-name">{roleName}</span>
      <span class="msg-time">{timeStr}</span>
    </div>
    {#if message.role === 'system'}
      <div class="msg-text">{message.text}</div>
    {:else if message.role === 'agent'}
      <!-- eslint-disable-next-line svelte/no-at-html-tags -->
      <div class="msg-text {message.streaming ? 'cursor' : ''}">{@html rendered}</div>
    {:else}
      <div class="msg-text">{message.text}</div>
    {/if}
  </div>
</div>

<style>
  .msg {
    display: flex;
    gap: 14px;
    padding: 10px 24px;
    animation: fadeSlide 0.18s ease;
  }

  .msg:hover {
    background: #ffffff04;
  }

  @keyframes fadeSlide {
    from {
      opacity: 0;
      transform: translateY(6px);
    }
    to {
      opacity: 1;
      transform: none;
    }
  }

  .msg-avatar {
    width: 28px;
    height: 28px;
    border-radius: 6px;
    flex-shrink: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 11px;
    font-weight: 600;
    letter-spacing: 0.05em;
    margin-top: 1px;
  }

  .msg.user .msg-avatar {
    background: var(--accent-lo);
    color: var(--accent-hi);
    border: 1px solid #7c6af744;
  }

  .msg.agent .msg-avatar {
    background: #1a1a20;
    color: var(--muted);
    border: 1px solid var(--border-hi);
  }

  .msg.system .msg-avatar {
    background: transparent;
    color: var(--muted);
    border: 1px dashed var(--border-hi);
  }

  .msg-body {
    flex: 1;
    min-width: 0;
  }

  .msg-meta {
    display: flex;
    align-items: baseline;
    gap: 8px;
    margin-bottom: 3px;
  }

  .msg-name {
    font-size: 11px;
    font-weight: 600;
    color: var(--muted);
    text-transform: uppercase;
    letter-spacing: 0.08em;
  }

  .msg.user .msg-name {
    color: var(--accent);
  }

  .msg-time {
    font-size: 10px;
    color: #3a3a4a;
  }

  .msg-text {
    color: var(--text);
    word-break: break-word;
  }

  .msg.system .msg-text {
    color: var(--muted);
    font-style: italic;
    font-size: 12px;
  }

  /* Streaming cursor */
  :global(.msg-text.cursor::after) {
    content: '▍';
    animation: blink 0.9s step-end infinite;
    color: var(--accent);
  }

  @keyframes blink {
    0%,
    100% {
      opacity: 1;
    }
    50% {
      opacity: 0;
    }
  }

  /* Markdown rendered output */
  :global(.msg-text p) {
    margin: 0 0 6px;
  }

  :global(.msg-text p:last-child) {
    margin-bottom: 0;
  }

  :global(.msg-text h1),
  :global(.msg-text h2),
  :global(.msg-text h3) {
    font-family: var(--font-serif);
    font-weight: normal;
    margin: 10px 0 4px;
    color: var(--text);
  }

  :global(.msg-text h1) {
    font-size: 18px;
  }

  :global(.msg-text h2) {
    font-size: 15px;
  }

  :global(.msg-text h3) {
    font-size: 13px;
    color: var(--accent-hi);
  }

  :global(.msg-text ul),
  :global(.msg-text ol) {
    padding-left: 20px;
    margin: 4px 0 8px;
  }

  :global(.msg-text li) {
    margin-bottom: 2px;
  }

  :global(.msg-text hr) {
    border: none;
    border-top: 1px solid var(--border);
    margin: 10px 0;
  }

  :global(.msg-text strong) {
    color: var(--text);
    font-weight: 600;
  }

  :global(.msg-text em) {
    color: var(--accent-hi);
    font-style: italic;
  }

  :global(.msg-text a) {
    color: var(--accent-hi);
    text-decoration: none;
  }

  :global(.msg-text a:hover) {
    text-decoration: underline;
  }

  :global(.msg-text code) {
    background: #1c1c24;
    border: 1px solid var(--border-hi);
    border-radius: 4px;
    padding: 1px 5px;
    font-size: 12px;
    color: var(--accent-hi);
  }

  :global(.msg-text pre) {
    background: #0e0e14;
    border: 1px solid var(--border);
    border-left: 3px solid var(--accent);
    border-radius: 6px;
    padding: 12px 14px;
    overflow-x: auto;
    margin: 8px 0;
    position: relative;
  }

  :global(.msg-text pre code) {
    background: none;
    border: none;
    padding: 0;
    color: #c9c9e0;
    font-size: 12px;
  }

  :global(.copy-btn) {
    position: absolute;
    top: 6px;
    right: 8px;
    background: var(--border-hi);
    border: none;
    border-radius: 4px;
    color: var(--muted);
    font-family: var(--font-mono);
    font-size: 10px;
    padding: 2px 7px;
    cursor: pointer;
    opacity: 0;
    transition: opacity 0.2s;
  }

  :global(.msg-text pre:hover .copy-btn) {
    opacity: 1;
  }

  :global(.copy-btn:hover) {
    background: var(--accent);
    color: #fff;
  }

  @media (max-width: 600px) {
    .msg {
      padding: 8px 14px;
      gap: 10px;
    }

    .msg-avatar {
      width: 24px;
      height: 24px;
      font-size: 9px;
    }

    :global(.msg-text pre) {
      font-size: 11px;
    }
  }
</style>
