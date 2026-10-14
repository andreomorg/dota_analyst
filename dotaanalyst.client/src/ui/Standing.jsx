import { standingDetail, standingOf, standingPer100 } from './standingLevels';

// The verdict (standingLevels.js) in the Stitch references' form: the words on the left, "how many in 100" on the
// right and a bar filled up to where the player is, in the verdict's colour. The words carry the information; the
// bar is a visual aid. who: shown to screen readers ("jogadores de Lich").
function Standing({ share, who, compact = false }) {
    const level = standingOf(share);
    const fill = Math.min(100, Math.max(0, share * 100));
    return (
        <span className={`standing tone-${level.tone}${compact ? ' is-compact' : ''}`}>
            <span className="standing-row">
                <span className="standing-label">{level.label}</span>
                <span className="standing-detail mono" aria-hidden={who ? 'true' : undefined}>{standingPer100(share)}</span>
                {who && <span className="visually-hidden">{standingDetail(share, who)}</span>}
            </span>
            <span className="standing-bar" aria-hidden="true">
                <span className="standing-fill" style={{ width: `${fill}%` }} />
            </span>
        </span>
    );
}

export default Standing;
