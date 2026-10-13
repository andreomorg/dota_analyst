// The game's own minimap, shared by the death and ward maps. Drawn in map units (mapGeometry.js), so the marks
// placed with the replay's coordinates land in the right place at any width.
//
// The image comes from the game files (materials/overviews in pak01_dir.vpk) and is placed with the game's own
// calibration (resource/overviews/dota.txt); how to extract it again after a patch: CLAUDE.md, "Minimapa".

import minimap from '../assets/minimap.webp';
import { MINIMAP_ORIGIN, MINIMAP_SIZE, svgY } from './mapGeometry';

function GameMap() {
    return (
        <image
            aria-hidden="true"
            className="game-map"
            href={minimap}
            x={MINIMAP_ORIGIN}
            y={svgY(-MINIMAP_ORIGIN)}
            width={MINIMAP_SIZE}
            height={MINIMAP_SIZE}
            preserveAspectRatio="none"
        />
    );
}

export default GameMap;
