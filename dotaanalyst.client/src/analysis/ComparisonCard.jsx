import { formatNumber } from '../matches/format';
import Standing from '../ui/Standing';
import { metricLabel } from './metrics';

// The player's per-minute numbers (OpenDota's benchmarks: GPM, XPM, kills, damage…) against every match of the hero,
// any skill level. The subject-by-subject comparison with our base (high rank, pros, lane matchup) lives in the tab
// of each subject (ThemeStatsCard), so nothing here is repeated there.
function ComparisonCard({ performance }) {
    if (!performance || performance.metrics.length === 0) {
        return null;
    }

    const who = `jogadores de ${performance.heroName}`;
    return (
        <div className="card comparison-card">
            <div className="card-head">
                <h3>Seus números por minuto contra todas as partidas de {performance.heroName}</h3>
                <span className="card-aside">OpenDota · qualquer nível</span>
            </div>
            <div className="table-scroll">
                <table className="data-table baseline-table">
                    <caption className="visually-hidden">Seus números por minuto comparados com {who}</caption>
                    <thead>
                        <tr>
                            <th scope="col">Estatística</th>
                            <th scope="col" className="num-col">Você</th>
                            <th scope="col">Como você ficou</th>
                        </tr>
                    </thead>
                    <tbody>
                        {performance.metrics.map(metric => (
                            <tr key={metric.metric}>
                                <th scope="row">{metricLabel(metric.metric)}</th>
                                <td className="num-col mono">{formatNumber(metric.value)}</td>
                                <td><Standing share={metric.percentile} who={who} /></td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </div>
    );
}

export default ComparisonCard;
