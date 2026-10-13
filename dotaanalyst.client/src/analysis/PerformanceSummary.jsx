import { formatNumber, toPercent } from '../matches/format';
import { HeroPortrait } from '../heroes/HeroImage';
import Kda from '../ui/Kda';
import SectionHead from '../ui/SectionHead';
import { standingDetail, standingOf } from '../ui/standingLevels';

// "Seu desempenho" (the Stitch overview): who you played and how the lane went, and the match's big numbers; every
// OpenDota percentile against other players of the same hero is in ComparisonCard (the "Como melhorar" tab) (higher is always better; the
// API inverts deaths).

const ROLES = { 1: 'Carry · posição 1', 2: 'Mid · posição 2', 3: 'Offlane · posição 3', 4: 'Suporte · posição 4', 5: 'Suporte · posição 5' };
const LANES = { top: 'rota de cima', middle: 'rota do meio', bottom: 'rota de baixo', jungle: 'selva', roaming: 'roaming' };
const OUTCOMES = {
    won: { label: 'Ganhou a rota', className: 'text-win' },
    even: { label: 'Rota empatada', className: 'text-even' },
    lost: { label: 'Perdeu a rota', className: 'text-loss' },
};

// Who had more of the lane's net worth at 10:00 (the same ±5 points the eye can tell on the bar).
function laneBalance(share) {
    if (share >= 0.55) {
        return { label: 'Seu lado na frente', className: 'text-win', fill: 'fill-win' };
    }
    return share <= 0.45
        ? { label: 'Adversários na frente', className: 'text-loss', fill: 'fill-loss' }
        : { label: 'Equilibrado', className: 'text-even', fill: 'fill-even' };
}

// Under a big number: the verdict against other players of the hero, in words.
function Percentile({ metric, heroName }) {
    if (!metric) {
        return null;
    }
    const level = standingOf(metric.percentile);
    return (
        <span className={`standing-inline tone-${level.tone}`} title={`Contra jogadores de ${heroName}`}>
            <strong>{level.label}</strong> · {standingDetail(metric.percentile)}
        </span>
    );
}

function Tile({ label, children, sub }) {
    return (
        <div className="tile stat-tile">
            <span className="stat-label">{label}</span>
            <div className="stat-value">{children}</div>
            {sub && <span className="stat-sub">{sub}</span>}
        </div>
    );
}

function names(players) {
    const list = players.map(player => player.heroName);
    return list.length <= 1 ? list.join('') : `${list.slice(0, -1).join(', ')} e ${list.at(-1)}`;
}

function LaneBox({ laning }) {
    if (!laning) {
        return null;
    }
    const outcome = OUTCOMES[laning.outcome];
    const ours = [laning.player, ...laning.allies].reduce((sum, hero) => sum + hero.netWorth, 0);
    const theirs = laning.opponents.reduce((sum, hero) => sum + hero.netWorth, 0);
    const share = ours + theirs > 0 && laning.opponents.length > 0 ? ours / (ours + theirs) : null;
    const difference = laning.netWorthDifference;

    return (
        <>
            <div className="tile lane-box">
                <div className="lane-box-head">
                    <span>Fase de rotas · {LANES[laning.lane] ?? laning.lane}</span>
                    {outcome && <strong className={outcome.className}>{outcome.label}</strong>}
                </div>
                <p>
                    {difference !== null && difference !== undefined && laning.opponents.length > 0
                        ? (
                            <>
                                <strong className={difference >= 0 ? 'text-win' : 'text-loss'}>
                                    {difference >= 0 ? '+' : '−'}{formatNumber(Math.abs(difference))}
                                </strong>
                                {' '}de patrimônio do seu lado aos 10:00 contra{' '}
                                <strong className="text-loss">{names(laning.opponents)}</strong>.
                            </>
                        )
                        : 'Sem adversário na sua rota: não há disputa para comparar.'}
                </p>
            </div>
            {share !== null && (
                <div className="meter">
                    <div className="meter-head">
                        <span>Patrimônio da rota aos 10:00</span>
                        <span className={laneBalance(share).className}>
                            {laneBalance(share).label} · <span className="mono">{toPercent(share)}%</span> seu
                        </span>
                    </div>
                    <span className="meter-track meter-split" aria-hidden="true">
                        <span className={`meter-fill ${laneBalance(share).fill}`} style={{ width: `${toPercent(share)}%` }} />
                    </span>
                </div>
            )}
        </>
    );
}

// analysis comes from the API; only shown when the point-of-view player is in the match (performance not null).
function PerformanceSummary({ analysis }) {
    const { performance, laning, smartStats } = analysis;
    const team = performance.isRadiant ? analysis.radiant : analysis.dire;
    const player = team.find(candidate => candidate.isCurrentPlayer);
    const metric = name => performance.metrics.find(candidate => candidate.metric === name);
    const minutes = analysis.durationSeconds / 60;
    const teamKills = performance.isRadiant ? analysis.radiantScore : analysis.direScore;
    const participation = smartStats?.player.killParticipation
        ?? (teamKills > 0 && player ? (player.kills + player.assists) / teamKills : null);
    const everyone = [...analysis.radiant, ...analysis.dire];
    const netWorthRank = player ? everyone.filter(other => other.netWorth > player.netWorth).length + 1 : null;

    return (
        <section className="section" aria-labelledby="performance-title">
            <SectionHead id="performance-title" title={`Seu desempenho (${performance.heroName})`}>
                {player && <span className="mono">Nível final {player.level}</span>}
            </SectionHead>

            <div className="overview-grid">
                <div className="card identity-card">
                    <div className="identity">
                        <HeroPortrait heroId={performance.heroId} />
                        <div>
                            <h3>{performance.heroName}</h3>
                            <p className="identity-team mono">{performance.isRadiant ? 'Radiant' : 'Dire'}</p>
                            {smartStats && <span className="chip chip-neutral">{ROLES[smartStats.role]}</span>}
                        </div>
                    </div>
                    <LaneBox laning={laning} />
                </div>

                {player && (
                    <div className="card stat-grid">
                        <Tile label="K / D / A" sub={`KDA ${formatNumber(Math.round(((player.kills + player.assists) / Math.max(1, player.deaths)) * 10) / 10)}`}>
                            <Kda kills={player.kills} deaths={player.deaths} assists={player.assists} size="large" />
                        </Tile>
                        <Tile
                            label="Participação em abates"
                            sub={`${player.kills + player.assists} de ${teamKills} abates do time`}
                        >
                            <span className="mono">{participation === null ? '—' : `${toPercent(participation)}%`}</span>
                        </Tile>
                        <Tile label="Ouro / XP por minuto" sub={<Percentile metric={metric('goldPerMinute')} heroName={performance.heroName} />}>
                            <span className="mono">{formatNumber(player.goldPerMinute)}</span>
                            <span className="stat-unit">GPM</span>
                            <span className="stat-second mono">{formatNumber(player.experiencePerMinute)} <span className="stat-unit">XPM</span></span>
                        </Tile>
                        <Tile label="Last hits / denies" sub={`${formatNumber(Math.round((player.lastHits / Math.max(1, minutes)) * 10) / 10)} last hits por minuto`}>
                            <span className="mono">{formatNumber(player.lastHits)}</span>
                            <span className="stat-unit">/</span>
                            <span className="stat-second-inline mono">{formatNumber(player.denies)}</span>
                        </Tile>
                        <Tile label="Dano em heróis" sub={<Percentile metric={metric('heroDamagePerMinute')} heroName={performance.heroName} />}>
                            <span className="mono">{formatNumber(player.heroDamage)}</span>
                        </Tile>
                        <Tile label="Dano em torres" sub={<Percentile metric={metric('towerDamage')} heroName={performance.heroName} />}>
                            <span className="mono">{formatNumber(player.towerDamage)}</span>
                        </Tile>
                        <Tile label="Cura em heróis" sub={<Percentile metric={metric('heroHealingPerMinute')} heroName={performance.heroName} />}>
                            <span className="mono">{formatNumber(player.heroHealing)}</span>
                        </Tile>
                        <Tile label="Patrimônio final" sub={netWorthRank && `${netWorthRank}º maior da partida`}>
                            <span className="mono">{formatNumber(player.netWorth)}</span>
                        </Tile>
                    </div>
                )}
            </div>

        </section>
    );
}

export default PerformanceSummary;
