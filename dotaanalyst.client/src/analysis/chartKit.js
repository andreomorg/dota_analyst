import { useLayoutEffect, useRef, useState } from 'react';
import { formatNumber } from '../matches/format';

// The pieces of the hand-made SVG charts over the match's minutes (TimelineSection, StatChart): size, scales and the
// end labels (the minute axis and the tooltip are components, in ChartParts.jsx). No chart library; colors are CSS
// variables (App.css).

export const HEIGHT = 220;
export const MARGIN = { top: 12, right: 96, bottom: 28, left: 56 };

// Width of the chart's container, so the SVG is drawn at its real size (text never stretched).
export function useWidth() {
    const ref = useRef(null);
    const [width, setWidth] = useState(640);
    useLayoutEffect(() => {
        const element = ref.current;
        if (!element) {
            return undefined;
        }
        const observer = new ResizeObserver(([entry]) => setWidth(Math.max(280, Math.floor(entry.contentRect.width))));
        observer.observe(element);
        return () => observer.disconnect();
    }, []);
    return [ref, width];
}

// "+12,3 mil" style ticks are noisy; thousands with a "k" stay short on the axis.
export function shortNumber(value) {
    const abs = Math.abs(value);
    const text = abs >= 1000 ? `${formatNumber(Math.round(abs / 100) / 10)}k` : formatNumber(Math.round(abs * 10) / 10);
    return value < 0 ? `−${text}` : value > 0 && text !== '0' ? `+${text}` : text;
}

export function minuteTicks(lastSecond) {
    const minutes = lastSecond / 60;
    const step = minutes > 40 ? 10 : 5;
    const ticks = [];
    for (let minute = 0; minute <= minutes; minute += step) {
        ticks.push(minute * 60);
    }
    return ticks;
}

export function scales(seconds, width, yMin, yMax) {
    const innerWidth = width - MARGIN.left - MARGIN.right;
    const innerHeight = HEIGHT - MARGIN.top - MARGIN.bottom;
    const last = seconds[seconds.length - 1] || 1;
    const x = second => MARGIN.left + (second / last) * innerWidth;
    const y = value => MARGIN.top + ((yMax - value) / (yMax - yMin || 1)) * innerHeight;
    return { x, y, innerWidth, innerHeight };
}

// The point nearest to the pointer, for the crosshair and the tooltip.
export function nearestPoint(event, svg, seconds, x) {
    const box = svg.getBoundingClientRect();
    const pointerX = event.clientX - box.left;
    let best = 0;
    seconds.forEach((second, index) => {
        if (Math.abs(x(second) - pointerX) < Math.abs(x(seconds[best]) - pointerX)) {
            best = index;
        }
    });
    return best;
}

// End labels move apart when two lines end close together (at least 14 px between them).
export function spreadLabels(labels) {
    const sorted = [...labels].sort((a, b) => a.y - b.y);
    for (let i = 1; i < sorted.length; i++) {
        sorted[i].y = Math.max(sorted[i].y, sorted[i - 1].y + 14);
    }
    return sorted;
}
