import { useState } from 'react';
import { Link } from 'react-router';
import { HeroIcon } from '../heroes/HeroImage';
import { formatDateTime, formatDuration, formatNumber } from '../matches/format';

// The match band of the analysis (Stitch reference "Resumo", shared by every tab): breadcrumb and date, the score of
// both teams with the duration and the final net worth lead, and from whose point of view the page is.

const LANES = { top: 'rota de cima', middle: 'rota do meio', bottom: 'rota de baixo', jungle: 'selva', roaming: 'roaming' };
const ROLES = { 1: 'Carry', 2: 'Mid', 3: 'Offlane', 4: 'Suporte 4', 5: 'Suporte 5' };

const capitalize = text => text.charAt(0).toUpperCase() + text.slice(1);

// 14200 -> "14,2 mil".
function shortGold(value) {
    const abs = Math.abs(value);
    return abs >= 1000 ? `${formatNumber(Math.round(abs / 100) / 10)}k` : formatNumber(abs);
}

function CopyId({ matchId }) {
    const [copied, setCopied] = useState(false);
    function copy() {
        navigator.clipboard?.writeText(String(matchId)).then(() => {
            setCopied(true);
            setTimeout(() => setCopied(false), 1500);
        }).catch(() => {});
    }
    return (
        <>
            <button type="button" className="icon-button copy-button" onClick={copy} aria-label="Copiar o número da partida" title="Copiar o número da partida">
                <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="9" y="9" width="13" height="13" rx="2" /><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" /></svg>
            </button>
            <span className="copy-feedback" role="status">{copied ? 'Copiado!' : ''}</span>
        </>
    );
}

// The result tag of a team: green for the winner, red for the loser, neutral while the result is unknown.
function ResultTag({ won }) {
    if (won === null) {
        return <span className="band-result">Sem resultado</span>;
    }
    return <span className={`band-result ${won ? 'is-win' : 'is-loss'}`}>{won ? 'Vitória' : 'Derrota'}</span>;
}

function tone(won) {
    return won === null ? '' : won ? ' text-win' : ' text-loss';
}

function MatchHeader({ analysis }) {
    const { performance, timeline, laning, smartStats } = analysis;
    const radiantWon = analysis.radiantWon ?? null;
    const direWon = radiantWon === null ? null : !radiantWon;
    // Net worth lead at the end, from Radiant's side.
    const lastLead = timeline?.netWorthLead?.at(-1);
    const radiantLead = lastLead === undefined ? null : timeline.perspective === 'dire' ? -lastLead : lastLead;
    const leader = radiantLead > 0 ? 'radiant' : 'dire';
    const team = performance && (performance.isRadiant ? analysis.radiant : analysis.dire);
    const player = team?.find(candidate => candidate.isCurrentPlayer);
    const lane = laning && LANES[laning.lane];
    const role = smartStats && ROLES[smartStats.role];
    const context = [
        performance && (performance.isRadiant ? 'Radiant' : 'Dire'),
        lane && `${capitalize(lane)}${role ? ` (${role})` : ''}`,
        !lane && role,
    ].filter(Boolean).join(' • ');

    return (
        <header id="match-overview" className="match-band">
            <div className="container">
                <div className="band-top">
                    <nav className="breadcrumb" aria-label="Caminho">
                        <Link to="/">Partidas</Link>
                        <span aria-hidden="true">/</span>
                        <h1 className="mono">Partida #{analysis.matchId}</h1>
                        <CopyId matchId={analysis.matchId} />
                    </nav>
                    <p className="band-date mono">{formatDateTime(analysis.startTime)}</p>
                </div>

                <div className="band-main">
                    <div className="band-score">
                        <p className="band-teams">
                            <span className="band-team">
                                <ResultTag won={radiantWon} />
                                <strong>Radiant</strong>
                                <span className={`band-kills mono${tone(radiantWon)}`}>
                                    <span className="visually-hidden">abates: </span>{formatNumber(analysis.radiantScore)}
                                </span>
                            </span>
                            <span className="band-vs mono" aria-hidden="true">vs</span>
                            <span className="band-team">
                                <span className={`band-kills mono${tone(direWon)}`}>
                                    <span className="visually-hidden">Dire, abates: </span>{formatNumber(analysis.direScore)}
                                </span>
                                <strong aria-hidden="true">Dire</strong>
                                <ResultTag won={direWon} />
                            </span>
                        </p>
                        <span className="band-divider" aria-hidden="true" />
                        <p className="band-duration mono">
                            <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="10" /><path d="M12 6v6l4 2" /></svg>
                            <span>Duração: <strong>{formatDuration(analysis.durationSeconds)}</strong></span>
                            {radiantLead !== null && radiantLead !== 0 && (
                                <>
                                    <span className="band-dot" aria-hidden="true">•</span>
                                    <span className={tone(leader === 'radiant' ? radiantWon : direWon).trim() || undefined}>
                                        +{shortGold(radiantLead)} patrimônio {leader === 'radiant' ? 'Radiant' : 'Dire'}
                                    </span>
                                </>
                            )}
                        </p>
                    </div>

                    {performance && (
                        <div className="band-perspective">
                            <HeroIcon heroId={performance.heroId} />
                            <div>
                                <p className="band-perspective-name">
                                    <span className="visually-hidden">Ponto de vista: </span>
                                    <strong>{performance.heroName}</strong>
                                    <span className="tag-you">Você</span>
                                    {context && <span className="band-perspective-context">{context}</span>}
                                </p>
                                {player && (
                                    <p className="band-perspective-kda mono">
                                        KDA final: <strong>{player.kills} / {player.deaths} / {player.assists}</strong>
                                    </p>
                                )}
                            </div>
                        </div>
                    )}
                </div>
            </div>
        </header>
    );
}

export default MatchHeader;
