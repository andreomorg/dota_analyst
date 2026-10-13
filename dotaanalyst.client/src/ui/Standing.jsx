import { LEVELS, standingDetail, standingOf } from './standingLevels';

// The verdict (standingLevels.js) with a five-step scale and a marker where the player is, as a visual aid; the words carry
// the information.
function Standing({ share, who, compact = false }) {
    const level = standingOf(share);
    return (
        <span className={`standing tone-${level.tone}${compact ? ' is-compact' : ''}`}>
            <span className="standing-label">{level.label}</span>
            <span className="standing-scale" aria-hidden="true">
                {LEVELS.slice().reverse().map(step => <span key={step.tone} className={`standing-step step-${step.tone}`} />)}
                <span className="standing-marker" style={{ left: `${Math.min(100, Math.max(0, share * 100))}%` }} />
            </span>
            {!compact && <span className="standing-detail">{standingDetail(share, who)}</span>}
        </span>
    );
}

export default Standing;
