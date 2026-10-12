import { useState } from 'react';
import { formatDuration } from '../matches/format';

// Where the player died, on a schematic map of our own (lanes, river, bases — not Valve's art). Coordinates come
// from the replay in map units, roughly −8200..8200 on both axes with Radiant's base bottom-left; the SVG viewBox
// uses the same units (y flipped), so the map scales to any width without stretching.

const EXTENT = 8200;
const VIEW_BOX = `${-EXTENT} ${-EXTENT} ${2 * EXTENT} ${2 * EXTENT}`;
// SVG y grows downwards; the map's grows upwards.
const toSvg = ([x, y]) => `${x},${-y}`;
const path = points => `M${points.map(toSvg).join('L')}`;

// Approximate lanes from the towers' positions (the same ones the Go parser's zones use).
const LANES = [
    [[-6900, -4300], [-6450, 5950], [4000, 6400]], // top
    [[-4500, -4000], [4000, 3600]], // middle
    [[-4300, -6800], [6350, -6250], [6750, 3500]], // bottom
];
const RIVER = [[-7600, 7300], [-6300, 5600], [-3200, 2600], [0, 0], [3000, -2700], [5900, -6100], [7500, -7600]];

const KILLERS = {
    tower: 'Torre',
    creeps: 'Creeps',
    neutrals: 'Neutros',
    roshan: 'Roshan',
    other: 'Outro',
};

function killerName(death) {
    if (death.killer === 'hero') {
        return death.killerHeroName ?? 'Herói';
    }
    return KILLERS[death.killer] ?? death.killer;
}

function SchematicMap() {
    return (
        <g aria-hidden="true">
            <rect className="map-ground" x={-EXTENT} y={-EXTENT} width={2 * EXTENT} height={2 * EXTENT} rx="400" />
            <path className="map-base radiant" d={`M${-EXTENT},${EXTENT} L${-EXTENT},3700 L-3600,3700 L-3600,${EXTENT} Z`} />
            <path className="map-base dire" d={`M${EXTENT},${-EXTENT} L${EXTENT},-3300 L3300,-3300 L3300,${-EXTENT} Z`} />
            <path className="map-river" d={path(RIVER)} />
            {LANES.map(lane => <path key={lane[0].join()} className="map-lane" d={path(lane)} />)}
            <text className="map-label" x={-7600} y={7700}>Radiant</text>
            <text className="map-label" x={7600} y={-7300} textAnchor="end">Dire</text>
        </g>
    );
}

// deaths comes from the API (null until the replay is read; absent on older responses: then nothing shows).
function DeathMap({ deaths }) {
    const [selected, setSelected] = useState(null);
    if (!deaths) {
        return null;
    }

    const laning = deaths.filter(death => death.inLaningPhase).length;
    return (
        <section className="death-map" aria-labelledby="death-map-title">
            <h2 id="death-map-title">Onde você morreu</h2>
            {deaths.length === 0
                ? <p className="laning-note">Você não morreu nesta partida.</p>
                : (
                    <>
                        <p className="laning-intro">
                            {deaths.length} {deaths.length === 1 ? 'morte' : 'mortes'}, {laning} na fase de rotas (até 10:00).
                            Mapa esquemático: rotas, rio e bases.
                        </p>
                        <ul className="chart-legend death-legend">
                            <li><span className="legend-swatch series-1" aria-hidden="true" /> Fase de rotas (até 10:00)</li>
                            <li><span className="legend-swatch series-2" aria-hidden="true" /> Depois dos 10:00</li>
                            <li><span className="legend-swatch reincarnated" aria-hidden="true" /> Voltou com Aegis</li>
                        </ul>
                        <div className="death-map-frame">
                            <svg viewBox={VIEW_BOX} role="img" aria-label={`Mapa com ${deaths.length} mortes; os detalhes estão na tabela abaixo.`}>
                                <SchematicMap />
                                {deaths.map((death, index) => (
                                    <g
                                        key={`${death.seconds}-${index}`}
                                        className={`death-marker ${death.inLaningPhase ? 'series-1' : 'series-2'}${death.reincarnated ? ' is-reincarnated' : ''}${selected === index ? ' is-selected' : ''}`}
                                        transform={`translate(${death.x},${-death.y})`}
                                        onPointerEnter={() => setSelected(index)}
                                        onPointerLeave={() => setSelected(null)}
                                        onClick={() => setSelected(selected === index ? null : index)}
                                    >
                                        {/* A bigger, invisible hit area than the mark. */}
                                        <circle className="death-hit" r="700" />
                                        <circle className="death-dot" r="430" />
                                        <text className="death-number" textAnchor="middle" dy="170">{index + 1}</text>
                                    </g>
                                ))}
                            </svg>
                            {selected !== null && deaths[selected] && (
                                <div className="chart-tooltip death-tooltip" role="status">
                                    <strong>{selected + 1}. {formatDuration(deaths[selected].seconds)}</strong>
                                    <span>Morto por: {killerName(deaths[selected])}</span>
                                    {deaths[selected].reincarnated && <span>Voltou com Aegis (não conta no placar)</span>}
                                </div>
                            )}
                        </div>
                        <div className="table-scroll">
                            <table className="death-table">
                                <caption className="visually-hidden">Suas mortes, em ordem</caption>
                                <thead>
                                    <tr>
                                        <th scope="col">#</th>
                                        <th scope="col">Tempo</th>
                                        <th scope="col">Morto por</th>
                                        <th scope="col">Fase</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {deaths.map((death, index) => (
                                        <tr key={`${death.seconds}-${index}`} className={selected === index ? 'current-player' : undefined}>
                                            <th scope="row">{index + 1}</th>
                                            <td>{formatDuration(death.seconds)}</td>
                                            <td>{killerName(death)}{death.reincarnated ? ' (voltou com Aegis)' : ''}</td>
                                            <td>{death.inLaningPhase ? 'Rotas' : 'Depois'}</td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    </>
                )}
        </section>
    );
}

export default DeathMap;
