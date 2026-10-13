import { HEIGHT, MARGIN, minuteTicks } from './chartKit';

// The components shared by the charts over the match's minutes (functions and sizes in chartKit.js).

// offset: extra room between the plot and the minute labels (the objectives strip of the lead chart).
export function TimeAxis({ seconds, x, offset = 0 }) {
    const bottom = HEIGHT - MARGIN.bottom + offset;
    return (
        <g className="chart-axis" aria-hidden="true">
            {minuteTicks(seconds[seconds.length - 1]).map(second => (
                <text key={second} x={x(second)} y={bottom + 18} textAnchor="middle">{second / 60}′</text>
            ))}
        </g>
    );
}

export function Tooltip({ point, x, width, children }) {
    if (point === null) {
        return null;
    }
    // Kept inside the chart: flips to the left of the crosshair on the right half.
    const left = x(point);
    const style = left > width / 2 ? { right: width - left + 8 } : { left: left + 8 };
    return <div className="chart-tooltip" style={style}>{children}</div>;
}
