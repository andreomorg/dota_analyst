import { formatNumber, toPercent } from '../matches/format';
import { metricLabel, ratingLabel } from './metrics';

// "Ouro por minuto (GPM): melhor que 51% dos jogadores de Lich".
// The percentile is always "higher is better" (the API already inverts deaths).
function describe(metric, heroName) {
    return `${metricLabel(metric.metric)}: melhor que ${toPercent(metric.percentile)}% dos jogadores de ${heroName}`;
}

function HighlightList({ title, metricNames, metricsByName, heroName, emptyText }) {
    // Keeps the API order (strengths best first, weaknesses worst first) and skips names without data.
    const items = metricNames.map(name => metricsByName.get(name)).filter(Boolean);

    return (
        <section className="highlight-list">
            <h3>{title}</h3>
            {items.length === 0
                ? <p>{emptyText}</p>
                : (
                    <ul>
                        {items.map(metric => <li key={metric.metric}>{describe(metric, heroName)}</li>)}
                    </ul>
                )}
        </section>
    );
}

function MetricRow({ metric }) {
    const percent = toPercent(metric.percentile);

    return (
        <li className="metric-row">
            <span className="metric-name">{metricLabel(metric.metric)}</span>
            <span className="metric-value">{formatNumber(metric.value)}</span>
            {/* The bar is decorative; the text next to it carries the same information for screen readers. */}
            <span className="percentile-bar" aria-hidden="true">
                <span className={`percentile-fill rating-${metric.rating}`} style={{ width: `${percent}%` }} />
            </span>
            <span className="metric-percentile">melhor que {percent}%</span>
            <span className={`metric-rating rating-${metric.rating}`}>{ratingLabel(metric.rating)}</span>
        </li>
    );
}

function PerformanceSummary({ performance }) {
    const metricsByName = new Map(performance.metrics.map(metric => [metric.metric, metric]));

    return (
        <section className="performance-summary" aria-labelledby="performance-title">
            <h2 id="performance-title">Seu desempenho</h2>
            <p className="performance-intro">
                Comparado a outros jogadores de {performance.heroName}.
            </p>

            <div className="highlights">
                <HighlightList
                    title="Pontos fortes"
                    metricNames={performance.strengths}
                    metricsByName={metricsByName}
                    heroName={performance.heroName}
                    emptyText="Nenhum destaque positivo nesta partida."
                />
                <HighlightList
                    title="Pontos a melhorar"
                    metricNames={performance.weaknesses}
                    metricsByName={metricsByName}
                    heroName={performance.heroName}
                    emptyText="Nenhum ponto fraco nesta partida."
                />
            </div>

            <h3>Todas as métricas</h3>
            <ul className="metric-list">
                {performance.metrics.map(metric => <MetricRow key={metric.metric} metric={metric} />)}
            </ul>
        </section>
    );
}

export default PerformanceSummary;
