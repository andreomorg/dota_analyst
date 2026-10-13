// The design's small labels. The text always says it; the colour only reinforces it.

export function ResultChip({ won, winLabel = 'Vitória', lossLabel = 'Derrota' }) {
    if (won === null || won === undefined) {
        return <span className="chip chip-neutral">Sem resultado</span>;
    }
    return won
        ? <span className="chip chip-win"><span className="chip-dot" aria-hidden="true" />{winLabel}</span>
        : <span className="chip chip-loss"><span className="chip-dot" aria-hidden="true" />{lossLabel}</span>;
}

export function YouChip() {
    return <span className="chip chip-you">Você</span>;
}
