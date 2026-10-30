// WinMarchy bar for Zebar. Workspaces on the left, the clock in the middle, tray and status on the
// right – laid out like Omarchy's bar. Which entries show is chosen in WinMarchy (config.modules);
// every entry does something on a click. Only fixed SVG markup goes through innerHTML; names,
// titles and tooltips are set as text.
import * as zebar from './zebar.js';
import config from './config.js';

const $ = (id) => document.getElementById(id);

const icons = {
  wifi: '<svg viewBox="0 0 16 16" width="14" height="14" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M1.5 6a9.5 9.5 0 0 1 13 0"/><path d="M4 8.8a6 6 0 0 1 8 0"/><path d="M6.4 11.5a2.6 2.6 0 0 1 3.2 0"/><circle cx="8" cy="13.6" r=".9" fill="currentColor" stroke="none"/></svg>',
  ethernet: '<svg viewBox="0 0 16 16" width="14" height="14" fill="none" stroke="currentColor" stroke-width="1.5"><rect x="3" y="4" width="10" height="8"/><path d="M5.5 12v2M8 12v2M10.5 12v2M6 7h4"/></svg>',
  vpn: '<svg viewBox="0 0 16 16" width="14" height="14" fill="none" stroke="currentColor" stroke-width="1.5"><rect x="3" y="7" width="10" height="7"/><path d="M5 7V5a3 3 0 0 1 6 0v2"/></svg>',
  offline: '<svg viewBox="0 0 16 16" width="14" height="14" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M2 2l12 12"/><path d="M1.5 6a9.5 9.5 0 0 1 4-2.2M10.5 3.8a9.5 9.5 0 0 1 4 2.2"/></svg>',
  volume: '<svg viewBox="0 0 16 16" width="14" height="14" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M2 6h3l4-3v10l-4-3H2z" fill="currentColor" stroke="none"/><path d="M11 5.5a3.5 3.5 0 0 1 0 5M12.8 3.5a6.3 6.3 0 0 1 0 9"/></svg>',
  muted: '<svg viewBox="0 0 16 16" width="14" height="14" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M2 6h3l4-3v10l-4-3H2z" fill="currentColor" stroke="none"/><path d="M11 6l4 4M15 6l-4 4"/></svg>',
};

function battery(percent, charging) {
  const w = Math.max(1, Math.round((Math.min(100, Math.max(0, percent)) / 100) * 11));
  const bolt = charging ? '<path d="M8.5 4.5L6.5 8h2.5l-2 3.5" stroke="var(--bg)" stroke-width="1.2" fill="none"/>' : '';
  return `<svg viewBox="0 0 18 16" width="16" height="14"><rect x="1" y="3.5" width="14" height="9" fill="none" stroke="currentColor" stroke-width="1.3"/><rect x="15.5" y="6" width="1.8" height="4" fill="currentColor"/><rect x="2.5" y="5" width="${w}" height="6" fill="currentColor"/>${bolt}</svg>`;
}

// Clicks call WinMarchy; zpack.json allows only "--action …" for WinMarchy.exe.
const act = (action) => zebar.shellSpawn(config.exe, `--action ${action}`).catch((e) => console.error(e));
const on = (id) => config.modules?.[id] !== false;

for (const id of ['logo', 'workspaces', 'clock', 'tray', 'direction', 'network', 'audio', 'cpu', 'memory', 'battery']) {
  if (!on(id)) $(id).hidden = true;
}

// Left click does the obvious thing, right click the second one (as in Omarchy's Waybar).
function clicks(id, left, right, tip) {
  const el = $(id);
  el.addEventListener('click', left);
  if (right) el.addEventListener('contextmenu', (e) => { e.preventDefault(); right(); });
  if (tip) el.title = tip;
}

clicks('logo', () => act('do:menu'), () => act('do:keybindings'), config.text.logoTip);
clicks('clock', () => act('app:calendar'), () => act('open:ms-settings:dateandtime'), config.text.clockTip);
clicks('network', () => act('open:ms-availablenetworks:'), () => act('open:ms-settings:network'));
clicks('cpu', () => act('app:taskmanager'), null, config.text.cpuTip);
clicks('memory', () => act('app:taskmanager'), null, config.text.cpuTip);
clicks('battery', () => act('open:ms-settings:batterysaver'), () => act('open:ms-settings:powersleep'), config.text.batteryTip);
clicks('direction', () => providers.outputMap.glazewm?.runCommand('toggle-tiling-direction'), null);
clicks('audio', () => act('open:ms-settings:sound'), () => {
  const a = providers.outputMap.audio;
  const dev = a?.defaultPlaybackDevice;
  if (dev) a.setMute(!dev.isMuted);
});
// Scrolling over the volume changes it in steps of five.
$('audio').addEventListener('wheel', (e) => {
  const a = providers.outputMap.audio;
  const dev = a?.defaultPlaybackDevice;
  if (!dev) return;
  e.preventDefault();
  a.setVolume(Math.max(0, Math.min(100, Math.round(dev.volume) + (e.deltaY < 0 ? 5 : -5))));
}, { passive: false });

// Only what is shown is measured.
const wanted = {
  glazewm: { type: 'glazewm' },
  date: { type: 'date', formatting: config.clock, locale: config.locale },
};
if (on('cpu')) wanted.cpu = { type: 'cpu', refreshInterval: 3000 };
if (on('memory')) wanted.memory = { type: 'memory', refreshInterval: 5000 };
if (on('battery')) wanted.battery = { type: 'battery', refreshInterval: 15000 };
if (on('network')) wanted.network = { type: 'network', refreshInterval: 5000 };
if (on('audio')) wanted.audio = { type: 'audio' };
if (on('tray')) wanted.systray = { type: 'systray' };
const providers = zebar.createProviderGroup(wanted);

function renderWorkspaces(glaze) {
  const box = $('workspaces');
  box.replaceChildren();
  if (!glaze) return;
  for (const ws of glaze.currentWorkspaces) {
    // The scratchpad and WinMarchy's staging area are hidden workspaces.
    if (ws.name === 'scratchpad' || ws.name === 'winmarchy-stage') continue;
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'workspace' + (ws.hasFocus ? ' focused' : '') + (ws.isDisplayed ? ' displayed' : '');
    b.textContent = ws.displayName ?? ws.name;
    b.addEventListener('click', () => glaze.runCommand(`focus --workspace ${ws.name}`));
    box.append(b);
  }
  const dir = $('direction');
  dir.textContent = glaze.isPaused ? '⏸' : glaze.tilingDirection === 'vertical' ? '↕' : '↔';
  dir.title = glaze.isPaused ? config.text.paused : config.text.direction;
}

// The tray as in Omarchy's Waybar: folded away behind a small arrow. Only the icons you pinned
// stay in sight; the arrow shows all of them. Middle click on an icon pins or unpins it (kept in
// the bar's own storage), WinMarchy's settings can name more. The same program is listed once, and
// network, sound and battery are left out when the bar shows them itself.
const trayState = { open: false };
const pinKey = 'winmarchy.tray.pinned';
const savedPins = () => { try { return JSON.parse(localStorage.getItem(pinKey) ?? '[]'); } catch { return []; } };
const iconName = (icon) => (icon.tooltip ?? '').split('\n')[0].trim().toLowerCase() || icon.iconHash;
const systemIcon = {
  network: /netzwerk|internetzugriff|network|wi-?fi|wlan|ethernet/i,
  audio: /lautsprecher|kopfhörer|lautstärke|speakers|headphones|volume/i,
  battery: /akku|batterie|battery/i,
};

function isPinned(icon) {
  const name = iconName(icon);
  return savedPins().includes(name) || (config.trayPinned ?? []).some((p) => p && name.includes(p.toLowerCase()));
}

function togglePin(icon) {
  const name = iconName(icon);
  const pins = savedPins();
  const next = pins.includes(name) ? pins.filter((p) => p !== name) : [...pins, name];
  try { localStorage.setItem(pinKey, JSON.stringify(next)); } catch { /* storage full or off */ }
  render();
}

function trayIcons(tray) {
  const seen = new Set();
  return tray.icons.filter((icon) => {
    const key = `${iconName(icon)}|${icon.iconHash}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return !Object.entries(systemIcon).some(([module, pattern]) => on(module) && pattern.test(icon.tooltip ?? ''));
  });
}

function renderTray(tray) {
  const box = $('tray');
  box.replaceChildren();
  if (!tray) return;
  const all = trayIcons(tray);
  const shown = trayState.open ? all : all.filter(isPinned);
  for (const icon of shown) {
    const img = document.createElement('img');
    img.src = icon.iconUrl;
    img.title = icon.tooltip ?? '';
    img.alt = '';
    img.className = isPinned(icon) ? 'pinned' : '';
    // Some programs only react to a click after the mouse came over their icon.
    img.addEventListener('mouseenter', () => tray.onHoverEnter(icon.id).catch(() => {}));
    img.addEventListener('mouseleave', () => tray.onHoverLeave(icon.id).catch(() => {}));
    img.addEventListener('click', (e) => { e.preventDefault(); tray.onHoverMove(icon.id).catch(() => {}); tray.onLeftClick(icon.id); });
    img.addEventListener('dblclick', (e) => { e.preventDefault(); tray.onLeftDoubleClick(icon.id); });
    img.addEventListener('contextmenu', (e) => { e.preventDefault(); tray.onRightClick(icon.id); });
    img.addEventListener('auxclick', (e) => { if (e.button === 1) { e.preventDefault(); togglePin(icon); } });
    box.append(img);
  }
  if (all.length > shown.length || trayState.open) {
    const more = document.createElement('button');
    more.type = 'button';
    more.className = 'tray-toggle';
    more.textContent = trayState.open ? '›' : '‹';
    more.title = trayState.open ? config.text.trayClose : config.text.trayOpen;
    more.addEventListener('click', () => { trayState.open = !trayState.open; render(); });
    box.prepend(more);
  }
}

function renderStatus(o) {
  $('clock').textContent = o.date?.formatted ?? '';

  const net = $('network');
  const type = o.network?.defaultInterface?.type;
  net.innerHTML = type === 'wifi' ? icons.wifi : type === 'ethernet' ? icons.ethernet : type === 'proprietary_virtual' ? icons.vpn : icons.offline;
  net.className = 'item' + (type ? '' : ' dim');
  net.title = (o.network?.defaultGateway?.ssid ?? o.network?.defaultInterface?.friendlyName ?? config.text.offline) + ' · ' + config.text.networkTip;

  const audio = $('audio');
  const dev = o.audio?.defaultPlaybackDevice;
  audio.innerHTML = dev ? (dev.isMuted ? icons.muted : icons.volume) : '';
  if (dev) audio.append(document.createTextNode(`${Math.round(dev.volume)}%`));
  audio.title = dev ? `${dev.name} · ${config.text.audioTip}` : '';

  const cpu = $('cpu');
  cpu.textContent = o.cpu ? `cpu ${Math.round(o.cpu.usage)}%` : '';
  cpu.className = 'item' + (o.cpu && o.cpu.usage > 85 ? ' warn' : ' dim');

  const mem = $('memory');
  mem.textContent = o.memory ? `ram ${Math.round(o.memory.usage)}%` : '';
  mem.className = 'item' + (o.memory && o.memory.usage > 90 ? ' warn' : ' dim');

  const bat = $('battery');
  if (o.battery && o.battery.state !== 'unknown') {
    bat.innerHTML = battery(o.battery.chargePercent, o.battery.isCharging);
    bat.append(document.createTextNode(`${Math.round(o.battery.chargePercent)}%`));
    bat.className = 'item' + (!o.battery.isCharging && o.battery.chargePercent < 15 ? ' warn' : '');
  } else {
    bat.replaceChildren();
  }
}

function render() {
  const o = providers.outputMap;
  renderWorkspaces(o.glazewm);
  renderTray(o.systray);
  renderStatus(o);
}

providers.onOutput(render);
render();

// Theme changes arrive as a new theme.css; reload it every few seconds without restarting the bar.
setInterval(() => {
  const link = $('theme');
  link.href = './theme.css?' + Date.now();
}, 3000);
