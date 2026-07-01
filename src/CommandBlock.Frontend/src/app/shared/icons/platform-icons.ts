// Monochrome (currentColor) glyphs for each Minecraft server platform/loader. Modrinth and
// CurseForge use their real marks from @ng-icons/simple-icons; the loaders without a simple-icon
// get a simple, distinct custom glyph here. Register with provideIcons({ ...PLATFORM_ICONS, ... }).

const s = (body: string) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">${body}</svg>`;
const f = (body: string) => `<svg viewBox="0 0 24 24" fill="currentColor">${body}</svg>`;

// Isometric block — Vanilla.
export const platformVanilla = s('<path d="M12 3 21 7.5 12 12 3 7.5Z"/><path d="M3 7.5v9L12 21v-9"/><path d="M21 7.5v9L12 21"/>');
// Folded sheet — Paper.
export const platformPaper = s('<path d="M7 3h7l4 4v14H7Z"/><path d="M14 3v4h4"/><path d="M10 12h5M10 16h5"/>');
// Square with inset diamond — Purpur.
export const platformPurpur = s('<rect x="4" y="4" width="16" height="16" rx="1.5"/><path d="M12 7.5 16.5 12 12 16.5 7.5 12Z"/>');
// Woven grid — Fabric.
export const platformFabric = s('<path d="M4 9h16M4 15h16M9 4v16M15 4v16"/>');
// Checkerboard patches — Quilt.
export const platformQuilt = '<svg viewBox="0 0 24 24" fill="none"><rect x="4" y="4" width="7" height="7" rx="1" fill="currentColor"/><rect x="13" y="13" width="7" height="7" rx="1" fill="currentColor"/><rect x="13" y="4" width="7" height="7" rx="1" stroke="currentColor" stroke-width="1.7"/><rect x="4" y="13" width="7" height="7" rx="1" stroke="currentColor" stroke-width="1.7"/></svg>';
// Hammer — Forge.
export const platformForge = s('<path d="M13.5 4.5 20 11l-2.5 2.5L11 7z"/><path d="M11 7 4 14l3 3 7-7"/>');
// Flame — NeoForge.
export const platformNeoforge = f('<path d="M12 2c1.2 3-2.2 4.3-2.2 7.3 0 1 .5 1.7 1.2 2.1-.2-1.6.6-2.6 1.5-3.1-.3 2 1.4 2.4 1.7 4.2.4 2-1 3.5-2.2 4C15 16.3 17 14.4 17 11.5 17 7.7 13.6 6.3 12 2Z"/>');
// Water drop — Spigot.
export const platformSpigot = f('<path d="M12 3s6 6.6 6 11a6 6 0 1 1-12 0c0-4.4 6-11 6-11Z"/>');

/// All custom platform glyphs, for provideIcons.
export const PLATFORM_ICONS = {
  platformVanilla, platformPaper, platformPurpur, platformFabric,
  platformQuilt, platformForge, platformNeoforge, platformSpigot,
};

/// Friendly display name for a server type (itzg TYPEs are uppercase; show them nicely).
export function platformLabel(serverType: string): string {
  switch ((serverType ?? '').toUpperCase()) {
    case 'VANILLA': return 'Vanilla';
    case 'PAPER': return 'Paper';
    case 'PURPUR': return 'Purpur';
    case 'FABRIC': return 'Fabric';
    case 'QUILT': return 'Quilt';
    case 'FORGE': return 'Forge';
    case 'NEOFORGE': return 'NeoForge';
    case 'SPIGOT': return 'Spigot';
    case 'MODRINTH': return 'Modrinth';
    case 'CURSEFORGE':
    case 'AUTO_CURSEFORGE': return 'CurseForge';
    default: return serverType;
  }
}

/// Maps a server type to the ng-icon name to render for it.
export function platformIcon(serverType: string): string {
  switch ((serverType ?? '').toUpperCase()) {
    case 'VANILLA': return 'platformVanilla';
    case 'PAPER': return 'platformPaper';
    case 'PURPUR': return 'platformPurpur';
    case 'FABRIC': return 'platformFabric';
    case 'QUILT': return 'platformQuilt';
    case 'FORGE': return 'platformForge';
    case 'NEOFORGE': return 'platformNeoforge';
    case 'SPIGOT': return 'platformSpigot';
    case 'MODRINTH': return 'simpleModrinth';
    case 'CURSEFORGE':
    case 'AUTO_CURSEFORGE': return 'simpleCurseforge';
    default: return 'platformVanilla';
  }
}
