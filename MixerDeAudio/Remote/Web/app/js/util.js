// Utilidades dos componentes (sem protocolo, sem estado global).

// Vibração curta no toque (D9 da Fase 13). O iPhone ignora; sem suporte não faz nada.
export function vibrar(ms = 8) {
  try { if (navigator.vibrate) navigator.vibrate(ms); } catch { /* sem vibração */ }
}
