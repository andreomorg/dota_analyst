// Turns a hero's heatmap grid (from the API: flat [column, row, samples] triples) into an image, colored like the
// heatmaps of football broadcasts: nothing where the hero never went, then green, yellow and red where it spent
// the most time.
//
// How: each cell is drawn as a soft blurred spot whose opacity is its share of the time, on a hidden canvas; the
// spots add up where cells are close. Then each pixel's opacity is turned into a color of the palette.

const PALETTE_STOPS = [
    [0, 'rgba(0, 200, 83, 0)'],
    [0.15, 'rgba(0, 200, 83, 0.55)'],
    [0.4, 'rgba(174, 234, 0, 0.75)'],
    [0.6, 'rgba(255, 235, 59, 0.85)'],
    [0.8, 'rgba(255, 145, 0, 0.9)'],
    [1, 'rgba(229, 57, 53, 0.95)'],
];

let palette = null;

// 256 RGBA colors, from the stops above.
function getPalette() {
    if (!palette) {
        const canvas = document.createElement('canvas');
        canvas.width = 256;
        canvas.height = 1;
        const context = canvas.getContext('2d');
        const gradient = context.createLinearGradient(0, 0, 256, 0);
        PALETTE_STOPS.forEach(([offset, color]) => gradient.addColorStop(offset, color));
        context.fillStyle = gradient;
        context.fillRect(0, 0, 256, 1);
        palette = context.getImageData(0, 0, 256, 1).data;
    }
    return palette;
}

// Adds the phases' triples into one Map: "column,row" -> samples.
export function sumCells(...phases) {
    const totals = new Map();
    phases.filter(Boolean).forEach(flat => {
        for (let index = 0; index + 2 < flat.length; index += 3) {
            const key = `${flat[index]},${flat[index + 1]}`;
            totals.set(key, (totals.get(key) ?? 0) + flat[index + 2]);
        }
    });
    return totals;
}

// The value treated as "full heat": a high percentile instead of the maximum, so one cell where the hero stood
// still for minutes (the fountain, a stacked camp) doesn't make every other place look cold.
function fullHeat(values) {
    const sorted = [...values].sort((a, b) => a - b);
    return sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * 0.92))] || 1;
}

// A data URL with the heatmap of `totals` (sumCells) for a gridSize x gridSize grid, or null when it is empty.
export function renderHeatmap(totals, gridSize, size = 512) {
    if (totals.size === 0) {
        return null;
    }

    const canvas = document.createElement('canvas');
    canvas.width = size;
    canvas.height = size;
    const context = canvas.getContext('2d');
    const cell = size / gridSize;
    const radius = cell * 1.7;
    const full = fullHeat(totals.values());

    totals.forEach((samples, key) => {
        const [column, row] = key.split(',').map(Number);
        const x = (column + 0.5) * cell;
        const y = (row + 0.5) * cell;
        const spot = context.createRadialGradient(x, y, 0, x, y, radius);
        spot.addColorStop(0, 'rgba(0, 0, 0, 1)');
        spot.addColorStop(1, 'rgba(0, 0, 0, 0)');
        context.globalAlpha = Math.min(1, samples / full) * 0.7;
        context.fillStyle = spot;
        context.fillRect(x - radius, y - radius, 2 * radius, 2 * radius);
    });

    const image = context.getImageData(0, 0, size, size);
    const pixels = image.data;
    const colors = getPalette();
    for (let index = 0; index < pixels.length; index += 4) {
        const heat = pixels[index + 3];
        const color = heat * 4;
        pixels[index] = colors[color];
        pixels[index + 1] = colors[color + 1];
        pixels[index + 2] = colors[color + 2];
        pixels[index + 3] = colors[color + 3];
    }
    context.putImageData(image, 0, 0);
    return canvas.toDataURL();
}
