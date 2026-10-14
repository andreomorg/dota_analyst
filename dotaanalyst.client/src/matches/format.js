// Game clock: before the horn it is negative (-80 -> "-1:20").
export function formatDuration(totalSeconds) {
    const sign = totalSeconds < 0 ? '-' : '';
    const absolute = Math.abs(totalSeconds);
    const minutes = Math.floor(absolute / 60);
    const seconds = absolute % 60;
    return `${sign}${minutes}:${String(seconds).padStart(2, '0')}`;
}

const dateFormatter = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' });

export function formatDate(isoDate) {
    return dateFormatter.format(new Date(isoDate));
}

const dayFormatter = new Intl.DateTimeFormat('pt-BR', { day: 'numeric', month: 'short', year: 'numeric' });
const timeFormatter = new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit' });

// "12 de out. de 2026 · 17:23".
export function formatDateTime(isoDate) {
    const date = new Date(isoDate);
    return `${dayFormatter.format(date)} · ${timeFormatter.format(date)}`;
}

const compactDateFormatter = new Intl.DateTimeFormat('pt-BR', { day: 'numeric', month: 'short' });

export function formatCompactDate(isoDate) {
    const date = new Date(isoDate);
    const parts = compactDateFormatter.formatToParts(date);
    const day = parts.find(part => part.type === 'day').value;
    const month = parts.find(part => part.type === 'month').value;
    return `${day} ${month}, ${timeFormatter.format(date)}`;
}

// Up to 2 decimals so per-minute rates like 0.07 kills/min stay readable; integers stay integers.
const numberFormatter = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 2 });

export function formatNumber(value) {
    return numberFormatter.format(value);
}

// Always shows the sign, except on zero: 1250 -> "+1.250", -300 -> "-300", 0 -> "0".
const signedNumberFormatter = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 2, signDisplay: 'exceptZero' });

export function formatSignedNumber(value) {
    return signedNumberFormatter.format(value);
}

// 0..1 -> whole percentage number (0.514 -> 51).
export function toPercent(fraction) {
    return Math.round(fraction * 100);
}
