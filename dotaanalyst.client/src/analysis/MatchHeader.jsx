import { useState } from 'react';
import { Link } from 'react-router';
import { formatDateTime, formatDuration, formatNumber } from '../matches/format';
import { ResultChip } from '../ui/Chips';

// The top band of the analysis (the Stitch "hero bar"): where you are, the scoreboard of both teams with the match
// time in the middle, and from whose point of view the page is.

const LANES = { top: 'rota de cima', middle: 'rota do meio', bottom: 'rota de baixo', jungle: 'selva', roaming: 'roaming' };
const ROLES = { 1: 'carry (posição 1)', 2: 'mid (posição 2)', 3: 'offlane (posição 3)', 4: 'suporte (posição 4)', 5: 'suporte (posição 5)' };

function shortGold(value) {
    const abs = Math.abs(value);
    return abs >= 1000 ? `${formatNumber(Math.round(abs / 100) / 10)} mil` : formatNumber(abs);
}

function towersTaken(objectives, team) {
    return objectives?.filter(objective => objective.kind === 'tower' && !objective.denied && objective.takenBy === team).length;
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
            <button type="button" className="icon-button" onClick={copy} aria-label="Copiar o número da partida" title="Copiar o número da partida">
                <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="8" y="8" width="11" height="11" rx="2" /><path d="M5 15V6a1 1 0 0 1 1-1h9" /></svg>
            </button>
            <span className="copy-feedback" role="status">{copied ? 'Copiado!' : ''}</span>
        </>
    );
}

function TeamBlock({ side, score, won, towers }) {
    const isRadiant = side === 'radiant';
    return (
        <div className={`score-team score-${side}`}>
            <span className={`team-badge team-badge-${side}`} aria-hidden="true">{isRadiant ? 'R' : 'D'}</span>
            <div className="score-team-text">
                <div className="score-team-name">
                    <strong>{isRadiant ? 'Radiant' : 'Dire'}</strong>
                    <ResultChip won={won} />
                </div>
                {towers !== undefined && <p className="score-team-sub">{towers} {towers === 1 ? 'torre derrubada' : 'torres derrubadas'}</p>}
            </div>
            <span className="score-value mono">
                <span className="visually-hidden">Abates do {isRadiant ? 'Radiant' : 'Dire'}: </span>
                {formatNumber(score)}
            </span>
        </div>
    );
}

function MatchHeader({ analysis }) {
    const { performance, timeline, laning, smartStats } = analysis;
    const radiantWon = analysis.radiantWon ?? null;
    // Net worth lead at the end, from Radiant's side.
    const lastLead = timeline?.netWorthLead?.at(-1);
    const radiantLead = lastLead === undefined ? null : timeline.perspective === 'dire' ? -lastLead : lastLead;
    const strongMetrics = performance?.metrics.filter(metric => metric.percentile >= 0.5).length;

    const context = [
        performance && (performance.isRadiant ? 'Radiant' : 'Dire'),
        laning && LANES[laning.lane],
        smartStats && ROLES[smartStats.role],
    ].filter(Boolean).join(' · ');

    return (
        <header id="match-overview" className="match-hero">
            <div className="container">
                <div className="match-hero-top">
                    <nav className="breadcrumb" aria-label="Caminho">
                        <Link to="/">Partidas</Link>
                        <span aria-hidden="true">/</span>
                        <h1 className="mono">Partida #{analysis.matchId}</h1>
                        <CopyId matchId={analysis.matchId} />
                    </nav>
                    <p className="match-meta mono">
                        <span>{formatDateTime(analysis.startTime)}</span>
                        <span aria-hidden="true">•</span>
                        <span>{analysis.isParsed ? 'Detalhes do OpenDota' : 'Sem detalhes do OpenDota'}</span>
                    </p>
                </div>

                <div className="scoreboard">
                    <TeamBlock
                        side="radiant"
                        score={analysis.radiantScore}
                        won={radiantWon}
                        towers={towersTaken(analysis.objectives, 'radiant')}
                    />
                    <div className="score-center">
                        <span className="score-center-label">Tempo de partida</span>
                        <span className="score-time mono">{formatDuration(analysis.durationSeconds)}</span>
                        {radiantLead !== null && radiantLead !== 0 && (
                            <span className={`score-lead ${radiantLead > 0 ? 'lead-radiant' : 'lead-dire'}`}>
                                {radiantLead > 0 ? 'Radiant' : 'Dire'} +{shortGold(radiantLead)} de patrimônio
                            </span>
                        )}
                    </div>
                    <TeamBlock
                        side="dire"
                        score={analysis.direScore}
                        won={radiantWon === null ? null : !radiantWon}
                        towers={towersTaken(analysis.objectives, 'dire')}
                    />
                </div>

                {performance && (
                    <div className="perspective">
                        <p>
                            <span className="perspective-tag">Perspectiva</span>
                            Você está vendo esta partida como <strong>{performance.heroName}</strong>
                            {context && <> ({context})</>}.
                        </p>
                        <p className="perspective-aside">
                            Acima da média em <strong className="mono">{strongMetrics} de {performance.metrics.length}</strong> métricas
                            do herói
                        </p>
                    </div>
                )}
            </div>
        </header>
    );
}

export default MatchHeader;
