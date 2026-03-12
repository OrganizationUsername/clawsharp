import type { HistoryEntry, MessageRole } from './types.js';

const STORAGE_KEY = 'cws_history';
const MAX_ENTRIES = 100;

export function loadHistory(): HistoryEntry[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw) as unknown;
    if (!Array.isArray(parsed)) return [];
    return parsed as HistoryEntry[];
  } catch {
    localStorage.removeItem(STORAGE_KEY);
    return [];
  }
}

export function saveHistoryEntry(role: MessageRole, text: string): void {
  let entries = loadHistory();
  entries.push({ role, text, time: new Date().toISOString() });
  if (entries.length > MAX_ENTRIES) {
    entries = entries.slice(-MAX_ENTRIES);
  }
  localStorage.setItem(STORAGE_KEY, JSON.stringify(entries));
}

export function clearHistory(): void {
  localStorage.removeItem(STORAGE_KEY);
}
