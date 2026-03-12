<script lang="ts">
  import type { Message } from '../lib/types.js';
  import MessageBubble from './Message.svelte';

  interface Props {
    messages: Message[];
    thinking: boolean;
  }

  const { messages, thinking }: Props = $props();

  let listEl = $state<HTMLDivElement | null>(null);

  // Scroll to bottom whenever messages change or thinking state changes
  $effect(() => {
    // Depend on messages array length and thinking flag
    const _len = messages.length;
    const _t = thinking;
    // Also depend on the last message text for streaming updates
    const _lastText = messages.at(-1)?.text ?? '';
    void _len;
    void _t;
    void _lastText;
    if (listEl) {
      listEl.scrollTop = listEl.scrollHeight;
    }
  });
</script>

<div class="messages" bind:this={listEl}>
  {#each messages as msg (msg.id)}
    <MessageBubble message={msg} />
  {/each}

  {#if thinking}
    <div class="thinking-bar">
      <div class="thinking-dots">
        <span></span>
        <span></span>
        <span></span>
      </div>
      <span class="thinking-label">thinking…</span>
    </div>
  {/if}
</div>

<style>
  .messages {
    flex: 1;
    overflow-y: auto;
    padding: 24px 0;
    scroll-behavior: smooth;
  }

  .messages::-webkit-scrollbar {
    width: 4px;
  }

  .messages::-webkit-scrollbar-track {
    background: transparent;
  }

  .messages::-webkit-scrollbar-thumb {
    background: var(--border-hi);
    border-radius: 2px;
  }

  .thinking-bar {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 6px 24px 0;
  }

  .thinking-dots {
    display: flex;
    gap: 3px;
  }

  .thinking-dots span {
    display: inline-block;
    width: 5px;
    height: 5px;
    border-radius: 50%;
    background: var(--accent);
    opacity: 0.3;
    animation: pulse 1.2s ease infinite;
  }

  .thinking-dots span:nth-child(2) {
    animation-delay: 0.2s;
  }

  .thinking-dots span:nth-child(3) {
    animation-delay: 0.4s;
  }

  @keyframes pulse {
    0%,
    80%,
    100% {
      opacity: 0.15;
      transform: scale(0.8);
    }
    40% {
      opacity: 0.9;
      transform: scale(1.1);
    }
  }

  .thinking-label {
    font-size: 11px;
    color: var(--muted);
  }

  @media (max-width: 600px) {
    .messages {
      padding: 16px 0;
    }
  }
</style>
