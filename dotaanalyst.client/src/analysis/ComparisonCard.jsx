import { formatNumber } from '../matches/format';
import Standing from '../ui/Standing';
import { standingOf } from '../ui/standingLevels';
import { metricLabel } from './metrics';

// The player's per-minute numbers (OpenDota's benchmarks: GPM, XPM, kills, damage…) against every match of the hero,
// any skill level (Stitch reference "Como melhorar"). The subject-by-subject comparison with our base (high rank,
// pros, lane matchup) lives in the tab of each subject (ThemeStatsCard), so nothing here is repeated there.

// What each benchmark measures (one line under its name). The API inverts deaths: a high share is few deaths.
const DESCRIPTIONS = {
    goldPerMinute: 'Ouro ganho por minuto ao longo da partida',
    experiencePerMinute: 'Experiência ganha por minuto ao longo da partida',
    killsPerMinute: 'Abates por minuto de jogo',
    deathsPerMinute: 'Mortes por minuto de jogo',
    assistsPerMinute: 'Assistências por minuto de jogo',
    lastHitsPerMinute: 'Creeps e neutros finalizados por minuto',
    deniesPerMinute: 'Creeps aliados negados por minuto',
    heroDamagePerMinute: 'Dano causado em heróis inimigos por minuto',
    heroHealingPerMinute: 'Cura em heróis aliados por minuto',
    towerDamage: 'Dano em construções na partida inteira',
};

const LOWER_IS_BETTER = new Set(['deathsPerMinute']);
const TOTALS = new Set(['towerDamage']);

function ComparisonCard({ performance }) {
    if (!performance || performance.metrics.length === 0) {
        return null;
    }

    const who = `jogadores de ${performance.heroName}`;
    return (
        <section className="comparison-section" aria-labelledby="comparison-title">
            <div className="comparison-head">
                <div>
                    <h2 id="comparison-title" className="comparison-title">Comparação Geral de Desempenho (OpenDota)</h2>
                    <p className="card-subtitle">Seus números por minuto contra todas as partidas de {performance.heroName}, de qualquer nível.</p>
                </div>
                <p className="comparison-source">Amostra: <span className="mono">benchmarks do OpenDota</span></p>
            </div>
            <div className="card table-card">
                <div className="table-scroll" tabIndex={0} role="region" aria-label="Comparação de desempenho, tabela com rolagem">
                    <table className="data-table comparison-table">
                        <caption className="visually-hidden">Seus números comparados com {who}</caption>
                        <thead>
                            <tr>
                                <th scope="col">Estatística</th>
                                <th scope="col">Você</th>
                                <th scope="col">Como você ficou</th>
                            </tr>
                        </thead>
                        <tbody>
                            {performance.metrics.map(metric => (
                                <tr key={metric.metric}>
                                    <th scope="row">
                                        <span className="comparison-name">
                                            {metricLabel(metric.metric)}
                                            {LOWER_IS_BETTER.has(metric.metric) && <span className="tag-lower mono">menos é melhor</span>}
                                        </span>
                                        {DESCRIPTIONS[metric.metric] && <span className="comparison-desc">{DESCRIPTIONS[metric.metric]}</span>}
                                    </th>
                                    <td className={`comparison-value mono tone-${standingOf(metric.percentile).tone}`}>
                                        {formatNumber(Math.round(metric.value * 100) / 100)}
                                        {!TOTALS.has(metric.metric) && <span className="comparison-unit"> /min</span>}
                                    </td>
                                    <td className="comparison-standing"><Standing share={metric.percentile} who={who} /></td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            </div>
        </section>
    );
}

export default ComparisonCard;
