<script lang="ts">
  interface Props {
    onPaired: (token: string) => void;
  }

  const { onPaired }: Props = $props();

  let code = $state('');
  let errorMsg = $state('');
  let busy = $state(false);

  async function submit() {
    if (!/^\d{6}$/.test(code.trim())) {
      errorMsg = 'Enter a 6-digit code.';
      return;
    }
    errorMsg = 'Connecting…';
    busy = true;
    try {
      const r = await fetch('/pair', {
        method: 'POST',
        headers: { 'X-Pairing-Code': code.trim() },
      });
      const data = (await r.json()) as { token?: string; error?: string };
      if (!r.ok || !data.token) {
        const err = data.error ?? '';
        errorMsg =
          err === 'invalid_code_or_locked_out'
            ? 'Invalid code or locked out. Try again.'
            : err === 'no_active_pairing_code'
              ? 'No active pairing code. Run clawsharp to generate one.'
              : err || 'Pairing failed.';
        return;
      }
      errorMsg = '';
      onPaired(data.token);
    } catch {
      errorMsg = 'Could not reach server.';
    } finally {
      busy = false;
    }
  }

  function onKeydown(e: KeyboardEvent) {
    if (e.key === 'Enter') submit();
  }
</script>

<div class="overlay">
  <div class="pair-title">claw<em>sharp</em></div>
  <p class="pair-subtitle">
    Enter the 6-digit pairing code shown in your terminal to connect.
  </p>
  <div class="pair-input-row">
    <input
      type="text"
      inputmode="numeric"
      maxlength="6"
      placeholder="000000"
      autocomplete="off"
      spellcheck="false"
      bind:value={code}
      onkeydown={onKeydown}
      disabled={busy}
    />
    <button class="pair-btn" onclick={submit} disabled={busy}>Connect</button>
  </div>
  <p class="pair-error">{errorMsg}</p>
</div>

<style>
  .overlay {
    position: fixed;
    inset: 0;
    background: var(--bg);
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 24px;
    z-index: 100;
    animation: fadeSlide 0.25s ease;
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

  .pair-title {
    font-family: var(--font-serif);
    font-size: 32px;
    color: var(--text);
    text-align: center;
    letter-spacing: -0.02em;
  }

  .pair-title em {
    color: var(--accent-hi);
    font-style: italic;
  }

  .pair-subtitle {
    font-size: 12px;
    color: var(--muted);
    text-align: center;
    max-width: 320px;
    line-height: 1.7;
  }

  .pair-input-row {
    display: flex;
    gap: 8px;
  }

  .pair-input-row input {
    background: var(--surface);
    border: 1px solid var(--border-hi);
    border-radius: var(--radius);
    color: var(--text);
    font-family: var(--font-mono);
    font-size: 22px;
    letter-spacing: 0.25em;
    text-align: center;
    width: 160px;
    padding: 12px 16px;
    outline: none;
    transition:
      border-color 0.2s,
      box-shadow 0.2s;
  }

  .pair-input-row input:focus {
    border-color: var(--accent);
    box-shadow: 0 0 0 3px var(--accent-lo);
  }

  .pair-input-row input:disabled {
    opacity: 0.6;
  }

  .pair-btn {
    background: var(--accent);
    color: #fff;
    border: none;
    border-radius: var(--radius);
    font-family: var(--font-mono);
    font-size: 13px;
    padding: 12px 20px;
    cursor: pointer;
    transition:
      background 0.15s,
      transform 0.1s;
  }

  .pair-btn:hover:not(:disabled) {
    background: var(--accent-hi);
  }

  .pair-btn:active:not(:disabled) {
    transform: scale(0.97);
  }

  .pair-btn:disabled {
    opacity: 0.5;
    cursor: not-allowed;
  }

  .pair-error {
    font-size: 12px;
    color: var(--red);
    min-height: 18px;
    text-align: center;
  }
</style>
