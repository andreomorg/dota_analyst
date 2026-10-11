export function formatDuration(totalSeconds) {
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    return `${minutes}:${String(seconds).padStart(2, '0')}`;
}

const dateFormatter = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' });

export function formatDate(isoDate) {
    return dateFormatter.format(new Date(isoDate));
}

// Up to 2 decimals so per-minute rates like 0.07 kills/min stay readable; integers stay integers.
const numberFormatter = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 2 });

export function formatNumber(value) {
    return numberFormatter.format(value);
}

// 0..1 -> whole percentage number (0.514 -> 51).
export function toPercent(fraction) {
    return Math.round(fraction * 100);
}
