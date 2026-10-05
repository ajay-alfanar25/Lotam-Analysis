// Mirrors the --hue-* custom properties in styles.scss exactly. Keep these
// two files in sync - if you add a hue here, add the matching CSS variables
// too (and vice versa). This is the "reusability" half of the theme: pure
// CSS can't compute "the 3rd color in a rotating palette" or "a consistent
// color for this technology code" - components that need that logic import
// from here instead of hardcoding a hex value locally.

export interface PaletteEntry {
  name: string;
  color: string; // CSS var reference, resolves through the same tokens as the SCSS side
  bg: string;
  border: string;
}

export const PALETTE: PaletteEntry[] = [
  { name: 'blue', color: 'var(--hue-blue)', bg: 'var(--hue-blue-bg)', border: 'var(--hue-blue-border)' },
  { name: 'purple', color: 'var(--hue-purple)', bg: 'var(--hue-purple-bg)', border: 'var(--hue-purple-border)' },
  { name: 'teal', color: 'var(--hue-teal)', bg: 'var(--hue-teal-bg)', border: 'var(--hue-teal-border)' },
  { name: 'pink', color: 'var(--hue-pink)', bg: 'var(--hue-pink-bg)', border: 'var(--hue-pink-border)' },
  { name: 'amber', color: 'var(--hue-amber)', bg: 'var(--hue-amber-bg)', border: 'var(--hue-amber-border)' },
  { name: 'emerald', color: 'var(--hue-emerald)', bg: 'var(--hue-emerald-bg)', border: 'var(--hue-emerald-border)' },
  { name: 'rose', color: 'var(--hue-rose)', bg: 'var(--hue-rose-bg)', border: 'var(--hue-rose-border)' },
  { name: 'indigo', color: 'var(--hue-indigo)', bg: 'var(--hue-indigo-bg)', border: 'var(--hue-indigo-border)' },
  { name: 'cyan', color: 'var(--hue-cyan)', bg: 'var(--hue-cyan-bg)', border: 'var(--hue-cyan-border)' },
];

/** Nth color in the palette, wrapping around - use for chart series, ranked
 * lists, anything where you just want visually distinct colors in order. */
export function paletteColorForIndex(index: number): PaletteEntry {
  return PALETTE[index % PALETTE.length];
}

/** Deterministic color for an arbitrary string key (e.g. a technology code
 * or category name) - the same key always gets the same color across the
 * whole app, without maintaining an explicit per-key color map by hand. */
export function paletteColorForKey(key: string): PaletteEntry {
  let hash = 0;
  for (let i = 0; i < key.length; i++) {
    hash = (hash << 5) - hash + key.charCodeAt(i);
    hash |= 0;
  }
  return PALETTE[Math.abs(hash) % PALETTE.length];
}

// Semantic severity/status colors - these stay fixed (never rotate), since
// "Critical = red" is a meaning, not an arbitrary assignment. Both
// SeverityBadgeComponent and any chart that breaks data down by severity
// should import these rather than redefining the mapping locally.
export const SEVERITY_COLORS: Record<string, PaletteEntry> = {
  Critical: { name: 'rose', color: 'var(--critical)', bg: 'var(--hue-rose-bg)', border: 'var(--hue-rose-border)' },
  High: { name: 'amber', color: 'var(--high)', bg: 'var(--hue-amber-bg)', border: 'var(--hue-amber-border)' },
  Medium: { name: 'blue', color: 'var(--medium)', bg: 'var(--hue-blue-bg)', border: 'var(--hue-blue-border)' },
  Low: { name: 'emerald', color: 'var(--low)', bg: 'var(--hue-emerald-bg)', border: 'var(--hue-emerald-border)' },
};

export const STATUS_COLORS: Record<string, PaletteEntry> = {
  Qualified: SEVERITY_COLORS['Low'],
  Reactivated: SEVERITY_COLORS['Low'],
  Conditional: SEVERITY_COLORS['High'],
  Suspended: SEVERITY_COLORS['Critical'],
  Disqualified: SEVERITY_COLORS['Critical'],
  Blacklisted: SEVERITY_COLORS['Critical'],
  Unknown: {
    name: 'grey',
    color: 'var(--text-dim)',
    bg: 'rgba(147,162,192,0.14)',
    border: 'rgba(147,162,192,0.30)',
  },
};

export const CHANGE_BADGE_COLORS: Record<string, PaletteEntry> = {
  New: SEVERITY_COLORS['Low'],
  Updated: SEVERITY_COLORS['High'],
  Removed: SEVERITY_COLORS['Critical'],
};
