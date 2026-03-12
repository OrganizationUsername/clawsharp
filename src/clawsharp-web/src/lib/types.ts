export type MessageRole = 'user' | 'agent' | 'system';

export type WsStatus = 'disconnected' | 'connecting' | 'connected' | 'error';

export interface Message {
  id: string;
  role: MessageRole;
  text: string;
  time: string; // ISO string
  streaming?: boolean;
}

export interface HistoryEntry {
  role: MessageRole;
  text: string;
  time: string;
}
