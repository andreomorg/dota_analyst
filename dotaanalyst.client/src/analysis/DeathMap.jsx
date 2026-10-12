import { useState } from 'react';
import { formatDuration } from '../matches/format';

import { svgY, VIEW_BOX } from './mapGeometry';
import SchematicMap from './SchematicMap';

// Where the player died, on the schematic map (SchematicMap.jsx).

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
                                        transform={`translate(${death.x},${svgY(death.y)})`}
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
