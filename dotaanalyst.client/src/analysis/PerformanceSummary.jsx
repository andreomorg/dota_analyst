import { HeroPortrait } from '../heroes/HeroImage';
import { formatNumber, formatSignedNumber, toPercent } from '../matches/format';
import { shortLabel, standingDetail, standingOf, standingPer100 } from '../ui/standingLevels';

// The player card at the top of the overview (Stitch reference "Resumo"): who you played, and five big numbers, each
// with the OpenDota verdict against other players of the same hero (higher is always better; the API inverts deaths).
// Every OpenDota percentile is listed in ComparisonCard (the "Como melhorar" tab).

const ROLES = { 1: 'Posição 1 · Carry', 2: 'Posição 2 · Mid', 3: 'Posição 3 · Offlane', 4: 'Posição 4 · Suporte', 5: 'Posição 5 · Suporte' };
const LANES = { top: 'rota de cima', middle: 'rota do meio', bottom: 'rota de baixo', jungle: 'selva', roaming: 'roaming' };

const capitalize = text => text.charAt(0).toUpperCase() + text.slice(1);
const oneDecimal = value => formatNumber(Math.round(value * 10) / 10);

function average(players, pick) {
    return players.reduce((sum, player) => sum + pick(player), 0) / players.length;
}

// The verdict at the bottom of a tile; the metric it is about is named on the right (the tile's number is the total).
function TileStanding({ metric, name, heroName }) {
    if (!metric) {
        return <p className="tile-standing is-empty">Sem comparação do OpenDota</p>;
    }
    const level = standingOf(metric.percentile);
    return (
        <div className={`tile-standing tone-${level.tone}`}>
            <p className="tile-standing-head mono">
                <span>{shortLabel(level)}</span>
                <span>{name}</span>
            </p>
            <span className="standing-bar" aria-hidden="true">
                <span className="standing-fill" style={{ width: `${toPercent(metric.percentile)}%` }} />
            </span>
            <p className="tile-standing-detail" aria-hidden="true">{standingPer100(metric.percentile)}</p>
            <span className="visually-hidden">{standingDetail(metric.percentile, `jogadores de ${heroName}`)} em {name}.</span>
        </div>
    );
}

function Tile({ label, value, valueClass, sub, children }) {
    return (
        <div className="perf-tile">
            <div>
                <span className="perf-tile-label mono">{label}</span>
                <p className={`perf-tile-value mono${valueClass ? ` ${valueClass}` : ''}`}>{value}</p>
                {sub && <p className="perf-tile-sub mono">{sub}</p>}
            </div>
            {children}
        </div>
    );
}

// analysis comes from the API; only shown when the point-of-view player is in the match (performance not null).
function PerformanceSummary({ analysis }) {
    const { performance, laning, smartStats } = analysis;
    const team = performance.isRadiant ? analysis.radiant : analysis.dire;
    const player = team.find(candidate => candidate.isCurrentPlayer);
    const metric = name => performance.metrics.find(candidate => candidate.metric === name);
    const minutes = Math.max(1, analysis.durationSeconds / 60);
    const everyone = [...analysis.radiant, ...analysis.dire];
    const above = performance.metrics.filter(candidate => candidate.percentile >= 0.5).length;
    const total = performance.metrics.length;
    const teamDamage = team.reduce((sum, member) => sum + member.heroDamage, 0);
    const context = [
        smartStats && ROLES[smartStats.role],
        laning && capitalize(LANES[laning.lane] ?? laning.lane),
    ].filter(Boolean).join(' • ');

    return (
        <section className="card perf-card" aria-labelledby="performance-title">
            <div className="perf-identity">
                <div className="perf-portrait">
                    <HeroPortrait heroId={performance.heroId} />
                    {player && <span className="perf-level mono">Nvl {player.level}</span>}
                </div>
                <div>
                    <h2 id="performance-title" className="perf-name">
                        {performance.heroName}
                        <span className="tag-you">Você</span>
                    </h2>
                    <p className="perf-context">{context || (performance.isRadiant ? 'Radiant' : 'Dire')}</p>
                    {total > 0 && (
                        <p className={`perf-verdict mono ${above * 2 >= total ? 'text-win' : 'text-loss'}`}>
                            <span className="perf-verdict-dot" aria-hidden="true" />
                            Acima da média em {above} de {total}
                            <span> métricas do herói (OpenDota)</span>
                        </p>
                    )}
                </div>
            </div>

            {player && (
                <div className="perf-tiles">
                    <Tile
                        label="K / D / A"
                        value={`${player.kills} / ${player.deaths} / ${player.assists}`}
                        sub={`Razão ${oneDecimal((player.kills + player.assists) / Math.max(1, player.deaths))}`}
                    >
                        <TileStanding metric={metric('killsPerMinute')} name="abates/min" heroName={performance.heroName} />
                    </Tile>
                    <Tile
                        label="Last hits / denies"
                        value={`${formatNumber(player.lastHits)} / ${formatNumber(player.denies)}`}
                        sub={`${oneDecimal(player.lastHits / minutes)} LH / min`}
                    >
                        <TileStanding metric={metric('lastHitsPerMinute')} name="LH/min" heroName={performance.heroName} />
                    </Tile>
                    <Tile
                        label="Ouro / minuto (GPM)"
                        value={formatNumber(player.goldPerMinute)}
                        valueClass={player.goldPerMinute > average(everyone, p => p.goldPerMinute) ? 'text-win' : undefined}
                        sub={`${formatSignedNumber(Math.round(player.goldPerMinute - average(everyone, p => p.goldPerMinute)))} vs média da partida`}
                    >
                        <TileStanding metric={metric('goldPerMinute')} name="GPM" heroName={performance.heroName} />
                    </Tile>
                    <Tile
                        label="XP / minuto (XPM)"
                        value={formatNumber(player.experiencePerMinute)}
                        sub={`${formatSignedNumber(Math.round(player.experiencePerMinute - average(everyone, p => p.experiencePerMinute)))} vs média da partida`}
                    >
                        <TileStanding metric={metric('experiencePerMinute')} name="XPM" heroName={performance.heroName} />
                    </Tile>
                    <Tile
                        label="Dano a heróis"
                        value={formatNumber(player.heroDamage)}
                        sub={teamDamage > 0 ? `${toPercent(player.heroDamage / teamDamage)}% do time` : null}
                    >
                        <TileStanding metric={metric('heroDamagePerMinute')} name="dano/min" heroName={performance.heroName} />
                    </Tile>
                </div>
            )}
        </section>
    );
}

export default PerformanceSummary;
