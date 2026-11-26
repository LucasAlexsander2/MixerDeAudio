// Ícones do protótipo aprovado (Fase 13, 4.4): grade 24x24, traço 1,75, currentColor. Conjunto completo da 4.4 (a 14c acrescentou left e right).
const IC = {
  back:'<path d="m15 5-7 7 7 7"/>',
  left:'<path d="M19 12H5M11 6l-6 6 6 6"/>',
  right:'<path d="M5 12h14M13 6l6 6-6 6"/>',
  volume:'<path d="M11 5 6 9H3v6h3l5 4z"/><path d="M15.5 8.5a5 5 0 0 1 0 7M18.5 5.5a9 9 0 0 1 0 13"/>',
  mute:'<path d="M11 5 6 9H3v6h3l5 4z"/><path d="m16 9 5 6M21 9l-5 6"/>',
  headphones:'<path d="M4 15v-3a8 8 0 0 1 16 0v3"/><rect x="3" y="14" width="4" height="6" rx="1.5"/><rect x="17" y="14" width="4" height="6" rx="1.5"/>',
  speaker:'<rect x="6" y="3" width="12" height="18" rx="2"/><circle cx="12" cy="15" r="2.5"/><circle cx="12" cy="7.5" r="1"/>',
  monitor:'<rect x="3" y="4" width="18" height="12" rx="2"/><path d="M8 20h8M12 16v4"/>',
  monitors:'<rect x="2" y="5" width="12" height="9" rx="1.5"/><rect x="10" y="9" width="12" height="9" rx="1.5"/>',
  sun:'<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/>',
  lock:'<rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V8a4 4 0 0 1 8 0v3"/>',
  play:'<path d="M7 4.5v15l12-7.5z"/>',
  pause:'<path d="M8 5v14M16 5v14"/>',
  prev:'<path d="M19 5v14L9 12z"/><path d="M5 5v14"/>',
  next:'<path d="M5 5v14l10-7z"/><path d="M19 5v14"/>',
  gamepad:'<rect x="2" y="7" width="20" height="11" rx="5"/><path d="M7 10.5v4M5 12.5h4"/><circle cx="15.5" cy="11.5" r=".6"/><circle cx="18" cy="13.5" r=".6"/>',
  power:'<path d="M12 3v9"/><path d="M6.3 6.3a8 8 0 1 0 11.4 0"/>',
  windows:'<rect x="3" y="4" width="18" height="16" rx="2"/><path d="M3 9h18"/>',
  shortcut:'<path d="M9 4H7a3 3 0 0 0 0 6h10a3 3 0 0 1 0 6h-2"/><path d="m12 13-3 3 3 3"/>',
  send:'<path d="m22 2-11 11"/><path d="M22 2 15 22l-4-9-9-4z"/>',
  moon:'<path d="M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z"/>',
  keyboard:'<rect x="2" y="6" width="20" height="12" rx="2"/><path d="M6 10h.01M10 10h.01M14 10h.01M18 10h.01M7 14h10"/>',
  zoom:'<circle cx="11" cy="11" r="7"/><path d="m20 20-4-4M11 8v6M8 11h6"/>',
  land:'<rect x="2" y="7" width="20" height="10" rx="2"/><path d="M18 12h.01"/>',
  grip:'<path d="M8 12h.01M12 12h.01M16 12h.01"/>',
  chevd:'<path d="m6 9 6 6 6-6"/>',
  up:'<path d="m6 15 6-6 6 6"/>',
  down:'<path d="m6 9 6 6 6-6"/>',
  pad:'<rect x="3" y="5" width="18" height="14" rx="3"/><path d="M12 14v5"/>',
  restart:'<path d="M20 12a8 8 0 1 1-2.4-5.7"/><path d="M20 4v5h-5"/>',
  x:'<path d="m6 6 12 12M18 6 6 18"/>',
  rew:'<path d="M11 6 3 12l8 6z"/><path d="M21 6l-8 6 8 6z"/>',
  fwd:'<path d="m13 6 8 6-8 6z"/><path d="M3 6l8 6-8 6z"/>',
  full:'<path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5"/>',
  ext:'<rect x="2" y="6" width="9" height="12" rx="1.5"/><rect x="13" y="6" width="9" height="12" rx="1.5"/>',
  activity:'<path d="M3 12h4l3-8 4 16 3-8h4"/>',
  clip:'<rect x="6" y="4" width="12" height="17" rx="2"/><path d="M9 4h6v3H9z"/>',
  file:'<path d="M6 3h8l4 4v14H6z"/><path d="M14 3v4h4"/>',
  rec:'<circle cx="12" cy="12" r="8"/><circle cx="12" cy="12" r="3"/>',
  calc:'<rect x="5" y="3" width="14" height="18" rx="2"/><path d="M8 7h8M8 12h.01M12 12h.01M16 12h.01M8 16h.01M12 16h.01M16 16h.01"/>',
  note:'<path d="M6 3h12v18H6z"/><path d="M9 8h6M9 12h6M9 16h4"/>',
  download:'<path d="M12 4v11"/><path d="m7 11 5 5 5-5"/><path d="M5 20h14"/>',
  music:'<path d="M9 18V5l11-2v13"/><circle cx="6.5" cy="18" r="2.5"/><circle cx="17.5" cy="16" r="2.5"/>',
  gear:'<circle cx="12" cy="12" r="3"/><path d="M12 2v3M12 19v3M2 12h3M19 12h3M5 5l2 2M17 17l2 2M5 19l2-2M17 7l2-2"/>',
  tasks:'<path d="M4 6h16M4 12h16M4 18h10"/>'
};

export function icone(nome) {
  return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + (IC[nome] || '') + '</svg>';
}

export function definirIcone(el, nome) {
  el.innerHTML = icone(nome);
}
