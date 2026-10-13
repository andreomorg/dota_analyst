import { formatDuration, toPercent } from '../matches/format';

// The numbers behind the heatmaps (HeatmapIndicators in the Domain): where and how each hero played in the chosen
// period, for the player, the hero they compare with and the match average. The values come from the API; only the
// words are here.

const percent = value => `${toPercent(value)}%`;

// Where the average position sits on the line between the two fountains.
function advanceText(value) {
    if (value < 0.35) {
        return 'perto da sua base';
    }
    if (value < 0.47) {
        return 'do seu lado do rio';
    }
    if (value <= 0.53) {
        return 'no rio';
    }
    return value < 0.65 ? 'além do rio' : 'perto da base inimiga';
}

const ROWS = [
    {
        key: 'aliveSeconds',
        label: 'Tempo vivo',
        hint: 'Com posição registrada; tempo morto fica de fora.',
        format: value => formatDuration(Math.round(value)),
    },
    {
        key: 'advance',
        label: 'Linha média',
        hint: '0% = sua fonte, 50% = rio, 100% = fonte inimiga.',
        format: value => `${percent(value)} · ${advanceText(value)}`,
    },
    {
        key: 'enemySide',
        label: 'Tempo no lado inimigo',
        hint: 'Além do rio. Mostra mais quem estava ganhando do que um erro.',
        format: percent,
    },
    {
        key: 'nearAlly',
        label: 'Perto de aliados',
        hint: 'Pelo menos 1 herói do seu time a até 1.500 unidades.',
        format: percent,
    },
    {
        key: 'grouped',
        label: 'Agrupado',
        hint: '2 ou mais heróis do seu time a até 1.500 unidades.',
        format: percent,
    },
    {
        key: 'coverage',
        label: 'Área do mapa percorrida',
        hint: 'Parte do mapa jogável onde ficou pelo menos 4 s. Suportes que rodam cobrem mais.',
        format: percent,
    },
    {
        key: 'concentration',
        label: 'Concentração',
        hint: 'Parte do tempo nos 5 lugares onde mais ficou: alto = parado num ponto (rota, acampamento).',
        format: percent,
    },
];

function cell(indicators, row) {
    const value = indicators?.[row.key];
    return value === null || value === undefined ? '—' : row.format(value);
}

// player/other: the heroes' views (with indicators); phaseKey: 'all', 'earlyGame', 'midGame' or 'lateGame';
// matchAverage: heatmaps.matchAverage. Absent on analyses from before the indicators: then nothing shows.
function HeatmapIndicators({ player, other, phaseKey, phaseLabel, matchAverage }) {
    const columns = [
        player && { key: 'player', label: 'Você', indicators: player.indicators?.[phaseKey] },
        other && { key: 'other', label: other.heroName, indicators: other.indicators?.[phaseKey] },
        matchAverage && { key: 'average', label: 'Média da partida', indicators: matchAverage[phaseKey] },
    ].filter(Boolean);
    if (!columns.some(column => column.indicators)) {
        return null;
    }

    return (
        <div className="heatmap-indicators">
            <h4 className="bar-group-title">Indicadores de posicionamento · {phaseLabel}</h4>
            <div className="table-scroll">
                <table className="data-table">
                    <caption className="visually-hidden">
                        Indicadores de posicionamento de {columns.map(column => column.label).join(', ')} em {phaseLabel}
                    </caption>
                    <thead>
                        <tr>
                            <th scope="col">Indicador</th>
                            {columns.map(column => (
                                <th key={column.key} scope="col" className="num-col">{column.label}</th>
                            ))}
                        </tr>
                    </thead>
                    <tbody>
                        {ROWS.map(row => (
                            <tr key={row.key}>
                                <th scope="row">
                                    {row.label}
                                    <span className="indicator-hint">{row.hint}</span>
                                </th>
                                {columns.map(column => (
                                    <td key={column.key} className="num-col mono">{cell(column.indicators, row)}</td>
                                ))}
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </div>
    );
}

export default HeatmapIndicators;
